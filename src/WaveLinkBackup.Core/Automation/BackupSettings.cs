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
public sealed record BackupSettings(
    string StorePath,
    bool AutoBackupEnabled = true,
    int AutoBackupKeepCount = SnapshotRetention.DefaultKeepCount,
    string? ChosenWaveLinkPath = null,
    bool IncludePresets = true,
    bool IncludePluginFiles = false)
{
    public static BackupSettings Default => new(SnapshotStore.DefaultStorePath);
}
