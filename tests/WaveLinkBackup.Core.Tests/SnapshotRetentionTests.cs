using WaveLinkBackup.Core.Automation;
using WaveLinkBackup.Core.Snapshots;

namespace WaveLinkBackup.Core.Tests;

public sealed class SnapshotRetentionTests
{
    private static readonly DateTimeOffset T0 = new(2026, 8, 16, 12, 0, 0, TimeSpan.Zero);

    private static Snapshot Snap(int minutesOld, SnapshotTrigger trigger, string name = "s")
    {
        var created = T0.AddMinutes(-minutesOld);
        var manifest = new SnapshotManifest(
            SnapshotManifest.CurrentSchemaVersion, name, "", created, trigger,
            "hash", null, 5, [], 0, 0, false, ["settings"],
            new Dictionary<string, SnapshotFile>());

        return new Snapshot($"id-{minutesOld}-{trigger}", $@"C:\store\id-{minutesOld}", manifest);
    }

    [Fact]
    public void Nothing_is_pruned_below_the_keep_count()
    {
        var snapshots = Enumerable.Range(1, 29).Select(i => Snap(i, SnapshotTrigger.Automatic));

        Assert.Empty(SnapshotRetention.SelectForPruning(snapshots, keepCount: 30));
    }

    [Fact]
    public void The_thirty_first_automatic_snapshot_prunes_exactly_one()
    {
        var snapshots = Enumerable.Range(1, 31).Select(i => Snap(i, SnapshotTrigger.Automatic));

        Assert.Single(SnapshotRetention.SelectForPruning(snapshots, keepCount: 30));
    }

    [Fact]
    public void The_OLDEST_automatic_snapshots_are_the_ones_pruned()
    {
        // Easy to invert, and inverting it destroys exactly the history worth keeping.
        var snapshots = new[]
        {
            Snap(10, SnapshotTrigger.Automatic, "oldest"),
            Snap(5, SnapshotTrigger.Automatic, "middle"),
            Snap(1, SnapshotTrigger.Automatic, "newest"),
        };

        var pruned = SnapshotRetention.SelectForPruning(snapshots, keepCount: 1);

        Assert.Equal(["oldest", "middle"], pruned.Select(s => s.Manifest.DisplayName));
    }

    [Fact]
    public void Forty_manual_snapshots_prune_to_forty()
    {
        // Never pruned, at any count, ever.
        var snapshots = Enumerable.Range(1, 40).Select(i => Snap(i, SnapshotTrigger.Manual));

        Assert.Empty(SnapshotRetention.SelectForPruning(snapshots, keepCount: 5));
    }

    [Fact]
    public void Pre_restore_snapshots_survive_pruning_at_any_count()
    {
        // Someone's way back from a mistake.
        var snapshots = Enumerable.Range(1, 40).Select(i => Snap(i, SnapshotTrigger.PreRestore));

        Assert.Empty(SnapshotRetention.SelectForPruning(snapshots, keepCount: 0));
    }

    [Fact]
    public void Manual_snapshots_do_not_count_toward_the_automatic_budget()
    {
        // 50 manual + 3 automatic, keeping 2: exactly one automatic goes.
        var snapshots = Enumerable.Range(1, 50).Select(i => Snap(i, SnapshotTrigger.Manual))
            .Concat(Enumerable.Range(51, 3).Select(i => Snap(i, SnapshotTrigger.Automatic)));

        var pruned = SnapshotRetention.SelectForPruning(snapshots, keepCount: 2);

        Assert.Single(pruned);
        Assert.Equal(SnapshotTrigger.Automatic, pruned[0].Manifest.Trigger);
    }

    [Fact]
    public void A_keep_count_of_zero_prunes_every_automatic_snapshot_and_nothing_else()
    {
        var snapshots = new[]
        {
            Snap(3, SnapshotTrigger.Automatic),
            Snap(2, SnapshotTrigger.Manual),
            Snap(1, SnapshotTrigger.PreRestore),
        };

        var pruned = SnapshotRetention.SelectForPruning(snapshots, keepCount: 0);

        Assert.Single(pruned);
        Assert.Equal(SnapshotTrigger.Automatic, pruned[0].Manifest.Trigger);
    }

    [Fact]
    public void A_negative_keep_count_is_treated_as_zero_rather_than_throwing()
    {
        var snapshots = new[] { Snap(1, SnapshotTrigger.Automatic) };

        Assert.Single(SnapshotRetention.SelectForPruning(snapshots, keepCount: -5));
    }

    [Fact]
    public void An_empty_store_prunes_nothing()
    {
        Assert.Empty(SnapshotRetention.SelectForPruning([], keepCount: 30));
    }

    [Fact]
    public void The_default_keep_count_is_thirty()
    {
        Assert.Equal(30, SnapshotRetention.DefaultKeepCount);
    }
}
