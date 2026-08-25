using System.Globalization;
using WaveLinkBackup.Core.Snapshots;

namespace WaveLinkBackup.App.ViewModels;

/// <summary>Which of the three delete confirmations to show. 05-delete-dialogs.md.</summary>
public enum DeleteVariant
{
    /// <summary>The ordinary case: other backups exist and this one is not a pre-restore copy.</summary>
    Normal,

    /// <summary>Deleting the only backup. Neutral, NOT amber (10-decisions.md §2).</summary>
    OnlyBackup,

    /// <summary>A pre-restore copy: the way back from a restore. No colour.</summary>
    PreRestore,
}

/// <summary>
/// The optional context block under the body: a mono label over a sentence. Present for the
/// OnlyBackup and PreRestore variants; absent for Normal.
/// </summary>
public sealed record DeleteContextBlock(string Label, string Body);

/// <summary>
/// The delete confirmation dialog's entire content, computed BEFORE anything is shown.
///
/// A pure projection, exactly like <see cref="RestoreDialogModel"/>: in comes the selected
/// snapshot and how many backups exist, out goes what the 480px dialog renders: title, one
/// sentence of consequence, an optional context block, and the mono meta line. No I/O, no WPF.
/// The view binds to this; it does not compute.
///
/// The variant is decided by two facts only: whether this is the ONLY backup, and whether it
/// is a pre-restore copy. OnlyBackup outranks PreRestore, when there is one backup and it is
/// a pre-restore copy, "it is the only backup you have" is the load-bearing sentence.
///
/// The dialog NEVER says "Recycle Bin" (05 §"Why the dialog never says Recycle Bin"): the
/// backup folder is user-chosen and the Recycle Bin does not exist on network shares, so the
/// one destination true on every volume is named instead: "the trash in your backup folder".
/// The Recycle Bin is named in exactly one place in the whole app: the empty-trash row.
/// </summary>
public sealed record DeleteDialogModel(
    string Title,
    string Body,
    DeleteContextBlock? Context,
    string MetaLine,
    DeleteVariant Variant)
{
    /// <summary>
    /// The OnlyBackup variant's ghost footer action, pinned left of Cancel + Delete. Null for
    /// the other two variants, which have no such button (05 §2).
    /// </summary>
    public string? BackUpNowInstead => Variant == DeleteVariant.OnlyBackup ? "Back up now instead" : null;

    /// <param name="selected">The backup being deleted.</param>
    /// <param name="totalBackups">How many backups exist, including this one.</param>
    public static DeleteDialogModel Build(Snapshot selected, int totalBackups)
    {
        var manifest = selected.Manifest;
        var takenLocal = manifest.CreatedUtc.ToLocalTime();

        var title = $"Delete \"{manifest.DisplayName}\"?";
        var metaLine = $"{Readable.Bytes(SizeOf(manifest))} · TAKEN "
                      + $"{takenLocal.ToString("d MMM", CultureInfo.InvariantCulture)} "
                      + $"{takenLocal.ToString("HH:mm", CultureInfo.InvariantCulture)} · "
                      + TriggerLabel(manifest.Trigger);

        if (totalBackups <= 1)
        {
            // Neutral, not amber: nothing is un-whole at this moment and the red Delete button
            // already carries the weight. The block names what the user would be left with.
            return new DeleteDialogModel(
                Title: title,
                Body: "It moves to the trash in your backup folder. It is the only backup you have.",
                Context: new DeleteContextBlock(
                    Label: "WHAT YOU'D BE LEFT WITH",
                    Body: "Wave Link's own copies, which cover about three days. This one waits in the "
                          + "trash until you empty it — after that it is gone."),
                MetaLine: metaLine,
                Variant: DeleteVariant.OnlyBackup);
        }

        if (manifest.Trigger == SnapshotTrigger.PreRestore)
        {
            // No colour. The block names when this one was taken and that it is the way back.
            return new DeleteDialogModel(
                Title: title,
                Body: "It moves to the trash in your backup folder and stops showing in the list. "
                      + $"Your other {totalBackups - 1} backups aren't affected.",
                Context: new DeleteContextBlock(
                    Label: "WHAT THIS ONE IS",
                    Body: $"Taken automatically at {takenLocal.ToString("HH:mm", CultureInfo.InvariantCulture)} on "
                          + $"{takenLocal.ToString("d MMM", CultureInfo.InvariantCulture)}, just before you restored. "
                          + "It is the way back from that restore."),
                MetaLine: metaLine,
                Variant: DeleteVariant.PreRestore);
        }

        return new DeleteDialogModel(
            Title: title,
            Body: "It moves to the trash in your backup folder and stops showing in the list. "
                  + $"Your other {totalBackups - 1} backups aren't affected.",
            Context: null,
            MetaLine: metaLine,
            Variant: DeleteVariant.Normal);
    }

    /// <summary>The snapshot's weight, as the row and every readout compute it.</summary>
    private static long SizeOf(SnapshotManifest manifest) => manifest.TotalSizeBytes;

    /// <summary>Uppercase mono, matching the row's WHY pill: MANUAL · AUTOMATIC · PRE-RESTORE.</summary>
    private static string TriggerLabel(SnapshotTrigger trigger) => trigger switch
    {
        SnapshotTrigger.Manual => "MANUAL",
        SnapshotTrigger.Automatic => "AUTOMATIC",
        SnapshotTrigger.PreRestore => "PRE-RESTORE",
        _ => "UNKNOWN",
    };
}
