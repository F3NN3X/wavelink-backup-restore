using WaveLinkBackup.Core.Results;

namespace WaveLinkBackup.App.ViewModels;

/// <summary>Where an error is shown. 06-errors.md, "Placement rule — scope decides".</summary>
public enum ErrorPlacement
{
    /// <summary>A standing fact about the machine, true until something changes (error 1).</summary>
    StatusStrip,

    /// <summary>The consequence of something the user just pressed (3, 5, 6, 7, 10, 11). All neutral fill.</summary>
    InlineStrip,

    /// <summary>A decision is required before work can continue (2, 4, 8, 9).</summary>
    Dialog,

    /// <summary>Nothing can be listed at all — the screen replaces the list (12).</summary>
    ReplacesList,
}

/// <summary>
/// How loud an error is. The weight rule: neutral unless the user's configuration is not whole.
/// A missing *location* is neutral — nothing is broken and nothing is lost; a location is simply
/// missing. An error that means a write or restore did not produce a whole config is amber.
/// </summary>
public enum ErrorWeight
{
    Neutral,
    Amber,
}

/// <summary>
/// The thirteen errors as data, so placement and weight are decided in ONE testable place rather
/// than scattered across views (06-errors.md, and 13-elevation.md for the thirteenth). The catalog is the single source of truth for the
/// weight rule; a future edit that re-weights an error fails the catalog tests before it ships.
///
/// Copy here is the designed sentence/heading per error, taken verbatim from 06-errors.md. Where
/// 06 prints a mono *meta* line (a path, a checksum, a PID) that value is machine-specific and
/// arrives at render time — it is not hard-coded in the catalog, which keeps every string
/// assertable from a table.
///
/// <b>Weight rule as 06 states it:</b> "Neutral if nothing happened. Amber only if the
/// configuration — live or restorable — is not whole." The inline strips are ALL neutral fill
/// (they are refusals: nothing was written, nothing changed). Only the malformed-settings dialog
/// (4) is amber, because there the LIVE configuration is the thing that is not whole.
/// </summary>
public sealed record AppError(
    int Code,
    ErrorPlacement Placement,
    ErrorWeight Weight,
    string Title,
    string Body,
    string? MonoLine = null)
{
    /// <summary>All thirteen, in the order the design numbers them.</summary>
    public static IReadOnlyList<AppError> All { get; } = new[]
    {
        // 1 — Wave Link not found / settings file missing. A standing fact → status strip.
        // The design renders this with an amber dot + text (--wl-warn) because the LIVE config
        // cannot be read at all, so it is not whole. (06 "Status strip (1)".)
        new AppError(1, ErrorPlacement.StatusStrip, ErrorWeight.Amber,
            "WAVE LINK NOT FOUND ON THIS COMPUTER",
            "Wave Link was not found on this computer, so there is nothing to back up yet."),

        // 2 — Two Wave Link installations, none chosen. A decision is required → chooser dialog.
        // Neutral: no config is damaged, a choice is simply needed. (06 "Dialogs §2".)
        new AppError(2, ErrorPlacement.Dialog, ErrorWeight.Neutral,
            "Two Wave Link installations",
            "Both have their own settings file. Pick the one you actually use — the other stays untouched."),

        // 3 — Could not read the settings file (locked/unreadable). Consequence of a press → inline.
        // All inline strips are neutral fill: nothing was written, nothing changed. (06 "Inline strips §3".)
        new AppError(3, ErrorPlacement.InlineStrip, ErrorWeight.Neutral,
            "Could not read the settings file",
            "Could not read the settings file, so nothing was backed up."),

        // 4 — Malformed settings file. A decision is required (retry / restore last good) → dialog.
        // AMBER: the live configuration is the thing that is not whole. (06 "Dialogs §4".)
        new AppError(4, ErrorPlacement.Dialog, ErrorWeight.Amber,
            "Wave Link's settings file is malformed",
            "Nothing was backed up — copying a broken file would give you a broken backup. " +
            "Wave Link may be mid-write; try again in a moment."),

        // 5 — Wave Link still running, so nothing was written. Consequence of a press → inline. Neutral fill.
        new AppError(5, ErrorPlacement.InlineStrip, ErrorWeight.Neutral,
            "Wave Link is still running",
            "Wave Link is still running, so nothing was written."),

        // 6 — The settings file couldn't be replaced (access denied). Consequence of a press → inline. Neutral fill.
        new AppError(6, ErrorPlacement.InlineStrip, ErrorWeight.Neutral,
            "The settings file couldn't be replaced",
            "The settings file couldn't be replaced. Your old settings are still in place."),

        // 7 — The backup's manifest can't be read. Consequence of a press → inline. Neutral fill.
        new AppError(7, ErrorPlacement.InlineStrip, ErrorWeight.Neutral,
            "This backup's manifest can't be read",
            "This backup's manifest can't be read, so we can't tell what's inside it."),

        // 8 — Made by a newer version of Wave Link Backup. A decision is required (update) → dialog.
        // NEUTRAL: the backup itself is fine; this copy just doesn't understand the format yet.
        // (06 "Dialogs §8".) No Restore button at all — it would not work.
        new AppError(8, ErrorPlacement.Dialog, ErrorWeight.Neutral,
            "This backup was made by a newer version",
            "It uses a format this copy doesn't understand yet. Update Wave Link Backup and it will " +
            "restore normally. The backup itself is fine."),

        // 9 — That folder is not a Wave Link Backup. A decision is required (choose another / keep) → dialog.
        // Appears in Settings, in place, after "Change folder…". Neutral: nothing lost, a location is wrong.
        new AppError(9, ErrorPlacement.Dialog, ErrorWeight.Neutral,
            "That folder is not a Wave Link Backup",
            "That folder isn't a Wave Link Backup folder. It has files in it but no manifest, so nothing " +
            "in there can be listed or checked. Point at the folder that holds your backups, or start fresh in an empty one."),

        // 10 — This backup is damaged and was not restored. Consequence of a press → inline. Neutral fill.
        new AppError(10, ErrorPlacement.InlineStrip, ErrorWeight.Neutral,
            "This backup is damaged",
            "This backup is damaged and was not restored. Your mixer hasn't changed."),

        // 11 — No backup with that id was found. Consequence of a press → inline. Neutral fill.
        new AppError(11, ErrorPlacement.InlineStrip, ErrorWeight.Neutral,
            "No backup with that id was found",
            "No backup with that id was found. Pick another from the list."),

        // 12 — The backup folder can't be used (missing/moved/unwritable). Nothing can be listed → replaces the list.
        // Same screen as H's missing folder in 08-settings-persistence.md. Neutral: nothing broken, nothing lost.
        new AppError(12, ErrorPlacement.ReplacesList, ErrorWeight.Neutral,
            "The backup folder can't be used",
            "The backup folder is missing or cannot be used right now. Nothing is lost — point at a folder to continue."),

        // 13 — Administrator rights declined for tier 4. Consequence of a press → inline. Neutral.
        //
        // Neutral is where the weight rule earns its keep. Amber means the configuration - live or
        // restorable - is not whole; declining changed NOTHING. The plug-ins on this machine are
        // exactly as they were, the backup still holds them, and the settings and presets went
        // back. It is a refusal, like every other neutral strip. Where a plug-in genuinely is
        // missing, the dialog's amber block already said so before the button was pressed.
        // (13-elevation.md §13.)
        new AppError(13, ErrorPlacement.InlineStrip, ErrorWeight.Neutral,
            "The plug-in files were left alone",
            "The plug-in files were left alone. Your settings and presets were restored."),
    };

    /// <summary>
    /// Administrator rights were declined at the UAC prompt, so tier 4 was skipped. Named rather
    /// than looked up by number at the call site, because it is the one error with no
    /// <see cref="CoreError"/> behind it — nothing in Core failed, and nothing in Core can know
    /// what a person clicked.
    /// </summary>
    public static AppError ElevationDeclined => ByCode(13);

    /// <summary>Look up one by its code (1–13).</summary>
    public static AppError ByCode(int code) => All[code - 1];
}

