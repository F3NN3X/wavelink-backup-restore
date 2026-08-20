using System.Text;
using WaveLinkBackup.Core.Analysis;
using WaveLinkBackup.Core.Automation;
using WaveLinkBackup.Core.Capture;
using WaveLinkBackup.Core.Discovery;
using WaveLinkBackup.Core.Io;
using WaveLinkBackup.Core.Restore;
using WaveLinkBackup.Core.Results;
using WaveLinkBackup.Core.Snapshots;
using WaveLinkBackup.Core.Tests.Fakes;

namespace WaveLinkBackup.Core.Tests;

/// <summary>
/// The assembled sequence. Phase 1 proved each primitive; these tests are about ORDER, which
/// is load-bearing at every step. See knowledge-base/recipes/restore-a-settings-file-safely.md
/// </summary>
public sealed class RestoreOrchestratorTests
{
    private const string LocalAppData = @"C:\Users\test\AppData\Local";
    private const string LocalState =
        LocalAppData + @"\Packages\Elgato.WaveLink_g54w8ztgkx496\LocalState";
    private const string SettingsPath = LocalState + @"\Settings.json";
    private const string StorePath = @"C:\Users\test\AppData\Local\WaveLinkBackup";

    private const string Healthy = """
        {"Update":{"LastUpdateVersion":"3.2.9"},
         "MixerConfiguration":{"InputSettings":{
           "a":{"InputName":"Wave Mic 1","AudioPluginConfigurations":[{"Name":"Pro-Q 4"}]},
           "b":{"InputName":"Voice","AudioPluginConfigurations":[]},
           "c":{"InputName":"Browser","AudioPluginConfigurations":[]}}}}
        """;

    private const string Collapsed = """
        {"Update":{"LastUpdateVersion":"3.3.0.4108"},
         "MixerConfiguration":{"InputSettings":{
           "x":{"InputName":"Elgato Wave:3"},"y":{"InputName":"System"}}}}
        """;

    private sealed class Harness
    {
        public FakeFileSystem Fs { get; } = new();
        public FakeWaveLinkProcess Process { get; } = new() { Running = true };
        public FakeClock Clock { get; } = new();
        public SnapshotStore Store { get; private set; } = null!;
        public RestoreOrchestrator Orchestrator { get; set; } = null!;

        public Harness(string liveJson = Collapsed)
        {
            Fs.AddFile(SettingsPath, liveJson);
            Fs.AddFile(LocalState + @"\Logs\newest.log",
                "Applied saved friendly name 'Wave Mic 1'\nVERSION 3.3.0.4108 (Beta)");

            Store = new SnapshotStore(Fs, Clock, StorePath);
            Orchestrator = new RestoreOrchestrator(
                Fs, Process, Store, new SettingsWriter(Fs, Process), new SettingsReader(Fs));
        }

        // The explicit constructor, not SettingsInspector(IFileSystem) — that convenience
        // overload resolves %LOCALAPPDATA% from the real environment, which no fake tree has.
        public SettingsInspection Live() =>
            new SettingsInspector(new SettingsLocator(Fs, LocalAppData), new SettingsReader(Fs))
                .Inspect().Value;

        public Snapshot AddSnapshot(string json, string name)
        {
            var bytes = Encoding.UTF8.GetBytes(json);
            var snapshot = Store.Write(bytes, SettingsAnalysis.Analyse(bytes).Value,
                SnapshotTrigger.Manual, name).Value;
            Clock.Advance(TimeSpan.FromMinutes(1));
            return snapshot;
        }
    }

    // -------------------------------------------------------------- the tiers (phase 6)

    private const string Roaming = @"C:\Users\test\AppData\Roaming";

    /// <summary>On another drive on purpose - see TierCaptureTests.Documents.</summary>
    private const string Documents = @"G:\win_user-folders\Documents";
    private const string ProQPath = @"C:\Program Files\Common Files\VST3\FabFilter Pro-Q 4.vst3";

