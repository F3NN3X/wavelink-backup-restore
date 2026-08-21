using WaveLinkBackup.App.ViewModels;

namespace WaveLinkBackup.App.Tests;

/// <summary>
/// "Equal flex cells, 4px apart, always in the same order and the same place, so a gap breaks the
/// pattern of the whole column before any text is read." - README.
///
/// **Two tests here were deleted rather than adjusted, and both were encoding a defect.**
/// <c>There_are_always_exactly_five</c> and <c>More_than_five_inputs_shows_the_first_five</c>
/// asserted that a rig with more than five channels had the rest dropped from the row - written
/// against technical-debt section 5's own note that "5 inputs is ONE user's rig", and pinning the
/// truncation instead of the alignment it was worried about. A nine-channel rig drew MIC 1, VOICE,
/// BROWSER, MUSIC, SYSTEM and no sign that four more channels existed. What actually has to hold -
/// every row draws the same number of cells, never fewer than five, and a gap always means a
/// missing input - is asserted below and is what the truncation broke.
/// </summary>
public sealed class InputSlotsTests
{
    private static readonly string[] Healthy =
        ["Wave Mic 1", "Voice", "Browser", "Game", "System"];

    private static readonly string[] Collapsed = ["Elgato Wave:3", "System"];

    private static readonly string[] NineChannels =
    [
        "Wave Mic 1", "Voice", "Browser", "Music", "System",
        "Meld Studio", "Media Player", "Aux 1", "Aux 2",
    ];

    /// <summary>A layout that judges nothing: no previous snapshot, so no row is collapsed.</summary>
    private static SlotLayout Layout(int slots) => new(slots, PreviousInputCount: 0);

    // ---------------------------------------------------------------- how wide the strip is

    [Fact]
    public void The_strip_is_five_cells_for_the_rig_the_design_was_drawn_for()
    {
        Assert.Equal(5, InputSlots.SlotsFor(5));
        Assert.Equal(5, InputSlots.Build(Healthy, Layout(InputSlots.SlotsFor(5))).Count);
    }

    /// <summary>
    /// Five is a floor, not a width. A two-input collapse has to read as two present and three
    /// missing, or the strip's whole "a gap breaks the pattern" argument evaporates on the one
    /// row it exists for.
    /// </summary>
    [Fact]
    public void A_smaller_rig_does_not_shrink_the_strip_below_five()
    {
        Assert.Equal(5, InputSlots.SlotsFor(2));
        Assert.Equal(5, InputSlots.Build(Collapsed, Layout(InputSlots.SlotsFor(2))).Count);
    }

    [Fact]
    public void A_bigger_rig_widens_the_strip_to_hold_every_channel()
    {
        Assert.Equal(9, InputSlots.SlotsFor(9));

        var slots = InputSlots.Build(NineChannels, Layout(9));

        Assert.Equal(9, slots.Count);
        Assert.All(slots, s => Assert.Equal(SlotKind.Named, s.Kind));
    }

    /// <summary>
    /// The channels that used to be dropped. Named here so the regression reads as itself rather
    /// than as a count.
    /// </summary>
    [Fact]
    public void The_channels_past_the_fifth_are_drawn_rather_than_dropped()
    {
        var labels = InputSlots.Build(NineChannels, Layout(9)).Select(s => s.Label).ToList();

        Assert.Contains("MELD", labels);
        Assert.Contains("AUX1", labels);
        Assert.Contains("AUX2", labels);
    }

    /// <summary>
    /// Every row draws the store's widest configuration, so an older backup taken before the rig
    /// grew shows the channels it does not hold as missing. That is the true statement: restoring
    /// it would not bring those channels back.
    /// </summary>
    [Fact]
    public void An_older_smaller_backup_keeps_the_full_width_and_shows_the_rest_missing()
    {
        var slots = InputSlots.Build(Healthy, Layout(9));

        Assert.Equal(9, slots.Count);
        Assert.All(slots.Take(5), s => Assert.Equal(SlotKind.Named, s.Kind));
        Assert.All(slots.Skip(5), s => Assert.Equal(SlotKind.Missing, s.Kind));
    }

    // ---------------------------------------------------------------- what the cells say

    [Fact]
    public void A_full_rig_reads_as_five_named_slots()
    {
        var slots = InputSlots.Build(Healthy, Layout(5));

        Assert.All(slots, s => Assert.Equal(SlotKind.Named, s.Kind));
        Assert.Equal(["MIC 1", "VOICE", "BROWSER", "GAME", "SYSTEM"], slots.Select(s => s.Label));
    }

