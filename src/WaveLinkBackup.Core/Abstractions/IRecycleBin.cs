using WaveLinkBackup.Core.Results;

namespace WaveLinkBackup.Core.Abstractions;

/// <summary>
/// The Windows Recycle Bin, as a seam.
///
/// It is a seam rather than a call because the implementation needs shell interop, and
/// <c>WaveLinkBackup.Core</c> deliberately targets <c>net10.0</c> with a build guard
/// (<c>GuardNoDesktopFramework</c>) that rejects the Windows Desktop ref pack. The interface
/// lives here; the interop lives in a shell. "Deleting is a platform gesture, not a file
/// operation" is a true statement worth encoding.
///
/// Only <see cref="SnapshotStore.EmptyTrash"/> uses it. Ordinary deletion is a directory move
/// into <c>.trash</c> and touches none of this, which is the point, because the Recycle Bin is
/// unavailable on exactly the volumes a careful person keeps backups on.
/// </summary>
public interface IRecycleBin
{
    /// <summary>
    /// Whether the Recycle Bin covers this path. False is normal, not an error: network
    /// shares and many removable volumes have no Recycle Bin, and the backup store is
    /// user-chosen. Callers must ask before promising the user an undo.
    /// </summary>
    bool IsAvailableFor(string path);

    /// <summary>
    /// Sends a directory to the Recycle Bin. Only called when <see cref="IsAvailableFor"/>
    /// returned true for it.
    /// </summary>
    Result Send(string path);
}
