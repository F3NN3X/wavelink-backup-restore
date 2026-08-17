using WaveLinkBackup.Core.Automation;

namespace WaveLinkBackup.App.Startup;

/// <summary>
/// The shell's command line. Deliberately NOT shared with the CLI's parser: the two have
/// almost nothing in common — no verbs here, and the CLI has no --tray — and coupling them
/// would mean every future CLI verb widened the shell's surface (ADR-009 took the same view of
/// hand-rolled parsing over a library).
///
/// Flags apply to THIS RUN and are never written back
/// (operations/design/screens/08-settings-persistence.md).
/// </summary>
/// <param name="Error">Non-null when parsing failed. The shell shows it and exits.</param>
public sealed record ShellArguments(
    bool StartInTray = false,
    string? StorePath = null,
    string? SettingsPath = null,
    int? KeepCount = null,
    string? Error = null)
{
    public bool IsValid => Error is null;

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
