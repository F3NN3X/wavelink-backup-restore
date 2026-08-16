using System.Text.RegularExpressions;

namespace WaveLinkBackup.Core.Analysis;

/// <param name="ParseFailed">Wave Link rejected the settings file and regenerated defaults.</param>
/// <param name="CreatedNewBackup">Wave Link wrote its own backup - the signature of a reset.</param>
/// <param name="AppliedNames">Friendly names Wave Link applied, in order of first appearance.</param>
public sealed record RestoreVerdict(
    bool ParseFailed,
    bool CreatedNewBackup,
    IReadOnlyList<string> AppliedNames)
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

        return new RestoreVerdict(
            ParseFailed: logText.Contains("Failed to parse settings file", StringComparison.OrdinalIgnoreCase),
            CreatedNewBackup: logText.Contains("Created a new backup file", StringComparison.OrdinalIgnoreCase),
            AppliedNames: names);
    }

    [GeneratedRegex(@"Applied saved friendly name '(?<name>[^']*)'", RegexOptions.IgnoreCase)]
    private static partial Regex AppliedFriendlyName();
}
