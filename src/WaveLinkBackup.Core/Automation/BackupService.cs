using WaveLinkBackup.Core.Io;
using WaveLinkBackup.Core.Results;
using WaveLinkBackup.Core.Snapshots;

namespace WaveLinkBackup.Core.Automation;

/// <param name="Snapshot">Null when the capture was skipped as a duplicate.</param>
/// <param name="SkippedAsDuplicate">
/// True when the settings were byte-identical to the newest snapshot. Not a failure - it is
/// the system working. Wave Link rewrites Settings.json on every launch with near-identical
/// bytes, and without this the store fills with thousands of identical 43 KB copies.
/// </param>
public sealed record CaptureResult(Snapshot? Snapshot, bool SkippedAsDuplicate)
{
    public bool Captured => Snapshot is not null;
}

/// <summary>
/// Taking backups, and forgetting old ones. The one place the dedup rule and its exception
/// both live, so they cannot drift apart.
/// </summary>
/// <param name="explicitSettingsPath">
/// Passed through to every inspection. Non-null when the user supplied --settings-path,
/// which bypasses discovery entirely - the escape hatch for a machine where we cannot find
/// the install (technical-debt.md 2.2). Threaded here rather than resolved at construction
/// so the whole service honours it, including the watcher's captures.
/// </param>
/// <param name="gatherPayload">
/// What to capture beyond the settings file, asked for at capture time rather than held.
///
/// A delegate rather than a <c>TierCapture</c> and a settings record, because the tier toggles
/// live in a file the user can change while this service is running: a closure reading the
/// shell's current settings means switching a tier on takes effect on the next capture instead of
/// on the next launch. Null means this caller never looked - no plugins.json, no extra tiers
/// (<see cref="SnapshotStore.Write"/>).
/// </param>
public sealed class BackupService(
    SettingsInspector inspector,
    SnapshotStore store,
    int keepCount = SnapshotRetention.DefaultKeepCount,
    string? explicitSettingsPath = null,
    Func<SettingsInspection, SnapshotPayload?>? gatherPayload = null)
{
    /// <summary>
    /// A backup the user asked for. NEVER deduplicated.
    ///
    /// The user pressed a button; answering "nothing changed, so I did nothing" is a worse
    /// experience than a duplicate row, and it makes the button feel broken. The design is
    /// explicit that success is quiet - the new row appearing IS the confirmation - which
    /// only works if a row actually appears.
    /// </summary>
    /// <param name="progress">
    /// Byte-level progress, for the shell's backing-up strip (the in-progress spec). Only the manual
    /// path takes one: an automatic capture has no surface to report to.
    /// </param>
    public Result<Snapshot> BackUpNow(
        string displayName, string notes = "", IProgress<SnapshotWriteProgress>? progress = null)
    {
        var live = inspector.Inspect(explicitSettingsPath);
        if (!live.IsSuccess) return live.Propagate<Snapshot>();

        var written = store.Write(
            live.Value.Bytes, live.Value.Analysis, SnapshotTrigger.Manual, displayName, notes,
            gatherPayload?.Invoke(live.Value), progress);

        if (written.IsSuccess) Prune();
        return written;
    }

    /// <summary>
    /// A backup the watcher decided on. Deduplicated against the newest snapshot.
    /// </summary>
    public Result<CaptureResult> CaptureAutomatic(string displayName = "Auto")
    {
        var live = inspector.Inspect(explicitSettingsPath);
        if (!live.IsSuccess) return live.Propagate<CaptureResult>();

        // Compare against the NEWEST snapshot, not against all of them. Wave Link cycling
        // between two configurations should record both; the thing worth suppressing is the
        // same bytes twice in a row.
        var newest = store.List().FirstOrDefault();
        if (newest is not null &&
            string.Equals(newest.Manifest.SettingsSha256, live.Value.Analysis.Fingerprint.Sha256,
                          StringComparison.OrdinalIgnoreCase))
        {
            return new CaptureResult(null, SkippedAsDuplicate: true);
        }

        var written = store.Write(
            live.Value.Bytes, live.Value.Analysis, SnapshotTrigger.Automatic, displayName,
            payload: gatherPayload?.Invoke(live.Value));

        if (!written.IsSuccess) return written.Propagate<CaptureResult>();

        Prune();
        return new CaptureResult(written.Value, SkippedAsDuplicate: false);
    }

    /// <summary>
    /// Deletes automatic snapshots beyond the keep count, oldest first. Manual and
    /// pre-restore snapshots are never touched - the rule lives in
    /// <see cref="SnapshotRetention"/> and is consulted, never re-derived.
    ///
    /// Verifies each candidate before deleting it, and refuses any that fail. A corrupted
    /// backup must never push a good one out.
    ///
    /// This verifies only the CONDEMNED, which is what keeps it cheap: pruning a 30-deep store
    /// removes one or two, so it hashes one or two. Verifying at list time - the obvious
    /// alternative - would rehash the whole store every time a window opened, which is exactly
    /// the cost phase 2 declined to pay.
    /// </summary>
    public IReadOnlyList<Snapshot> Prune()
    {
        var doomed = SnapshotRetention.SelectForPruning(store.List(), keepCount);
        if (doomed.Count == 0) return [];

        var deleted = new List<Snapshot>();

        foreach (var snapshot in doomed)
        {
            // A damaged snapshot is immortal to the pruner. It stays deletable by hand -
            // making it unremovable would be a worse failure than the one this prevents.
            if (!store.Verify(snapshot).IsSuccess) continue;

            // Best effort per snapshot: one undeletable directory - locked by a sync client,
            // say - must not stop the rest, and must not fail the capture that triggered it.
            if (store.Delete(snapshot.Id).IsSuccess) deleted.Add(snapshot);
        }

        return deleted;
    }
}
