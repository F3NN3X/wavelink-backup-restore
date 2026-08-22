namespace WaveLinkBackup.App.Updates;

/// <summary>
/// One release, as a feed describes it. Everything the design's available-update row prints
/// (screens/12: <c>YOU HAVE 1.2.3 · RELEASED 12 AUG · 4.1 MB</c>) plus what it takes to fetch.
/// </summary>
/// <param name="Sha256">
/// The published checksum of <see cref="DownloadUrl"/>, lowercase hex, or null when the release
/// did not publish one.
///
/// **This is integrity, not authenticity.** It proves the bytes that arrived are the bytes the
/// release names — which catches a truncated download, a corrupted mirror and a tampered
/// transport. It does NOT prove who published the release, because whoever controls the release
/// controls the checksum beside it. Only code signing answers that, and this app is not signed
/// yet. An update with no checksum is refused rather than installed hopefully.
/// </param>
/// <param name="NotesUrl">Where "What changed" goes. Null hides that button rather than inventing a link.</param>
public sealed record UpdateRelease(
    Version Version,
    DateTimeOffset? PublishedAt,
    string DownloadUrl,
    long SizeBytes,
    string? Sha256,
    string? NotesUrl);

/// <summary>What a check found. Four outcomes, matching the four states the design draws.</summary>
public enum UpdateCheckResult
{
    /// <summary>Never checked in this process, and no remembered answer.</summary>
    Unknown,

    /// <summary>Checked, and this is the newest there is.</summary>
    UpToDate,

    /// <summary>Checked, and something newer exists.</summary>
    UpdateAvailable,

    /// <summary>The check itself failed — no network, a rate limit, a feed that changed shape.</summary>
    CheckFailed,
}

/// <param name="Release">Present only on <see cref="UpdateCheckResult.UpdateAvailable"/>.</param>
/// <param name="FailureDetail">
/// Present only on <see cref="UpdateCheckResult.CheckFailed"/>: the mono line under the message.
/// A failed CHECK is neutral, like a failed update — nothing about the running app is un-whole.
/// </param>
public sealed record UpdateCheck(
    UpdateCheckResult Result,
    UpdateRelease? Release = null,
    string? FailureDetail = null)
{
    public static UpdateCheck Unknown { get; } = new(UpdateCheckResult.Unknown);

    public static UpdateCheck UpToDate { get; } = new(UpdateCheckResult.UpToDate);

    public static UpdateCheck Failed(string detail) =>
        new(UpdateCheckResult.CheckFailed, FailureDetail: detail);

    public static UpdateCheck Available(UpdateRelease release) =>
        new(UpdateCheckResult.UpdateAvailable, release);
}

/// <summary>
/// Where to look, and what to look for. Configuration rather than constants because this repo has
/// no remote yet (technical-debt.md §5's rule, applied to a value that looks like it could be
/// hard-coded and cannot): the feed is a fact about a deployment, not about the program.
/// </summary>
/// <param name="AssetSuffix">
/// Which asset in a release is the one to download — matched on the end of the file name, so a
/// versioned name like <c>WaveLinkBackup-1.4.0-app-win-x64.zip</c> still resolves. The suffix
/// must be specific enough to pick out the APP archive when the release also carries the CLI's
/// <c>WaveLinkBackup-CLI-X.Y.Z-win-x64.zip</c>: both end in <c>win-x64.zip</c>, so matching on
/// that alone would make the updater install the wrong bytes.
/// </param>
public sealed record UpdateSource(string Owner, string Repository, string AssetSuffix = "app-win-x64.zip")
{
    /// <summary>
    /// Whether a feed can be built from this at all. False disables the whole UPDATES section —
    /// a "Check now" button that cannot reach anything is worse than no button.
    /// </summary>
    public bool IsConfigured => Owner.Length > 0 && Repository.Length > 0;

    /// <summary>The releases page, for "What changed" and the manual-download fallback.</summary>
    public string ReleasesUrl => $"https://github.com/{Owner}/{Repository}/releases";

    public string LatestReleaseApiUrl =>
        $"https://api.github.com/repos/{Owner}/{Repository}/releases/latest";
}

/// <summary>
/// Turns the shapes a release tag comes in into a <see cref="System.Version"/>.
///
/// Its own type because the failure is silent and expensive: a tag this cannot read makes every
/// release look older than the running build, and the app would report itself up to date forever.
/// </summary>
public static class ReleaseVersion
{
    /// <summary>
    /// <c>v1.4.0</c>, <c>1.4.0</c>, <c>1.4</c> and <c>1.4.0-beta.2</c> all read; anything else is
    /// null. Pre-release suffixes are DROPPED rather than ordered — this app has no pre-release
    /// channel, and inventing an ordering for one would decide, silently, whether a beta counts as
    /// newer than the release it precedes.
    /// </summary>
    public static Version? Parse(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag)) return null;

        var text = tag.Trim();
        if (text.StartsWith('v') || text.StartsWith('V')) text = text[1..];

        // Cut a pre-release or build suffix: 1.4.0-beta.2, 1.4.0+abc123.
        var cut = text.IndexOfAny(['-', '+']);
        if (cut >= 0) text = text[..cut];

        return Version.TryParse(text, out var version) ? Normalise(version) : null;
    }

    /// <summary>
    /// The running build's version, with its unset components zeroed the same way a parsed tag's
    /// are — so <c>1.4.0</c> from a tag and <c>1.4.0.0</c> from the assembly compare equal rather
    /// than the assembly always looking newer.
    /// </summary>
    public static Version Current =>
        Normalise(typeof(ReleaseVersion).Assembly.GetName().Version ?? new Version(0, 0));

    /// <summary>Printed as the design writes it: <c>1.2.3</c>, never <c>1.2.3.0</c>.</summary>
    public static string Display(Version version) =>
        version.Build > 0 || version.Major > 0 || version.Minor > 0
            ? $"{version.Major}.{version.Minor}.{version.Build}"
            : version.ToString();

    private static Version Normalise(Version version) =>
        new(version.Major, version.Minor, Math.Max(version.Build, 0), 0);
}
