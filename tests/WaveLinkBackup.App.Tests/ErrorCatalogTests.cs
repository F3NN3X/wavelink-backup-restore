using WaveLinkBackup.App.ViewModels;
using WaveLinkBackup.Core.Results;

namespace WaveLinkBackup.App.Tests;

/// <summary>
/// Pins the twelve errors to their designed placement and weight (06-errors.md), the weight rule,
/// and the pure mapper from Core signals. This is the guard against a future edit silently
/// re-placing or re-weighting an error: change the catalog and these fail before it ships.
/// </summary>
public sealed class ErrorCatalogTests
{
    [Theory]
    [InlineData(1, ErrorPlacement.StatusStrip, ErrorWeight.Neutral)]
    [InlineData(2, ErrorPlacement.Dialog, ErrorWeight.Neutral)]
    [InlineData(3, ErrorPlacement.InlineStrip, ErrorWeight.Amber)]
    [InlineData(4, ErrorPlacement.Dialog, ErrorWeight.Amber)]
    [InlineData(5, ErrorPlacement.InlineStrip, ErrorWeight.Amber)]
    [InlineData(6, ErrorPlacement.InlineStrip, ErrorWeight.Amber)]
    [InlineData(7, ErrorPlacement.InlineStrip, ErrorWeight.Amber)]
    [InlineData(8, ErrorPlacement.Dialog, ErrorWeight.Amber)]
    [InlineData(9, ErrorPlacement.ReplacesList, ErrorWeight.Neutral)]
    [InlineData(10, ErrorPlacement.StatusStrip, ErrorWeight.Neutral)]
    [InlineData(11, ErrorPlacement.InlineStrip, ErrorWeight.Amber)]
    [InlineData(12, ErrorPlacement.ReplacesList, ErrorWeight.Neutral)]
    public void Each_error_has_its_designed_placement_and_weight(int code, ErrorPlacement placement, ErrorWeight weight)
    {
        var error = AppError.ByCode(code);

        Assert.Equal(code, error.Code);
        Assert.Equal(placement, error.Placement);
        Assert.Equal(weight, error.Weight);
    }

    [Fact]
    public void Catalog_holds_exactly_twelve_errors_numbered_one_through_twelve()
    {
        Assert.Equal(12, AppError.All.Count);
        for (var code = 1; code <= 12; code++)
            Assert.Equal(code, AppError.ByCode(code).Code);
    }

    [Fact]
    public void Weight_rule_location_missing_is_neutral()
    {
        // A missing location: nothing broken, nothing lost. All four must be neutral.
        Assert.Equal(ErrorWeight.Neutral, AppError.ByCode(1).Weight);   // Wave Link not found
        Assert.Equal(ErrorWeight.Neutral, AppError.ByCode(9).Weight);   // folder not a backup
        Assert.Equal(ErrorWeight.Neutral, AppError.ByCode(10).Weight);  // auto skipped, folder missing
        Assert.Equal(ErrorWeight.Neutral, AppError.ByCode(12).Weight);  // folder can't be used
    }

    [Fact]
    public void Weight_rule_config_not_whole_is_amber()
    {
        // A write or restore that did not produce a whole config. All five must be amber.
        Assert.Equal(ErrorWeight.Amber, AppError.ByCode(3).Weight);   // unwritable
        Assert.Equal(ErrorWeight.Amber, AppError.ByCode(4).Weight);   // disk full
        Assert.Equal(ErrorWeight.Amber, AppError.ByCode(5).Weight);   // write failed
        Assert.Equal(ErrorWeight.Amber, AppError.ByCode(6).Weight);   // corrupt on restore
        Assert.Equal(ErrorWeight.Amber, AppError.ByCode(7).Weight);   // relaunch failed
    }

    [Fact]
    public void Every_error_has_non_empty_title_and_body()
    {
        foreach (var error in AppError.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(error.Title), $"error {error.Code} has no title");
            Assert.False(string.IsNullOrWhiteSpace(error.Body), $"error {error.Code} has no body");
        }
    }

    // --- Mapper: Core signal -> the right one of the twelve (or null) -------------------------

    [Fact]
    public void Healthy_signal_maps_to_null()
    {
        Assert.Null(AppErrorMapper.FromCoreSignal(new CoreSignal()));
    }

    [Fact]
    public void Wave_link_not_found_maps_to_error_1_and_beats_an_operation_error()
    {
        var signal = new CoreSignal(Error: new WriteFailed("denied"), WaveLinkFound: false);

        Assert.Equal(1, AppErrorMapper.FromCoreSignal(signal)!.Code);
    }

    [Fact]
    public void Multiple_packages_maps_to_error_2()
    {
        var signal = new CoreSignal(Error: new MultiplePackagesFound(new[] { "a", "b" }));

        Assert.Equal(2, AppErrorMapper.FromCoreSignal(signal)!.Code);
    }

    [Fact]
    public void Settings_unreadable_maps_to_error_3()
    {
        var signal = new CoreSignal(Error: new SettingsUnreadable("C:\\x.json", "locked"));

        Assert.Equal(3, AppErrorMapper.FromCoreSignal(signal)!.Code);
    }

    [Fact]
    public void Write_failed_with_disk_full_reason_maps_to_error_4()
    {
        var signal = new CoreSignal(Error: new WriteFailed("not enough space on the drive"));

        Assert.Equal(4, AppErrorMapper.FromCoreSignal(signal)!.Code);
    }

    [Fact]
    public void Write_failed_generic_maps_to_error_5()
    {
        var signal = new CoreSignal(Error: new WriteFailed("access denied"));

        Assert.Equal(5, AppErrorMapper.FromCoreSignal(signal)!.Code);
    }

    [Fact]
    public void Snapshot_corrupted_maps_to_error_6()
    {
        var signal = new CoreSignal(Error: new SnapshotCorrupted("C:\\b", "checksum mismatch"));

        Assert.Equal(6, AppErrorMapper.FromCoreSignal(signal)!.Code);
    }

    [Fact]
    public void Relaunch_failed_maps_to_error_7()
    {
        var signal = new CoreSignal(RelaunchFailed: true);

        Assert.Equal(7, AppErrorMapper.FromCoreSignal(signal)!.Code);
    }

    [Fact]
    public void Store_unavailable_maps_to_error_12_full_screen()
    {
        var signal = new CoreSignal(Error: new StoreUnavailable("D:\\backups", "missing"));

        Assert.Equal(12, AppErrorMapper.FromCoreSignal(signal)!.Code);
    }

    [Fact]
    public void Not_a_snapshot_maps_to_error_9()
    {
        var signal = new CoreSignal(Error: new NotASnapshot("D:\\Recordings", "no manifest"));

        Assert.Equal(9, AppErrorMapper.FromCoreSignal(signal)!.Code);
    }

    [Fact]
    public void Folder_unusable_without_an_error_maps_to_error_10()
    {
        var signal = new CoreSignal(FolderUsable: false);

        Assert.Equal(10, AppErrorMapper.FromCoreSignal(signal)!.Code);
    }

    [Fact]
    public void An_error_the_catalog_does_not_claim_maps_to_null()
    {
        // MalformedSettings surfaces through its own path (the plan dialog), not the catalog.
        var signal = new CoreSignal(Error: new MalformedSettings("unexpected character"));

        Assert.Null(AppErrorMapper.FromCoreSignal(signal));
    }
}
