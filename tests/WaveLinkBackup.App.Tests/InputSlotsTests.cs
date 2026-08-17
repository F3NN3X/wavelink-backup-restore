using WaveLinkBackup.App.ViewModels;

namespace WaveLinkBackup.App.Tests;

/// <summary>
/// "Five equal flex cells, 4px apart, always in the same order and the same place, so a gap
/// breaks the pattern of the whole column before any text is read." - README.
///
/// Five always five is the information design, which is why it is asserted here rather than
/// left to a template that happens to be five wide.
/// </summary>
public sealed class InputSlotsTests
{
    private static readonly string[] Healthy =
        ["Wave Mic 1", "Voice", "Browser", "Game", "System"];

    private static readonly string[] Collapsed = ["Elgato Wave:3", "System"];

    [Fact]
    public void There_are_always_exactly_five()
    {
        Assert.Equal(5, InputSlots.Build(Healthy, 5).Count);
        Assert.Equal(5, InputSlots.Build(Collapsed, 5).Count);
        Assert.Equal(5, InputSlots.Build([], 5).Count);
        Assert.Equal(5, InputSlots.Build(Healthy, 2).Count);
    }

    [Fact]
    public void A_full_rig_reads_as_five_named_slots()
    {
        var slots = InputSlots.Build(Healthy, peakInputCount: 5);

        Assert.All(slots, s => Assert.Equal(SlotKind.Named, s.Kind));
        Assert.Equal(
            ["MIC 1", "VOICE", "BROWSER", "GAME", "SYSTEM"],
            slots.Select(s => s.Label));
    }

    // The whole reason genericness is a property of the ROW: SYSTEM is green above and amber
    // here, from the same string.
    [Fact]
    public void A_row_below_the_store_s_high_water_mark_renders_its_slots_generic()
    {
        var slots = InputSlots.Build(Collapsed, peakInputCount: 5);

        Assert.Equal(SlotKind.Generic, slots[0].Kind);
        Assert.Equal(SlotKind.Generic, slots[1].Kind);
        Assert.Equal(["WAVE:3", "SYSTEM"], slots.Take(2).Select(s => s.Label));
    }

    [Fact]
    public void The_missing_slots_come_last_and_carry_an_em_dash()
    {
        var slots = InputSlots.Build(Collapsed, peakInputCount: 5);

        Assert.All(slots.Skip(2), s =>
        {
            Assert.Equal(SlotKind.Missing, s.Kind);
            Assert.Equal("—", s.Label);
        });
    }

    // One backup in the store is its own high-water mark. It has not collapsed; there is just
    // nothing to have collapsed FROM.
    [Fact]
    public void A_row_at_the_high_water_mark_is_never_generic()
    {
        var slots = InputSlots.Build(Collapsed, peakInputCount: 2);

        Assert.All(slots.Take(2), s => Assert.Equal(SlotKind.Named, s.Kind));
    }

    [Fact]
    public void An_empty_rig_is_five_missing_slots()
    {
        var slots = InputSlots.Build([], peakInputCount: 5);

        Assert.All(slots, s => Assert.Equal(SlotKind.Missing, s.Kind));
    }

    // technical-debt section 5: "5 inputs is ONE user's rig". Six is not an error, and the
    // sixth must not push a slot out of alignment or crash the strip.
    [Fact]
    public void More_than_five_inputs_shows_the_first_five()
    {
        var slots = InputSlots.Build(
            ["Wave Mic 1", "Voice", "Browser", "Game", "System", "Return"], peakInputCount: 6);

        Assert.Equal(5, slots.Count);
        Assert.All(slots, s => Assert.Equal(SlotKind.Named, s.Kind));
        Assert.DoesNotContain("RETURN", slots.Select(s => s.Label));
    }
}
