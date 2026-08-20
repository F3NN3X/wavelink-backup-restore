namespace WaveLinkBackup.Cli.CommandLine;

public enum Verb
{
    None,
    Help,
    Version,
    Backup,
    List,
    Restore,
    Rename,
    Delete,
    Verify,
    Prune,
    EmptyTrash,
    Watch,

    /// <summary>
    /// Everything the app knows about itself, redacted, on stdout. The CLI's half of
    /// technical-debt.md §6: a user asked for a diagnostic on a headless machine has to be given
    /// one, or they will paste their settings file instead.
    /// </summary>
    Diagnostics,
}

/// <summary>
/// A parsed command line. Produced by <see cref="CommandLineParser"/>, consumed by the
/// dispatcher - nothing in between touches the console or the filesystem.
/// </summary>
/// <param name="Error">Non-null when parsing failed. The command is then unusable.</param>
/// <param name="WithPlugins">
/// Restore the plug-in binaries too. Off unless asked for: it is the only thing in this program
/// that writes outside the user's own folders, and `C:\Program Files\Common Files\VST3` needs
/// administrator rights ([[ADR-006]]).
/// </param>
public sealed record ParsedCommand(
    Verb Verb,
    IReadOnlyList<string> Arguments,
    string? Name = null,
    string? SettingsPath = null,
    string? StorePath = null,
    int? KeepCount = null,
    int? IntervalSeconds = null,
    bool AssumeYes = false,
    bool Json = false,
    bool WithPlugins = false,
    string? Error = null)
{
    public static ParsedCommand Failed(string error) =>
        new(Verb.None, [], Error: error);

    public bool IsValid => Error is null;
}
