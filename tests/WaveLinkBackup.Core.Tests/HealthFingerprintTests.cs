using WaveLinkBackup.Core.Analysis;

namespace WaveLinkBackup.Core.Tests;

/// <summary>
/// The health check, and the one rule it must never break: comparison is RELATIVE.
/// Five inputs and 43 KB is one user's rig, so there is no absolute threshold anywhere -
/// only a comparison against that user's own earlier snapshot. SPEC.md 11.
/// </summary>
public sealed class HealthFingerprintTests
{
    private static HealthFingerprint Fingerprint(
        int inputs, string[] names, int effects = 0, long size = 40_000, string sha = "aa") =>
        new(inputs, names, effects, effects > 0 ? 1 : 0, size, sha);

    private static readonly HealthFingerprint Healthy =
        Fingerprint(5, ["Wave Mic 1", "Voice", "Browser", "Music", "System"], effects: 17, size: 43_052, sha: "aaaa");

    /// <summary>What a reset looks like. SPEC.md 3.</summary>
    private static readonly HealthFingerprint Collapsed =
        Fingerprint(2, ["Elgato Wave:3", "System"], size: 11_819, sha: "bbbb");

    [Fact]
    public void A_collapse_shows_as_inputs_lost_and_names_lost()
    {
        var comparison = Collapsed.CompareTo(Healthy);

        Assert.True(comparison.LooksCollapsed);
        Assert.Equal(3, comparison.InputsLost);
        Assert.Equal(["Wave Mic 1", "Voice", "Browser", "Music"], comparison.NamesLost);
        Assert.Equal(17, comparison.EffectsLost);
        Assert.Equal(11_819 - 43_052, comparison.SizeDeltaBytes);
    }

    [Fact]
    public void Recovering_from_a_collapse_is_not_itself_a_collapse()
    {
        var comparison = Healthy.CompareTo(Collapsed);

        Assert.False(comparison.LooksCollapsed);
        Assert.Equal(0, comparison.InputsLost);
        Assert.Equal(3, comparison.InputsGained);

        // The generic placeholder name DOES disappear on recovery, and NamesLost says so.
        // That is correct: Core reports what changed and leaves the judgement to the
        // caller. Filtering "Elgato Wave:3" here would hard-code a magic constant into the
        // health check - the absolute-threshold mistake wearing a different hat.
        Assert.Equal(["Elgato Wave:3"], comparison.NamesLost);
    }

    [Fact]
    public void Three_inputs_is_healthy_when_the_user_has_always_had_three()
    {
        // The reason there is no absolute threshold. This user would be permanently
        // "suspect" against a hard-coded five.
        var small = Fingerprint(3, ["Mic", "Game", "System"], sha: "cccc");

        Assert.False(small.CompareTo(small).LooksCollapsed);
        Assert.False(small.CompareTo(Fingerprint(3, ["Mic", "Game", "System"], sha: "dddd")).LooksCollapsed);
    }

    [Fact]
    public void Identical_content_reports_no_change_which_is_the_dedup_decision()
    {
        var again = Fingerprint(5, ["Wave Mic 1", "Voice", "Browser", "Music", "System"],
            effects: 17, size: 43_052, sha: "aaaa");

        Assert.False(Healthy.CompareTo(again).ContentChanged);
    }

    [Fact]
    public void Different_content_reports_a_change_even_at_identical_size()
    {
        // Wave Link rewrites the file on every launch with near-identical bytes; size is
        // not a substitute for the hash.
        var edited = Fingerprint(5, ["Wave Mic 1", "Voice", "Browser", "Music", "System"],
            effects: 17, size: 43_052, sha: "zzzz");

        Assert.True(Healthy.CompareTo(edited).ContentChanged);
    }

    [Fact]
    public void A_renamed_channel_shows_as_a_lost_name_without_a_lost_input()
    {
        var renamed = Fingerprint(5, ["Wave Mic 1", "Voice", "Browser", "Game", "System"],
            effects: 17, size: 43_052, sha: "eeee");

        var comparison = renamed.CompareTo(Healthy);

        Assert.Equal(0, comparison.InputsLost);
        Assert.False(comparison.LooksCollapsed);
        Assert.Equal(["Music"], comparison.NamesLost);
    }

    [Fact]
    public void Name_comparison_is_case_sensitive_because_the_mixer_is()
    {
        var recased = Fingerprint(1, ["voice"], sha: "ffff");
        var original = Fingerprint(1, ["Voice"], sha: "gggg");

        Assert.Equal(["Voice"], recased.CompareTo(original).NamesLost);
    }
}
