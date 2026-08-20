using System.Reflection;
using System.Text.RegularExpressions;

namespace WaveLinkBackup.Core.Tests;

/// <summary>
/// Guards 2 and 3 from the phase-1 design. Both are source scans, and both exist because the
/// bug they catch appears far from where it is introduced — one of them cannot be caught at
/// runtime in CI at all, because CI has no Wave Link running.
/// </summary>
public sealed class SourceGuardTests
{
    private static IEnumerable<(string Path, string Text)> CoreSources()
    {
        var root = Assembly.GetExecutingAssembly()
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .Single(a => a.Key == "CoreSourceRoot").Value!;

        foreach (var file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") ||
                file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
                continue;

            yield return (file, File.ReadAllText(file));
        }
    }

    private static string Offenders(string pattern)
    {
        var regex = new Regex(pattern, RegexOptions.Multiline);
        var hits = CoreSources()
            .SelectMany(s => regex.Matches(StripComments(s.Text))
                .Select(m => $"  {Path.GetFileName(s.Path)}: {m.Value}"));

        return string.Join(Environment.NewLine, hits);
    }

    // The rules are about code, not about the comments that explain the rules.
    private static string StripComments(string source) =>
        Regex.Replace(Regex.Replace(source, @"/\*.*?\*/", "", RegexOptions.Singleline),
                      @"//.*$", "", RegexOptions.Multiline);

    [Fact]
    public void Core_never_reads_a_file_without_choosing_a_share_mode()
    {
        // Settings.json is locked while Wave Link runs, so File.ReadAllBytes fails on most
        // captures. CI has no Wave Link running and therefore cannot catch this at runtime.
        // _docs/knowledge-base/gotchas/capture-fails-while-wave-link-is-running.md
        var offenders = Offenders(@"File\.ReadAll(Bytes|Text|Lines)\b|File\.OpenRead\b");

        Assert.True(offenders.Length == 0,
            $"Core must read through IFileSystem.ReadSharedBytes, which requests " +
            $"FileShare.ReadWrite | FileShare.Delete. Found:{Environment.NewLine}{offenders}");
    }

    [Fact]
    public void Core_never_uses_reflection_based_json_serialization()
    {
        // Keeps NativeAOT open for the CLI (technical-debt.md 2.4). JsonDocument, JsonNode
        // and Utf8JsonWriter are all reflection-free; JsonSerializer.Serialize<T> is not.
        var offenders = Offenders(@"JsonSerializer\.(Serialize|Deserialize)");

        Assert.True(offenders.Length == 0,
            $"Core must use JsonDocument / JsonNode / Utf8JsonWriter. " +
            $"Found:{Environment.NewLine}{offenders}");
    }

    [Fact]
    public void Core_never_writes_to_the_console()
    {
        // ADR-004: Core is headless. The watcher runs without a window.
        var offenders = Offenders(@"\bConsole\.(Write|Read|Error|Out)\b");

        Assert.True(offenders.Length == 0,
            $"Core is headless (ADR-004); shells present output. " +
            $"Found:{Environment.NewLine}{offenders}");
    }

    [Fact]
    public void The_scanner_actually_matches_something_it_should_reject()
    {
        // A guard that cannot fail is not a guard. This pins the regexes themselves.
        var regex = new Regex(@"File\.ReadAll(Bytes|Text|Lines)\b|File\.OpenRead\b");

        Assert.Matches(regex, "var b = File.ReadAllBytes(path);");
        Assert.DoesNotMatch(regex, "var b = fileSystem.ReadSharedBytes(path);");
        Assert.Equal("", StripComments("// File.ReadAllBytes(path)").Trim());
    }

    // ---------------------------------- technical-debt.md §5: numbers that are not constants

    /// <summary>
    /// The package family id is stable per Store identity and must never be assumed. SPEC.md §11
    /// and technical-debt.md §5 both say to glob <c>Elgato.WaveLink_*</c>, and this is the one
    /// entry on that list where a hard-coded value would work perfectly on the author's machine
    /// and find nothing on somebody else's.
    /// </summary>
    [Fact]
    public void Core_never_hard_codes_the_package_family_id()
    {
        var offenders = Offenders(@"g54w8ztgkx496");

        Assert.True(offenders.Length == 0,
            "The package family id is hard-coded. Glob Elgato.WaveLink_* instead:" +
            $"{Environment.NewLine}{offenders}");
    }

    /// <summary>
    /// %LOCALAPPDATA% is redirected on some corporate and OneDrive setups, so a composed path is
    /// a path that is right here and wrong there. Environment.GetFolderPath, never a string.
    /// </summary>
    [Fact]
    public void Core_never_composes_a_profile_path_from_a_string()
    {
        var offenders = Offenders(@"""[^""]*(%LOCALAPPDATA%|%APPDATA%|%USERPROFILE%)[^""]*""");

        Assert.True(offenders.Length == 0,
            "A profile path is composed from a literal. Use Environment.GetFolderPath:" +
            $"{Environment.NewLine}{offenders}");
    }

    /// <summary>
    /// "Never gate on one version; record it and warn on mismatch." 3.3.0.4108 is a beta and
    /// release users are on 3.2.9 — a comparison against either is a rule about one moment.
    /// </summary>
    [Fact]
    public void Core_never_compares_against_a_wave_link_version()
    {
        var offenders = Offenders(@"(==|!=|>=|<=|>|<)\s*""3\.[0-9.]+""");

        Assert.True(offenders.Length == 0,
            "A Wave Link version is being gated on. Record it and warn on mismatch:" +
            $"{Environment.NewLine}{offenders}");
    }

    /// <summary>
    /// The VST3 folder is a DEFAULT and is overridable. Plug-in paths are resolved from each
    /// plug-in's own FilePath; the standard directories are a fallback, never the answer.
    /// </summary>
    [Fact]
    public void Core_never_treats_the_standard_vst3_folder_as_the_plugin_location()
    {
        var offenders = Offenders(@"""[A-Za-z]:\\Program Files[^""]*""");

        Assert.True(offenders.Length == 0,
            "An absolute Program Files path is a literal in Core. Resolve from FilePath:" +
            $"{Environment.NewLine}{offenders}");
    }
}
