using WaveLinkBackup.App.ViewModels;

namespace WaveLinkBackup.App.Tests;

/// <summary>
/// The empty-trash confirmation, in isolation from Settings. 08-settings-persistence.md is
/// authoritative for the copy and the volume rule; these tests assert exactly that behaviour
/// without standing up a Window, because the dialog binds to the strings this model exposes and
/// those are what must be right.
///
/// The load-bearing assertion is the BRANCH: on a local drive the row reports
/// <c>RequiresConfirmation == false</c>, so no dialog is ever built; on a volume with no Recycle
/// Bin it reports true, so this model is what gets shown. The two are pinned together here — local
/// → no dialog, non-local → dialog required — because that pairing IS the feature ("confirm only
/// where irreversible").
/// </summary>
public sealed class EmptyTrashDialogModelTests
{
    // 12_582_912 B / 1_048_576 = 12.0 MB exactly → "12 MB" (Readable truncates, drops the .0).
    private const long SizeBytes = 12_582_912;

    private const string LocalTrash = @"C:\Users\test\AppData\Local\WaveLinkBackup\.trash";
    private const string NasTrash = @"\\nas\backups\.trash";

    // -------------------------------------------------------------- the branch: local → no dialog

    [Fact]
    public void Local_drive_needs_no_dialog_so_the_model_is_never_built()
    {
        var row = TrashRowModel.Build(3, SizeBytes, LocalTrash, recycleBinAvailable: true);

        Assert.True(row.HasItems);
        // The branch: reversible (Recycle Bin catches it) → the action runs immediately. No model,
        // no window, no confirmation. This is why the dialog exists only for the other case.
        Assert.False(row.RequiresConfirmation);
    }

    [Fact]
    public void Local_drive_with_a_single_item_also_needs_no_dialog()
    {
        var row = TrashRowModel.Build(1, SizeBytes, LocalTrash, recycleBinAvailable: true);

        Assert.False(row.RequiresConfirmation);
    }

    // -------------------------------------------------------------- the branch: non-local → dialog required

    [Fact]
    public void Network_drive_requires_the_dialog()
    {
        var row = TrashRowModel.Build(3, SizeBytes, NasTrash, recycleBinAvailable: false);

        Assert.True(row.HasItems);
        // Irreversible (no Recycle Bin) → the confirmation is required, and THIS model is its content.
        Assert.True(row.RequiresConfirmation);

        var dialog = EmptyTrashDialogModel.Build(3, SizeBytes, NasTrash);
        Assert.Equal("Empty the trash?", dialog.Title);
    }

    [Fact]
    public void Removable_drive_requires_the_dialog_too()
    {
        // A stick is not a fixed drive: IsAvailableFor is false for it, same as the NAS.
        var row = TrashRowModel.Build(2, SizeBytes, @"E:\backups\.trash", recycleBinAvailable: false);

        Assert.True(row.RequiresConfirmation);
    }

    [Fact]
    public void Empty_trash_needs_no_dialog_even_on_a_network_drive()
    {
        // Nothing to delete → nothing to confirm, regardless of volume. The branch is gated on
        // HasItems as well as the volume, so an empty NAS trash runs "immediately" (i.e. does nothing).
        var row = TrashRowModel.Build(0, 0, NasTrash, recycleBinAvailable: false);

        Assert.False(row.RequiresConfirmation);
    }

    // -------------------------------------------------------------- the copy

    [Fact]
    public void Body_names_the_count_and_that_windows_cannot_keep_them()
    {
        var dialog = EmptyTrashDialogModel.Build(3, SizeBytes, NasTrash);

        Assert.Contains("3 backups", dialog.Body);
        Assert.Contains("for good", dialog.Body);
        // The honest reason there is no undo — named here only to say the Recycle Bin is ABSENT.
        Assert.Contains("no Recycle Bin", dialog.Body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("no undo", dialog.Body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Singular_count_uses_backup_not_backups()
    {
        var dialog = EmptyTrashDialogModel.Build(1, SizeBytes, NasTrash);

        Assert.Contains("1 backup for good", dialog.Body);
        Assert.DoesNotContain("backups for good", dialog.Body);
    }

    [Fact]
    public void Confirm_label_names_the_count()
    {
        Assert.Equal("Delete 3 backups", EmptyTrashDialogModel.Build(3, SizeBytes, NasTrash).ConfirmLabel);
        Assert.Equal("Delete 1 backup", EmptyTrashDialogModel.Build(1, SizeBytes, NasTrash).ConfirmLabel);
    }

    [Fact]
    public void Meta_line_is_size_and_trash_path()
    {
        var dialog = EmptyTrashDialogModel.Build(2, SizeBytes, NasTrash);

        Assert.Equal($"12 MB · {NasTrash}", dialog.MetaLine);
    }
}
