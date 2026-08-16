using System.Security.Cryptography;
using WaveLinkBackup.Core.Abstractions;
using WaveLinkBackup.Core.Analysis;
using WaveLinkBackup.Core.Results;

namespace WaveLinkBackup.Core.Snapshots;

/// <summary>A snapshot in the store: its manifest, and where it lives.</summary>
public sealed record Snapshot(string Id, string Directory, SnapshotManifest Manifest)
{
    public string SettingsPath => Path.Combine(Directory, SnapshotManifest.SettingsFileName);
}

/// <summary>
/// Where snapshots live. OUTSIDE LocalState, always.
///
/// This is the fix for the critical inherited defect: upstream writes
/// Settings.json.backup-(ts) beside Settings.json, inside the MSIX package's LocalState -
/// which resetting or uninstalling the package deletes wholesale, taking every backup with
/// it. The backups are destroyed by exactly the event you would want to recover from.
/// See ADR-003 and technical-debt.md 1.1.
/// </summary>
public sealed class SnapshotStore(IFileSystem fileSystem, IClock clock, string storePath)
{
    /// <summary>
    /// The default store, resolved through GetFolderPath rather than a composed string -
    /// %LOCALAPPDATA% is redirected on some corporate and OneDrive setups.
    /// </summary>
    public static string DefaultStorePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "WaveLinkBackup");

    public string StorePath => storePath;

    /// <summary>Writes a snapshot from settings bytes that have already been analysed.</summary>
    public Result<Snapshot> Write(
        byte[] settingsBytes,
        SettingsAnalysisResult analysis,
        SnapshotTrigger trigger,
        string displayName,
        string notes = "")
    {
        try
        {
            fileSystem.CreateDirectory(storePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new StoreUnavailable(storePath, ex.Message);
        }

        var createdUtc = clock.UtcNow;
        var fingerprint = analysis.Fingerprint;

        var id = SnapshotId.Create(createdUtc, fingerprint.Sha256);
        var directory = Path.Combine(storePath, id);

        // Same minute, same content hash means the same configuration - rare, and meaningful
        // rather than accidental. Phase 3's dedup will usually skip the write entirely.
        for (var attempt = 2; fileSystem.DirectoryExists(directory); attempt++)
        {
            id = SnapshotId.WithSuffix(SnapshotId.Create(createdUtc, fingerprint.Sha256), attempt);
            directory = Path.Combine(storePath, id);
        }

        var manifest = new SnapshotManifest(
            SchemaVersion: SnapshotManifest.CurrentSchemaVersion,
            DisplayName: displayName,
            Notes: notes,
            CreatedUtc: createdUtc,
            Trigger: trigger,
            SettingsSha256: fingerprint.Sha256,
            WaveLinkVersion: analysis.WaveLinkVersion,
            InputCount: fingerprint.InputCount,
            InputNames: fingerprint.InputNames,
            EffectCount: fingerprint.EffectCount,
            EffectChannelCount: fingerprint.EffectChannelCount,
            HasDuplicateKeys: analysis.Report.HasCaseInsensitiveDuplicateKeys,
            Tiers: ["settings"],
            Files: new Dictionary<string, SnapshotFile>(StringComparer.Ordinal)
            {
                [SnapshotManifest.SettingsFileName] =
                    new(fingerprint.Sha256, settingsBytes.LongLength),
            });

        try
        {
            fileSystem.CreateDirectory(directory);

            // Settings first, manifest last. The manifest is what makes a directory a
            // snapshot, so writing it last means an interrupted write leaves a directory
            // the guard rejects rather than one it half-accepts.
            fileSystem.WriteBytes(Path.Combine(directory, SnapshotManifest.SettingsFileName), settingsBytes);
            fileSystem.WriteBytes(
                Path.Combine(directory, SnapshotManifest.ManifestFileName),
                ManifestSerializer.Write(manifest));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new StoreUnavailable(directory, ex.Message);
        }

        return new Snapshot(id, directory, manifest);
    }

    /// <summary>
    /// Every readable snapshot, newest first. Unreadable directories are SKIPPED rather than
    /// failing the listing - one corrupt snapshot must not hide the rest, which are exactly
    /// what the user needs at that moment.
    /// </summary>
    public IReadOnlyList<Snapshot> List()
    {
        if (!fileSystem.DirectoryExists(storePath)) return [];

        var guard = new SnapshotGuard(fileSystem);
        var snapshots = new List<Snapshot>();

        foreach (var directory in fileSystem.EnumerateDirectories(storePath, "*"))
        {
            var manifestPath = Path.Combine(directory, SnapshotManifest.ManifestFileName);
            if (!fileSystem.FileExists(manifestPath)) continue;

            byte[] bytes;
            try { bytes = fileSystem.ReadSharedBytes(manifestPath); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { continue; }

            // Listing reads the manifest only - not every file's hash. Verification is a
            // restore-time concern; making it a listing concern would rehash the whole store
            // on every window open.
            var manifest = ManifestSerializer.Read(bytes);
            if (!manifest.IsSuccess) continue;

            snapshots.Add(new Snapshot(Path.GetFileName(directory), directory, manifest.Value));
        }

        return [.. snapshots.OrderByDescending(s => s.Manifest.CreatedUtc)];
    }

    public Result<Snapshot> Get(string id)
    {
        var snapshot = List().FirstOrDefault(s => string.Equals(s.Id, id, StringComparison.OrdinalIgnoreCase));
        return snapshot is null ? new SnapshotNotFound(id) : snapshot;
    }

    /// <summary>
    /// Renames a snapshot. A METADATA WRITE - no directory is moved, nothing is sanitised,
    /// and a name containing slashes or quotes is simply a string. That property is the whole
    /// reason the display name never appears in a path.
    /// </summary>
    public Result<Snapshot> Rename(string id, string displayName, string? notes = null)
    {
        var found = Get(id);
        if (!found.IsSuccess) return found;

        var updated = found.Value.Manifest with
        {
            DisplayName = displayName,
            Notes = notes ?? found.Value.Manifest.Notes,
        };

        try
        {
            fileSystem.WriteBytes(
                Path.Combine(found.Value.Directory, SnapshotManifest.ManifestFileName),
                ManifestSerializer.Write(updated));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new StoreUnavailable(found.Value.Directory, ex.Message);
        }

        return found.Value with { Manifest = updated };
    }

    public Result Delete(string id)
    {
        var found = Get(id);
        if (!found.IsSuccess) return Result.Fail(found.Error);

        try
        {
            fileSystem.DeleteDirectory(found.Value.Directory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new StoreUnavailable(found.Value.Directory, ex.Message);
        }

        return Result.Ok();
    }

    /// <summary>
    /// Hashes bytes the same way the fingerprint does. Phase 3 uses this to decide whether a
    /// capture is worth storing; nothing consults it yet.
    /// </summary>
    public static string HashOf(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
}
