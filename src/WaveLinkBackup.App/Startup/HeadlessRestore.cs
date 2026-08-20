using WaveLinkBackup.Core.Abstractions;
using WaveLinkBackup.Core.Automation;
using WaveLinkBackup.Core.Capture;
using WaveLinkBackup.Core.Discovery;
using WaveLinkBackup.Core.Io;
using WaveLinkBackup.Core.Process;
using WaveLinkBackup.Core.Restore;
using WaveLinkBackup.Core.Results;
using WaveLinkBackup.Core.Snapshots;

namespace WaveLinkBackup.App.Startup;

/// <summary>
/// The exit codes the elevated copy reports back through, mirroring the CLI's so the two ways of
/// running a restore cannot disagree about what "damaged" means.
///
/// Deliberately NOT a reference to <c>WaveLinkBackup.Cli.CommandLine.ExitCode</c>: the App project
/// does not depend on the CLI and should not start now, over five integers. The values are asserted
/// equal by a test, which is the cheap half of the duplication and the half that matters.
/// </summary>
public static class RestoreExitCode
{
    public const int Success = 0;
    public const int Failure = 1;
    public const int NotInstalled = 2;
    public const int MultiplePackages = 3;
    public const int Unreadable = 4;
    public const int StillRunning = 5;
    public const int NotFound = 6;
    public const int Damaged = 7;
    public const int StoreFailed = 8;

    public static int For(CoreError error) => error switch
    {
        WaveLinkNotInstalled => NotInstalled,
        MultiplePackagesFound => MultiplePackages,
        SettingsUnreadable or MalformedSettings => Unreadable,
        WaveLinkStillRunning => StillRunning,
        SnapshotNotFound => NotFound,
        NotASnapshot or SnapshotCorrupted or MalformedManifest or UnsupportedSnapshotSchema => Damaged,
        StoreUnavailable => StoreFailed,
        _ => Failure,
    };
}

/// <summary>
/// One restore, then exit — what the elevated copy of this app does and the only thing it does
/// (operations/design/screens/13-elevation.md).
///
/// It exists because `C:\Program Files\Common Files\VST3` is not user-writable and the running
/// shell has no way to become administrator in place. Rather than elevate the whole app all day
/// for a thing it does rarely, the shell starts a second copy of itself with `--restore &lt;id&gt;
/// --with-plugins`, Windows draws its own consent dialog, and this runs.
///
/// **No window, no tray, no watcher, no single-instance mutex.** This process is one operation,
/// not a second instance; see <see cref="ShellArguments.IsHeadlessRestore"/> for why the mutex in
/// particular has to be skipped.
///
/// **It takes the pre-restore snapshot itself**, because it is the process doing the write. That
/// is what makes a declined UAC prompt cost nothing: at the moment Windows asks, this process does
/// not exist yet and nothing has been touched.
/// </summary>
public static class HeadlessRestore
{
    /// <param name="fileSystem">Injected so a test can run the whole path without a real disk.</param>
    /// <param name="process">Injected for the same reason: no test may close the real Wave Link.</param>
    public static int Run(
        ShellArguments arguments,
        BackupSettings settings,
        IFileSystem fileSystem,
        IWaveLinkProcess process,
        IClock clock,
        string? localAppData = null)
    {
        if (arguments.RestoreSnapshotId is not { Length: > 0 } id) return RestoreExitCode.Failure;

        var inspector = SettingsInspector.For(
            fileSystem, localAppData ?? SettingsLocator.SystemLocalAppData);

        var live = inspector.Inspect(settings.ChosenWaveLinkPath);
        if (!live.IsSuccess) return RestoreExitCode.For(live.Error!);

        var store = new SnapshotStore(fileSystem, clock, settings.StorePath);

        var orchestrator = new RestoreOrchestrator(
            fileSystem, process, store,
            new SettingsWriter(fileSystem, process),
            new SettingsReader(fileSystem),
            // The copy the user comes back to should be as complete as any other, so the
            // pre-restore snapshot obeys the same tier settings a normal capture does.
            settingsInspection => TierCapture.For(fileSystem).Gather(settingsInspection, settings));

        var result = orchestrator.Restore(
            id, live.Value, new RestoreOptions(Presets: true, PluginBinaries: arguments.WithPlugins));

        // A restore that ran is a success even when Wave Link's log could not confirm it. The
        // shell that started this process re-reads the store and shows the outcome; reporting an
        // unconfirmed restore as a failed one here would make it show the wrong strip for a write
        // that went through.
        return result.IsSuccess ? RestoreExitCode.Success : RestoreExitCode.For(result.Error!);
    }
}
