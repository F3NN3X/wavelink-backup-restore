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
/// What an installation turns out to be, once somebody has looked at it. Supplied by the caller
/// rather than derived here, because finding out means reading a file — and this model is pure.
/// </summary>
/// <param name="IsRunning">
/// Whether this is the installation Wave Link is currently running from.
///
/// An approximation, and a documented one. Windows offers no mapping from a running MSIX
/// process back to the package instance it came from, so the caller decides this by asking which
/// candidate's settings file was written most recently while Wave Link is up. That is the same
/// evidence a person would use, and it is wrong only in the case where two installations were both
/// touched within the same moment and neither is the running one.
/// </param>
public sealed record ErrorInstallDetail(
    string? Version, int InputCount, long SizeBytes, DateTimeOffset? SavedAt, bool IsRunning);

/// <summary>
/// One row of error 2's chooser: a Wave Link installation the user must pick between.
///
/// 06 §2 gives each row a version in Rubik 500, a RUNNING chip where applicable, the ellipsised
/// path, and a <c>SETTINGS SAVED … · N INPUTS · N KB</c> meta line — and the selected row a
/// <c>--wl-bg</c> fill with a 3px accent left edge. Until 0.6.1 it drew a bare radio and a path,
/// which makes "choose between two installations" a decision by file path alone — the exact thing
/// this dialog exists to make easier (technical-debt.md §4.21 item 7).
/// </summary>
public sealed record ErrorInstallOption(
    string Path,
    string? Version = null,
    bool IsRunning = false,
    int InputCount = 0,
    long SizeBytes = 0,
    DateTimeOffset? SavedAt = null)
{
    /// <summary>
    /// The row's title. The version when there is one; otherwise the folder's own name, because a
    /// row headed "Wave Link" twice tells the user nothing.
    /// </summary>
    public string Title => Version is { Length: > 0 } version
        ? $"Wave Link {version}"
        : System.IO.Path.GetFileName(System.IO.Path.GetDirectoryName(Path) ?? Path);

    /// <summary>
    /// <c>SETTINGS SAVED 23:12 · 5 INPUTS · 43 KB</c>. Each part omits itself when it is not
    /// known, so a candidate whose settings could not be read still shows its path and its radio
    /// rather than a row of blanks.
    /// </summary>
    public string Meta
    {
        get
        {
            var parts = new List<string>(3);

            if (SavedAt is { } at) parts.Add($"SETTINGS SAVED {Readable.TimeOfDay(at)}");
            if (InputCount > 0) parts.Add($"{InputCount} INPUT{(InputCount == 1 ? "" : "S")}");
            if (SizeBytes > 0) parts.Add(Readable.Bytes(SizeBytes).ToUpperInvariant());

            return string.Join(" · ", parts);
        }
    }

    /// <summary>Whether the row draws its meta line at all.</summary>
    public bool HasMeta => Meta.Length > 0;

    /// <summary>
    /// The selected-row treatment. Mutable, unlike the rest of the record: the radio group writes
    /// it, and the row's fill and left edge follow. It is UI state, not a fact about the install.
    /// </summary>
    public bool IsSelected { get; set; }
}

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

    /// <summary>One candidate path plus whatever the caller could find out about it.</summary>
    private static ErrorInstallOption Describe(string path, ErrorInstallDetail? detail) =>
        detail is null
            ? new ErrorInstallOption(path)
            : new ErrorInstallOption(
                path, detail.Version, detail.IsRunning,
                detail.InputCount, detail.SizeBytes, detail.SavedAt);

    /// <summary>
    /// Build the model for one of the three dialog errors. Throws on any other error: this is the
    /// dialog path, and a non-dialog error reaching here is a caller bug, not a state to render.
    /// </summary>
    public static ErrorDialogModel Build(CoreError error) => Build(error, describe: null);

    /// <param name="describe">
    /// What each error-2 candidate turns out to be, when somebody can look. Null — and a null
    /// answer for any one candidate — leaves that row with its path and its radio, which is what
    /// every row had before this existed.
    /// </param>
    public static ErrorDialogModel Build(
        CoreError error, Func<string, ErrorInstallDetail?>? describe) => error switch
    {
        // 2 — two installations. Neutral. The chooser lists what Core found; the user picks one.
        // The answer must persist (08-settings-persistence.md) — the caller reads ChosenPath after
        // ShowDialog and writes it to settings, which is why the model exposes the selected option.
        MultiplePackagesFound { Candidates: var candidates } => new ErrorDialogModel(
            Title: AppError.ByCode(2).Title,
            Body: AppError.ByCode(2).Body,
            Weight: ErrorWeight.Neutral,
            Note: null,
            Options: candidates.Select(c => Describe(c, describe?.Invoke(c))).ToList(),
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
