namespace WaveLinkBackup.App.ViewModels;

/// <summary>
/// One section of the help dialog: a heading and one or more sentences. The sections are static
/// copy about how this app behaves - no I/O, no WPF, nothing to compute. A section whose body is
/// empty renders its heading only.
/// </summary>
public sealed record HelpSection(string Heading, string Body);

/// <summary>
/// The help dialog's entire content: the sections in display order and the two links in the
/// footer. Everything here is a constant - the view binds to it and computes nothing.
///
/// The copy deliberately says WHAT happens rather than HOW (the README's own rule for this app):
/// "snapshots are kept", not "content hashes are compared". A user who wants the mechanism has
/// the documentation link at the bottom; a user who just wants to know what is safe does not need
/// it.
/// </summary>
public sealed record HelpDialogModel(
    string Title,
    IReadOnlyList<HelpSection> Sections,
    string DocumentationLabel,
    string? DocumentationUrl)
{
    /// <summary>
    /// The running build's help. Everything is static except the documentation URL, which is read
    /// from the environment rather than compiled in - the same rule as App.ReleaseSource
    /// (technical-debt.md §5): it is a fact about a DEPLOYMENT, and absent means null, which hides
    /// the link entirely rather than pointing at nothing.
    /// </summary>
    public static HelpDialogModel Build() => new(
        "How this app works",
        [
            new("What gets backed up",
                "Wave Link keeps its entire setup - every channel, routing assignment and effect " +
                "chain - in one settings file. This app copies that file into the backup folder. " +
                "Optionally it also captures the VST3 plug-ins your setup references, so a restore " +
                "does not leave you with dead channels."),

            new("How snapshots are kept",
                "One snapshot per distinct configuration: identical settings are stored once, and " +
                "nothing is ever overwritten. Snapshots you take yourself are never deleted " +
                "automatically, and neither are the copies taken just before a restore. Everything " +
                "stays on this computer - nothing is sent anywhere."),

            new("How restoring works",
                "Restoring closes Wave Link, replaces its settings file with the one from the " +
                "snapshot you chose, and reopens it. A snapshot is always taken automatically " +
                "before a restore, so there is always a way back. If a plug-in your setup uses is " +
                "missing, the app tells you exactly which one to install."),

            new("The tray icon",
                "Right-click the tray icon for everything: take a backup now, open the backup " +
                "folder, pause backups for an hour, or change settings. The menu's first line shows " +
                "when the last backup was taken and how it went."),
        ],
        "Documentation",
        ReleaseLink("WLBACKUP_REPO_URL"));

    private static string? ReleaseLink(string variable)
    {
        var value = Environment.GetEnvironmentVariable(variable);
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }
}
