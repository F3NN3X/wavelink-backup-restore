using System.Text;
using WaveLinkBackup.Core.Analysis;
using WaveLinkBackup.Core.Results;
using WaveLinkBackup.Core.Snapshots;
using WaveLinkBackup.Core.Tests.Fakes;

namespace WaveLinkBackup.Core.Tests;

public sealed class SnapshotStoreTests
{
    private const string LocalState =
        @"C:\Users\test\AppData\Local\Packages\Elgato.WaveLink_g54w8ztgkx496\LocalState";
    private const string Store = @"C:\Users\test\AppData\Local\WaveLinkBackup";

    private const string Settings = """
        {
          "Update": { "LastUpdateVersion": "3.3.0.4108" },
          "MixerConfiguration": {
            "InputSettings": {
              "a": { "InputName": "Wave Mic 1", "AudioPluginConfigurations": [{ "Name": "Pro-Q 4" }] },
              "b": { "InputName": "Voice", "AudioPluginConfigurations": [] }
            }
          }
        }
        """;

    private static (SnapshotStore Store, FakeFileSystem Fs, FakeClock Clock) Subject()
    {
        var fs = new FakeFileSystem();
        var clock = new FakeClock();
        return (new SnapshotStore(fs, clock, Store), fs, clock);
    }

    private static (byte[] Bytes, SettingsAnalysisResult Analysis) Content(string json = Settings)
    {
        var bytes = Encoding.UTF8.GetBytes(json);
        return (bytes, SettingsAnalysis.Analyse(bytes).Value);
    }

    private static Snapshot Write(SnapshotStore store, string name = "Before 3.3 beta",
        SnapshotTrigger trigger = SnapshotTrigger.Manual, string json = Settings)
    {
        var (bytes, analysis) = Content(json);
        var result = store.Write(bytes, analysis, trigger, name);
        Assert.True(result.IsSuccess);
        return result.Value;
    }

    // -------------------------------------------------------------- the critical defect

    [Fact]
    public void A_snapshot_survives_deleting_the_entire_LocalState_directory()
    {
        // THE test for this phase. Upstream writes backups inside LocalState, which an MSIX
        // package reset deletes wholesale - the backups destroyed by exactly the event you
        // would want to recover from. technical-debt.md 1.1, ADR-003.
        var (store, fs, _) = Subject();
        fs.AddFile(LocalState + @"\Settings.json", Settings);

        var snapshot = Write(store);

        fs.DeleteDirectory(LocalState);

        Assert.False(fs.DirectoryExists(LocalState));
        Assert.Single(store.List());
        Assert.True(new SnapshotGuard(fs).Verify(snapshot.Directory).IsSuccess);
    }

