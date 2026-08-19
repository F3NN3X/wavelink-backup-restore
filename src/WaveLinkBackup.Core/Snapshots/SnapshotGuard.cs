using System.Security.Cryptography;
using WaveLinkBackup.Core.Abstractions;
using WaveLinkBackup.Core.Results;

namespace WaveLinkBackup.Core.Snapshots;

/// <summary>
/// Answers one question before a restore: "did we write this, and does it still match?"
///
/// Replaces upstream's ValidateManagedPath, which refused anything not matching
/// ^Settings\.json\.backup-\d{8}-\d{9}$ beside Settings.json. That regex blocks custom names
/// AND custom locations AND puts backups in the directory an MSIX reset deletes - three
/// problems from one design choice.
///
/// The instinct behind it was right: a mistyped path must never write arbitrary bytes into a
/// config file. So the assertion is kept and moved from the NAME to the CONTENTS, which also
/// catches something the regex never could - a snapshot corrupted after it was written, by a
/// failed sync, a bad disk, or someone editing it.
/// </summary>
public sealed class SnapshotGuard(IFileSystem fileSystem)
{
    /// <summary>Reads and verifies a snapshot directory. Success means it is safe to restore.</summary>
    public Result<SnapshotManifest> Verify(string snapshotDirectory)
    {
        var manifestPath = Path.Combine(snapshotDirectory, SnapshotManifest.ManifestFileName);

        if (!fileSystem.FileExists(manifestPath))
        {
            return new NotASnapshot(snapshotDirectory, "it has no manifest.json");
        }

        byte[] manifestBytes;
        try
        {
            manifestBytes = fileSystem.ReadSharedBytes(manifestPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new NotASnapshot(snapshotDirectory, ex.Message);
        }

        var manifest = ManifestSerializer.Read(manifestBytes);
        if (!manifest.IsSuccess) return manifest;

        // Every recorded file must exist AND still hash to what the manifest says.
        foreach (var (name, expected) in manifest.Value.Files)
        {
            var path = SnapshotManifest.PathIn(snapshotDirectory, name);

            if (!fileSystem.FileExists(path))
            {
                return new SnapshotCorrupted(snapshotDirectory, $"'{name}' is missing");
            }

            byte[] bytes;
            try
            {
                bytes = fileSystem.ReadSharedBytes(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return new SnapshotCorrupted(snapshotDirectory, $"'{name}' could not be read: {ex.Message}");
            }

            if (bytes.LongLength != expected.SizeBytes)
            {
                return new SnapshotCorrupted(snapshotDirectory,
                    $"'{name}' is {bytes.LongLength:N0} bytes but should be {expected.SizeBytes:N0}");
            }

            var actual = Convert.ToHexStringLower(SHA256.HashData(bytes));
            if (!string.Equals(actual, expected.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                return new SnapshotCorrupted(snapshotDirectory, $"'{name}' has changed since it was backed up");
            }
        }

        return manifest;
    }
}
