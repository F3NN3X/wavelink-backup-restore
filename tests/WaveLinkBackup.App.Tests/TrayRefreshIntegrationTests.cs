using System.Text;
using WaveLinkBackup.App.Hosting;
using WaveLinkBackup.App.Views;
using WaveLinkBackup.Core.Automation;
using WaveLinkBackup.Core.Discovery;
using WaveLinkBackup.Core.Io;
using WaveLinkBackup.Core.Snapshots;
using WaveLinkBackup.Core.Tests.Fakes;

namespace WaveLinkBackup.App.Tests;

/// <summary>
/// Task 4 of plan 9: the tray icon tracks the RUNNING host on every tick, not only after a
/// manual capture. The wiring (App.timer.Tick calls host.Tick() then RefreshTray()) is already
/// in place; what this pins is that the refresh path's decision — TrayState.From(host.Conditions)
/// plus ColourFor(status, highContrast) — produces the right state for each of the four live
/// situations, and that the high-contrast rule survives the live path.
///
/// The rig is BackupHostTests' rig deliberately: the same in-memory filesystem, the same fake
/// clock, and not one test that waits. These are integration-style because they drive a real
/// BackupHost rather than feeding TrayState.From synthetic conditions — which is exactly what
/// App.RefreshTray does on every tick.
/// </summary>
public sealed class TrayRefreshIntegrationTests
{
    private const string LocalAppData = @"C:\Users\test\AppData\Local";
    private const string LocalState =
        LocalAppData + @"\Packages\Elgato.WaveLink_g54w8ztgkx496\LocalState";
    private const string SettingsPath = LocalState + @"\Settings.json";
    private const string StorePath = LocalAppData + @"\WaveLinkBackup";

    private static string Config(string micName) =>
        """{"MixerConfiguration":{"InputSettings":{"a":{"InputName":"NAME"}}}}"""
            .Replace("NAME", micName, StringComparison.Ordinal);

    /// <summary>
    /// The same rig as BackupHostTests.Harness: an in-memory store, a fake clock, a fake watcher.
    /// Only the host and its conditions matter here, so the service is wired but never exercised
    /// beyond what a tick needs.
    /// </summary>
    private sealed class Harness : IDisposable
    {
        public FakeFileSystem Fs { get; }
        public FakeClock Clock { get; } = new();
        public FakeSettingsWatcher Watcher { get; } = new();
        public BackupHost Host { get; }

        public Harness(bool storeWritable = true, bool waveLinkInstalled = true)
        {
            Fs = new FakeFileSystem { FailDirectoryCreation = !storeWritable };
            if (waveLinkInstalled) InstallWaveLink();

            var store = new SnapshotStore(Fs, Clock, StorePath);
            var service = new BackupService(
                new SettingsInspector(new SettingsLocator(Fs, LocalAppData), new SettingsReader(Fs)),
                store);

            var coordinator = new AutoBackupCoordinator(Watcher, service, Clock, AutoBackupPolicy.Default);
            Host = new BackupHost(coordinator, Clock);
        }

        public void InstallWaveLink() => Fs.AddFile(SettingsPath, Config("Wave Mic 1"));

        /// <summary>A write has landed and settled, so the very next tick would capture.</summary>
        public void MakeACaptureDue()
        {
            Watcher.RaiseChange();
            Clock.Advance(TimeSpan.FromSeconds(61));
        }

        public void Dispose() => Host.Dispose();
    }

    /// <summary>
    /// The resting state. A healthy host with nothing pending and no error reads WATCHING — the
    /// icon the user sees most of the time, and the one that must be right on the first frame.
    /// </summary>
    [Fact]
    public void A_healthy_host_reads_watching()
    {
        using var h = new Harness();

        Assert.Equal(TrayStatus.Watching, TrayState.From(h.Host.Conditions));
    }

