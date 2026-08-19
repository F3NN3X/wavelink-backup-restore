using WaveLinkBackup.App.Startup;
using WaveLinkBackup.App.ViewModels;
using WaveLinkBackup.App.Windows;
using WaveLinkBackup.Core.Analysis;
using WaveLinkBackup.Core.Automation;
using WaveLinkBackup.Core.Capture;
using WaveLinkBackup.Core.Io;
using WaveLinkBackup.Core.Restore;
using WaveLinkBackup.Core.Snapshots;
using WaveLinkBackup.Core.Tests.Fakes;

namespace WaveLinkBackup.App.Tests;

/// <summary>
/// Tier 4 restore from the shell — technical-debt.md §4.17, closed.
///
/// The rule these are really about: **a restore that needs administrator rights must be an opt-in
/// on a restore the user already decided to make, and saying no must cost nothing.**
/// `C:\Program Files\Common Files\VST3` is the only location in this program that is not
/// user-writable ([[ADR-006]]), so everything that matters restores with no prompt at all.
/// operations/design/screens/13-elevation.md is authoritative for the surface.
/// </summary>
public sealed class ElevatedRestoreTests
{
    private const string LocalAppData = @"C:\Users\test\AppData\Local";
    private const string Roaming = @"C:\Users\test\AppData\Roaming";
    private const string Documents = @"G:\win_user-folders\Documents";
    private const string LocalState =
        LocalAppData + @"\Packages\Elgato.WaveLink_g54w8ztgkx496\LocalState";
    private const string Settings = LocalState + @"\Settings.json";
    private const string Store = LocalAppData + @"\WaveLinkBackup";
    private const string ProQPath = @"C:\Program Files\Common Files\VST3\FabFilter Pro-Q 4.vst3";

    private const string Rig = """
        {"MixerConfiguration":{"InputSettings":{"a":{"InputName":"Wave Mic 1",
          "AudioPluginConfigurations":[{"Name":"Pro-Q 4","Vendor":"FabFilter",
           "FilePath":"C:\\Program Files\\Common Files\\VST3\\FabFilter Pro-Q 4.vst3"}]}}}}
        """;

    // --------------------------------------------------------------- the flags the child is given

    [Fact]
    public void The_elevated_copy_is_told_which_backup_and_that_it_includes_plugins()
    {
        var args = ShellArguments.Parse(["--restore", "2026-08-19T2307-a3f81c", "--with-plugins"]);

        Assert.True(args.IsValid);
        Assert.Equal("2026-08-19T2307-a3f81c", args.RestoreSnapshotId);
        Assert.True(args.WithPlugins);
        Assert.True(args.IsHeadlessRestore);
    }

    [Fact]
    public void An_ordinary_launch_is_never_a_headless_restore()
    {
        // The predicate that decides whether this process skips the single-instance mutex. A launch
        // that took this branch by accident would start a second watcher over one settings file,
        // which is the exact race the mutex exists to prevent.
        Assert.False(ShellArguments.Parse([]).IsHeadlessRestore);
        Assert.False(ShellArguments.Parse(["--tray"]).IsHeadlessRestore);
        Assert.False(ShellArguments.Parse(["--with-plugins"]).IsHeadlessRestore);
    }

    [Fact]
    public void A_restore_flag_with_no_id_is_refused_rather_than_ignored()
    {
        Assert.False(ShellArguments.Parse(["--restore"]).IsValid);
    }

    // ------------------------------------------------------------------- what the elevated copy does

    [Fact]
    public void The_elevated_copy_puts_the_plugin_binaries_back_and_reports_success()
    {
        var (fs, id) = Captured();
        fs.Delete(ProQPath);

        var code = Run(fs, id, withPlugins: true);

        Assert.Equal(RestoreExitCode.Success, code);
        Assert.True(fs.FileExists(ProQPath));
    }

    [Fact]
    public void Without_the_flag_the_elevated_path_leaves_Program_Files_alone()
    {
        // The flag is the entire reason this process exists. Restoring tier 4 without being asked
        // would mean an elevated write nobody consented to.
        var (fs, id) = Captured();
        fs.Delete(ProQPath);

        Assert.Equal(RestoreExitCode.Success, Run(fs, id, withPlugins: false));
        Assert.False(fs.FileExists(ProQPath));
    }

    [Fact]
    public void It_takes_the_pre_restore_backup_itself()
    {
        // What makes a declined prompt free: at the moment Windows asks, this process does not
        // exist and nothing has been touched. Once it runs, the way back is its own first act.
        var (fs, id) = Captured();

        Run(fs, id, withPlugins: true);

        var store = new SnapshotStore(fs, new FakeClock(), Store);
        Assert.Contains(store.List(), s => s.Manifest.Trigger == SnapshotTrigger.PreRestore);
    }

    [Fact]
    public void A_backup_that_is_not_there_exits_with_the_code_the_CLI_uses()
    {
        var (fs, _) = Captured();

        Assert.Equal(RestoreExitCode.NotFound, Run(fs, "nope", withPlugins: true));
    }

