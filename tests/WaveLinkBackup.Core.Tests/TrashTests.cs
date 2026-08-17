using System.Text;
using WaveLinkBackup.Core.Analysis;
using WaveLinkBackup.Core.Results;
using WaveLinkBackup.Core.Snapshots;
using WaveLinkBackup.Core.Tests.Fakes;

namespace WaveLinkBackup.Core.Tests;

/// <summary>
/// Two-stage delete. Deleting moves a snapshot into <c>&lt;store&gt;/.trash/</c> — a plain
/// directory move, no interop — and Empty trash forwards it to the Recycle Bin.
///
/// The reason it is two stages rather than one call to SHFileOperation: **the store is
/// user-chosen, and the Recycle Bin does not exist on network shares.** A directory move
/// behaves identically on every volume type; SHFileOperation does not.
/// See technical-debt.md 7.1.
/// </summary>
public sealed class TrashTests
{
    private const string Store = @"C:\store";
    private const string Settings =
        """{"MixerConfiguration":{"InputSettings":{"a":{"InputName":"Wave Mic 1"}}}}""";

    private static (SnapshotStore Store, FakeFileSystem Fs, FakeClock Clock) Subject()
    {
        var fs = new FakeFileSystem();
        var clock = new FakeClock();
        return (new SnapshotStore(fs, clock, Store), fs, clock);
    }

    private static Snapshot Write(SnapshotStore store, string name = "doomed")
    {
        var bytes = Encoding.UTF8.GetBytes(Settings);
        return store.Write(bytes, SettingsAnalysis.Analyse(bytes).Value, SnapshotTrigger.Manual, name).Value;
    }

    // ------------------------------------------------------------------ stage one

    [Fact]
    public void Deleting_moves_the_snapshot_into_trash_rather_than_destroying_it()
    {
        var (store, fs, _) = Subject();
        var snapshot = Write(store);

        Assert.True(store.Delete(snapshot.Id).IsSuccess);

        Assert.Empty(store.List());
        Assert.True(fs.FileExists(Path.Combine(Store, ".trash", snapshot.Id, "manifest.json")));
        Assert.True(fs.FileExists(Path.Combine(Store, ".trash", snapshot.Id, "settings.json")));
    }

    [Fact]
    public void A_trashed_snapshot_keeps_its_id_manifest_and_verifiability()
    {
        // Ids are machine-generated (timestamp + content hash) with identity in the manifest,
        // so a move needs no rename and cannot collide. ADR-003.
        var (store, fs, _) = Subject();
        var snapshot = Write(store, "keep my name");
        store.Delete(snapshot.Id);

        var trashed = Path.Combine(Store, ".trash", snapshot.Id);

        var verified = new SnapshotGuard(fs).Verify(trashed);
        Assert.True(verified.IsSuccess);
        Assert.Equal("keep my name", verified.Value.DisplayName);
    }

    [Fact]
    public void The_trash_is_not_listed_as_backups()
    {
        // Otherwise deleting something would appear to do nothing.
        var (store, _, clock) = Subject();
        var doomed = Write(store, "doomed");
        clock.Advance(TimeSpan.FromMinutes(1));
        Write(store, "keeper");

        store.Delete(doomed.Id);

        Assert.Equal(["keeper"], store.List().Select(s => s.Manifest.DisplayName));
    }

    [Fact]
    public void Trashed_snapshots_are_listable_separately()
    {
        var (store, _, _) = Subject();
        var snapshot = Write(store, "doomed");
        store.Delete(snapshot.Id);

        var trashed = Assert.Single(store.ListTrash());

        Assert.Equal(snapshot.Id, trashed.Id);
        Assert.Equal("doomed", trashed.Manifest.DisplayName);
    }

    [Fact]
    public void Deleting_two_snapshots_trashes_both()
    {
        var (store, _, clock) = Subject();
        var first = Write(store, "one");
        clock.Advance(TimeSpan.FromMinutes(1));
        var second = Write(store, "two");

        store.Delete(first.Id);
        store.Delete(second.Id);

        Assert.Empty(store.List());
        Assert.Equal(2, store.ListTrash().Count);
    }

    [Fact]
    public void Deleting_something_that_does_not_exist_is_still_an_expected_failure()
    {
        var (store, _, _) = Subject();

        Assert.IsType<SnapshotNotFound>(store.Delete("nope").Error);
    }

