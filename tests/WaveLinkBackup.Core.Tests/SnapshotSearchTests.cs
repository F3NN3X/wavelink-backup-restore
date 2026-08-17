using WaveLinkBackup.Core.Snapshots;

namespace WaveLinkBackup.Core.Tests;

/// <summary>
/// Filtering the list. Names only — screens/07-search.md makes that promise to the user in the
/// footer copy ("SEARCH LOOKS AT NAMES ONLY"), so widening it later would make the copy a lie.
/// </summary>
public sealed class SnapshotSearchTests
{
    private static Snapshot Named(string name) => new(
        Id: name,
        Directory: $@"C:\store\{name}",
        Manifest: new SnapshotManifest(
            SchemaVersion: SnapshotManifest.CurrentSchemaVersion,
            DisplayName: name,
            Notes: "",
            CreatedUtc: new DateTimeOffset(2026, 8, 15, 23, 7, 0, TimeSpan.Zero),
            Trigger: SnapshotTrigger.Manual,
            SettingsSha256: "abc",
            WaveLinkVersion: null,
            InputCount: 5,
            InputNames: [],
            EffectCount: 0,
            EffectChannelCount: 0,
            HasDuplicateKeys: false,
            Tiers: ["settings"],
            Files: new Dictionary<string, SnapshotFile>()));

    private static readonly IReadOnlyList<Snapshot> Store =
        [Named("Before 3.3 beta"), Named("Full rig + plugins"), Named("Auto"), Named("BETA test")];

    [Fact]
    public void An_empty_query_returns_everything()
    {
        Assert.Equal(4, SnapshotSearch.Filter(Store, "").Count);
        Assert.Equal(4, SnapshotSearch.Filter(Store, null).Count);
        Assert.Equal(4, SnapshotSearch.Filter(Store, "   ").Count);
    }

    [Fact]
    public void Matching_is_case_insensitive_and_substring()
    {
        var matches = SnapshotSearch.Filter(Store, "beta");

        Assert.Equal(["Before 3.3 beta", "BETA test"], matches.Select(s => s.Manifest.DisplayName));
    }

    [Fact]
    public void A_query_that_matches_nothing_returns_nothing()
    {
        Assert.Empty(SnapshotSearch.Filter(Store, "wave:3"));
    }

    [Fact]
    public void Notes_and_directory_are_not_searched()
    {
        Assert.Empty(SnapshotSearch.Filter([Named("Auto")], "store"));
    }

    [Fact]
    public void Segments_split_a_match_into_parts()
    {
        var segments = SnapshotSearch.Segments("Before 3.3 beta", "beta");

        Assert.Equal([("Before 3.3 ", false), ("beta", true)],
                     segments.Select(s => (s.Text, s.IsMatch)));
    }

    /// <summary>The row shows what the user called the backup, not what they typed.</summary>
    [Fact]
    public void Segments_preserve_the_original_casing_of_the_match()
    {
        var segments = SnapshotSearch.Segments("BETA test", "beta");

        Assert.Equal("BETA", segments[0].Text);
        Assert.True(segments[0].IsMatch);
    }

    [Fact]
    public void Every_occurrence_is_marked()
    {
        var segments = SnapshotSearch.Segments("beta beta", "beta");

        Assert.Equal(3, segments.Count);
        Assert.Equal([true, false, true], segments.Select(s => s.IsMatch));
    }

    [Fact]
    public void An_empty_query_yields_one_unmatched_segment()
    {
        Assert.Equal([("Auto", false)],
                     SnapshotSearch.Segments("Auto", "").Select(s => (s.Text, s.IsMatch)));
    }

    [Fact]
    public void A_name_with_no_match_yields_one_unmatched_segment()
    {
        Assert.Equal([("Auto", false)],
                     SnapshotSearch.Segments("Auto", "zzz").Select(s => (s.Text, s.IsMatch)));
    }
}