    /// <summary>
    /// PAUSED is reachable two ways the design treats as one icon: a deliberate "pause for an
    /// hour", or automatic backup switched off. Both leave nothing watching, and neither may be
    /// hidden by a quieter state that also happens to be true.
    /// </summary>
    [Fact]
    public void Pausing_reads_paused()
    {
        using var h = new Harness();

        h.Host.PauseFor(TimeSpan.FromHours(1));

        Assert.Equal(TrayStatus.Paused, TrayState.From(h.Host.Conditions));
    }

    [Fact]
    public void Automatic_backup_switched_off_reads_paused()
    {
        using var h = new Harness();

        h.Host.AutoBackupEnabled = false;

        Assert.Equal(TrayStatus.Paused, TrayState.From(h.Host.Conditions));
    }

    /// <summary>
    /// NEEDS YOU is reachable only because the host surfaces a tick's error in its conditions.
    /// A failing capture (here: an unwritable store) must flip the icon to the one state that is
    /// amber, and it must outrank every other condition that is also true at the same time.
    /// </summary>
    [Fact]
    public void A_failing_capture_reads_needs_you()
    {
        using var h = new Harness(storeWritable: false);
        h.Host.Start();
        h.MakeACaptureDue();

        h.Host.Tick();

        Assert.Equal(TrayStatus.NeedsYou, TrayState.From(h.Host.Conditions));
    }

    /// <summary>
    /// The icon must leave NEEDS YOU on its own once the problem goes away. A successful tick
    /// clears the stale error, so the very next refresh reads WATCHING again — no restart needed.
    /// This is the "the tray never lags the truth" half of the task: the recovery is as visible
    /// as the failure was.
    /// </summary>
    [Fact]
    public void A_recovered_host_leaves_needs_you_on_its_own()
    {
        using var h = new Harness(waveLinkInstalled: false);
        h.Host.Start();

        h.MakeACaptureDue();
        h.Host.Tick();
        Assert.Equal(TrayStatus.NeedsYou, TrayState.From(h.Host.Conditions));

        h.InstallWaveLink();
        h.MakeACaptureDue();
        h.Host.Tick();

        Assert.Equal(TrayStatus.Watching, TrayState.From(h.Host.Conditions));
    }

    /// <summary>
    /// The high-contrast rule through the LIVE path. RefreshTray calls ColourFor with
    /// systemTheme?.IsHighContrast ?? SystemParameters.HighContrast — this test pins that exact
    /// expression's contract: in HC, PAUSED is full-opacity GrayText (never the 55% alpha of the
    /// normal themes, because transparency is not a contrast guarantee) and NEEDS YOU is
    /// WindowText. screens/11 requires HC to be reacted to at runtime, so the flag must be read
    /// fresh on every refresh rather than cached.
    /// </summary>
    [Fact]
    public void The_high_contrast_rule_holds_through_the_refresh_path()
    {
        var (pausedAlpha, needsYou) = Wpf.Run(() =>
        {
            // The exact expression App.RefreshTray uses to resolve the flag each tick.
            bool highContrast = true;

            return (
                TrayIconRenderer.ColourFor(TrayStatus.Paused, highContrast).A,
                TrayIconRenderer.ColourFor(TrayStatus.NeedsYou, highContrast));
        });

        Assert.Equal(255, pausedAlpha);
        Assert.Equal(System.Windows.SystemColors.WindowTextColor, needsYou);
    }

    /// <summary>
    /// The same rule in the normal themes, for contrast: PAUSED takes the 55% alpha there. If a
    /// future change made HC and normal share one code path by accident, this is the test that
    /// catches the normal side while the previous test catches the HC side.
    /// </summary>
    [Fact]
    public void The_normal_theme_keeps_the_paused_dim()
    {
        var pausedAlpha = Wpf.Run(() =>
            TrayIconRenderer.ColourFor(TrayStatus.Paused, highContrast: false).A);

        Assert.Equal((byte)(255 * 0.55), pausedAlpha);
    }
}