    private const string WithPlugin = """
        {"Update":{"LastUpdateVersion":"3.2.9"},
         "MixerConfiguration":{"InputSettings":{
           "a":{"InputName":"Wave Mic 1","AudioPluginConfigurations":[
             {"Name":"Pro-Q 4","Vendor":"FabFilter",
              "FilePath":"C:\\Program Files\\Common Files\\VST3\\FabFilter Pro-Q 4.vst3"}]}}}}
        """;

    /// <summary>A rig whose snapshot carries all four tiers, restored through the real sequence.</summary>
    private static (Harness H, Snapshot Snapshot) FourTierRig()
    {
        var h = new Harness(WithPlugin);
        h.Fs.AddFile(Roaming + @"\FabFilter\Pro-Q 4\My curve.ffp", "curve");
        h.Fs.AddFile(ProQPath, "plugin bytes");

        var capture = new TierCapture(h.Fs, Roaming, Documents);
        var orchestrator = new RestoreOrchestrator(
            h.Fs, h.Process, h.Store, new SettingsWriter(h.Fs, h.Process), new SettingsReader(h.Fs),
            live => capture.Gather(live, BackupSettings.Default with { IncludePluginFiles = true }),
            Roaming, Documents);

        var live = h.Live();
        var payload = capture.Gather(live, BackupSettings.Default with { IncludePluginFiles = true });
        var snapshot = h.Store.Write(
            live.Bytes, live.Analysis, SnapshotTrigger.Manual, "Four tiers", payload: payload).Value;

        h.Clock.Advance(TimeSpan.FromMinutes(1));
        h.Orchestrator = orchestrator;

        return (h, snapshot);
    }

