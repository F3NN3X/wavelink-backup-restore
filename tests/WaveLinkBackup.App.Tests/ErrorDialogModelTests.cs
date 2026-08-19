using WaveLinkBackup.App.ViewModels;
using WaveLinkBackup.Core.Results;

namespace WaveLinkBackup.App.Tests;

/// <summary>
/// The error dialog's pure projection (06-errors.md "Dialogs"): in comes one of the three Core
/// errors that are placed in a dialog (2, 4, 8), out goes what the view renders. These tests pin
/// the copy verbatim from the catalog, the weight rule (error 4 is the only amber), the chooser
/// rows for error 2, the note block for 4 and 8, and the footer buttons per variant - so a future
/// edit that re-weights an error or changes a button label fails here before it ships. The view
/// tests (ErrorDialogViewTests) pin what actually renders; these pin what is COMPUTED.
/// </summary>
public sealed class ErrorDialogModelTests
{
    // -------------------------------------------------------------- error 2 - two installations

    [Fact]
    public void Two_installations_builds_a_neutral_chooser_with_one_row_per_candidate()
    {
        var model = ErrorDialogModel.Build(new MultiplePackagesFound(
            ["C:\\Program Files\\Wave Link", "D:\\Apps\\Wave Link"]));

        Assert.Equal("Two Wave Link installations", model.Title);
        Assert.Equal(
            "Both have their own settings file. Pick the one you actually use — the other stays untouched.",
            model.Body);
        // Neutral: no config is damaged, a choice is simply needed.
        Assert.Equal(ErrorWeight.Neutral, model.Weight);
        // The chooser lists exactly what Core found, in order.
        Assert.Equal(2, model.Options.Count);
        Assert.Equal("C:\\Program Files\\Wave Link", model.Options[0].Path);
        Assert.Equal("D:\\Apps\\Wave Link", model.Options[1].Path);
        // No note block - the content IS the chooser rows.
        Assert.Null(model.Note);
        // The "remember this one" checkbox is error 2's only footer extra.
        Assert.Equal("Use this one", model.PrimaryLabel);
        Assert.Equal("Cancel", model.SecondaryLabel);
        Assert.Null(model.GhostLabel);
        Assert.Equal("Remember this one and stop asking", model.RememberLabel);
        // The chooser card is wider than the other two (620 vs 560).
        Assert.Equal(620, model.CardWidth);
    }

    [Fact]
    public void Two_installations_with_a_single_candidate_still_builds()
    {
        var model = ErrorDialogModel.Build(new MultiplePackagesFound(["C:\\Program Files\\Wave Link"]));

        Assert.Single(model.Options);
        Assert.Equal(ErrorWeight.Neutral, model.Weight);
    }

    // -------------------------------------------------------------- error 4 - malformed settings

    [Fact]
    public void Malformed_settings_builds_an_amber_dialog_with_a_note_block_and_no_chooser()
    {
        var model = ErrorDialogModel.Build(new MalformedSettings("unexpected token at line 12, column 3"));

        Assert.Equal("Wave Link's settings file is malformed", model.Title);
        Assert.Equal(
            "Nothing was backed up — copying a broken file would give you a broken backup. " +
            "Wave Link may be mid-write; try again in a moment.",
            model.Body);
        // AMBER: the live configuration is the thing that is not whole. The only amber of the three.
        Assert.Equal(ErrorWeight.Amber, model.Weight);
        // No chooser - there is nothing to pick between.
        Assert.Empty(model.Options);
        // The note names the way out and carries the machine-specific parse detail as its second line.
        Assert.NotNull(model.Note);
        Assert.Null(model.Note!.Label);
        Assert.Equal(
            "If it stays broken, restore your last good backup — that replaces the file with one that parses.",
            model.Note.Body);
        Assert.Equal("unexpected token at line 12, column 3", model.Note.SecondLine);
        // Retry / close / open-the-folder. No remember checkbox.
        Assert.Equal("Try again", model.PrimaryLabel);
        Assert.Equal("Close", model.SecondaryLabel);
        Assert.Equal("Open the folder", model.GhostLabel);
        Assert.Null(model.RememberLabel);
        Assert.Equal(560, model.CardWidth);
    }

    // -------------------------------------------------------------- error 8 - newer version

    [Fact]
    public void Newer_version_builds_a_neutral_dialog_with_a_two_line_version_readout()
    {
        var model = ErrorDialogModel.Build(new UnsupportedSnapshotSchema(Found: 3, Supported: 2));

        Assert.Equal("This backup was made by a newer version", model.Title);
        Assert.Equal(
            "It uses a format this copy doesn't understand yet. Update Wave Link Backup and it will " +
            "restore normally. The backup itself is fine.",
            model.Body);
        // Neutral: the backup is fine, this copy just doesn't understand the format yet.
        Assert.Equal(ErrorWeight.Neutral, model.Weight);
        // No chooser - there is nothing to pick between.
        Assert.Empty(model.Options);
        // The block names both versions, mono.
        Assert.NotNull(model.Note);
        Assert.Null(model.Note!.Label);
        Assert.Equal("MADE WITH WAVE LINK BACKUP 3", model.Note.Body);
        Assert.Equal("YOU HAVE 2", model.Note.SecondLine);
        // Update / close. No ghost, no remember checkbox.
        Assert.Equal("Get the update", model.PrimaryLabel);
        Assert.Equal("Close", model.SecondaryLabel);
        Assert.Null(model.GhostLabel);
        Assert.Null(model.RememberLabel);
        Assert.Equal(560, model.CardWidth);
    }

    // -------------------------------------------------------------- only 2, 4 and 8 are dialogs

    [Fact]
    public void A_non_dialog_error_reaching_Build_throws()
    {
        // The inline-strip and replaces-list errors must be refused by the dialog path rather than
        // rendered with a wrong placement. Each is constructed exactly as Core would produce it, so
        // the assertion is about the TYPE reaching Build, not its arguments.
        foreach (var error in new[]
        {
            (CoreError)new SettingsUnreadable("C:\\Wave Link\\settings.json", "denied"),
            new WaveLinkStillRunning(["WaveLink.exe"]),
            new WriteFailed("access denied"),
            new MalformedManifest("bad json"),
            new NotASnapshot("C:\\somewhere\\else", "no manifest"),
            new SnapshotCorrupted("2026-08-11T2136-a3f81c", "sha mismatch"),
            new SnapshotNotFound("no-such-id"),
            new StoreUnavailable("C:\\Backups", "missing"),
        })
        {
            Assert.Throws<ArgumentException>(() => ErrorDialogModel.Build(error));
        }
    }
}
