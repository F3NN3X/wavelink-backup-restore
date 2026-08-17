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
}

/// <summary>
/// A parsed command line. Produced by <see cref="CommandLineParser"/>, consumed by the
/// dispatcher - nothing in between touches the console or the filesystem.
/// </summary>
/// <param name="Error">Non-null when parsing failed. The command is then unusable.</param>
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
    string? Error = null)
{
    public static ParsedCommand Failed(string error) =>
        new(Verb.None, [], Error: error);

    public bool IsValid => Error is null;
}
