using System.Text;
using WaveLinkBackup.App.Hosting;
using WaveLinkBackup.Core.Automation;
using WaveLinkBackup.Core.Discovery;
using WaveLinkBackup.Core.Io;
using WaveLinkBackup.Core.Results;
using WaveLinkBackup.Core.Snapshots;
using WaveLinkBackup.Core.Tests.Fakes;

namespace WaveLinkBackup.App.Tests;

/// <summary>
/// The host owns the coordinator, the timer and pause. Pause is deliberately the host's
/// business: AutoBackupCoordinator owns no timer and waits to be ticked, so pausing is simply
/// not ticking it — putting a pause concept into Core would move a UI affordance into a library
/// that has no UI.
///
/// The rig is AutoBackupCoordinatorTests' rig, deliberately: the same in-memory filesystem, the
/// same fake clock, and not one test that waits.
/// </summary>
public sealed class BackupHostTests
{
    private const string LocalAppData = @"C:\Users\test\AppData\Local";
    private const string LocalState =
        LocalAppData + @"\Packages\Elgato.WaveLink_g54w8ztgkx496\LocalState";
    private const string SettingsPath = LocalState + @"\Settings.json";
    private const string StorePath = LocalAppData + @"\WaveLinkBackup";

    private static string Config(string micName) =>
        """{"MixerConfiguration":{"InputSettings":{"a":{"InputName":"NAME"}}}}"""
            .Replace("NAME", micName, StringComparison.Ordinal);

    private sealed class Harness : IDisposable
    {
        public FakeFileSystem Fs { get; }
        public FakeClock Clock { get; } = new();
        public FakeSettingsWatcher Watcher { get; } = new();
        public SnapshotStore Store { get; }
        public AutoBackupCoordinator Coordinator { get; }
        public BackupHost Host { get; }

        /// <param name="storeWritable">False models a read-only or denied backup folder.</param>
        /// <param name="waveLinkInstalled">False models nothing to back up.</param>
        public Harness(bool storeWritable = true, bool waveLinkInstalled = true)
        {
            Fs = new FakeFileSystem { FailDirectoryCreation = !storeWritable };
            if (waveLinkInstalled) InstallWaveLink();

            Store = new SnapshotStore(Fs, Clock, StorePath);

            var service = new BackupService(
                new SettingsInspector(new SettingsLocator(Fs, LocalAppData), new SettingsReader(Fs)),
                Store);

            Coordinator = new AutoBackupCoordinator(Watcher, service, Clock, AutoBackupPolicy.Default);
            Host = new BackupHost(Coordinator, Clock);
        }

        /// <summary>AddFile bypasses CreateDirectory, so this works on an unwritable store too.</summary>
        public void InstallWaveLink() => Fs.AddFile(SettingsPath, Config("Wave Mic 1"));

        public void EditSettings(string micName) =>
            Fs.WriteBytes(SettingsPath, Encoding.UTF8.GetBytes(Config(micName)));

        /// <summary>A write has landed and settled, so the very next tick would capture.</summary>
        public void MakeACaptureDue()
        {
            Watcher.RaiseChange();
            Clock.Advance(TimeSpan.FromSeconds(61));
        }

        public void Dispose() => Host.Dispose();
    }

    [Fact]
    public void A_new_host_is_not_paused()
    {
        using var h = new Harness();

        Assert.False(h.Host.IsPaused);
        Assert.False(h.Host.Conditions.IsPaused);
        Assert.Equal(TrayStatus.Watching, TrayState.From(h.Host.Conditions));
    }

    /// <summary>
    /// Pausing is NOT ticking. The pending write must survive it untouched, or an hour's pause
    /// would quietly throw away the change that was waiting to be captured.
    /// </summary>
    [Fact]
    public void Pausing_stops_ticks_reaching_the_coordinator()
    {
        using var h = new Harness();
        h.Host.Start();
        h.MakeACaptureDue();

        var pending = h.Coordinator.PendingSince;
        h.Host.PauseFor(TimeSpan.FromHours(1));

        var tick = h.Host.Tick();

        Assert.False(tick.Captured);
        Assert.Empty(h.Store.List());
        Assert.Equal(pending, h.Coordinator.PendingSince);
    }

    [Fact]
    public void The_pause_expires_on_its_own()
    {
        using var h = new Harness();

        h.Host.PauseFor(TimeSpan.FromHours(1));
        Assert.True(h.Host.IsPaused);

        h.Clock.Advance(TimeSpan.FromMinutes(61));

        Assert.False(h.Host.IsPaused);
    }

    [Fact]
    public void Resuming_ends_the_pause_early()
    {
        using var h = new Harness();

        h.Host.PauseFor(TimeSpan.FromHours(1));
        h.Host.Resume();

        Assert.False(h.Host.IsPaused);
    }

    /// <summary>
    /// What makes the tray's NEEDS YOU reachable at all. Without the error surfacing here the
    /// icon has a state it can never enter, and a failing watcher is invisible.
    /// </summary>
    [Fact]
    public void A_failing_tick_surfaces_its_error_in_the_conditions()
    {
        using var h = new Harness(storeWritable: false);
        h.Host.Start();
        h.MakeACaptureDue();

        var tick = h.Host.Tick();

        Assert.False(tick.Captured);
        Assert.IsType<StoreUnavailable>(h.Host.Conditions.LastError);
        Assert.Equal(TrayStatus.NeedsYou, TrayState.From(h.Host.Conditions));
    }

    /// <summary>
    /// The tray must leave NEEDS YOU on its own once the problem goes away. Requiring a restart
    /// to clear a stale error would be its own bug report.
    /// </summary>
    [Fact]
    public void A_successful_tick_clears_a_previous_error()
    {
        using var h = new Harness(waveLinkInstalled: false);
        h.Host.Start();

        h.MakeACaptureDue();
        h.Host.Tick();
        Assert.NotNull(h.Host.Conditions.LastError);

        h.InstallWaveLink();
        h.MakeACaptureDue();
        var recovered = h.Host.Tick();

        Assert.True(recovered.Captured);
        Assert.Null(h.Host.Conditions.LastError);
        Assert.Equal(TrayStatus.Watching, TrayState.From(h.Host.Conditions));
    }
}