/// <summary>The signals the shell already has when something goes wrong. All value types — pure.</summary>
public sealed record CoreSignal(
    /// <summary>A Core error, when an operation returned one (null for healthy / standing facts).</summary>
    CoreError? Error = null,
    /// <summary>Wave Link was found on this machine (false → the "not found" standing fact, error 1).</summary>
    bool WaveLinkFound = true,
    /// <summary>The backup folder exists and is usable (false → error 12 territory).</summary>
    bool FolderUsable = true);

/// <summary>
/// The one place a Core signal becomes an <see cref="AppError"/> — or null when there is no error.
/// Pure: in comes the signals the shell already holds, out goes the designed error (or nothing).
/// This is what keeps "which of the twelve" from being re-decided in every view.
/// </summary>
public static class AppErrorMapper
{
    public static AppError? FromCoreSignal(CoreSignal signal)
    {
        // A standing fact beats an operation error: if Wave Link isn't there, that is what the
        // status strip says, regardless of what a backup attempt reported.
        if (!signal.WaveLinkFound)
            return AppError.ByCode(1);

        var error = signal.Error;
        if (error is not null)
        {
            switch (error)
            {
                case MultiplePackagesFound:
                    return AppError.ByCode(2);
                case SettingsUnreadable:
                    return AppError.ByCode(3);
                case MalformedSettings:
                    return AppError.ByCode(4);
                case WaveLinkStillRunning:
                    return AppError.ByCode(5);
                case WriteFailed:
                    return AppError.ByCode(6);
                case MalformedManifest:
                    return AppError.ByCode(7);
                case UnsupportedSnapshotSchema:
                    return AppError.ByCode(8);
                case NotASnapshot:
                    return AppError.ByCode(9);
                case SnapshotCorrupted:
                    return AppError.ByCode(10);
                case SnapshotNotFound:
                    return AppError.ByCode(11);
                case StoreUnavailable:
                    // The folder can't be used at all → the full screen, not an inline strip.
                    return AppError.ByCode(12);
                default:
                    return null;
            }
        }

        // No operation error: a standing fact about the folder.
        if (!signal.FolderUsable)
            return AppError.ByCode(12);

        return null;
    }

    /// <summary>
    /// The crash-report pointer for a failed restore (technical-debt.md §8.1a). 06-errors.md has no
    /// "something unexpected happened" surface, so the evidence lives in the redacted report and the
    /// one place the app can still speak after an unexpected fault — the danger row — points at it.
    /// The pointer is appended only when BOTH hold: the failure carries no designed inline-strip code
    /// (a designed error has its own surface, and a crash is not that error), and a report was written
    /// this run. Null otherwise — the row stays exactly as it was before §8.1a.
    /// </summary>
    public static string? CrashReportPointer(string? failureMessage, CoreError? coreError, string? crashReportPath)
    {
        if (string.IsNullOrEmpty(crashReportPath))
            return null;

        // A designed inline-strip error renders its own surface; the danger row never shows for it.
        var appError = coreError is { } error ? FromCoreSignal(new CoreSignal(error)) : null;
        if (appError is not null && appError.Placement == ErrorPlacement.InlineStrip)
            return null;

        var failure = string.IsNullOrEmpty(failureMessage) ? "The restore failed." : failureMessage!;
        return failure + "\nDetails in the crash report: " + crashReportPath;
    }
}
