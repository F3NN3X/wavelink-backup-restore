using System.Text;
using WaveLinkBackup.Core.Automation;
using WaveLinkBackup.Core.Discovery;
using WaveLinkBackup.Core.Io;
using WaveLinkBackup.Core.Snapshots;
using WaveLinkBackup.Core.Tests.Fakes;

namespace WaveLinkBackup.Core.Tests;

/// <summary>
/// The watcher, the debounce, the rate limit, dedup and pruning, wired together.
///
/// NOT ONE TEST HERE WAITS. Everything runs off FakeClock and a fake watcher; the suite
/// completes in milliseconds. A test that actually slept for the 60-second debounce would be
/// a test nobody runs.
/// </summary>
public sealed class AutoBackupCoordinatorTests
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
        public FakeFileSystem Fs { get; } = new();
        public FakeClock Clock { get; } = new();
        public FakeSettingsWatcher Watcher { get; } = new();
        public SnapshotStore Store { get; }
        public BackupService Service { get; }
        public AutoBackupCoordinator Coordinator { get; }

        public Harness(int keepCount = SnapshotRetention.DefaultKeepCount)
        {
            Fs.AddFile(SettingsPath, Config("Wave Mic 1"));

            Store = new SnapshotStore(Fs, Clock, StorePath);
            Service = new BackupService(
                new SettingsInspector(new SettingsLocator(Fs, LocalAppData), new SettingsReader(Fs)),
                Store, keepCount);
            Coordinator = new AutoBackupCoordinator(Watcher, Service, Clock, AutoBackupPolicy.Default);
        }

        /// <summary>Changes the settings file so the next capture is not a duplicate.</summary>
        public void EditSettings(string micName) =>
            Fs.WriteBytes(SettingsPath, Encoding.UTF8.GetBytes(Config(micName)));

        public void Dispose() => Coordinator.Dispose();
    }

    // ------------------------------------------------------------------ debounce

    [Fact]
    public void A_burst_of_five_writes_in_ten_seconds_produces_one_snapshot()
    {
        using var h = new Harness();
        h.Coordinator.Start();

        for (var i = 0; i < 5; i++)
        {
            h.Watcher.RaiseChange();
            h.Clock.Advance(TimeSpan.FromSeconds(2));
        }

        Assert.Equal(CaptureDecision.Waiting, h.Coordinator.Tick().Decision);

        h.Clock.Advance(TimeSpan.FromSeconds(61));

        Assert.True(h.Coordinator.Tick().Captured);
        Assert.Single(h.Store.List());
    }

    [Fact]
    public void A_tick_with_no_write_does_nothing()
    {
        using var h = new Harness();
        h.Coordinator.Start();

        Assert.Equal(CaptureDecision.NothingPending, h.Coordinator.Tick().Decision);
        Assert.Empty(h.Store.List());
    }

    [Fact]
    public void The_pending_write_is_cleared_after_a_capture()
    {
        using var h = new Harness();
        h.Watcher.RaiseChange();
        h.Clock.Advance(TimeSpan.FromSeconds(61));
        h.Coordinator.Tick();

        Assert.Null(h.Coordinator.PendingSince);
        Assert.Equal(CaptureDecision.NothingPending, h.Coordinator.Tick().Decision);
    }

    // ------------------------------------------------------------------ rate limit

    [Fact]
    public void Two_changes_thirty_minutes_apart_produce_one_automatic_snapshot()
    {
        using var h = new Harness();

        h.Watcher.RaiseChange();
        h.Clock.Advance(TimeSpan.FromSeconds(61));
        Assert.True(h.Coordinator.Tick().Captured);

        h.EditSettings("Wave Mic 2");
        h.Clock.Advance(TimeSpan.FromMinutes(30));
        h.Watcher.RaiseChange();
        h.Clock.Advance(TimeSpan.FromSeconds(61));

        Assert.Equal(CaptureDecision.RateLimited, h.Coordinator.Tick().Decision);
        Assert.Single(h.Store.List());
    }

    [Fact]
    public void A_change_after_the_hour_is_captured()
    {
        using var h = new Harness();

        h.Watcher.RaiseChange();
        h.Clock.Advance(TimeSpan.FromSeconds(61));
        h.Coordinator.Tick();

        h.EditSettings("Wave Mic 2");
        h.Clock.Advance(TimeSpan.FromMinutes(61));
        h.Watcher.RaiseChange();
        h.Clock.Advance(TimeSpan.FromSeconds(61));

        Assert.True(h.Coordinator.Tick().Captured);
        Assert.Equal(2, h.Store.List().Count);
    }

    [Fact]
    public void A_manual_backup_during_the_rate_limit_window_still_writes()
    {
        // The user asked. Refusing with "not yet" makes the button feel broken.
        using var h = new Harness();

        h.Watcher.RaiseChange();
        h.Clock.Advance(TimeSpan.FromSeconds(61));
        h.Coordinator.Tick();

        h.Clock.Advance(TimeSpan.FromMinutes(1));
        var manual = h.Service.BackUpNow("I want one now");

        Assert.True(manual.IsSuccess);
        Assert.Equal(2, h.Store.List().Count);
    }

    // ------------------------------------------------------------------ dedup

    [Fact]
    public void Identical_content_produces_no_second_snapshot()
    {
        // Wave Link rewrites Settings.json on every launch with near-identical bytes.
        using var h = new Harness();

        h.Watcher.RaiseChange();
        h.Clock.Advance(TimeSpan.FromSeconds(61));
        h.Coordinator.Tick();

        h.Clock.Advance(TimeSpan.FromMinutes(61));
        h.Watcher.RaiseChange();
        h.Clock.Advance(TimeSpan.FromSeconds(61));

        var tick = h.Coordinator.Tick();

        Assert.Equal(CaptureDecision.Capture, tick.Decision);
        Assert.False(tick.Captured);
        Assert.True(tick.Capture!.SkippedAsDuplicate);
        Assert.Single(h.Store.List());
    }

    [Fact]
    public void A_manual_backup_of_identical_content_DOES_write()
    {
        // The dedup exception. Success is meant to be quiet - the new row appearing is the
        // only confirmation - which only works if a row appears.
        using var h = new Harness();
        h.Service.BackUpNow("first");

        var second = h.Service.BackUpNow("second, identical");

        Assert.True(second.IsSuccess);
        Assert.Equal(2, h.Store.List().Count);
    }

    [Fact]
    public void A_skipped_duplicate_does_not_restart_the_rate_limit()
    {
        // Otherwise a launch-time rewrite would mask a genuine edit made moments later.
        using var h = new Harness();

        h.Watcher.RaiseChange();
        h.Clock.Advance(TimeSpan.FromSeconds(61));
        h.Coordinator.Tick();                                   // captured at T+61s

        h.Clock.Advance(TimeSpan.FromMinutes(61));
        h.Watcher.RaiseChange();
        h.Clock.Advance(TimeSpan.FromSeconds(61));
        Assert.False(h.Coordinator.Tick().Captured);            // duplicate, skipped

        h.EditSettings("Wave Mic 2");
        h.Clock.Advance(TimeSpan.FromMinutes(2));
        h.Watcher.RaiseChange();
        h.Clock.Advance(TimeSpan.FromSeconds(61));

        // Only ~3 minutes since the skip, but the last real capture was over an hour ago.
        Assert.True(h.Coordinator.Tick().Captured);
    }

    // ------------------------------------------------------------------ retention

    [Fact]
    public void Automatic_snapshots_prune_to_the_keep_count()
    {
        using var h = new Harness(keepCount: 3);

        for (var i = 0; i < 5; i++)
        {
            h.EditSettings($"Mic {i}");
            h.Watcher.RaiseChange();
            h.Clock.Advance(TimeSpan.FromMinutes(61));
            h.Coordinator.Tick();
        }

        Assert.Equal(3, h.Store.List().Count);
    }

    [Fact]
    public void Pruning_never_removes_a_manual_snapshot()
    {
        using var h = new Harness(keepCount: 1);

        h.Service.BackUpNow("keep me");
        h.Clock.Advance(TimeSpan.FromMinutes(1));
        h.Service.BackUpNow("keep me too");

        for (var i = 0; i < 4; i++)
        {
            h.EditSettings($"Mic {i}");
            h.Watcher.RaiseChange();
            h.Clock.Advance(TimeSpan.FromMinutes(61));
            h.Coordinator.Tick();
        }

        var all = h.Store.List();
        Assert.Equal(2, all.Count(s => s.Manifest.Trigger == SnapshotTrigger.Manual));
        Assert.Equal(1, all.Count(s => s.Manifest.Trigger == SnapshotTrigger.Automatic));
    }

    [Fact]
    public void Pruning_removes_the_oldest_automatic_snapshot_not_the_newest()
    {
        using var h = new Harness(keepCount: 2);

        foreach (var name in (string[])["oldest", "middle", "newest"])
        {
            h.EditSettings(name);
            h.Watcher.RaiseChange();
            h.Clock.Advance(TimeSpan.FromMinutes(61));
            h.Coordinator.Tick();
        }

        var remaining = h.Store.List().Select(s => s.Manifest.InputNames[0]).ToArray();

        Assert.Equal(["newest", "middle"], remaining);
    }

    // ------------------------------------------------------------------ shutdown

    [Fact]
    public void Capture_on_shutdown_ignores_the_debounce_and_the_rate_limit()
    {
        // The original incident happened during an update, while the app was restarting.
        using var h = new Harness();

        h.Watcher.RaiseChange();
        h.Clock.Advance(TimeSpan.FromSeconds(61));
        h.Coordinator.Tick();

        h.EditSettings("changed right before quitting");
        h.Clock.Advance(TimeSpan.FromSeconds(2));

        Assert.True(h.Coordinator.CaptureOnShutdown().Captured);
        Assert.Equal(2, h.Store.List().Count);
    }

    [Fact]
    public void Capture_on_shutdown_still_deduplicates_so_a_quiet_exit_costs_nothing()
    {
        using var h = new Harness();
        h.Service.BackUpNow("already saved");

        var result = h.Coordinator.CaptureOnShutdown();

        Assert.False(result.Captured);
        Assert.True(result.Capture!.SkippedAsDuplicate);
        Assert.Single(h.Store.List());
    }

    // ------------------------------------------------------------------ lifecycle

    [Fact]
    public void Starting_and_stopping_drive_the_watcher()
    {
        using var h = new Harness();

        h.Coordinator.Start();
        Assert.True(h.Watcher.Started);
        Assert.True(h.Coordinator.IsRunning);

        h.Coordinator.Stop();
        Assert.False(h.Watcher.Started);
        Assert.False(h.Coordinator.IsRunning);
    }

    [Fact]
    public void Disposing_the_coordinator_disposes_the_watcher_and_unsubscribes()
    {
        var h = new Harness();
        h.Coordinator.Dispose();

        Assert.True(h.Watcher.Disposed);

        // No longer listening: a late event must not resurrect a pending write.
        h.Watcher.RaiseChange();
        Assert.Null(h.Coordinator.PendingSince);
    }

    [Fact]
    public void Disposing_twice_is_harmless()
    {
        var h = new Harness();
        h.Coordinator.Dispose();
        h.Coordinator.Dispose();
    }

    [Fact]
    public void A_missed_event_is_reconciled_by_the_next_one()
    {
        // FileSystemWatcher can drop events under load. That is latency, not data loss: the
        // next write reconciles by content hash.
        using var h = new Harness();

        h.EditSettings("changed while we were not listening");   // no event raised
        h.Clock.Advance(TimeSpan.FromMinutes(5));

        Assert.Equal(CaptureDecision.NothingPending, h.Coordinator.Tick().Decision);

        h.Watcher.RaiseChange();
        h.Clock.Advance(TimeSpan.FromSeconds(61));

        Assert.True(h.Coordinator.Tick().Captured);
        Assert.Equal("changed while we were not listening", h.Store.List()[0].Manifest.InputNames[0]);
    }

    [Fact]
    public void A_capture_that_cannot_find_Wave_Link_reports_the_decision_without_throwing()
    {
        var fs = new FakeFileSystem();
        var clock = new FakeClock();
        using var watcher = new FakeSettingsWatcher();
        var store = new SnapshotStore(fs, clock, StorePath);
        var service = new BackupService(
            new SettingsInspector(new SettingsLocator(fs, LocalAppData), new SettingsReader(fs)), store);
        using var coordinator = new AutoBackupCoordinator(watcher, service, clock);

        watcher.RaiseChange();
        clock.Advance(TimeSpan.FromSeconds(61));

        var tick = coordinator.Tick();

        Assert.Equal(CaptureDecision.Capture, tick.Decision);
        Assert.False(tick.Captured);
        Assert.Empty(store.List());
    }
}