    [Fact]
    public void The_store_lives_outside_LocalState()
    {
        var (store, _, _) = Subject();

        Assert.DoesNotContain("LocalState", Write(store).Directory, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Packages", SnapshotStore.DefaultStorePath, StringComparison.OrdinalIgnoreCase);
    }

    // -------------------------------------------------------------- writing

    [Fact]
    public void A_written_snapshot_carries_the_whole_fingerprint_in_its_manifest()
    {
        var (store, _, _) = Subject();

        var manifest = Write(store).Manifest;

        Assert.Equal("Before 3.3 beta", manifest.DisplayName);
        Assert.Equal(2, manifest.InputCount);
        Assert.Equal(["Wave Mic 1", "Voice"], manifest.InputNames);
        Assert.Equal(1, manifest.EffectCount);
        Assert.Equal(1, manifest.EffectChannelCount);
        Assert.Equal("3.3.0.4108", manifest.WaveLinkVersion);
        Assert.False(manifest.HasDuplicateKeys);
        Assert.Equal(["settings"], manifest.Tiers);
    }

    [Fact]
    public void The_stored_settings_bytes_are_identical_to_the_source()
    {
        // Capture is a byte copy, all the way through the store.
        var (store, fs, _) = Subject();
        var (bytes, analysis) = Content();

        var snapshot = store.Write(bytes, analysis, SnapshotTrigger.Manual, "x").Value;

        Assert.Equal(bytes, fs.Read(snapshot.SettingsPath));
    }

    [Fact]
    public void The_directory_name_is_machine_generated_and_ignores_the_display_name()
    {
        var (store, _, _) = Subject();

        var snapshot = Write(store, """Mic chain 3/4" <hot>""");

        Assert.DoesNotContain("Mic chain", snapshot.Id, StringComparison.Ordinal);
        Assert.StartsWith("2026-08-15T2307-", snapshot.Id, StringComparison.Ordinal);
    }

    [Fact]
    public void A_display_name_that_would_be_illegal_in_a_path_is_stored_and_read_back()
    {
        var (store, _, _) = Subject();
        const string awkward = """Mic chain 3/4" & "loud" \ """;

        Write(store, awkward);

        Assert.Equal(awkward, store.List()[0].Manifest.DisplayName);
    }

    [Fact]
    public void Two_snapshots_in_the_same_minute_get_distinct_directories()
    {
        var (store, _, _) = Subject();

        var first = Write(store, "one");
        var second = Write(store, "two", json: Settings.Replace("Voice", "Browser", StringComparison.Ordinal));

        Assert.NotEqual(first.Id, second.Id);
        Assert.Equal(2, store.List().Count);
    }

    [Fact]
    public void Identical_content_in_the_same_minute_still_produces_two_snapshots_for_now()
    {
        // Dedup is phase 3. settingsSha256 is recorded here so it has something to compare.
        var (store, _, _) = Subject();

        Write(store, "one");
        Write(store, "two");

        var all = store.List();
        Assert.Equal(2, all.Count);
        Assert.Equal(all[0].Manifest.SettingsSha256, all[1].Manifest.SettingsSha256);
    }

    // -------------------------------------------------------------- listing

    [Fact]
    public void Listing_returns_newest_first()
    {
        var (store, _, clock) = Subject();

        Write(store, "older");
        clock.Advance(TimeSpan.FromHours(2));
        Write(store, "newer");

        Assert.Equal(["newer", "older"], store.List().Select(s => s.Manifest.DisplayName));
    }

    [Fact]
    public void An_empty_or_missing_store_lists_nothing_rather_than_failing()
    {
        var (store, _, _) = Subject();

        Assert.Empty(store.List());
    }

    [Fact]
    public void One_unreadable_snapshot_does_not_hide_the_others()
    {
        // The moment a user needs the list is the worst moment to fail it entirely.
        var (store, fs, clock) = Subject();
        Write(store, "good one");
        clock.Advance(TimeSpan.FromMinutes(5));
        var broken = Write(store, "broken");

        fs.WriteBytes(Path.Combine(broken.Directory, "manifest.json"), Encoding.UTF8.GetBytes("{ not json"));

        var listed = store.List();

        Assert.Single(listed);
        Assert.Equal("good one", listed[0].Manifest.DisplayName);
    }

    [Fact]
    public void Unrelated_directories_in_the_store_are_ignored()
    {
        var (store, fs, _) = Subject();
        Write(store);
        fs.AddFile(Store + @"\random-folder\notes.txt", "hello");

        Assert.Single(store.List());
    }

    // -------------------------------------------------------------- rename

    [Fact]
    public void Renaming_moves_no_files_and_only_rewrites_the_manifest()
    {
        // The property that makes free-text names possible at all. ADR-003.
        var (store, fs, _) = Subject();
        var snapshot = Write(store, "old name");
        var before = fs.EnumerateFiles(snapshot.Directory, "*").ToArray();

        var renamed = store.Rename(snapshot.Id, """Mic chain 3/4" """);

        Assert.True(renamed.IsSuccess);
        Assert.Equal(snapshot.Id, renamed.Value.Id);
        Assert.Equal(snapshot.Directory, renamed.Value.Directory);
        Assert.Equal(before, fs.EnumerateFiles(snapshot.Directory, "*").ToArray());
        Assert.Equal("""Mic chain 3/4" """, store.List()[0].Manifest.DisplayName);
    }

    [Fact]
    public void Renaming_leaves_notes_alone_unless_asked()
    {
        var (store, _, _) = Subject();
        var snapshot = Write(store);
        store.Rename(snapshot.Id, "a", "some notes");

        store.Rename(snapshot.Id, "b");

        Assert.Equal("some notes", store.List()[0].Manifest.Notes);
    }

    [Fact]
    public void Renaming_something_that_does_not_exist_is_an_expected_failure()
    {
        var (store, _, _) = Subject();

        Assert.IsType<SnapshotNotFound>(store.Rename("nope", "x").Error);
    }

    // -------------------------------------------------------------- delete

    [Fact]
    public void Deleting_removes_the_snapshot_and_leaves_the_rest()
    {
        var (store, _, clock) = Subject();
        var doomed = Write(store, "doomed");
        clock.Advance(TimeSpan.FromMinutes(5));
        Write(store, "keeper");

        Assert.True(store.Delete(doomed.Id).IsSuccess);

        Assert.Equal(["keeper"], store.List().Select(s => s.Manifest.DisplayName));
    }

    [Fact]
    public void Deleting_something_that_does_not_exist_is_an_expected_failure()
    {
        var (store, _, _) = Subject();

        Assert.IsType<SnapshotNotFound>(store.Delete("nope").Error);
    }

    // -------------------------------------------------------------- triggers

    [Theory]
    [InlineData(SnapshotTrigger.Manual, false)]
    [InlineData(SnapshotTrigger.PreRestore, false)]
    [InlineData(SnapshotTrigger.Automatic, true)]
    public void Only_automatic_snapshots_are_prunable(SnapshotTrigger trigger, bool prunable)
    {
        // Phase 3 will prune. A user who named a snapshot has said it matters.
        var (store, _, _) = Subject();

        Assert.Equal(prunable, Write(store, "x", trigger).Manifest.IsPrunable);
    }

    [Fact]
    public void A_snapshot_of_a_file_with_duplicate_keys_is_stored_and_marked_suspect()
    {
        // Suspect informs; it never blocks. The snapshot may be the only one there is.
        var (store, _, _) = Subject();

        var snapshot = Write(store, "suspect", json: """
            {"MixerConfiguration":{"InputSettings":{"a":{"InputName":"x"}}},"D":1,"d":2}
            """);

        Assert.True(snapshot.Manifest.HasDuplicateKeys);
        Assert.True(snapshot.Manifest.IsSuspect);
    }

    [Fact]
    public void An_unwritable_store_is_an_expected_failure_not_a_crash()
    {
        var fs = new FakeFileSystem { FailDirectoryCreation = true };
        var store = new SnapshotStore(fs, new FakeClock(), Store);
        var (bytes, analysis) = Content();

        Assert.IsType<StoreUnavailable>(store.Write(bytes, analysis, SnapshotTrigger.Manual, "x").Error);
    }
}
