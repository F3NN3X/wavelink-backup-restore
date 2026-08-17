using System.Text;
using WaveLinkBackup.Core.Analysis;
using WaveLinkBackup.Core.Automation;
using WaveLinkBackup.Core.Discovery;
using WaveLinkBackup.Core.Io;
using WaveLinkBackup.Core.Snapshots;
using WaveLinkBackup.Core.Tests.Fakes;

namespace WaveLinkBackup.Core.Tests;

/// <summary>
/// screens/05 closes with a list: ".trash must be invisible to the list, the search, every
/// count and size readout, and the keep-count. A folder containing only .trash is still a
/// valid backup folder — error 9 must not fire on it."
///
/// The search and the readouts are phase-5 UI. Everything Core owns is pinned here, because
/// each one is a place where a trashed backup leaking back in would look like a bug in
/// deletion rather than in counting.
/// </summary>
public sealed class TrashInvisibilityTests
{
    private const string LocalAppData = @"C:\Users\test\AppData\Local";
    private const string SettingsPath =
        LocalAppData + @"\Packages\Elgato.WaveLink_g54w8ztgkx496\LocalState\Settings.json";
    private const string Store = @"C:\store";

    private static string Config(string micName) =>
        """{"MixerConfiguration":{"InputSettings":{"a":{"InputName":"NAME"}}}}"""
            .Replace("NAME", micName, StringComparison.Ordinal);

    private sealed class Harness
    {
        public FakeFileSystem Fs { get; } = new FakeFileSystem().AddFile(SettingsPath, Config("Wave Mic 1"));
        public FakeClock Clock { get; } = new();
        public SnapshotStore Store { get; private set; } = null!;
        public BackupService Service { get; private set; } = null!;

        public Harness Build(int keepCount = 1000)
        {
            Store = new SnapshotStore(Fs, Clock, TrashInvisibilityTests.Store);
            Service = new BackupService(SettingsInspector.For(Fs, LocalAppData), Store, keepCount);
            return this;
        }

        public Snapshot Capture(string micName)
        {
            Fs.WriteBytes(SettingsPath, Encoding.UTF8.GetBytes(Config(micName)));
            Clock.Advance(TimeSpan.FromMinutes(61));
            return Service.CaptureAutomatic(micName).Value.Snapshot!;
        }
    }

    [Fact]
    public void Trashed_backups_do_not_count_toward_the_keep_count()
    {
        // Otherwise deleting a backup would silently make the NEXT automatic capture prune a
        // good one, because the count still included the deleted one.
        var h = new Harness().Build();

        var a = h.Capture("one");
        var b = h.Capture("two");
        h.Capture("three");
        h.Store.Delete(a.Id);
        h.Store.Delete(b.Id);

        // One live snapshot, two in the trash. Keeping 2 must prune nothing.
        var pruned = new BackupService(
            SettingsInspector.For(h.Fs, LocalAppData), h.Store, keepCount: 2).Prune();

        Assert.Empty(pruned);
        Assert.Single(h.Store.List());
        Assert.Equal(2, h.Store.ListTrash().Count);
    }

    [Fact]
    public void A_store_containing_only_trash_is_still_a_usable_store()
    {
        // "Error 9 must not fire on it." Listing an all-trash store is empty, not invalid.
        var h = new Harness().Build();
        var only = h.Capture("one");
        h.Store.Delete(only.Id);

        Assert.Empty(h.Store.List());
        Assert.Single(h.Store.ListTrash());

        // And it still accepts new backups rather than reporting a broken folder.
        var again = h.Service.BackUpNow("after the purge");
        Assert.True(again.IsSuccess);
        Assert.Single(h.Store.List());
    }

    [Fact]
    public void A_trashed_backup_cannot_be_restored_because_it_is_not_in_the_list()
    {
        // "Restoring from a trashed backup is not possible: it is not in the list."
        var h = new Harness().Build();
        var snapshot = h.Capture("one");
        h.Store.Delete(snapshot.Id);

        Assert.False(h.Store.Get(snapshot.Id).IsSuccess);
    }

    [Fact]
    public void Trashed_backups_are_excluded_from_dedup_so_deleting_then_recapturing_works()
    {
        // Dedup compares against the newest LIVE snapshot. If the trash counted, deleting a
        // backup and immediately re-taking it would silently do nothing.
        var h = new Harness().Build();
        var first = h.Capture("one");
        h.Store.Delete(first.Id);

        h.Clock.Advance(TimeSpan.FromMinutes(61));
        var again = h.Service.CaptureAutomatic("one again");

        Assert.True(again.IsSuccess);
        Assert.True(again.Value.Captured);
        Assert.Single(h.Store.List());
    }

    [Fact]
    public void The_trash_survives_being_the_only_thing_in_the_store()
    {
        var h = new Harness().Build();
        var snapshot = h.Capture("one");
        h.Store.Delete(snapshot.Id);

        var (count, bytes) = h.Store.TrashSize();

        Assert.Equal(1, count);
        Assert.True(bytes > 0);
    }
}
