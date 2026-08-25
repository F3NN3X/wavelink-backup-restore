using WaveLinkBackup.App.Updates;
using WaveLinkBackup.Core.Automation;

namespace WaveLinkBackup.App.Startup;

/// <summary>
/// The shell's command line. Deliberately NOT shared with the CLI's parser: the two have
/// almost nothing in common, no verbs here, and the CLI has no --tray, and coupling them
/// would mean every future CLI verb widened the shell's surface (ADR-009 took the same view of
/// hand-rolled parsing over a library).
///
/// Flags apply to THIS RUN and are never written back
/// (the settings-persistence spec).
/// </summary>
/// <param name="Error">Non-null when parsing failed. The shell shows it and exits.</param>
/// <param name="RestoreSnapshotId">
/// Set only by the elevated copy this app starts of ITSELF, to put tier 4's plug-in files back
/// (the elevation spec). Not a documented flag and not in any help text:
/// it names one snapshot and means "do this restore and exit", which is not something a user has
/// any reason to type.
/// </param>
/// <param name="WithPlugins">
/// Whether that restore includes tier 4. Only meaningful alongside
/// <paramref name="RestoreSnapshotId"/>. It is the entire reason the elevated copy exists.
/// </param>
/// <param name="ApplyUpdateForProcessId">
/// Set only by the STAGED copy an update starts of itself, and for the same reason
/// <paramref name="RestoreSnapshotId"/> exists: a process cannot overwrite its own executable
/// while it is running, so a newer copy has to do the swap from outside the directory being
/// replaced. Names the process to wait for.
/// </param>
/// <param name="ApplyUpdateInstallDirectory">Which directory the staged copy replaces.</param>
public sealed record ShellArguments(
    bool StartInTray = false,
    string? StorePath = null,
    string? SettingsPath = null,
    int? KeepCount = null,
    string? Error = null,
    string? RestoreSnapshotId = null,
    bool WithPlugins = false,
    int? ApplyUpdateForProcessId = null,
    string? ApplyUpdateInstallDirectory = null)
{
    public bool IsValid => Error is null;

    /// <summary>
    /// True when this process exists to perform one restore and exit: no window, no tray, no
    /// watcher, and no single-instance mutex.
    ///
    /// The mutex is the important omission. It is `Local\` and per-user, so the elevated copy runs
    /// as the SAME user and would find the mutex already taken by the window that started it, see
    /// itself as a second launch, and exit without restoring anything. That is correct behaviour
    /// for a second launch and wrong for this one, because this process is not a second instance.
    /// it is one operation, and the race the mutex prevents is two watchers over one settings file.
    /// </summary>
    public bool IsHeadlessRestore => RestoreSnapshotId is not null;

    /// <summary>
    /// True when this process exists to replace an install and exit.
    ///
    /// It omits the same things a headless restore does, and one more: it must NOT read or write
    /// settings, because the directory holding this copy is about to be renamed out from under it.
    /// All it does is wait, rename twice, and start the result.
    /// </summary>
    public bool IsApplyingUpdate =>
        ApplyUpdateForProcessId is not null && ApplyUpdateInstallDirectory is not null;

    private static ShellArguments Failed(string error) => new(Error: error);

    public static ShellArguments Parse(string[] args)
    {
        var result = new ShellArguments();

        for (var i = 0; i < args.Length; i++)
        {
            var flag = args[i];

            switch (flag)
            {
                case "--tray":
                    result = result with { StartInTray = true };
                    break;

                case "--store":
                    if (!TryValue(args, ref i, out var store)) return Failed("--store needs a folder.");
                    result = result with { StorePath = store };
                    break;

                case "--settings":
                    if (!TryValue(args, ref i, out var settings)) return Failed("--settings needs a path.");
                    result = result with { SettingsPath = settings };
                    break;

                case "--restore":
                    if (!TryValue(args, ref i, out var id)) return Failed("--restore needs a backup id.");
                    result = result with { RestoreSnapshotId = id };
                    break;

                case "--with-plugins":
                    result = result with { WithPlugins = true };
                    break;

                case UpdateInstaller.ApplyFlag:
                    if (!TryValue(args, ref i, out var pid)) return Failed($"{UpdateInstaller.ApplyFlag} needs a process id.");
                    if (!int.TryParse(pid, out var processId)) return Failed($"'{pid}' is not a process id.");
                    if (!TryValue(args, ref i, out var target)) return Failed($"{UpdateInstaller.ApplyFlag} needs a folder.");
                    result = result with
                    {
                        ApplyUpdateForProcessId = processId,
                        ApplyUpdateInstallDirectory = target,
                    };
                    break;

                case "--keep":
                    if (!TryValue(args, ref i, out var keep)) return Failed("--keep needs a number.");
                    if (!int.TryParse(keep, out var count)) return Failed($"'{keep}' is not a number.");
                    result = result with { KeepCount = count };
                    break;

                default:
                    // Ignoring an unknown flag is how a typo silently becomes "watch the
                    // default folder instead of the one you meant".
                    return Failed($"'{flag}' is not something this app understands.");
            }
        }

        return result;
    }

    /// <summary>Layers the flags over what the settings file said. Produces a value; saves nothing.</summary>
    public BackupSettings ApplyTo(BackupSettings settings) => settings with
    {
        StorePath = StorePath ?? settings.StorePath,
        AutoBackupKeepCount = KeepCount ?? settings.AutoBackupKeepCount,
        ChosenWaveLinkPath = SettingsPath ?? settings.ChosenWaveLinkPath,
    };

    private static bool TryValue(string[] args, ref int index, out string value)
    {
        if (index + 1 >= args.Length)
        {
            value = string.Empty;
            return false;
        }

        value = args[++index];
        return true;
    }
}