    [Fact]
    public void A_snapshot_deleted_twice_does_not_collide_in_the_trash()
    {
        // Same id trashed, restored by hand, deleted again — rare, but a silent overwrite
        // would destroy the first copy.
        var (store, fs, _) = Subject();
        var snapshot = Write(store, "first");
        store.Delete(snapshot.Id);

        // Simulate the user putting it back, then deleting again.
        var bytes = Encoding.UTF8.GetBytes(Settings);
        var again = store.Write(bytes, SettingsAnalysis.Analyse(bytes).Value,
            SnapshotTrigger.Manual, "second").Value;
        store.Delete(again.Id);

        Assert.Equal(2, store.ListTrash().Count);
        Assert.Equal(["first", "second"],
            store.ListTrash().Select(s => s.Manifest.DisplayName).Order(StringComparer.Ordinal));
        _ = fs;
    }

    // ------------------------------------------------------------------ stage two

    [Fact]
    public void Emptying_the_trash_sends_each_snapshot_to_the_recycle_bin()
    {
        var (store, fs, clock) = Subject();
        var first = Write(store, "one");
        clock.Advance(TimeSpan.FromMinutes(1));
        var second = Write(store, "two");
        store.Delete(first.Id);
        store.Delete(second.Id);

        var bin = new FakeRecycleBin(fs);
        var emptied = store.EmptyTrash(bin);

        Assert.Equal(2, emptied.Count);
        Assert.Equal(2, bin.Recycled.Count);
        Assert.Empty(store.ListTrash());
        Assert.False(fs.DirectoryExists(Path.Combine(Store, ".trash", first.Id)));
    }

    [Fact]
    public void Emptying_an_empty_trash_does_nothing_and_does_not_fail()
    {
        var (store, _, _) = Subject();

        Assert.Empty(store.EmptyTrash(new FakeRecycleBin()));
    }

    [Fact]
    public void Where_the_recycle_bin_is_unavailable_the_deletion_is_permanent()
    {
        // Network shares and many removable volumes. The store is user-chosen, so this is a
        // normal condition — and it is why deletion is two-stage rather than one
        // SHFileOperation call that would have quietly done this from the start.
        var (store, fs, _) = Subject();
        var snapshot = Write(store);
        store.Delete(snapshot.Id);

        var bin = new FakeRecycleBin { Available = false };
        var emptied = store.EmptyTrash(bin);

        Assert.Single(emptied);
        Assert.Empty(bin.Recycled);
        Assert.False(fs.DirectoryExists(Path.Combine(Store, ".trash", snapshot.Id)));
    }

    [Fact]
    public void Callers_can_ask_whether_emptying_will_be_permanent_before_doing_it()
    {
        // The Settings row has to say which it will be, and cannot find out afterwards.
        var (store, _, _) = Subject();

        Assert.True(store.TrashGoesToRecycleBin(new FakeRecycleBin()));
        Assert.False(store.TrashGoesToRecycleBin(new FakeRecycleBin { Available = false }));
    }

    [Fact]
    public void The_trash_reports_its_size_so_the_settings_row_can_show_one()
    {
        var (store, _, clock) = Subject();
        Assert.Equal((0, 0L), store.TrashSize());

        var first = Write(store, "one");
        clock.Advance(TimeSpan.FromMinutes(1));
        var second = Write(store, "two");
        store.Delete(first.Id);
        store.Delete(second.Id);

        var (count, bytes) = store.TrashSize();

        Assert.Equal(2, count);
        Assert.True(bytes > 0);
    }

    [Fact]
    public void One_unremovable_snapshot_does_not_stop_the_rest_being_emptied()
    {
        var (store, fs, clock) = Subject();
        var stuck = Write(store, "stuck");
        clock.Advance(TimeSpan.FromMinutes(1));
        var fine = Write(store, "fine");
        store.Delete(stuck.Id);
        store.Delete(fine.Id);

        fs.FailDirectoryDeleteFor = Path.Combine(Store, ".trash", stuck.Id);

        var emptied = store.EmptyTrash(new FakeRecycleBin { Available = false });

        Assert.Single(emptied);
        Assert.Equal("fine", emptied[0].Manifest.DisplayName);
        Assert.Single(store.ListTrash());
    }
}
