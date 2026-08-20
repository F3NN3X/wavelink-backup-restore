using WaveLinkBackup.Core.Snapshots;

namespace WaveLinkBackup.Core.Automation;

/// <summary>
/// What the user can change. Mirrors the Settings dialog, which has no Save button - every
/// control commits immediately, so this is read, not edited-then-applied.
///
/// Shell-only preferences do NOT live here. "Closing the window hides it in the tray" and the
/// remembered window geometry are the shell's business, and Core has no window to hide and no
/// tray to hide it in (ADR-004). They persist in a file the App project owns.
/// </summary>
/// <param name="ChosenWaveLinkPath">
/// Which installation to watch and restore into, when more than one exists. Null means "not
/// chosen yet". Required by error 2: without storing the answer, the chooser asks again on
/// every launch (screens/10-decisions.md 4).
/// </param>
/// <param name="IncludePresets">
/// Tier 3 — the presets each effect saved under <c>%APPDATA%\&lt;Vendor&gt;\</c>. On by default:
/// they are the user's own irreplaceable work (their EQ curves, their gate thresholds) and about
/// 10 MB ([[ADR-006]]).
/// </param>
/// <param name="IncludePluginFiles">
/// Tier 4 — the <c>.vst3</c> files themselves. OFF by default: ~40 MB, re-downloadable from the
/// vendor, and copying one does not copy the licence.
///
/// Tiers 1 and 2 have no switch, deliberately. Together they are under half a megabyte and they
/// are the difference between a restore that works and one that leaves the user guessing; a
/// switch implies a meaningful choice, and there isn't one.
/// </param>
/// <param name="AutoBackupIntervalMinutes">
/// How close together two automatic backups may be. A **cap on change-driven backups, not a
/// timer** - nothing is written when nothing changes, so lowering it does not make the app busier
/// on a quiet machine. Sixty was a constant until the Settings dialog got a control for it, while
/// the dialog said "at most one an hour" as though it were a fact about the world.
/// </param>
/// <param name="CheckForUpdates">
/// Whether to look for a new version weekly. On by default, and it ONLY looks — screens/12: "It
/// never installs anything without you", and an available update is never a notification, a badge
/// or a banner. The switch exists because looking is still a network request the user may not
/// want made.
/// </param>
/// <param name="LastUpdateCheckUtc">
/// When the last check ran, successful or not. Recording a FAILED look too is deliberate:
/// otherwise a machine that is offline for a fortnight re-checks on every tick.
/// </param>
/// <param name="DailyBackupMinutes">
/// Minutes past local midnight to take a backup at each day, or null for "only when things
/// change". Stored as an int rather than a <see cref="TimeOnly"/> because this record is written
/// to JSON by a hand-rolled serializer (no reflection, [[ADR-001]]), and a plain number needs no
/// format to agree on. <see cref="DailyBackupAt"/> is the shape everything else uses.
/// </param>
public sealed record BackupSettings(
    string StorePath,
    bool AutoBackupEnabled = true,
    int AutoBackupKeepCount = SnapshotRetention.DefaultKeepCount,
    string? ChosenWaveLinkPath = null,
    bool IncludePresets = true,
    bool IncludePluginFiles = false,
    int AutoBackupIntervalMinutes = BackupSettings.DefaultIntervalMinutes,
    int? DailyBackupMinutes = null,
    bool CheckForUpdates = true,
    DateTimeOffset? LastUpdateCheckUtc = null)
{
    /// <summary>One hour — what the interval was when it was a constant (ADR-007).</summary>
    public const int DefaultIntervalMinutes = 60;

    /// <summary>
    /// The ladder the Settings dialog's stepper moves through: 15 min to 24 h. A ladder rather than
    /// free-form minutes so every position is a number a person would actually choose, and so `−`
    /// from the bottom cannot reach zero (operations/design/screens/14-backup-timing.md).
    /// </summary>
    public static IReadOnlyList<int> IntervalLadder { get; } = [15, 30, 60, 120, 240, 720, 1440];

    /// <summary>Where the daily stepper starts when the user switches it on: 03:00.</summary>
    public const int DefaultDailyMinutes = 3 * 60;

    /// <summary>Half an hour, wrapping at midnight — the daily stepper's step.</summary>
    public const int DailyStepMinutes = 30;

    /// <summary>The daily time as a wall clock, or null when the daily backup is off.</summary>
    public TimeOnly? DailyBackupAt => DailyBackupMinutes is { } minutes
        ? TimeOnly.FromTimeSpan(TimeSpan.FromMinutes(Math.Clamp(minutes, 0, 24 * 60 - 1)))
        : null;

    public static BackupSettings Default => new(SnapshotStore.DefaultStorePath);
}
