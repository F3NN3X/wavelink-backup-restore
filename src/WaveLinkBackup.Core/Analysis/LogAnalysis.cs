using System.Text.RegularExpressions;

namespace WaveLinkBackup.Core.Analysis;

/// <param name="ParseFailed">Wave Link rejected the settings file and regenerated defaults.</param>
/// <param name="CreatedNewBackup">Wave Link wrote its own backup - the signature of a reset.</param>
/// <param name="AppliedNames">Friendly names Wave Link applied, in order of first appearance.</param>
/// <param name="Version">From the log's startup banner, e.g. "3.3.0.4108". Null if absent.</param>
/// <param name="Channel">
/// "Beta" when the banner says so, else null. Worth capturing separately: beta channels ship
/// new validators, and 3.3.0.4108 Beta rejected a file 3.2.9 accepted. SPEC.md 5.
/// </param>
public sealed record RestoreVerdict(
    bool ParseFailed,
    bool CreatedNewBackup,
    IReadOnlyList<string> AppliedNames,
    string? Version = null,
    string? Channel = null)
{
    /// <summary>
    /// Success is the ABSENCE of a parse failure PLUS the presence of applied names.
    /// Absence of evidence is not success - a log with neither means the restore cannot be
    /// confirmed, which is different from confirming it worked.
    /// </summary>
    public bool Succeeded => !ParseFailed && AppliedNames.Count > 0;
}

/// <summary>
/// The only trustworthy confirmation that a restore worked. A mixer that looks correct can
/// be a freshly generated default: five plausible channel names are not evidence of
/// anything. SPEC.md 4.
///
/// Pure - finding the newest log file is the IO half, and lives elsewhere.
/// </summary>
public static partial class LogAnalysis
{
    public static RestoreVerdict Verify(string logText)
    {
        if (string.IsNullOrWhiteSpace(logText)) return new RestoreVerdict(false, false, []);

        var names = new List<string>();
        foreach (Match match in AppliedFriendlyName().Matches(logText))
        {
            var name = match.Groups["name"].Value;
            if (!names.Contains(name, StringComparer.Ordinal)) names.Add(name);
        }

        // Wave Link's startup banner looks like:
        //     APPLICATION   Elgato Wave Link
        //     VERSION       3.3.0.4108 (Beta)
        var banner = VersionBanner().Match(logText);

        return new RestoreVerdict(
            ParseFailed: logText.Contains("Failed to parse settings file", StringComparison.OrdinalIgnoreCase),
            CreatedNewBackup: logText.Contains("Created a new backup file", StringComparison.OrdinalIgnoreCase),
            AppliedNames: names,
            Version: banner.Success ? banner.Groups["version"].Value : null,
            Channel: banner.Success && banner.Groups["channel"].Success
                ? banner.Groups["channel"].Value
                : null);
    }

    [GeneratedRegex(@"Applied saved friendly name '(?<name>[^']*)'", RegexOptions.IgnoreCase)]
    private static partial Regex AppliedFriendlyName();

    [GeneratedRegex(@"VERSION\s+(?<version>\d+(?:\.\d+)+)(?:\s*\((?<channel>[^)]+)\))?",
                    RegexOptions.IgnoreCase)]
    private static partial Regex VersionBanner();
}