    [Fact]
    public void The_missing_slots_come_last_and_carry_an_em_dash()
    {
        var slots = InputSlots.Build(Collapsed, Layout(5));

        Assert.All(slots.Skip(2), s =>
        {
            Assert.Equal(SlotKind.Missing, s.Kind);
            Assert.Equal("—", s.Label);
        });
    }

    [Fact]
    public void An_empty_rig_is_five_missing_slots()
    {
        var slots = InputSlots.Build([], Layout(5));

        Assert.Equal(5, slots.Count);
        Assert.All(slots, s => Assert.Equal(SlotKind.Missing, s.Kind));
    }

    /// <summary>
    /// The labels are what yields to the extra channels, never the channels themselves. Nine cells
    /// hold four characters, and past a dozen the cells keep their rules and lose their words - the
    /// shape-first health encoding is the half that carries the meaning.
    /// </summary>
    [Theory]
    [InlineData(5, 9)]
    [InlineData(9, 4)]
    [InlineData(12, 3)]
    [InlineData(16, 0)]
    public void The_label_budget_narrows_as_the_strip_widens(int slotCount, int expected)
    {
        Assert.Equal(expected, InputSlots.LabelBudget(slotCount));
    }

    [Fact]
    public void Past_the_readable_width_the_cells_keep_their_rules_and_lose_their_labels()
    {
        var slots = InputSlots.Build(NineChannels, Layout(16));

        Assert.Equal(16, slots.Count);
        Assert.All(slots, s => Assert.Equal(string.Empty, s.Label));
        Assert.All(slots.Take(9), s => Assert.Equal(SlotKind.Named, s.Kind));
        Assert.All(slots.Skip(9), s => Assert.Equal(SlotKind.Missing, s.Kind));
    }

    // ---------------------------------------------------------------- what counts as collapsed

    /// <summary>
    /// The reported bug: adding four channels to Wave Link repainted every older backup amber.
    /// They had lost nothing - the rig had gained something - and amber is this app's word for
    /// "Wave Link reset your configuration".
    /// </summary>
    [Fact]
    public void A_rig_that_grew_leaves_its_older_backups_alone()
    {
        var older = InputSlots.Build(Healthy, new SlotLayout(9, PreviousInputCount: 5));

        Assert.All(older.Take(5), s => Assert.Equal(SlotKind.Named, s.Kind));
    }

    /// <summary>
    /// The case the amber strip exists for: nine channels yesterday, two today. SYSTEM is green in
    /// the test above and amber here, from the same string - which is why genericness is a property
    /// of the ROW and never of the name.
    /// </summary>
    [Fact]
    public void A_backup_with_fewer_inputs_than_the_one_before_it_reads_generic()
    {
        var wide = InputSlots.Build(Collapsed, new SlotLayout(9, PreviousInputCount: 9));

        Assert.Equal(SlotKind.Generic, wide[0].Kind);
        Assert.Equal(SlotKind.Generic, wide[1].Kind);

        // The design's own five-wide strip, where the labels have room to say it in full.
        var five = InputSlots.Build(Collapsed, new SlotLayout(5, PreviousInputCount: 5));

        Assert.Equal(["WAVE:3", "SYSTEM"], five.Take(2).Select(s => s.Label));
    }

    [Theory]
    [InlineData(5, 5, false)]
    [InlineData(9, 5, false)]
    [InlineData(2, 9, true)]
    [InlineData(2, 0, false)]
    [InlineData(0, 0, false)]
    public void Collapse_is_a_drop_against_the_previous_snapshot(
        int inputCount, int previousInputCount, bool expected)
    {
        Assert.Equal(expected, InputSlots.IsCollapsed(inputCount, previousInputCount));
    }

    /// <summary>
    /// The oldest backup in the store has nothing to have collapsed FROM, and a zero must read as
    /// "no comparison" rather than as a loss of everything.
    /// </summary>
    [Fact]
    public void The_oldest_backup_is_never_generic()
    {
        var slots = InputSlots.Build(Collapsed, new SlotLayout(5, PreviousInputCount: 0));

        Assert.All(slots.Take(2), s => Assert.Equal(SlotKind.Named, s.Kind));
    }
}
