using WaveLinkBackup.Core.Abstractions;

namespace WaveLinkBackup.App.ViewModels;

/// <summary>
/// Whether emptying the trash is recoverable. Decided per volume, not per app launch — the
/// backup folder is user-chosen and may sit on a NAS or a stick. 08-settings-persistence.md.
/// </summary>
public enum VolumeKind
{
    /// <summary>A fixed local drive: Windows keeps a Recycle Bin here, so emptying is reversible.</summary>
    LocalRecycleBin,

    /// <summary>Network or removable — no Recycle Bin. Emptying deletes for good and must confirm.</summary>
    NoRecycleBin,
}

/// <summary>
/// The empty-trash row's entire content, computed BEFORE anything is shown.
///
/// A pure projection, exactly like <see cref="DeleteDialogModel"/>: in comes the trash count and
/// size (the store already knows both — <c>SnapshotStore.TrashSize</c>), the path of the
/// <c>.trash</c> folder, and whether the volume has a Recycle Bin; out goes what Settings renders.
/// No I/O, no WPF. The view binds to this; it does not compute.
///
/// Three states:
/// <list type="bullet">
///   <item><b>HasItems</b> — "N deleted backups are waiting in the trash". The description and the
///         need for a confirmation both depend on the volume: local names the Recycle Bin and runs
///         immediately; network/removable says it deletes for good and confirms.</item>
///   <item><b>Empty</b> — "The trash is empty". The row STAYS VISIBLE (it is how anyone learns the
///         trash exists, since there is no trash view to stumble into) but its action is not
///         interactive — 40% opacity.</item>
/// </list>
///
/// This is the ONE place in the whole app that names the Recycle Bin (05 §"Why the dialog never
/// says Recycle Bin"). The delete confirmation deliberately does not, because it cannot promise an
/// undo on every volume.
/// </summary>
public sealed record TrashRowModel(
    string Title,
    string Description,
    string MonoLine,
    VolumeKind Volume,
    bool HasItems)
{
    /// <summary>
    /// Whether the "Empty trash" action is interactive. Empty → false (40% opacity, not clickable).
    /// </summary>
    public bool ActionEnabled => HasItems;

    /// <summary>
    /// Whether pressing the action opens a confirmation first. Local drives skip it — the Recycle
    /// Bin makes emptying reversible, and "a dialog guarding a reversible action is the noise that
    /// teaches people to click through the ones that matter". Network/removable confirm.
    /// </summary>
    public bool RequiresConfirmation => HasItems && Volume == VolumeKind.NoRecycleBin;

    /// <param name="count">How many snapshots are in the trash (<c>SnapshotStore.TrashSize().Count</c>).</param>
    /// <param name="bytes">Their total size (<c>SnapshotStore.TrashSize().Bytes</c>).</param>
    /// <param name="trashPath">The <c>.trash</c> folder, for the mono line.</param>
    /// <param name="recycleBinAvailable">
    /// Whether the volume holds a Recycle Bin. Callers pass
    /// <c>SnapshotStore.TrashGoesToRecycleBin(recycleBin)</c>, which is
    /// <c>IRecycleBin.IsAvailableFor</c> on the trash path — the per-volume detection, already
    /// failing toward "no Recycle Bin" where it cannot tell. Re-call this whenever the folder
    /// changes; never cache the answer across a folder move.
    /// </param>
    public static TrashRowModel Build(int count, long bytes, string trashPath, bool recycleBinAvailable)
    {
        var volume = recycleBinAvailable ? VolumeKind.LocalRecycleBin : VolumeKind.NoRecycleBin;

        if (count <= 0)
        {
            return new TrashRowModel(
                Title: "The trash is empty",
                Description: "Backups you delete wait here until you empty it.",
                MonoLine: $"{Readable.Bytes(bytes)} · {trashPath}",
                Volume: volume,
                HasItems: false);
        }

        var title = count == 1
            ? "1 deleted backup is waiting in the trash"
            : $"{count} deleted backups are waiting in the trash";

        return volume == VolumeKind.LocalRecycleBin
            ? new TrashRowModel(
                Title: title,
                Description: "Emptying hands them to the Windows Recycle Bin, so you would still have one more "
                             + "chance to change your mind.",
                MonoLine: $"{Readable.Bytes(bytes)} · {trashPath}",
                Volume: volume,
                HasItems: true)
            : new TrashRowModel(
                Title: title,
                Description: "This folder is on a network drive, where Windows keeps no Recycle Bin — emptying "
                             + "deletes them for good.",
                MonoLine: $"{Readable.Bytes(bytes)} · {trashPath}",
                Volume: volume,
                HasItems: true);
    }
}
