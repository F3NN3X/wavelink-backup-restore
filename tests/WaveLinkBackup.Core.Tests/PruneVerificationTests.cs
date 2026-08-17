using System.Text;
using WaveLinkBackup.Core.Analysis;
using WaveLinkBackup.Core.Automation;
using WaveLinkBackup.Core.Discovery;
using WaveLinkBackup.Core.Io;
using WaveLinkBackup.Core.Snapshots;
using WaveLinkBackup.Core.Tests.Fakes;

namespace WaveLinkBackup.Core.Tests;

/// <summary>
/// "A corrupted file must never push a good one out." Design decision 6.
///
/// Retention cannot see damage — it is found by hashing, and phase 2 deliberately kept hashing
/// out of listing so opening a window does not rehash the store. The resolution is to verify
/// **only the condemned**: pruning removes one or two, so it hashes one or two, not thirty.
/// See technical-debt.md 7.2.
/// </summary>
public sealed class PruneVerificationTests
{
    private const string LocalAppData = @"C:\Users\test\AppData\Local";
    private const string SettingsPath =
        LocalAppData + @"\Packages\Elgato.WaveLink_g54w8ztgkx496\LocalState\Settings.json";
    private const string Store = @"C:\store";

    private static string Config(string micName) =>
        """{"MixerConfiguration":{"InputSettings":{"a":{"InputName":"NAME"}}}}"""
            .Replace("NAME", micName, StringComparison.Ordinal);

    /// <summary>
    /// Captures run through a service with a generous keep-count so nothing is pruned while
    /// the store is being built — <see cref="BackupService.CaptureAutomatic"/> prunes as a
    /// side effect, which would otherwise consume the very candidates under test.
    /// <see cref="PruneWith"/> then builds a second service over the same store with the
    /// keep-count that matters, so pruning is isolated and observable.
    /// </summary>
    private sealed class Harness
    {
        public FakeFileSystem Fs { get; } = new FakeFileSystem().AddFile(SettingsPath, Config("Wave Mic 1"));
        public FakeClock Clock { get; } = new();

        public SnapshotStore Store { get; private set; } = null!;
        private BackupService builder = null!;

        public Harness Build()
        {
            Store = new SnapshotStore(Fs, Clock, PruneVerificationTests.Store);
            builder = new BackupService(SettingsInspector.For(Fs, LocalAppData), Store, keepCount: 1000);
            return this;
        }

        /// <summary>Takes an automatic snapshot with distinct content so dedup does not skip it.</summary>
        public Snapshot Capture(string micName)
        {
            Fs.WriteBytes(SettingsPath, Encoding.UTF8.GetBytes(Config(micName)));
            Clock.Advance(TimeSpan.FromMinutes(61));

            var result = builder.CaptureAutomatic(micName);
            Assert.True(result.IsSuccess);
            return result.Value.Snapshot!;
        }

        public Snapshot CaptureManual(string name)
        {
            var result = builder.BackUpNow(name);
            Assert.True(result.IsSuccess);
            return result.Value;
        }

        public IReadOnlyList<Snapshot> PruneWith(int keepCount) =>
            new BackupService(SettingsInspector.For(Fs, LocalAppData), Store, keepCount).Prune();

        /// <summary>Corrupts a snapshot the way a failed sync or a bad disk would.</summary>
        public void Damage(Snapshot snapshot) =>
            Fs.WriteBytes(snapshot.SettingsPath, Encoding.UTF8.GetBytes("{ truncated"));
    }

    [Fact]
    public void A_damaged_snapshot_is_never_pruned_even_when_it_is_the_oldest()
    {
        var h = new Harness().Build();
        const int keep = 2;

        var oldest = h.Capture("one");
        h.Capture("two");
        h.Capture("three");
        h.Damage(oldest);

        var pruned = h.PruneWith(keep);

        Assert.Empty(pruned);
        Assert.Contains(h.Store.List(), s => s.Id == oldest.Id);
    }

    [Fact]
    public void A_healthy_snapshot_is_still_pruned_normally()
    {
        var h = new Harness().Build();
        const int keep = 2;

        var oldest = h.Capture("one");
        h.Capture("two");
        h.Capture("three");

        var pruned = h.PruneWith(keep);

        Assert.Single(pruned);
        Assert.Equal(oldest.Id, pruned[0].Id);
    }

    [Fact]
    public void A_damaged_snapshot_does_not_shield_a_healthy_older_one()
    {
        // Verification is per-candidate: the damaged one is skipped, the rest still go.
        var h = new Harness().Build();
        const int keep = 1;

        var oldest = h.Capture("one");
        var middle = h.Capture("two");
        h.Capture("three");
        h.Damage(middle);

        var pruned = h.PruneWith(keep);

        Assert.Single(pruned);
        Assert.Equal(oldest.Id, pruned[0].Id);
        Assert.Contains(h.Store.List(), s => s.Id == middle.Id);
    }

    [Fact]
    public void Pruning_verifies_only_the_condemned_not_the_whole_store()
    {
        // The reason this does not reintroduce the cost phase 2 avoided. Keeping 4 of 5 means
        // one candidate, so exactly one snapshot's files get read.
        var h = new Harness().Build();
        const int keep = 4;

        for (var i = 0; i < 5; i++) h.Capture($"mic {i}");

        h.Fs.ReadCounts.Clear();
        h.PruneWith(keep);

        var settingsReads = h.Fs.ReadCounts
            .Where(kv => kv.Key.EndsWith("settings.json", StringComparison.OrdinalIgnoreCase))
            .Sum(kv => kv.Value);

        Assert.Equal(1, settingsReads);
    }

    [Fact]
    public void A_damaged_snapshot_stays_deletable_by_hand()
    {
        // It is immortal to the pruner, not to the user — otherwise a corrupt backup would be
        // unremovable, which is a worse failure than the one this rule prevents.
        var h = new Harness().Build();
        var snapshot = h.Capture("one");
        h.Damage(snapshot);

        Assert.Empty(h.PruneWith(0));
        Assert.True(h.Store.Delete(snapshot.Id).IsSuccess);
        Assert.Empty(h.Store.List());
        Assert.Single(h.Store.ListTrash());
    }

    [Fact]
    public void Manual_snapshots_are_still_never_candidates_at_all()
    {
        // Unchanged by this: IsPrunable filters before verification is even considered.
        var h = new Harness().Build();
        const int keep = 0;
        h.CaptureManual("mine");

        Assert.Empty(h.PruneWith(keep));
        Assert.Single(h.Store.List());
    }
}
