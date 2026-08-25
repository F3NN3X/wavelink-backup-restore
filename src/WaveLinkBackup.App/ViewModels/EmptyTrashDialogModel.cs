namespace WaveLinkBackup.App.ViewModels;

/// <summary>
/// The empty-trash confirmation's entire content, computed BEFORE anything is shown.
///
/// A pure projection, exactly like <see cref="DeleteDialogModel"/> and <see cref="TrashRowModel"/>:
/// in comes the trash count and size plus the <c>.trash</c> path; out goes what the 480px dialog
/// renders: a title, one sentence of consequence naming the count and that Windows can't keep
/// them, the mono meta line, and the confirm button's label. No I/O, no WPF. The view binds to
/// this; it does not compute.
///
/// This model exists ONLY for the irreversible case: a volume with no Recycle Bin (network or
/// removable). On a local drive there is nothing to confirm: emptying hands the snapshots to the
/// Windows Recycle Bin, so the action runs immediately and <see cref="TrashRowModel"/> reports
/// <c>RequiresConfirmation == false</c>. That branch decision lives in the row model; this dialog
/// is what that branch opens when it is true. The two are pinned together by the tests: local → no
/// dialog, non-local → dialog required.
///
/// Naming rule (05 §"Why the dialog never says Recycle Bin"): like the delete confirmation, this
/// dialog names the destination truth, "Windows keeps no Recycle Bin here", because that is the
/// honest reason there is no undo. It does not promise one. The trash ROW is still the only place
/// that names the Recycle Bin as a destination; here it is named only to say it is absent.
/// </summary>
public sealed record EmptyTrashDialogModel(
    string Title,
    string Body,
    string MetaLine,
    string ConfirmLabel)
{
    /// <param name="count">How many snapshots are in the trash (<c>SnapshotStore.TrashSize().Count</c>).</param>
    /// <param name="bytes">Their total size (<c>SnapshotStore.TrashSize().Bytes</c>).</param>
    /// <param name="trashPath">The <c>.trash</c> folder, for the mono line.</param>
    public static EmptyTrashDialogModel Build(int count, long bytes, string trashPath)
    {
        var noun = count == 1 ? "backup" : "backups";

        // One sentence of consequence: names the count and that there is no Recycle Bin to catch
        // them. "For good" is the load-bearing phrase. It is the difference from every other
        // destructive action in this app, all of which land in the trash first.
        var body = $"This deletes {count} {noun} for good. This folder is on a volume where Windows "
                   + "keeps no Recycle Bin, so there is no undo.";

        return new EmptyTrashDialogModel(
            Title: "Empty the trash?",
            Body: body,
            MetaLine: $"{Readable.Bytes(bytes)} · {trashPath}",
            ConfirmLabel: count == 1 ? "Delete 1 backup" : $"Delete {count} backups");
    }
}
