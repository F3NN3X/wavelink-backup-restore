using WaveLinkBackup.Core.Results;

namespace WaveLinkBackup.App.ViewModels;

/// <summary>Where an error is shown. 06-errors.md, "Placement rule — scope decides".</summary>
public enum ErrorPlacement
{
    /// <summary>A standing fact about the machine, true until something changes (error 1).</summary>
    StatusStrip,

    /// <summary>The consequence of something the user just pressed (3, 5, 6, 7, 10, 11).</summary>
    InlineStrip,

    /// <summary>A decision is required before work can continue (2, 4, 8).</summary>
    Dialog,

    /// <summary>Nothing can be listed at all — the screen replaces the list (9, 12).</summary>
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
/// The twelve errors as data, so placement and weight are decided in ONE testable place rather
/// than scattered across views (06-errors.md). The catalog is the single source of truth for the
/// weight rule; a future edit that re-weights an error fails the catalog tests before it ships.
///
/// Copy here is the designed sentence/heading per error. Where 06 prints a mono *meta* line
/// (a path, a checksum, a PID) that value is machine-specific and arrives at render time — it is
/// not hard-coded in the catalog, which keeps every string assertable from a table.
/// </summary>
public sealed record AppError(
    int Code,
    ErrorPlacement Placement,
    ErrorWeight Weight,
    string Title,
    string Body,
    string? MonoLine = null)
{
    /// <summary>All twelve, in the order 06-errors.md numbers them.</summary>
    public static IReadOnlyList<AppError> All { get; } = new[]
    {
        // 1 — Wave Link not running / settings file missing. A standing fact → status strip, neutral.
        new AppError(1, ErrorPlacement.StatusStrip, ErrorWeight.Neutral,
            "WAVE LINK NOT FOUND ON THIS COMPUTER",
            "Wave Link was not found on this computer, so there is nothing to back up yet."),

        // 2 — Multiple installs, none chosen. A decision is required → chooser dialog, neutral.
        new AppError(2, ErrorPlacement.Dialog, ErrorWeight.Neutral,
            "Two Wave Link installations",
            "Both have their own settings file. Pick the one you actually use — the other stays untouched."),

        // 3 — Backup folder unwritable (settings unreadable/locked). Consequence of a press → inline, amber.
        new AppError(3, ErrorPlacement.InlineStrip, ErrorWeight.Amber,
            "Could not read the settings file",
            "Could not read the settings file, so nothing was backed up."),

        // 4 — Disk full while writing. A decision is required (free space or retry) → dialog, amber.
        new AppError(4, ErrorPlacement.Dialog, ErrorWeight.Amber,
            "Not enough room to write the backup",
            "There is not enough free space to write this backup. Nothing was written."),

        // 5 — Backup write failed (generic). Consequence of a press → inline, amber.
        new AppError(5, ErrorPlacement.InlineStrip, ErrorWeight.Amber,
            "The settings file could not be replaced",
            "The settings file couldn't be replaced. Your old settings are still in place."),

        // 6 — Corrupt / unreadable backup on restore. Consequence of a press → inline, amber.
        new AppError(6, ErrorPlacement.InlineStrip, ErrorWeight.Amber,
            "This backup is damaged",
            "This backup is damaged and was not restored. Your mixer hasn't changed."),

        // 7 — Restore relaunch failed (Wave Link didn't come back). Consequence of a press → inline, amber.
        new AppError(7, ErrorPlacement.InlineStrip, ErrorWeight.Amber,
            "Wave Link did not come back",
            "The restore finished but Wave Link did not start again. Open it to check your mixer."),

        // 8 — Pre-restore copy failed before a restore. A decision is required → dialog, amber.
        new AppError(8, ErrorPlacement.Dialog, ErrorWeight.Amber,
            "Could not save today's settings first",
            "The pre-restore backup could not be taken, so the restore was not started. Nothing was changed."),

        // 9 — Backup folder vanished / not a valid backup folder. Nothing can be listed → replaces list, neutral.
        new AppError(9, ErrorPlacement.ReplacesList, ErrorWeight.Neutral,
            "That folder is not a Wave Link Backup",
            "That folder isn't a Wave Link Backup folder. It has files in it but no manifest, so nothing in there can be listed or checked."),

        // 10 — Automatic backup skipped, folder missing. A standing fact → status strip, neutral.
        new AppError(10, ErrorPlacement.StatusStrip, ErrorWeight.Neutral,
            "AUTOMATIC BACKUP SKIPPED · FOLDER MISSING",
            "The backup folder is missing, so the automatic backup did nothing this time."),

        // 11 — Restore rejected by analysis (SUSPECT input drop). Consequence of a press → inline, amber.
        new AppError(11, ErrorPlacement.InlineStrip, ErrorWeight.Amber,
            "No backup with that id was found",
            "No backup with that id was found. Pick another from the list."),

        // 12 — The backup folder can't be used (missing/moved/unwritable). Nothing can be listed → replaces list, neutral.
        new AppError(12, ErrorPlacement.ReplacesList, ErrorWeight.Neutral,
            "The backup folder can't be used",
            "The backup folder is missing or cannot be used right now. Nothing is lost — point at a folder to continue."),
    };

    /// <summary>Look up one of the twelve by its code (1–12).</summary>
    public static AppError ByCode(int code) => All[code - 1];
}

/// <summary>The signals the shell already has when something goes wrong. All value types — pure.</summary>
public sealed record CoreSignal(
    /// <summary>A Core error, when an operation returned one (null for healthy / standing facts).</summary>
    CoreError? Error = null,
    /// <summary>Wave Link was found on this machine (false → the "not found" standing fact).</summary>
    bool WaveLinkFound = true,
    /// <summary>The backup folder exists and is usable (false → error 10/12 territory).</summary>
    bool FolderUsable = true,
    /// <summary>A restore finished but Wave Link did not relaunch (error 7).</summary>
    bool RelaunchFailed = false);

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
                case WriteFailed:
                    // Disk full surfaces as a write failure whose reason names the space problem.
                    return error.Message.Contains("not enough", StringComparison.OrdinalIgnoreCase)
                        || error.Message.Contains("full", StringComparison.OrdinalIgnoreCase)
                        ? AppError.ByCode(4)
                        : AppError.ByCode(5);
                case SnapshotCorrupted:
                    return AppError.ByCode(6);
                case StoreUnavailable:
                    // The folder can't be used at all → the full screen, not an inline strip.
                    return AppError.ByCode(12);
                case NotASnapshot:
                    return AppError.ByCode(9);
                default:
                    // MalformedSettings, WaveLinkStillRunning, MalformedManifest,
                    // UnsupportedSnapshotSchema, SnapshotNotFound have no one of the twelve that is
                    // exactly them; they surface through their existing paths (the restore outcome
                    // strip / plan dialog), so the catalog does not claim them here.
                    return null;
            }
        }

        // No operation error: a standing fact about the folder, or a restore that finished but did
        // not bring Wave Link back.
        if (signal.RelaunchFailed)
            return AppError.ByCode(7);

        if (!signal.FolderUsable)
            return AppError.ByCode(10);

        return null;
    }
}
