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

    /// <summary>
    /// Puts a file on the fake filesystem and returns the payload entry that copies it. A
    /// <see cref="CapturedFile"/> names a source rather than carrying bytes, so a payload fixture
    /// has to put the source somewhere the store can read it from.
    /// </summary>
    private static CapturedFile Source(FakeFileSystem fs, string path, string relative, string content)
    {
        fs.AddFile(path, content);
        return new CapturedFile(relative, path, fs.GetFileSize(path));
    }

    // -------------------------------------------------------------- tier 2

    private static SnapshotPayload PluginPayload =>
        SnapshotPayload.ForPlugins([
            new("Pro-Q 4", "FabFilter",
                @"C:\Program Files\Common Files\VST3\FabFilter\FabFilter Pro-Q 4.vst3",
                "4.1.2", "a1b2c3d4", ["Wave Mic 1"])]);

    [Fact]
    public void A_resolved_plugin_set_is_written_as_plugins_json_beside_the_settings()
    {
        var (store, fs, _) = Subject();
        var (bytes, analysis) = Content();

        var snapshot = store.Write(bytes, analysis, SnapshotTrigger.Manual, "x", payload: PluginPayload).Value;

        Assert.True(fs.FileExists(snapshot.PluginsPath));
        Assert.Equal("Pro-Q 4",
            PluginManifestSerializer.Read(fs.Read(snapshot.PluginsPath)).Plugins.Single().Name);
    }

    [Fact]
    public void A_snapshot_carrying_plugins_json_claims_the_plugin_manifest_tier()
    {
        var (store, _, _) = Subject();
        var (bytes, analysis) = Content();

        var manifest = store.Write(bytes, analysis, SnapshotTrigger.Manual, "x", payload: PluginPayload).Value.Manifest;

        Assert.Equal(["settings", "plugin-manifest"], manifest.Tiers);
        Assert.True(manifest.Files.ContainsKey("plugins.json"));
    }

    [Fact]
    public void A_rig_with_no_third_party_plugins_still_claims_the_tier()
    {
        // "We looked and found none" - which is what makes the restore warning's silence
        // trustworthy. A snapshot that never looked claims nothing.
        var (store, fs, _) = Subject();
        var (bytes, analysis) = Content();

        var snapshot = store.Write(bytes, analysis, SnapshotTrigger.Manual, "x", payload: SnapshotPayload.Empty).Value;

        Assert.Contains("plugin-manifest", snapshot.Manifest.Tiers);
        Assert.Empty(PluginManifestSerializer.Read(fs.Read(snapshot.PluginsPath)).Plugins);
    }

    [Fact]
    public void A_caller_that_never_resolved_the_plugin_set_writes_no_plugins_json_and_claims_no_tier()
    {
        var (store, fs, _) = Subject();

        var snapshot = Write(store);

        Assert.Equal(["settings"], snapshot.Manifest.Tiers);
        Assert.False(fs.FileExists(snapshot.PluginsPath));
    }

    [Fact]
    public void Plugins_json_is_hashed_and_sized_in_the_manifest_like_every_other_file()
    {
        // The guard has no idea which tier a file belongs to, and must not need one.
        var (store, fs, _) = Subject();
        var (bytes, analysis) = Content();

        var snapshot = store.Write(bytes, analysis, SnapshotTrigger.Manual, "x", payload: PluginPayload).Value;
        var recorded = snapshot.Manifest.Files["plugins.json"];

        Assert.Equal(SnapshotStore.HashOf(fs.Read(snapshot.PluginsPath)), recorded.Sha256);
        Assert.Equal(fs.Read(snapshot.PluginsPath).LongLength, recorded.SizeBytes);
        Assert.True(new SnapshotGuard(fs).Verify(snapshot.Directory).IsSuccess);
    }

    [Fact]
    public void Captured_files_are_written_under_the_snapshot_at_their_recorded_paths()
    {
        // The store knows nothing about tiers: it writes what the payload carries, hashes every
        // one into the manifest, and the guard verifies them all with the code it already had.
        var (store, fs, _) = Subject();
        var (bytes, analysis) = Content();

        var payload = PluginPayload with
        {
            Files =
            [
                Source(fs, @"C:\src\Settings.auto.1.json", "wavelink-backups/AutoBackup/Settings.auto.1.json", "auto"),
                Source(fs, @"C:\src\Bright.ffp", "presets/FabFilter/Pro-Q 4/Vocals/Bright.ffp", "bright"),
            ],
            Tiers = ["presets"],
        };

        var snapshot = store.Write(bytes, analysis, SnapshotTrigger.Manual, "x", payload: payload).Value;

        Assert.Equal(
            "bright"u8.ToArray(),
            fs.Read(SnapshotManifest.PathIn(snapshot.Directory, "presets/FabFilter/Pro-Q 4/Vocals/Bright.ffp")));
        Assert.Equal(6, snapshot.Manifest.Files["presets/FabFilter/Pro-Q 4/Vocals/Bright.ffp"].SizeBytes);
        Assert.Equal(
            ["settings", "plugin-manifest", "presets"],
            snapshot.Manifest.Tiers);
        Assert.True(new SnapshotGuard(fs).Verify(snapshot.Directory).IsSuccess);
    }

    [Fact]
    public void An_edited_preset_file_fails_verification_like_any_other()
    {
        var (store, fs, _) = Subject();
        var (bytes, analysis) = Content();

        var payload = PluginPayload with
        {
            Files = [Source(fs, @"C:\src\one.ffp", "presets/FabFilter/one.ffp", "original")],
            Tiers = ["presets"],
        };

        var snapshot = store.Write(bytes, analysis, SnapshotTrigger.Manual, "x", payload: payload).Value;
        fs.AddFile(SnapshotManifest.PathIn(snapshot.Directory, "presets/FabFilter/one.ffp"), "tampered");

        Assert.IsType<SnapshotCorrupted>(new SnapshotGuard(fs).Verify(snapshot.Directory).Error);
    }

    [Fact]
    public void An_edited_plugins_json_fails_verification()
    {
        var (store, fs, _) = Subject();
        var (bytes, analysis) = Content();

        var snapshot = store.Write(bytes, analysis, SnapshotTrigger.Manual, "x", payload: PluginPayload).Value;
        fs.AddFile(snapshot.PluginsPath, """{"schemaVersion":1,"plugins":[]}""");

        Assert.IsType<SnapshotCorrupted>(new SnapshotGuard(fs).Verify(snapshot.Directory).Error);
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

    /// <summary>
    /// The manifest records what the COPY wrote, not what the capture measured beforehand. The
    /// two used to be the same number by construction, because the store hashed the bytes it had
    /// been handed; now the bytes never pass through it, and a file that changed length between
    /// being chosen and being copied must be recorded at its real length or the guard rejects a
    /// snapshot that is perfectly intact.
    /// </summary>
    [Fact]
    public void A_files_recorded_size_and_hash_are_the_ones_the_copy_produced()
    {
        var (store, fs, _) = Subject();
        var (bytes, analysis) = Content();

        // A stale figure on the payload entry — the shape of a file rewritten between the walk
        // that found it and the copy that took it.
        fs.AddFile(@"C:\src\grew.ffp", "the file as it actually is now");
        var payload = PluginPayload with
        {
            Files = [new CapturedFile("presets/appdata/grew.ffp", @"C:\src\grew.ffp", SizeBytes: 3)],
            Tiers = ["presets"],
        };

        var snapshot = store.Write(bytes, analysis, SnapshotTrigger.Manual, "x", payload: payload).Value;

        Assert.Equal(30, snapshot.Manifest.Files["presets/appdata/grew.ffp"].SizeBytes);
        Assert.True(new SnapshotGuard(fs).Verify(snapshot.Directory).IsSuccess);
    }
}
