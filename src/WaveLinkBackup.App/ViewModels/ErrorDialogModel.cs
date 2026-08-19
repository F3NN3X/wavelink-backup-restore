using WaveLinkBackup.Core.Results;

namespace WaveLinkBackup.App.ViewModels;

/// <summary>Which of the three decision dialogs to show. 06-errors.md "Dialogs".</summary>
public enum ErrorDialogVariant
{
    /// <summary>Error 2 — two Wave Link installations, none chosen. Neutral, chooser.</summary>
    TwoInstallations,

    /// <summary>Error 4 — malformed settings file. Amber: the live config is not whole.</summary>
    MalformedSettings,

    /// <summary>Error 8 — backup made by a newer version. Neutral; no Restore at all.</summary>
    NewerVersion,
}

/// <summary>
/// One row of error 2's chooser: a Wave Link installation the user must pick between. The design
/// renders each as a bordered row with a radio (06 §2). Version and path are what Core gives us;
/// the RUNNING chip is a visual affordance this task does not yet compute, so it stays null and
/// the row simply omits it.
/// </summary>
public sealed record ErrorInstallOption(string Path, string? Version = null, bool IsRunning = false);

/// <summary>
/// The optional note block under the body: a mono label over a sentence (error 4's amber "if it
/// stays broken" note) or a two-line mono readout (error 8's "made with / you have"). Null for
/// error 2, which has no block — its content is the chooser rows instead.
/// </summary>
public sealed record ErrorNoteBlock(string? Label, string Body, string? SecondLine = null);

/// <summary>
/// The error dialog's entire content, computed BEFORE anything is shown — a pure projection in the
/// same shape as <see cref="DeleteDialogModel"/> and <see cref="RestoreDialogModel"/>. In comes one
/// of the three Core errors that 06-errors.md places in a dialog (2, 4, 8); out goes what the
/// dialog renders: title, body, weight, an optional note block, the error-2 chooser rows, and the
/// footer buttons. No I/O, no WPF — the view binds to this and computes nothing.
///
/// Copy is taken verbatim from 06-errors.md (the catalog's <see cref="AppError"/> already holds the
/// title/body; the block text and button labels live here because they are dialog-specific). The
/// machine-specific mono values (a parse error, a schema version) arrive at render time from the
/// Core error itself — they are never hard-coded.
///
/// <b>Weight rule as 06 states it:</b> neutral unless the configuration is not whole. Error 4 is
/// the only amber of the three — there the LIVE settings file is the thing that cannot be read.
/// Errors 2 and 8 are neutral: a choice is needed (2) or this copy just doesn't understand a newer
/// format yet (8); nothing is damaged either way.
/// </summary>
public sealed record ErrorDialogModel(
    string Title,
    string Body,
    ErrorWeight Weight,
    ErrorNoteBlock? Note,
    IReadOnlyList<ErrorInstallOption> Options,
    string PrimaryLabel,
    string SecondaryLabel,
    string? GhostLabel,
    /// <summary>Error 2 only: the "remember this one" checkbox. Null for errors 4 and 8.</summary>
    string? RememberLabel,
    /// <summary>The card's width in DIPs — 620 for the chooser (error 2), 560 for the other two.</summary>
    double CardWidth)
{

    /// <summary>
    /// Build the model for one of the three dialog errors. Throws on any other error: this is the
    /// dialog path, and a non-dialog error reaching here is a caller bug, not a state to render.
    /// </summary>
    public static ErrorDialogModel Build(CoreError error) => error switch
    {
        // 2 — two installations. Neutral. The chooser lists what Core found; the user picks one.
        // The answer must persist (08-settings-persistence.md) — the caller reads ChosenPath after
        // ShowDialog and writes it to settings, which is why the model exposes the selected option.
        MultiplePackagesFound { Candidates: var candidates } => new ErrorDialogModel(
            Title: AppError.ByCode(2).Title,
            Body: AppError.ByCode(2).Body,
            Weight: ErrorWeight.Neutral,
            Note: null,
            Options: candidates.Select(c => new ErrorInstallOption(Path: c)).ToList(),
            PrimaryLabel: "Use this one",
            SecondaryLabel: "Cancel",
            GhostLabel: null,
            RememberLabel: "Remember this one and stop asking",
            CardWidth: 620),

        // 4 — malformed settings. AMBER: the live configuration is the thing that is not whole.
        // The mono detail (a parse position) comes from Core; the note names the way out.
        MalformedSettings { Detail: var detail } => new ErrorDialogModel(
            Title: AppError.ByCode(4).Title,
            Body: AppError.ByCode(4).Body,
            Weight: ErrorWeight.Amber,
            Note: new ErrorNoteBlock(
                Label: null,
                Body: "If it stays broken, restore your last good backup — that replaces the file "
                     + "with one that parses.",
                SecondLine: detail),
            Options: [],
            PrimaryLabel: "Try again",
            SecondaryLabel: "Close",
            GhostLabel: "Open the folder",
            RememberLabel: null,
            CardWidth: 560),

        // 8 — newer version. Neutral: the backup is fine, this copy just doesn't understand it yet.
        // No Restore button at all — it would not work (06 §8). The block names both versions.
        UnsupportedSnapshotSchema { Found: var found, Supported: var supported } => new ErrorDialogModel(
            Title: AppError.ByCode(8).Title,
            Body: AppError.ByCode(8).Body,
            Weight: ErrorWeight.Neutral,
            Note: new ErrorNoteBlock(
                Label: null,
                Body: $"MADE WITH WAVE LINK BACKUP {found}",
                SecondLine: $"YOU HAVE {supported}"),
            Options: [],
            PrimaryLabel: "Get the update",
            SecondaryLabel: "Close",
            GhostLabel: null,
            RememberLabel: null,
            CardWidth: 560),

        _ => throw new ArgumentException(
            $"ErrorDialogModel.Build does not render '{error.GetType().Name}' — only errors 2, 4 and 8 are dialogs.",
            nameof(error)),
    };
}
