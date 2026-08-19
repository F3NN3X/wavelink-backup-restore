using WaveLinkBackup.Core.Abstractions;
using WaveLinkBackup.Core.Snapshots;

namespace WaveLinkBackup.Core.Io;

/// <summary>
/// A snapshot's own plugins.json, off disk.
///
/// **Null means the snapshot never looked** — it was written before tier 2 existed, or by a caller
/// with no capture wired. That is not the same as a snapshot which looked and found no third-party
/// plugins, and the restore warning depends on the difference: the second can honestly say
/// "nothing is missing", the first cannot say anything at all.
/// </summary>
public sealed class SnapshotPluginReader(IFileSystem fileSystem)
{
    public PluginManifest? Read(Snapshot snapshot)
    {
        if (!snapshot.Manifest.Tiers.Contains(SnapshotManifest.PluginManifestTier, StringComparer.Ordinal))
        {
            return null;
        }

        if (!fileSystem.FileExists(snapshot.PluginsPath)) return null;

        try
        {
            // Tolerant by construction: the serializer degrades field by field and never throws,
            // so a damaged plugins.json costs the warning its detail, never the restore.
            return PluginManifestSerializer.Read(fileSystem.ReadSharedBytes(snapshot.PluginsPath));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
