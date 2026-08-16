using WaveLinkBackup.Core.Snapshots;

namespace WaveLinkBackup.Core.Automation;

/// <summary>
/// Which snapshots to prune. PURE - a list in, a list out.
///
/// Time-based retention ("keep 90 days") is deliberately not offered. It looks uniform and is
/// wrong: it deletes the last good config from four months ago while keeping ninety identical
/// copies from this week. Hash-dedup plus a count already produces the right behaviour,
/// because identical days cost nothing. ADR-007.
/// </summary>
public static class SnapshotRetention
{
    public const int DefaultKeepCount = 30;

    /// <summary>
    /// The automatic snapshots to delete, oldest first.
    ///
    /// MANUAL AND PRE-RESTORE SNAPSHOTS ARE NEVER RETURNED, at any count, ever. A user who
    /// named a snapshot has said it matters, and a pre-restore snapshot is someone's way back
    /// from a mistake. The rule is <see cref="SnapshotManifest.IsPrunable"/>, consulted rather
    /// than re-derived here.
    /// </summary>
    public static IReadOnlyList<Snapshot> SelectForPruning(
        IEnumerable<Snapshot> snapshots,
        int keepCount = DefaultKeepCount)
    {
        if (keepCount < 0) keepCount = 0;

        var prunable = snapshots
            .Where(s => s.Manifest.IsPrunable)
            .OrderByDescending(s => s.Manifest.CreatedUtc)
            .ToList();

        // Keep the newest `keepCount`; everything older goes. Skip() on a
        // newest-first list means the tail is the oldest, which is what we want to lose.
        return [.. prunable.Skip(keepCount).OrderBy(s => s.Manifest.CreatedUtc)];
    }
}