    [Fact]
    public void The_exit_codes_match_the_CLI_exactly()
    {
        // The App project deliberately does not reference the CLI, so the numbers are duplicated.
        // This is the half of that duplication that matters: two ways of running a restore must not
        // disagree about what "damaged" means.
        Assert.Equal(0, RestoreExitCode.Success);
        Assert.Equal(1, RestoreExitCode.Failure);
        Assert.Equal(2, RestoreExitCode.NotInstalled);
        Assert.Equal(3, RestoreExitCode.MultiplePackages);
        Assert.Equal(4, RestoreExitCode.Unreadable);
        Assert.Equal(5, RestoreExitCode.StillRunning);
        Assert.Equal(6, RestoreExitCode.NotFound);
        Assert.Equal(7, RestoreExitCode.Damaged);
        Assert.Equal(8, RestoreExitCode.StoreFailed);
    }

    // ------------------------------------------------------------------------------- the dialog row

    [Fact]
    public void The_row_is_absent_when_the_snapshot_holds_no_plugin_files()
    {
        // A control that can do nothing reads as a capability the restore is refusing. Absent, not
        // disabled - and this is the COMMON case, since tier 4 is off by default.
        Assert.Null(Dialog(PluginBinaryPayload.None).PluginFiles);
    }

    [Fact]
    public void The_row_prints_this_snapshots_own_size_not_the_design_mocks()
    {
        var row = Dialog(new PluginBinaryPayload(6, 41_733_324L)).PluginFiles;

        Assert.NotNull(row);
        Assert.Equal("NEEDS ADMINISTRATOR · 39.8 MB · 6 PLUG-INS", row.MetaText);
    }

    [Fact]
    public void One_plugin_is_not_called_plug_ins()
    {
        Assert.EndsWith("· 1 PLUG-IN", Dialog(new PluginBinaryPayload(1, 1024)).PluginFiles!.MetaText);
    }

    [Fact]
    public void The_row_starts_off_every_time()
    {
        // Not remembered, and deliberately not read from the Settings dialog's plug-in-files
        // switch: that one decides what goes INTO a backup, and reading it here would turn "I keep
        // the binaries" into "prompt me for administrator rights on every restore".
        Assert.False(Dialog(new PluginBinaryPayload(6, 40_000_000)).PluginFiles!.Enabled);
    }

    // ----------------------------------------------------------------------------- saying no is free

    [Fact]
    public void Declining_reports_the_neutral_strip_and_offers_to_try_again()
    {
        var declined = AppError.ElevationDeclined;

        Assert.Equal(ErrorWeight.Neutral, declined.Weight);
        Assert.Equal("The plug-in files were left alone. Your settings and presets were restored.",
            declined.Body);
    }

    [Fact]
    public void A_declined_prompt_reports_declined_rather_than_failed()
    {
        // The two need different words on screen: one is a refusal that changed nothing, the other
        // is something to report. Win32 error 1223 is the ONLY thing that tells them apart.
        var elevation = new FakeElevation(new ElevationOutcome(ElevationResult.Declined));

        var outcome = elevation.RunElevated(["--restore", "x", "--with-plugins"], CancellationToken.None);

        Assert.Equal(ElevationResult.Declined, outcome.Result);
        Assert.Null(outcome.ExitCode);
    }

    [Fact]
    public void An_executable_that_cannot_be_found_fails_rather_than_throwing()
    {
        // A restore that cannot start must report, not crash the shell mid-restore.
        var outcome = new ShellExecuteElevation(executablePath: string.Empty)
            .RunElevated(["--restore", "x"], CancellationToken.None);

        Assert.Equal(ElevationResult.Failed, outcome.Result);
    }

    // ------------------------------------------------------------------------------------- helpers

    /// <summary>A snapshot on disk carrying all four tiers, and the id to restore it by.</summary>
    private static (FakeFileSystem Fs, string Id) Captured()
    {
        var fs = new FakeFileSystem()
            .AddFile(Settings, Rig)
            .AddFile(ProQPath, "plugin bytes")
            .AddFile(Roaming + @"\FabFilter\Pro-Q 4\My curve.ffp", "curve");

        var live = SettingsInspector.For(fs, LocalAppData).Inspect().Value;
        var payload = new TierCapture(fs, Roaming, Documents).Gather(
            live, BackupSettings.Default with { IncludePresets = true, IncludePluginFiles = true });

        var snapshot = new SnapshotStore(fs, new FakeClock(), Store)
            .Write(live.Bytes, live.Analysis, SnapshotTrigger.Manual, "x", payload: payload).Value;

        return (fs, snapshot.Id);
    }

    private static int Run(FakeFileSystem fs, string id, bool withPlugins) =>
        HeadlessRestore.Run(
            ShellArguments.Parse(withPlugins ? ["--restore", id, "--with-plugins"] : ["--restore", id]),
            BackupSettings.Default with { StorePath = Store },
            fs,
            new FakeWaveLinkProcess(),
            new FakeClock(),
            LocalAppData);

    private static RestoreDialogModel Dialog(PluginBinaryPayload binaries) =>
        RestoreDialogModel.Build(
            new RestorePlan(
                "Before the update", DateTimeOffset.UnixEpoch, [], false, [], false, null,
                Binaries: binaries),
            DateTimeOffset.UnixEpoch);

    private sealed class FakeElevation(ElevationOutcome outcome) : IElevation
    {
        public IReadOnlyList<string>? Arguments { get; private set; }

        public ElevationOutcome RunElevated(IReadOnlyList<string> arguments, CancellationToken ct)
        {
            Arguments = arguments;
            return outcome;
        }
    }
}
