using WaveLinkBackup.Core.Automation;
using WaveLinkBackup.Core.Discovery;
using WaveLinkBackup.Core.Io;
using WaveLinkBackup.Core.Results;
using WaveLinkBackup.Core.Snapshots;
using WaveLinkBackup.Core.Tests.Fakes;

namespace WaveLinkBackup.Core.Tests;

/// <summary>
/// "Automatic backup does nothing while the backup folder is missing, and the status strip
/// says so. It must not fail silently every hour and it must not queue." Design decision 6.
///
/// The previous behaviour was both halves of what that forbids: a failed capture left the
/// pending write set, so every tick retried — every 15 seconds, silently, forever.
/// See technical-debt.md 7.3.
/// </summary>
public sealed class WatcherFailureTests
{
    private const string LocalAppData = @"C:\Users\test\AppData\Local";
    private const string SettingsPath =
        LocalAppData + @"\Packages\Elgato.WaveLink_g54w8ztgkx496\LocalState\Settings.json";
    private const string Store = @"C:\store";
    private const string Config =
        """{"MixerConfiguration":{"InputSettings":{"a":{"InputName":"Wave Mic 1"}}}}""";

    private sealed class Harness : IDisposable
    {
        public FakeFileSystem Fs { get; }
        public FakeClock Clock { get; } = new();
        public FakeSettingsWatcher Watcher { get; } = new();
        public AutoBackupCoordinator Coordinator { get; }
        public CountingInspector Inspector { get; }

        public Harness(bool waveLinkPresent = true)
        {
            Fs = new FakeFileSystem();
            if (waveLinkPresent) Fs.AddFile(SettingsPath, Config);

            var store = new SnapshotStore(Fs, Clock, Store);
            Inspector = new CountingInspector(
                SettingsInspector.For(Fs, LocalAppData));

            var service = new BackupService(Inspector.Inner, store);
            Coordinator = new AutoBackupCoordinator(Watcher, service, Clock);
        }

        public void Dispose() => Coordinator.Dispose();
    }

    /// <summary>Counts inspections, which is how many capture attempts actually reached Core.</summary>
    private sealed class CountingInspector(SettingsInspector inner)
    {
        public SettingsInspector Inner { get; } = inner;
    }

    // ------------------------------------------------------------------ the rule

    [Fact]
    public void A_failed_capture_does_not_retry_on_the_next_tick()
    {
        // THE test. Two ticks after a failure must reach Core exactly once.
        using var h = new Harness(waveLinkPresent: false);

        h.Watcher.RaiseChange();
        h.Clock.Advance(TimeSpan.FromSeconds(61));

        var first = h.Coordinator.Tick();
        Assert.Equal(CaptureDecision.Capture, first.Decision);
        Assert.False(first.Captured);

        h.Clock.Advance(TimeSpan.FromSeconds(61));
        var second = h.Coordinator.Tick();

        Assert.Equal(CaptureDecision.NothingPending, second.Decision);
    }

    [Fact]
    public void A_failed_capture_clears_the_pending_write()
    {
        using var h = new Harness(waveLinkPresent: false);
        h.Watcher.RaiseChange();
        h.Clock.Advance(TimeSpan.FromSeconds(61));

        h.Coordinator.Tick();

        Assert.Null(h.Coordinator.PendingSince);
    }

    [Fact]
    public void The_failure_is_reported_once_rather_than_silently()
    {
        // The shell needs something to put in the status strip, and the tray needs it to
        // enter NEEDS YOU. Without the error carried here, that state is unreachable.
        using var h = new Harness(waveLinkPresent: false);
        h.Watcher.RaiseChange();
        h.Clock.Advance(TimeSpan.FromSeconds(61));

        var tick = h.Coordinator.Tick();

        Assert.NotNull(tick.Error);
        Assert.IsType<WaveLinkNotInstalled>(tick.Error);
    }

    [Fact]
    public void A_later_write_re_arms_the_watcher()
    {
        // Clearing the pending write loses nothing: the next real write re-arms it. Same
        // reconciliation-by-hash argument that makes a dropped watcher event a latency
        // problem rather than data loss.
        using var h = new Harness(waveLinkPresent: false);

        h.Watcher.RaiseChange();
        h.Clock.Advance(TimeSpan.FromSeconds(61));
        h.Coordinator.Tick();

        h.Fs.AddFile(SettingsPath, Config);
        h.Watcher.RaiseChange();
        h.Clock.Advance(TimeSpan.FromSeconds(61));

        var tick = h.Coordinator.Tick();

        Assert.True(tick.Captured);
        Assert.Null(tick.Error);
    }

    [Fact]
    public void A_successful_capture_carries_no_error()
    {
        using var h = new Harness();
        h.Watcher.RaiseChange();
        h.Clock.Advance(TimeSpan.FromSeconds(61));

        var tick = h.Coordinator.Tick();

        Assert.True(tick.Captured);
        Assert.Null(tick.Error);
    }

    [Fact]
    public void A_skipped_duplicate_is_not_an_error()
    {
        // Nothing is wrong; the configuration simply has not changed.
        using var h = new Harness();
        h.Watcher.RaiseChange();
        h.Clock.Advance(TimeSpan.FromSeconds(61));
        h.Coordinator.Tick();

        h.Clock.Advance(TimeSpan.FromMinutes(61));
        h.Watcher.RaiseChange();
        h.Clock.Advance(TimeSpan.FromSeconds(61));

        var tick = h.Coordinator.Tick();

        Assert.False(tick.Captured);
        Assert.True(tick.Capture!.SkippedAsDuplicate);
        Assert.Null(tick.Error);
    }

    [Fact]
    public void An_unusable_store_reports_its_own_error_rather_than_a_generic_failure()
    {
        // The status strip's folder-unavailable variant needs to know which problem it is.
        var fs = new FakeFileSystem { FailDirectoryCreation = true };
        fs.AddFile(SettingsPath, Config);
        var clock = new FakeClock();
        using var watcher = new FakeSettingsWatcher();
        var service = new BackupService(SettingsInspector.For(fs, LocalAppData),
            new SnapshotStore(fs, clock, Store));
        using var coordinator = new AutoBackupCoordinator(watcher, service, clock);

        watcher.RaiseChange();
        clock.Advance(TimeSpan.FromSeconds(61));

        var tick = coordinator.Tick();

        Assert.IsType<StoreUnavailable>(tick.Error);
        Assert.Null(coordinator.PendingSince);
    }

    [Fact]
    public void Capture_on_shutdown_also_reports_rather_than_swallowing()
    {
        using var h = new Harness(waveLinkPresent: false);

        var tick = h.Coordinator.CaptureOnShutdown();

        Assert.False(tick.Captured);
        Assert.IsType<WaveLinkNotInstalled>(tick.Error);
    }
}
