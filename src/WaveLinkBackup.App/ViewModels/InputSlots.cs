namespace WaveLinkBackup.App.ViewModels;

/// <summary>How a slot in the five-slot health strip is drawn.</summary>
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
/// The five-slot health strip: "five equal flex cells, 4px apart, always in the same order and
/// the same place, so a gap breaks the pattern of the whole column before any text is read."
///
/// Five ALWAYS five, padded with Missing. Design section C makes this structural in the view
/// model rather than an accident of a template, because it is the information design.
/// </summary>
public static class InputSlots
{
    /// <summary>
    /// Not a claim about how many inputs a rig has - technical-debt section 5 is explicit that
    /// five is one user's rig. It is the WIDTH OF THE STRIP, which is a layout constant.
    /// </summary>
    public const int SlotCount = 5;

    /// <param name="peakInputCount">
    /// The highest input count in the user's own store. A row below it has lost inputs relative
    /// to that user's own best, which is the collapsed case - so its present slots render
    /// generic.
    ///
    /// A property of the ROW, not of the name: README lists System as a healthy input AND as one
    /// of the two a collapsed configuration falls back to, so no name-matching rule could ever
    /// be right. It is also HealthFingerprint's own argument - health is decided against that
    /// user's previous snapshot, never against an absolute threshold.
    /// </param>
    public static IReadOnlyList<InputSlot> Build(IReadOnlyList<string> inputNames, int peakInputCount)
    {
        var collapsed = inputNames.Count < peakInputCount;
        var kind = collapsed ? SlotKind.Generic : SlotKind.Named;

        var slots = new InputSlot[SlotCount];

        for (var i = 0; i < SlotCount; i++)
        {
            slots[i] = i < inputNames.Count
                ? new InputSlot(Readable.SlotLabel(inputNames[i]), kind)
                : new InputSlot("—", SlotKind.Missing);
        }

        return slots;
    }
}
