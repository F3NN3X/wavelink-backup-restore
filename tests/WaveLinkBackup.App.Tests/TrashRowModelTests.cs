using WaveLinkBackup.App.ViewModels;

namespace WaveLinkBackup.App.Tests;

/// <summary>
/// The empty-trash row's three states, in isolation from Settings. 08-settings-persistence.md is
/// authoritative for the copy and the volume rule; these tests assert exactly that behaviour
/// without standing up a Window, because the row binds to the strings this model exposes and those
/// are what must be right.
///
/// The volume comes in as a plain bool — the store's <c>TrashGoesToRecycleBin</c>, which is
/// <c>IRecycleBin.IsAvailableFor</c> on the trash path. That seam already does the per-volume
/// detection (UNC → false, fixed drive → true, cannot-tell → false), so the model never re-derives
/// a drive type and the tests never touch a real volume.
/// </summary>
public sealed class TrashRowModelTests
{
    // 12_582_912 B / 1_048_576 = 12.0 MB exactly → "12 MB" (Readable truncates, drops the .0).
    private const long SizeBytes = 12_582_912;

    private const string LocalTrash = @"C:\Users\test\AppData\Local\WaveLinkBackup\.trash";
    private const string NasTrash = @"\\nas\backups\.trash";

    // -------------------------------------------------------------- local drive, with items

    [Fact]
    public void Local_drive_with_items_names_the_recycle_bin_and_needs_no_confirmation()
    {
        var model = TrashRowModel.Build(2, SizeBytes, LocalTrash, recycleBinAvailable: true);

        Assert.True(model.HasItems);
        Assert.Equal(VolumeKind.LocalRecycleBin, model.Volume);
        Assert.Equal("2 deleted backups are waiting in the trash", model.Title);
        Assert.Contains("Windows Recycle Bin", model.Description);
        // The one place in the whole app that may say it.
        Assert.False(model.RequiresConfirmation);
        Assert.True(model.ActionEnabled);
    }

    [Fact]
    public void Local_drive_with_one_item_says_backup_not_backups()
    {
        var model = TrashRowModel.Build(1, SizeBytes, LocalTrash, recycleBinAvailable: true);

        Assert.Equal("1 deleted backup is waiting in the trash", model.Title);
    }

    // -------------------------------------------------------------- network / removable, with items

    [Fact]
    public void Network_drive_with_items_says_deletes_for_good_and_requires_confirmation()
    {
        var model = TrashRowModel.Build(3, SizeBytes, NasTrash, recycleBinAvailable: false);

        Assert.True(model.HasItems);
        Assert.Equal(VolumeKind.NoRecycleBin, model.Volume);
        Assert.Equal("3 deleted backups are waiting in the trash", model.Title);
        Assert.Contains("deletes them for good", model.Description);
        // Irreversible → confirm. The honest reason is named — Windows keeps no Recycle Bin on
        // this volume — which is exactly why the promise of an undo (the local copy) is absent.
        Assert.Contains("no Recycle Bin", model.Description, StringComparison.OrdinalIgnoreCase);
        Assert.True(model.RequiresConfirmation);
        Assert.True(model.ActionEnabled);
    }

    [Fact]
    public void Removable_drive_with_items_also_requires_confirmation()
    {
        // A stick is not a fixed drive: IsAvailableFor is false for it, same as the NAS.
        var model = TrashRowModel.Build(1, SizeBytes, @"E:\backups\.trash", recycleBinAvailable: false);

        Assert.Equal(VolumeKind.NoRecycleBin, model.Volume);
        Assert.True(model.RequiresConfirmation);
    }

    // -------------------------------------------------------------- empty

    [Fact]
    public void Empty_trash_keeps_the_row_visible_but_disables_the_action()
    {
        var model = TrashRowModel.Build(0, 0, LocalTrash, recycleBinAvailable: true);

        Assert.False(model.HasItems);
        Assert.Equal("The trash is empty", model.Title);
        // Still present (it is how anyone learns the trash exists), but not interactive.
        Assert.False(model.ActionEnabled);
        Assert.False(model.RequiresConfirmation);
    }

    [Fact]
    public void Empty_trash_on_a_network_drive_is_still_not_interactive()
    {
        var model = TrashRowModel.Build(0, 0, NasTrash, recycleBinAvailable: false);

        Assert.Equal(VolumeKind.NoRecycleBin, model.Volume);
        Assert.False(model.ActionEnabled);
        Assert.False(model.RequiresConfirmation);
    }

    // -------------------------------------------------------------- the mono line

    [Fact]
    public void Mono_line_is_size_and_trash_path()
    {
        var model = TrashRowModel.Build(2, SizeBytes, LocalTrash, recycleBinAvailable: true);

        Assert.Equal($"12 MB · {LocalTrash}", model.MonoLine);
    }

    [Fact]
    public void Mono_line_reports_zero_bytes_when_empty()
    {
        var model = TrashRowModel.Build(0, 0, LocalTrash, recycleBinAvailable: true);

        Assert.Equal($"0 B · {LocalTrash}", model.MonoLine);
    }
}