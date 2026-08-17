using System.IO;
using WaveLinkBackup.Core.Abstractions;
using WaveLinkBackup.Core.Snapshots;

namespace WaveLinkBackup.App.Hosting;

/// <summary>The three row states screen 1 draws.</summary>
public enum SnapshotHealth
{
    /// <summary>Verified, and validation passed when it was taken.</summary>
    Whole,

    /// <summary>
    /// Validation failed WHEN THE BACKUP WAS TAKEN. Contents are readable, it is still
    /// restorable, and it may be the only copy that exists. A warning - amber.
    /// </summary>
    Suspect,

    /// <summary>
    /// The recorded checksums no longer match: corrupted AFTER writing. Contents are
    /// unknowable, so it cannot be restored. A refusal - and deliberately NOT amber, because
    /// amber is a claim about contents and a damaged backup has none. It loses its colour
    /// rather than gaining one (02).
    /// </summary>
    Damaged,
}

/// <param name="ManifestBytes">What the manifest says this snapshot's files weigh.</param>
/// <param name="ActualBytes">What the settings file weighs now, or null when it could not be read.</param>
public sealed record HealthVerdict(
    SnapshotHealth Health,
    long ManifestBytes,
    long? ActualBytes,
    DateTimeOffset CheckedAt);

/// <summary>
/// Hashes the store so the list can say DAMAGED.
///
/// SnapshotStore.List() reads manifests only and is right not to hash - its own comment says
/// verification is a restore-time concern, and hashing there would rehash every backup on every
/// window open. So the shell verifies on its OWN thread, on open and on F5, and rows flip from
/// WHOLE or SUSPECT to DAMAGED as answers come back.
///
/// Tier 1 is one small settings.json per snapshot, so this is milliseconds today. The cost
/// arrives in phase 6 with presets and plugins - on a background thread, where it can be
/// reported, rather than in a window that will not open.
/// </summary>
public sealed class HealthProbe(SnapshotStore store, IFileSystem fileSystem, IClock clock)
{
    /// <summary>
    /// Pure, and a table test. Damaged OUTRANKS suspect: "contents are unknowable" and
    /// "contents are not whole" cannot both be drawn, and the first is the one still true.
    /// </summary>
    public static SnapshotHealth Decide(bool verified, bool isSuspect) =>
        !verified ? SnapshotHealth.Damaged
        : isSuspect ? SnapshotHealth.Suspect
        : SnapshotHealth.Whole;

    public HealthVerdict Check(Snapshot snapshot)
    {
        var verified = store.Verify(snapshot).IsSuccess;
        var manifestBytes = snapshot.Manifest.Files.Values.Sum(f => f.SizeBytes);

        // Measured only when it matters: 02's damaged detail line needs both figures, and a
        // whole row needs neither.
        var actualBytes = verified ? manifestBytes : ActualSizeOf(snapshot);

        return new HealthVerdict(
            Decide(verified, snapshot.Manifest.IsSuspect), manifestBytes, actualBytes, clock.UtcNow);
    }

    /// <summary>
    /// Null, not zero, when the file has gone. "The file is 0 KB" and "there is no file" are
    /// different sentences and only one of them is true.
    /// </summary>
    private long? ActualSizeOf(Snapshot snapshot)
    {
        var path = snapshot.SettingsPath;

        if (!fileSystem.FileExists(path)) return null;

        try { return fileSystem.ReadSharedBytes(path).LongLength; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }

    /// <summary>
    /// Verifies every snapshot off the UI thread, reporting each as it lands rather than in one
    /// batch at the end - a store whose third backup is damaged should say so without waiting
    /// for the thirtieth.
    ///
    /// <paramref name="report"/> is invoked on the PROBE's thread. The caller marshals.
    /// </summary>
    public Task ProbeAsync(
        IReadOnlyList<Snapshot> snapshots,
        Action<string, HealthVerdict> report,
        CancellationToken token) => Task.Run(() =>
    {
        foreach (var snapshot in snapshots)
        {
            // Checked before the work AND before the report: an F5 mid-probe must not leave the
            // previous run writing verdicts into the list that replaced it.
            if (token.IsCancellationRequested) return;

            var verdict = Check(snapshot);

            if (token.IsCancellationRequested) return;

            report(snapshot.Id, verdict);
        }
    }, token);
}
