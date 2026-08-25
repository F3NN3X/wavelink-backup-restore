namespace WaveLinkBackup.App.ViewModels;

/// <summary>How a slot in the health strip is drawn.</summary>
public enum SlotKind
{
    /// <summary>Present and named by the user. ok-soft fill, 2px solid ok rule, ok label.</summary>
    Named,

    /// <summary>
    /// Present but generic - the collapsed case, where Wave Link fell back to device-derived
    /// names. Transparent fill, 2px solid warn rule, warn label.
    /// </summary>
    Generic,

    /// <summary>Absent. Transparent fill, 2px DASHED line2 rule, an em dash at 45%.</summary>
    Missing,
}

public readonly record struct InputSlot(string Label, SlotKind Kind);

/// <summary>
/// How one row's strip is laid out, and what it is judged against.
/// </summary>
/// <param name="SlotCount">
/// How many cells EVERY row in the list draws - <see cref="InputSlots.SlotsFor"/> of the store's
/// peak. Uniform across rows on purpose: the strip only works as a scanning aid because a gap
/// appears in the same place on every row, and a strip whose width varied row by row would make
/// two rows incomparable at a glance, which is the one thing it exists to do.
/// </param>
/// <param name="PreviousInputCount">
/// How many inputs the snapshot immediately OLDER than this one had, or 0 when this is the
/// oldest. See <see cref="InputSlots.IsCollapsed"/>.
/// </param>
public readonly record struct SlotLayout(int SlotCount, int PreviousInputCount);

/// <summary>
/// The health strip: "equal flex cells, 4px apart, always in the same order and the same place,
/// so a gap breaks the pattern of the whole column before any text is read."
///
/// It was five cells, always. Five is one user's rig (technical-debt section 5) and the design
/// says so itself; a nine-channel rig had its last four channels silently dropped from the row.
/// The strip is now as wide as the widest configuration in the store, never narrower than five -
/// so the design's layout is exactly preserved on the rig it was drawn for, and a bigger rig is
/// drawn whole rather than truncated. The labels are what yields to the extra channels, not the
/// channels themselves: see <see cref="LabelBudget"/>.
/// </summary>
public static class InputSlots
{
    /// <summary>
    /// The floor, and the design's own number. Not a claim about how many inputs a rig has - it
    /// is the width the strip never drops below, so a two-input collapse still reads as three
    /// missing cells rather than as a tidy pair.
    /// </summary>
    public const int MinimumSlots = 5;

    /// <summary>
    /// The INPUTS column's content width: the design's 300px strip (the column is 320 and the
    /// last 20 are the grid gap, which lives as a right margin inside the cell).
    /// </summary>
    public const double StripWidth = 300;

    /// <summary>The design's "4px apart", as a 2px margin on each side of every cell.</summary>
    private const double CellGap = 4;

    /// <summary>
    /// One character of the slot-label role - mono 500 9.5px at .06em tracking. MEASURED from the
    /// rendered element (6.24px at the time of writing) rather than computed from font metrics,
    /// rounded up so a budget is never one character wider than its cell, and held down by
    /// RowTemplateTests.The_label_budget_is_what_actually_fits_a_cell, which measures it again.
    /// </summary>
    private const double CharacterWidth = 6.25;

    /// <summary>
    /// Below this, a label is noise rather than information: two characters cannot tell MUSIC from
    /// MEDIA PLAYER, and a wrong-looking abbreviation is worse than an honest blank. The cells keep
    /// their rules, so the shape-first health encoding - solid, solid-warn, dashed - survives at
    /// any rig size, which is the half of the design that carries the meaning.
    /// </summary>
    private const int MinimumLabelChars = 3;

    /// <summary>How many cells the whole list draws, given the store's biggest configuration.</summary>
    public static int SlotsFor(int peakInputCount) => Math.Max(MinimumSlots, peakInputCount);

    /// <summary>
    /// Whether this snapshot is the collapsed case: FEWER INPUTS THAN THE SNAPSHOT BEFORE IT.
    ///
    /// It used to be "fewer than the store's peak", which was right while a rig never changed and
    /// wrong the moment one grew. Adding four channels retroactively repainted every older backup
    /// amber - they had not lost anything, the rig had gained something - and amber is the app's
    /// word for "Wave Link reset your configuration". Judging against the previous snapshot is
    /// what <see cref="Core.Analysis.HealthFingerprint"/> has always done and what this file's own
    /// comment already claimed: health is decided against that user's previous snapshot, never
    /// against an absolute threshold.
    /// </summary>
    public static bool IsCollapsed(int inputCount, int previousInputCount) =>
        previousInputCount > 0 && inputCount < previousInputCount;

    /// <summary>
    /// How many characters one cell can hold at this slot count, or 0 for no label at all.
    ///
    /// The strip is a fixed width, so more channels means narrower cells: five give nine
    /// characters, nine give four, and past a dozen the labels go entirely. This is arithmetic
    /// rather than a layout trigger because the answer has to be the SAME for every row - a
    /// per-cell measurement would let one row label a channel its neighbour blanked, and the
    /// column would stop being scannable.
    /// </summary>
    public static int LabelBudget(int slotCount)
    {
        if (slotCount <= 0) return 0;

        var cell = (StripWidth - (CellGap * (slotCount - 1))) / slotCount;
        var characters = (int)Math.Floor(cell / CharacterWidth);

        return characters >= MinimumLabelChars ? characters : 0;
    }

    /// <summary>
    /// The strip for one row: its own inputs, then missing cells out to
    /// <see cref="SlotLayout.SlotCount"/>.
    ///
    /// A row with MORE inputs than the layout allows for cannot happen through the list - the
    /// layout is built from the peak - but is clamped rather than trusted, because the strip's
    /// whole promise is that every row draws the same number of cells.
    /// </summary>
    public static IReadOnlyList<InputSlot> Build(IReadOnlyList<string> inputNames, SlotLayout layout)
    {
        var count = Math.Max(layout.SlotCount, MinimumSlots);
        var kind = IsCollapsed(inputNames.Count, layout.PreviousInputCount)
            ? SlotKind.Generic
            : SlotKind.Named;

        var budget = LabelBudget(count);
        var slots = new InputSlot[count];

        for (var i = 0; i < count; i++)
        {
            slots[i] = i < inputNames.Count
                ? new InputSlot(Readable.SlotLabel(inputNames[i], budget), kind)
                : new InputSlot(budget > 0 ? "—" : string.Empty, SlotKind.Missing);
        }

        return slots;
    }
}