    [Fact]
    public void A_restore_puts_the_presets_back_and_leaves_Program_Files_alone()
    {
        // The privilege model in one test: the default restore rewrites the settings and the
        // presets, and never touches the one location that would need administrator rights.
        var (h, snapshot) = FourTierRig();
        h.Fs.DeleteDirectory(Roaming + @"\FabFilter");
        h.Fs.Delete(ProQPath);

        var result = h.Orchestrator.Restore(snapshot.Id, h.Live());

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value.Tiers!.PresetFilesRestored);
        Assert.False(result.Value.Tiers.NeedsElevation);
        Assert.True(h.Fs.FileExists(Roaming + @"\FabFilter\Pro-Q 4\My curve.ffp"));
        Assert.False(h.Fs.FileExists(ProQPath));
    }

    [Fact]
    public void Asking_for_the_plugin_files_puts_them_back_too()
    {
        var (h, snapshot) = FourTierRig();
        h.Fs.Delete(ProQPath);

        var result = h.Orchestrator.Restore(
            snapshot.Id, h.Live(), new RestoreOptions(PluginBinaries: true));

        Assert.Equal(1, result.Value.Tiers!.PluginFilesRestored);
        Assert.Equal("plugin bytes"u8.ToArray(), h.Fs.Read(ProQPath));
    }

    [Fact]
    public void A_denied_plugin_write_does_not_fail_the_restore_that_already_happened()
    {
        // By the time tier 4 runs, the settings file - the product - is written. Rolling the
        // restore back over a Program Files permission would be the worse outcome by far.
        var (h, snapshot) = FourTierRig();
        h.Fs.WriteFailures[ProQPath] = new Queue<Exception>([new UnauthorizedAccessException("denied")]);

        var result = h.Orchestrator.Restore(
            snapshot.Id, h.Live(), new RestoreOptions(PluginBinaries: true));

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.Tiers!.NeedsElevation);
        Assert.Equal(WithPlugin, Encoding.UTF8.GetString(h.Fs.Read(SettingsPath)));
    }

    [Fact]
    public void The_pre_restore_snapshot_carries_the_same_tiers_the_capture_would()
    {
        // It is what the user comes back to, so it must not be thinner than the backup they are
        // escaping from.
        var (h, snapshot) = FourTierRig();

        var result = h.Orchestrator.Restore(snapshot.Id, h.Live());

        Assert.Equal(
            ["settings", "plugin-manifest", "presets", "plugins"],
            result.Value.PreRestoreSnapshot.Manifest.Tiers);
    }

    // -------------------------------------------------------------- the happy path

    [Fact]
    public void A_restore_replaces_the_settings_and_relaunches()
    {
        var h = new Harness();
        var good = h.AddSnapshot(Healthy, "Before 3.3 beta");

        var result = h.Orchestrator.Restore(good.Id, h.Live());

        Assert.True(result.IsSuccess);
        Assert.Equal(Healthy, Encoding.UTF8.GetString(h.Fs.Read(SettingsPath)));
        Assert.True(result.Value.Relaunched);
        Assert.Equal("Elgato.WaveLink_g54w8ztgkx496", h.Process.LaunchedPackageFamily);
    }

    [Fact]
    public void A_restore_always_takes_a_pre_restore_snapshot_first()
    {
        // Automatic, never a checkbox, and there is no parameter to skip it. It is what makes
        // the destructive button safe to press.
        var h = new Harness();
        var good = h.AddSnapshot(Healthy, "good");

        var outcome = h.Orchestrator.Restore(good.Id, h.Live()).Value;

        Assert.Equal(SnapshotTrigger.PreRestore, outcome.PreRestoreSnapshot.Manifest.Trigger);
        Assert.Equal(RestoreOrchestrator.PreRestoreName, outcome.PreRestoreSnapshot.Manifest.DisplayName);

        // And it captured the state we were escaping FROM, not the one we restored.
        Assert.Equal(2, outcome.PreRestoreSnapshot.Manifest.InputCount);
    }

    [Fact]
    public void The_pre_restore_snapshot_is_taken_before_Wave_Link_is_closed()
    {
        // Ordering: it wants the live state, and closing first would risk the app rewriting
        // the file on the way out.
        var h = new Harness();
        var good = h.AddSnapshot(Healthy, "good");

        Assert.Equal(0, h.Process.CloseAttempts);
        var outcome = h.Orchestrator.Restore(good.Id, h.Live()).Value;

        Assert.Equal(1, h.Process.CloseAttempts);
        Assert.True(h.Fs.FileExists(outcome.PreRestoreSnapshot.SettingsPath));
    }

    [Fact]
    public void The_restore_is_confirmed_from_the_log_not_the_file()
    {
        var h = new Harness();
        var good = h.AddSnapshot(Healthy, "good");

        var outcome = h.Orchestrator.Restore(good.Id, h.Live()).Value;

        Assert.True(outcome.Confirmed);
        Assert.Equal("3.3.0.4108", outcome.Verdict!.Version);
        Assert.Equal("Beta", outcome.Verdict.Channel);
    }

    [Fact]
    public void A_log_that_reports_a_parse_failure_means_unconfirmed()
    {
        var h = new Harness();
        h.Fs.WriteBytes(LocalState + @"\Logs\newest.log",
            Encoding.UTF8.GetBytes("Failed to parse settings file\nCreated a new backup file"));
        var good = h.AddSnapshot(Healthy, "good");

        var outcome = h.Orchestrator.Restore(good.Id, h.Live()).Value;

        Assert.False(outcome.Confirmed);
        Assert.True(outcome.Verdict!.ParseFailed);
    }

    [Fact]
    public void An_unreadable_log_means_unconfirmed_not_failed()
    {
        var h = new Harness();
        h.Fs.DeleteDirectory(LocalState + @"\Logs");
        var good = h.AddSnapshot(Healthy, "good");

        var result = h.Orchestrator.Restore(good.Id, h.Live());

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value.Verdict);
        Assert.False(result.Value.Confirmed);
    }

    [Fact]
    public void Confirmed_tracks_the_verdicts_success_including_a_null_verdict()
    {
        // RestoreOutcome.Confirmed is a computed projection over Verdict.Succeeded. The three
        // branches: confirmed (Succeeded true), unconfirmed (Succeeded false), and null verdict
        // (log unreadable) - the last being the one the orchestrator tests above never assert
        // directly, only through outcome.Confirmed == false.
        var h = new Harness();
        var good = h.AddSnapshot(Healthy, "good");

        var confirmed = h.Orchestrator.Restore(good.Id, h.Live()).Value;
        Assert.True(confirmed.Confirmed);
        Assert.True(confirmed.Verdict!.Succeeded);

        // Unreadable log: Confirmed must be false without a NullReferenceException.
        h.Fs.DeleteDirectory(LocalState + @"\Logs");
        var unconfirmed = h.Orchestrator.Restore(good.Id, h.Live()).Value;
        Assert.False(unconfirmed.Confirmed);
        Assert.Null(unconfirmed.Verdict);
    }

    // -------------------------------------------------------------- refusals

    [Fact]
    public void A_snapshot_that_does_not_exist_is_refused_before_anything_happens()
    {
        var h = new Harness();

        Assert.IsType<SnapshotNotFound>(h.Orchestrator.Restore("nope", h.Live()).Error);
        Assert.Equal(0, h.Process.CloseAttempts);
        Assert.Empty(h.Store.List());
    }

    [Fact]
    public void A_corrupted_snapshot_is_refused_before_Wave_Link_is_touched()
    {
        var h = new Harness();
        var good = h.AddSnapshot(Healthy, "good");
        h.Fs.WriteBytes(good.SettingsPath, Encoding.UTF8.GetBytes(Collapsed));

        Assert.IsType<SnapshotCorrupted>(h.Orchestrator.Restore(good.Id, h.Live()).Error);
        Assert.Equal(0, h.Process.CloseAttempts);
        Assert.Equal(Collapsed, Encoding.UTF8.GetString(h.Fs.Read(SettingsPath)));
    }

    [Fact]
    public void A_process_that_will_not_exit_blocks_the_write_and_leaves_the_pre_restore_snapshot()
    {
        // The recovery property: a restore that dies after step 3 still leaves the way back.
        var h = new Harness();
        h.Process.StaysRunningAfterClose = true;
        var good = h.AddSnapshot(Healthy, "good");

        var result = h.Orchestrator.Restore(good.Id, h.Live());

        Assert.IsType<WaveLinkStillRunning>(result.Error);
        Assert.Equal(Collapsed, Encoding.UTF8.GetString(h.Fs.Read(SettingsPath)));
        Assert.Contains(h.Store.List(),
            s => s.Manifest.Trigger == SnapshotTrigger.PreRestore);
    }

    [Fact]
    public void A_restore_from_an_explicit_path_succeeds_without_relaunching()
    {
        // technical-debt.md 2.2: a user we cannot auto-locate gets a route, and is told
        // plainly that they must start Wave Link themselves.
        var fs = new FakeFileSystem();
        fs.AddFile(@"D:\rescued\Settings.json", Collapsed);
        var process = new FakeWaveLinkProcess { Running = true };
        var store = new SnapshotStore(fs, new FakeClock(), StorePath);
        var orchestrator = new RestoreOrchestrator(
            fs, process, store, new SettingsWriter(fs, process), new SettingsReader(fs));

        var live = SettingsInspector.For(fs, LocalAppData).Inspect(@"D:\rescued\Settings.json").Value;
        var bytes = Encoding.UTF8.GetBytes(Healthy);
        var good = store.Write(bytes, SettingsAnalysis.Analyse(bytes).Value, SnapshotTrigger.Manual, "g").Value;

        var result = orchestrator.Restore(good.Id, live);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.Relaunched);
        Assert.Null(process.LaunchedPackageFamily);
        Assert.Equal(Healthy, Encoding.UTF8.GetString(fs.Read(@"D:\rescued\Settings.json")));
    }

    // -------------------------------------------------------------- plan

    [Fact]
    public void The_plan_describes_what_would_change_without_changing_anything()
    {
        var h = new Harness();
        var good = h.AddSnapshot(Healthy, "Before 3.3 beta");

        var plan = h.Orchestrator.Plan(good.Id, h.Live());

        Assert.True(plan.IsSuccess);
        Assert.Equal("Before 3.3 beta", plan.Value.SnapshotName);
        Assert.Equal(0, h.Process.CloseAttempts);
        Assert.Equal(Collapsed, Encoding.UTF8.GetString(h.Fs.Read(SettingsPath)));

        var inputs = plan.Value.Rows.Single(r => r.Label == "Inputs");
        Assert.Equal("2", inputs.Now);
        Assert.Equal("3", inputs.After);
        Assert.True(inputs.Changes);
    }

    [Fact]
    public void The_plan_warns_when_the_versions_differ()
    {
        var h = new Harness();
        var good = h.AddSnapshot(Healthy, "older version");

        var plan = h.Orchestrator.Plan(good.Id, h.Live()).Value;

        Assert.NotNull(plan.VersionWarning);
        Assert.Contains("3.2.9", plan.VersionWarning, StringComparison.Ordinal);
        Assert.True(plan.HasWarnings);
    }

    [Fact]
    public void The_plan_warns_when_a_restore_would_drop_inputs()
    {
        var h = new Harness(liveJson: Healthy);
        var bad = h.AddSnapshot(Collapsed, "the bad one");

        var plan = h.Orchestrator.Plan(bad.Id, h.Live()).Value;

        Assert.True(plan.LosesInputs);
        Assert.Contains("Wave Mic 1", plan.InputNamesLost);
        Assert.True(plan.HasWarnings);
    }

    [Fact]
    public void An_unchanged_row_is_not_marked_as_changing()
    {
        var h = new Harness(liveJson: Healthy);
        var same = h.AddSnapshot(Healthy, "identical");

        var plan = h.Orchestrator.Plan(same.Id, h.Live()).Value;

        Assert.All(plan.Rows, r => Assert.False(r.Changes));
        Assert.False(plan.HasWarnings);
    }

    // ------------------ whether a tier 4 restore actually needs elevating (technical-debt.md §7.5)

    /// <summary>
    /// The measurement that replaced an assumption. The app used to elevate whenever the user
    /// opted in, inferring "needs administrator" from the fact that plug-ins usually live under
    /// `C:\Program Files`. That is wrong on any machine where a plug-in installer has loosened the
    /// folder's ACL — which is common, and is true of the reference rig, where
    /// `C:\Program Files\Common Files\VST3` carries an explicit `Everyone:(F)` ACE.
    /// </summary>
    [Fact]
    public void A_writable_plugin_folder_means_no_elevation_is_needed()
    {
        var (h, snapshot) = FourTierRig();

        var plan = h.Orchestrator!.Plan(snapshot.Id, h.Live()).Value;

        Assert.True(plan.BinaryPayload.Any);
        Assert.False(
            plan.BinaryPayload.NeedsElevation,
            "Nothing marked the plug-in folder unwritable, so the restore must not ask for rights.");
    }

    [Fact]
    public void An_unwritable_plugin_folder_still_needs_elevation()
    {
        var (h, snapshot) = FourTierRig();
        h.Fs.UnwritableDirectories.Add(@"C:\Program Files");

        var plan = h.Orchestrator!.Plan(snapshot.Id, h.Live()).Value;

        Assert.True(plan.BinaryPayload.NeedsElevation);
    }

    /// <summary>
    /// **Any, not all.** Elevation is one prompt for the whole restore, so a single unwritable
    /// destination needs it — there is no partial answer that helps the user.
    /// </summary>
    [Fact]
    public void One_unwritable_destination_among_several_needs_elevation()
    {
        var (h, snapshot) = FourTierRig();

        // The rig's own plug-in stays writable; a second one does not.
        h.Fs.UnwritableDirectories.Add(@"C:\Program Files\Common Files\VST3\Locked");

        var plan = h.Orchestrator!.Plan(snapshot.Id, h.Live()).Value;

        // Only one plug-in is captured here, and it is writable, so this is still false...
        Assert.False(plan.BinaryPayload.NeedsElevation);

        // ...and marking the folder that plug-in is actually in flips it.
        h.Fs.UnwritableDirectories.Add(@"C:\Program Files\Common Files\VST3");

        Assert.True(h.Orchestrator!.Plan(snapshot.Id, h.Live()).Value.BinaryPayload.NeedsElevation);
    }

    /// <summary>
    /// A snapshot with no plug-in binaries reports no elevation, so the row is absent AND cannot
    /// contribute a spurious prompt to a settings-only restore.
    /// </summary>
    [Fact]
    public void A_snapshot_without_plugin_binaries_needs_no_elevation()
    {
        Assert.False(PluginBinaryPayload.None.NeedsElevation);
        Assert.False(PluginBinaryPayload.None.Any);
    }
}
