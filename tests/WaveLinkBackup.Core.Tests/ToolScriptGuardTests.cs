using System.Reflection;
using System.Text.RegularExpressions;

namespace WaveLinkBackup.Core.Tests;

/// <summary>
/// The share-mode rule, extended to the PowerShell in <c>tools/</c>.
///
/// <para>
/// <see cref="SourceGuardTests.Core_never_reads_a_file_without_choosing_a_share_mode"/> scans
/// <c>*.cs</c> and nothing else, so the first script written against a live install repeated the
/// exact mistake that guard exists to prevent: <c>[IO.File]::ReadAllText</c> on
/// <c>Settings.json</c>, which throws "used by another process" the moment Wave Link is open -
/// which is always, on the rig those scripts are for. It failed on the first run rather than in
/// CI, because CI has no Wave Link to lock the file. That is the same asymmetry the C# guard was
/// written for, and the same argument for catching it by scan.
/// </para>
///
/// <para>
/// See <c>_docs/knowledge-base/gotchas/capture-fails-while-wave-link-is-running.md</c>.
/// </para>
/// </summary>
public sealed class ToolScriptGuardTests
{
    private static string ToolsRoot => Assembly.GetExecutingAssembly()
        .GetCustomAttributes<AssemblyMetadataAttribute>()
        .Single(a => a.Key == "ToolsSourceRoot").Value!;

    private static IEnumerable<(string Path, string Text)> ToolScripts()
    {
        if (!Directory.Exists(ToolsRoot)) yield break;

        foreach (var file in Directory.EnumerateFiles(ToolsRoot, "*.ps1", SearchOption.AllDirectories))
        {
            yield return (file, File.ReadAllText(file));
        }
    }

    /// <summary>
    /// Block comments first, then line comments. Same reasoning as the C# scanner: the rules are
    /// about code, not about the prose explaining the rules - and this file's own scripts explain
    /// the rule in a comment that names the very pattern being banned.
    /// </summary>
    internal static string StripComments(string script) =>
        Regex.Replace(
            Regex.Replace(script, @"<#.*?#>", "", RegexOptions.Singleline),
            @"#.*$", "", RegexOptions.Multiline);

    [Fact]
    public void Tool_scripts_never_read_a_file_without_choosing_a_share_mode()
    {
        var regex = new Regex(@"\[(System\.)?IO\.File\]::(ReadAll(Text|Bytes|Lines)|OpenRead)\b");

        var offenders = ToolScripts()
            .SelectMany(s => regex.Matches(StripComments(s.Text))
                .Select(m => $"  {Path.GetFileName(s.Path)}: {m.Value}"))
            .ToArray();

        Assert.True(offenders.Length == 0,
            "Scripts in tools/ read files a running Wave Link holds open, so they must open a " +
            "FileStream with FileShare.ReadWrite | FileShare.Delete - the share mode Core's " +
            $"FileSystem.OpenShared uses. Found:{Environment.NewLine}{string.Join(Environment.NewLine, offenders)}");
    }

    [Fact]
    public void A_tool_script_that_reads_settings_json_names_a_share_mode()
    {
        // The rule above bans one spelling of the mistake. This one is the positive form, and it
        // is the load-bearing half: whatever API a script reaches for, if it touches Settings.json
        // it has to have thought about the lock.
        var offenders = ToolScripts()
            .Where(s => s.Text.Contains("Settings.json", StringComparison.OrdinalIgnoreCase))
            .Where(s => !s.Text.Contains("FileShare", StringComparison.Ordinal))
            .Select(s => $"  {Path.GetFileName(s.Path)}")
            .ToArray();

        Assert.True(offenders.Length == 0,
            "A script that reads Settings.json must request a share mode; Wave Link holds that " +
            $"file open. Found:{Environment.NewLine}{string.Join(Environment.NewLine, offenders)}");
    }

    [Fact]
    public void The_scan_is_not_vacuous()
    {
        // A guard that scans nothing passes forever. If tools/ is emptied or the metadata path
        // breaks, this is what says so rather than three green ticks over an empty set.
        Assert.True(Directory.Exists(ToolsRoot), $"ToolsSourceRoot does not exist: {ToolsRoot}");
        Assert.NotEmpty(ToolScripts());
    }

    [Fact]
    public void The_scanner_actually_matches_something_it_should_reject()
    {
        // A guard nobody has seen fail is a guard nobody knows works. This pins the regex and
        // the comment stripper against the exact text of the mistake that prompted them.
        var regex = new Regex(@"\[(System\.)?IO\.File\]::(ReadAll(Text|Bytes|Lines)|OpenRead)\b");

        Assert.Matches(regex, "$text = [IO.File]::ReadAllText($Path)");
        Assert.Matches(regex, "$b = [System.IO.File]::ReadAllBytes($Path)");
        Assert.DoesNotMatch(regex, "$stream = [IO.FileStream]::new($Path, [IO.FileMode]::Open)");

        // The banned spelling inside a comment is prose, not code.
        Assert.DoesNotMatch(regex, StripComments("# [IO.File]::ReadAllText($Path) is banned"));
        Assert.DoesNotMatch(regex, StripComments("<#\n[IO.File]::ReadAllText($Path)\n#>"));
    }
}
