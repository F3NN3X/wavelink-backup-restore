using System.Reflection;
using System.Text.RegularExpressions;

namespace WaveLinkBackup.Core.Tests;

/// <summary>
/// One wording guard, on the one field whose behaviour is only obvious because a comment explains
/// it.
///
/// <para>
/// <c>LastUpdateCheckUtc</c> records an attempt, not a success — "successful or not... otherwise a
/// machine that is offline for a fortnight re-checks on every tick". The App moved its automatic
/// check onto a 15-second tick and recorded the timestamp only on success, which made that
/// sentence false and would have re-tried roughly 5,700 times a day on an offline machine. The
/// comment was right and the new code was wrong.
/// </para>
///
/// <para>
/// Guarding prose is unusual here and deliberate: the rule has no runtime representation to
/// assert, so the explanation IS the specification. If someone deletes it, the next person to move
/// this check has nothing to be contradicted by.
/// </para>
/// </summary>
public sealed class SettingsDocumentationTests
{
    private static string BackupSettingsSource()
    {
        var root = Assembly.GetExecutingAssembly()
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .Single(a => a.Key == "CoreSourceRoot").Value!;

        return File.ReadAllText(Path.Combine(root, "Automation", "BackupSettings.cs"));
    }

    [Fact]
    public void The_last_check_field_still_says_it_records_failures()
    {
        var source = BackupSettingsSource();
        var comment = Regex.Match(
            source, @"<param name=""LastUpdateCheckUtc"">.*?</param>", RegexOptions.Singleline).Value;

        Assert.True(comment.Length > 0, "The LastUpdateCheckUtc param comment is gone.");
        Assert.Contains("successful or not", comment, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("every tick", comment, StringComparison.OrdinalIgnoreCase);
    }
}
