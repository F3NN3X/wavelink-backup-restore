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

    /// <summary>
    /// Where deleted snapshots wait. A directory inside the store, so it travels with it and
    /// behaves identically on every volume type — unlike the Recycle Bin, which does not exist
    /// on network shares.
    /// </summary>
    public const string TrashFolderName = ".trash";

    public string StorePath => storePath;

    // Public: the settings dialog's trash row prints it in its mono line (TrashRowModel.Build takes
    // it), and emptying re-detects per volume through it.
    public string TrashPath => Path.Combine(storePath, TrashFolderName);

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
    public IReadOnlyList<Snapshot> List() => ReadSnapshotsIn(storePath);

    private IReadOnlyList<Snapshot> ReadSnapshotsIn(string directoryPath)
    {
        if (!fileSystem.DirectoryExists(directoryPath)) return [];

        var snapshots = new List<Snapshot>();

        foreach (var directory in fileSystem.EnumerateDirectories(directoryPath, "*"))
        {
            // The trash lives inside the store, so listing the store must skip it — otherwise
            // deleting something would appear to do nothing.
            if (string.Equals(Path.GetFileName(directory), TrashFolderName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

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

    /// <summary>
    /// Moves a snapshot into the trash. **Nothing is destroyed here.**
    ///
    /// A plain directory move: no interop, no rename, no re-hashing. The id already carries a
    /// timestamp and a content hash, and identity lives in the manifest ([[ADR-003]]), so the
    /// moved directory is still a valid, verifiable snapshot in its new home.
    ///
    /// <see cref="EmptyTrash"/> is the destructive half.
    /// </summary>
    public Result Delete(string id)
    {
        var found = Get(id);
        if (!found.IsSuccess) return Result.Fail(found.Error);

        var destination = Path.Combine(TrashPath, found.Value.Id);

        // Same id trashed twice — the user restored one by hand and deleted again. Rare, and a
        // silent overwrite would destroy the earlier copy.
        for (var attempt = 2; fileSystem.DirectoryExists(destination); attempt++)
        {
            destination = Path.Combine(TrashPath, SnapshotId.WithSuffix(found.Value.Id, attempt));
        }

        try
        {
            fileSystem.CreateDirectory(TrashPath);
            fileSystem.MoveDirectory(found.Value.Directory, destination);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new StoreUnavailable(found.Value.Directory, ex.Message);
        }

        return Result.Ok();
    }

    /// <summary>What is in the trash, newest first. Same shape as <see cref="List"/>.</summary>
    public IReadOnlyList<Snapshot> ListTrash() => ReadSnapshotsIn(TrashPath);

    /// <summary>
    /// Whether a snapshot still matches its recorded hashes. Lives here rather than on the
    /// caller because the store already owns the filesystem, and threading one into every
    /// consumer just to build a guard would be dependency for its own sake.
    ///
    /// **Not called by <see cref="List"/>** — that would rehash the whole store on every
    /// window open. Callers verify what they are about to act on.
    /// </summary>
    public Result<SnapshotManifest> Verify(Snapshot snapshot) =>
        new SnapshotGuard(fileSystem).Verify(snapshot.Directory);

    /// <summary>
    /// Count and total bytes, for the Settings row. Cheap — it reads manifests, not files.
    /// </summary>
    public (int Count, long Bytes) TrashSize()
    {
        var trashed = ListTrash();
        var bytes = trashed.Sum(s => s.Manifest.Files.Values.Sum(f => f.SizeBytes));

        return (trashed.Count, bytes);
    }

    /// <summary>
    /// Whether emptying will be recoverable. **Ask before promising an undo** — the answer is
    /// false on network shares and many removable volumes, and the store is user-chosen.
    /// </summary>
    public bool TrashGoesToRecycleBin(IRecycleBin recycleBin) => recycleBin.IsAvailableFor(TrashPath);

    /// <summary>
    /// The destructive half. Hands each trashed snapshot to the Recycle Bin where one exists,
    /// and deletes it outright where one does not.
    ///
    /// Best effort per snapshot: one directory locked by a sync client must not strand the
    /// rest. Returns what actually went.
    /// </summary>
    public IReadOnlyList<Snapshot> EmptyTrash(IRecycleBin recycleBin)
    {
        var toRecycleBin = TrashGoesToRecycleBin(recycleBin);
        var emptied = new List<Snapshot>();

        foreach (var snapshot in ListTrash())
        {
            try
            {
                if (toRecycleBin)
                {
                    if (!recycleBin.Send(snapshot.Directory).IsSuccess) continue;
                }
                else
                {
                    fileSystem.DeleteDirectory(snapshot.Directory);
                }

                emptied.Add(snapshot);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Skip it; the next empty will try again.
            }
        }

        return emptied;
    }

    /// <summary>
    /// Hashes bytes the same way the fingerprint does. Phase 3 uses this to decide whether a
    /// capture is worth storing; nothing consults it yet.
    /// </summary>
    public static string HashOf(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
}
