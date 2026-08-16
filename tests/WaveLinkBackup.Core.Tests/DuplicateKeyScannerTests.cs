using System.Text;
using WaveLinkBackup.Core.Analysis;

namespace WaveLinkBackup.Core.Tests;

/// <summary>
/// The original incident: Wave Link's SettingsJsonNormalizer rejects a file with
/// case-insensitively duplicated property names and resets to defaults. See
/// _docs/knowledge-base/gotchas/file-parses-but-wave-link-resets.md
/// </summary>
public sealed class DuplicateKeyScannerTests
{
    private static IReadOnlyList<DuplicateKeyFinding> Scan(string json) =>
        DuplicateKeyScanner.Scan(Encoding.UTF8.GetBytes(json));

    [Fact]
    public void Finds_case_insensitive_duplicates_at_the_root()
    {
        var findings = Scan("""{"Volume":1,"volume":2}""");

        var finding = Assert.Single(findings);
        Assert.Equal("$", finding.Path);
        Assert.Equal(["Volume", "volume"], finding.Names);
    }

    [Fact]
    public void Finds_exact_duplicates_without_throwing()
    {
        // JsonNode.Parse throws ArgumentException on this input. JsonDocument does not,
        // which is exactly why the scanner is built on JsonDocument.
        var findings = Scan("""{"Volume":1,"Volume":2}""");

        Assert.Single(findings);
    }

    [Fact]
    public void Finds_duplicates_nested_in_objects()
    {
        var findings = Scan("""{"MixerConfiguration":{"InputSettings":{"A":1,"a":2}}}""");

        var finding = Assert.Single(findings);
        Assert.Equal("$.MixerConfiguration.InputSettings", finding.Path);
    }

    [Fact]
    public void Finds_duplicates_nested_inside_arrays()
    {
        var findings = Scan("""{"AudioPluginConfigurations":[{"Name":"a","name":"b"}]}""");

        var finding = Assert.Single(findings);
        Assert.Equal("$.AudioPluginConfigurations[0]", finding.Path);
    }

    [Fact]
    public void Reports_every_duplicate_group_not_just_the_first()
    {
        var findings = Scan("""{"A":1,"a":2,"B":3,"b":4}""");

        Assert.Equal(2, findings.Count);
    }

    [Fact]
    public void A_clean_document_yields_nothing()
    {
        Assert.Empty(Scan("""{"MixerConfiguration":{"InputSettings":{"one":1,"two":2}}}"""));
    }

    [Fact]
    public void Keys_differing_only_beyond_case_are_not_duplicates()
    {
        Assert.Empty(Scan("""{"Volume":1,"Volume2":2}"""));
    }

    [Fact]
    public void Non_object_roots_do_not_throw()
    {
        Assert.Empty(Scan("[1,2,3]"));
        Assert.Empty(Scan("42"));
    }
}
