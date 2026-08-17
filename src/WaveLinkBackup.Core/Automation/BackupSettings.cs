using WaveLinkBackup.Core.Snapshots;

namespace WaveLinkBackup.Core.Automation;

/// <summary>
/// What the user can change. Mirrors the Settings dialog, which has no Save button - every
/// control commits immediately, so this is read, not edited-then-applied.
///
/// Tier toggles (presets, plugin files) arrive in phase 6; adding them here now would be a
/// setting nothing reads.
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
public sealed record BackupSettings(
    string StorePath,
    bool AutoBackupEnabled = true,
    int AutoBackupKeepCount = SnapshotRetention.DefaultKeepCount,
    string? ChosenWaveLinkPath = null)
{
    public static BackupSettings Default => new(SnapshotStore.DefaultStorePath);
}
