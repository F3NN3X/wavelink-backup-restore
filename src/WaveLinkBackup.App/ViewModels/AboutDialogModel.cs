using WaveLinkBackup.App.Updates;

namespace WaveLinkBackup.App.ViewModels;

/// <summary>
/// The about dialog's entire content, computed once at construction: the app's name and version,
/// one sentence of what it is, the licence line, the not-affiliated line, and the links.
///
/// The version comes from <see cref="ReleaseVersion.Current"/> - the same source the updater
/// compares against - so the number shown here can never drift from the one in the UPDATES
/// section or the release tag. Reading it from the assembly rather than hard-coding a string is
/// what keeps that true: there is one place the version is written (Directory.Build.props) and
/// everywhere else reads it.
/// </summary>
public sealed record AboutDialogModel(
    string Title,
    string AppName,
    string Version,
    string Description,
    string LicenceLine,
    string AffiliationLine,
    string ReleasesLabel,
    string? ReleasesUrl,
    string RepositoryLabel,
    string? RepositoryUrl)
{
    /// <summary>
    /// The running build's facts. The links are read from the environment rather than compiled in -
    /// the same rule as App.ReleaseSource (technical-debt.md §5): they are facts about a DEPLOYMENT,
    /// and absent means the link hides itself rather than pointing at a wrong repository.
    /// </summary>
    public static AboutDialogModel Build() => new(
        Title: "About",
        AppName: "Wave Link Backup",
        Version: ReleaseVersion.Display(ReleaseVersion.Current),
        Description: "A free, open-source Windows utility that snapshots and restores your Elgato " +
            "Wave Link setup - every channel, routing assignment and effect chain.",
        LicenceLine: "MIT licence",
        AffiliationLine: "Not affiliated with, endorsed by, or supported by Elgato. \"Wave Link\" is " +
            "their trademark; this is an independent utility for its users.",
        ReleasesLabel: "Releases",
        ReleasesUrl: ReleaseLink("WLBACKUP_RELEASES_URL"),
        RepositoryLabel: "Source code",
        RepositoryUrl: ReleaseLink("WLBACKUP_REPO_URL"));

    private static string? ReleaseLink(string variable)
    {
        var value = Environment.GetEnvironmentVariable(variable);
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }
}
