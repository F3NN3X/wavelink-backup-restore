using WaveLinkBackup.Core.Snapshots;

namespace WaveLinkBackup.Core.Automation;

/// <summary>
/// What the user can change. Mirrors the Settings dialog, which has no Save button - every
/// control commits immediately, so this is read, not edited-then-applied.
///
/// Tier toggles (presets, plugin files) arrive in phase 6; adding them here now would be a
/// setting nothing reads.
/// </summary>
public sealed record BackupSettings(
    string StorePath,
    bool AutoBackupEnabled = true,
    int AutoBackupKeepCount = SnapshotRetention.DefaultKeepCount)
{
    public static BackupSettings Default => new(SnapshotStore.DefaultStorePath);
}
