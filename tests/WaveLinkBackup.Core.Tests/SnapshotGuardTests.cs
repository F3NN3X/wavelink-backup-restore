using System.Text;
using WaveLinkBackup.Core.Analysis;
using WaveLinkBackup.Core.Results;
using WaveLinkBackup.Core.Snapshots;
using WaveLinkBackup.Core.Tests.Fakes;

namespace WaveLinkBackup.Core.Tests;

/// <summary>
/// Replaces upstream's filename regex. Same protection - a mistyped path must never write
/// arbitrary bytes into a config file - with no constraint on naming or location, plus one
/// thing the regex could never catch: corruption after the snapshot was written.
/// </summary>
public sealed class SnapshotGuardTests
{
    private const string Store = @"C:\store";
    private const string Settings =
        """{"MixerConfiguration":{"InputSettings":{"a":{"InputName":"Wave Mic 1"}}}}""";

    private static (SnapshotGuard Guard, SnapshotStore Store, FakeFileSystem Fs) Subject()
    {
        var fs = new FakeFileSystem();
        return (new SnapshotGuard(fs), new SnapshotStore(fs, new FakeClock(), Store), fs);
    }

    private static Snapshot Write(SnapshotStore store)
    {
        var bytes = Encoding.UTF8.GetBytes(Settings);
        return store.Write(bytes, SettingsAnalysis.Analyse(bytes).Value, SnapshotTrigger.Manual, "x").Value;
    }

    [Fact]
    public void A_snapshot_we_wrote_verifies()
    {
        var (guard, store, _) = Subject();

        var result = guard.Verify(Write(store).Directory);

        Assert.True(result.IsSuccess);
        Assert.Equal("x", result.Value.DisplayName);
    }

    [Fact]
    public void A_directory_with_no_manifest_is_not_a_snapshot()
    {
        // The mistyped-path case: pointing restore at "My Documents" must not write it.
        var (guard, _, fs) = Subject();
        fs.AddFile(@"C:\Users\test\Documents\notes.txt", "hello");

        var error = Assert.IsType<NotASnapshot>(guard.Verify(@"C:\Users\test\Documents").Error);
        Assert.Contains("manifest", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_manifest_that_is_not_json_is_rejected()
    {
        var (guard, store, fs) = Subject();
        var snapshot = Write(store);
        fs.WriteBytes(Path.Combine(snapshot.Directory, "manifest.json"), Encoding.UTF8.GetBytes("{ nope"));

        Assert.IsType<MalformedManifest>(guard.Verify(snapshot.Directory).Error);
    }

    [Fact]
    public void A_manifest_from_a_future_schema_is_rejected_with_a_readable_message()
    {
        var (guard, store, fs) = Subject();
        var snapshot = Write(store);

        var future = snapshot.Manifest with { SchemaVersion = SnapshotManifest.CurrentSchemaVersion + 1 };
        fs.WriteBytes(Path.Combine(snapshot.Directory, "manifest.json"), ManifestSerializer.Write(future));

        var error = Assert.IsType<UnsupportedSnapshotSchema>(guard.Verify(snapshot.Directory).Error);
        Assert.Contains("Update", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_missing_settings_file_is_corruption()
    {
        var (guard, store, fs) = Subject();
        var snapshot = Write(store);
        fs.Delete(snapshot.SettingsPath);

        Assert.IsType<SnapshotCorrupted>(guard.Verify(snapshot.Directory).Error);
    }

    [Fact]
    public void Settings_edited_after_the_snapshot_was_written_are_caught_by_the_hash()
    {
        // What the filename regex could never do. A failed sync, a bad disk, or a user
        // "fixing" a backup by hand all land here.
        var (guard, store, fs) = Subject();
        var snapshot = Write(store);

        // Same length, different content — so size alone would not notice.
        var tampered = Encoding.UTF8.GetBytes(Settings.Replace("Wave Mic 1", "Wave Mic 2", StringComparison.Ordinal));
        fs.WriteBytes(snapshot.SettingsPath, tampered);

        var error = Assert.IsType<SnapshotCorrupted>(guard.Verify(snapshot.Directory).Error);
        Assert.Contains("changed", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_truncated_settings_file_is_caught_by_size_before_the_hash()
    {
        var (guard, store, fs) = Subject();
        var snapshot = Write(store);
        fs.WriteBytes(snapshot.SettingsPath, Encoding.UTF8.GetBytes("{}"));

        var error = Assert.IsType<SnapshotCorrupted>(guard.Verify(snapshot.Directory).Error);
        Assert.Contains("bytes", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_manifest_that_cannot_be_read_is_rejected_rather_than_throwing()
    {
        var (guard, store, fs) = Subject();
        var snapshot = Write(store);
        var manifestPath = Path.Combine(snapshot.Directory, "manifest.json");
        fs.ReadFailures[manifestPath] = new Queue<Exception>([new IOException("locked")]);

        Assert.IsType<NotASnapshot>(guard.Verify(snapshot.Directory).Error);
    }

    [Fact]
    public void The_guard_places_no_constraint_on_the_store_location()
    {
        // Upstream requires backups beside Settings.json. The whole point of moving identity
        // into the manifest is that anywhere works.
        var fs = new FakeFileSystem();
        var store = new SnapshotStore(fs, new FakeClock(), @"D:\somewhere entirely else\backups");
        var bytes = Encoding.UTF8.GetBytes(Settings);

        var snapshot = store.Write(bytes, SettingsAnalysis.Analyse(bytes).Value, SnapshotTrigger.Manual, "x").Value;

        Assert.True(new SnapshotGuard(fs).Verify(snapshot.Directory).IsSuccess);
    }
}
