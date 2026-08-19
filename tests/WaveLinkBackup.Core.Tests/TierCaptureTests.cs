using WaveLinkBackup.Core.Automation;
using WaveLinkBackup.Core.Capture;
using WaveLinkBackup.Core.Io;
using WaveLinkBackup.Core.Snapshots;
using WaveLinkBackup.Core.Tests.Fakes;

namespace WaveLinkBackup.Core.Tests;

/// <summary>
/// Tiers 1 (the other half), 3 and 4: what a capture gathers beyond the settings file.
///
/// The rule every test here is really about: **nothing in these tiers may fail a capture.** The
/// settings file is the product; no plug-in, preset or stale copy of Wave Link's own is worth
/// losing it over. The other rule is that a tier is claimed only when it is actually in there -
/// a badge that promises plug-ins a restore cannot produce is worse than no badge.
/// </summary>
public sealed class TierCaptureTests
{
    private const string LocalAppData = @"C:\Users\test\AppData\Local";
    private const string Roaming = @"C:\Users\test\AppData\Roaming";

    /// <summary>
    /// Deliberately NOT under the profile: the reference rig has Documents redirected to another
    /// drive, and a test that put it beside Roaming would pass for code that composed the path from
    /// %USERPROFILE% (technical-debt.md §4.18).
    /// </summary>
    private const string Documents = @"G:\win_user-folders\Documents";
    private const string LocalState =
        LocalAppData + @"\Packages\Elgato.WaveLink_g54w8ztgkx496\LocalState";
    private const string Settings = LocalState + @"\Settings.json";
    private const string Backup = LocalState + @"\Backup";

    private const string ProQPath = @"C:\Program Files\Common Files\VST3\FabFilter\FabFilter Pro-Q 4.vst3";
    private const string ClearPath = @"C:\Program Files\Common Files\VST3\Clear.vst3";

    private const string TwoPlugins = """
        {"MixerConfiguration":{"InputSettings":{
          "a":{"InputName":"Wave Mic 1","AudioPluginConfigurations":[
            {"Name":"Pro-Q 4","Vendor":"FabFilter",
             "FilePath":"C:\\Program Files\\Common Files\\VST3\\FabFilter\\FabFilter Pro-Q 4.vst3"},
            {"Name":"Clear","Vendor":"Supertone","FilePath":"C:\\Program Files\\Common Files\\VST3\\Clear.vst3"}]},
          "b":{"InputName":"Voice","AudioPluginConfigurations":[
            {"Name":"Clear","Vendor":"Supertone","FilePath":"C:\\Program Files\\Common Files\\VST3\\Clear.vst3"}]}
        }}}
        """;

    private static FakeFileSystem Rig(string settings = TwoPlugins) =>
        new FakeFileSystem().AddFile(Settings, settings);

    private static SettingsInspection Live(FakeFileSystem fs) =>
        SettingsInspector.For(fs, LocalAppData).Inspect().Value;

    private static SnapshotPayload Gather(FakeFileSystem fs, BackupSettings? settings = null) =>
        new TierCapture(fs, Roaming, Documents).Gather(Live(fs), settings ?? BackupSettings.Default);

    private static BackupSettings Tiers(bool presets = false, bool binaries = false) =>
        BackupSettings.Default with { IncludePresets = presets, IncludePluginFiles = binaries };

    // ------------------------------------------------------------ tier 1: Wave Link's own copies

    [Fact]
    public void Wave_Links_own_backup_copies_travel_with_the_settings_file()
    {
        // ADR-006 defines tier 1 as the settings file PLUS these, ~470 KB. They carry history a
        // first run cannot have: the AutoBackups reach back about three days, the .bak
        // atomic-save artifacts reach back months.
        var fs = Rig()
            .AddFile(Backup + @"\AutoBackup\Settings.auto.20260819-2307.json", "auto one")
            .AddFile(Backup + @"\Settings.json.bak.a1b2.c3d4", "atomic one");

        var files = Gather(fs, Tiers()).Files.Select(f => f.RelativePath).ToList();

        Assert.Contains("wavelink-backups/AutoBackup/Settings.auto.20260819-2307.json", files);
        Assert.Contains("wavelink-backups/Settings.json.bak.a1b2.c3d4", files);
    }

    [Fact]
    public void They_are_captured_even_with_every_switchable_tier_off()
    {
        // Tier 1 has no switch, deliberately (ADR-006).
        var fs = Rig().AddFile(Backup + @"\AutoBackup\Settings.auto.1.json", "auto");

        Assert.Single(Gather(fs, Tiers(presets: false, binaries: false)).Files);
    }

    [Fact]
    public void Only_the_newest_ten_of_each_kind_are_taken()
    {
        // Ten is what Wave Link keeps in AutoBackup, so the cap never binds on a healthy machine.
        // It is here for the machine nobody has cleaned: nothing rotates the .bak files at all.
        var fs = Rig();
        for (var i = 1; i <= 14; i++)
        {
            var path = $@"{Backup}\AutoBackup\Settings.auto.{i:00}.json";
            fs.AddFile(path, $"copy {i}")
              .SetLastWriteTimeUtc(path, new DateTime(2026, 8, i, 0, 0, 0, DateTimeKind.Utc));
        }

        var captured = Gather(fs, Tiers()).Files.Select(f => f.RelativePath).ToList();

        Assert.Equal(10, captured.Count);
        Assert.Contains("wavelink-backups/AutoBackup/Settings.auto.14.json", captured);
        Assert.DoesNotContain("wavelink-backups/AutoBackup/Settings.auto.04.json", captured);
    }

    [Fact]
    public void Other_files_in_the_Backup_folder_are_left_alone()
    {
        // The .bak artifacts share that directory with whatever else Wave Link keeps there.
        var fs = Rig()
            .AddFile(Backup + @"\Settings.json.bak.a1b2.c3d4", "wanted")
            .AddFile(Backup + @"\something-else.log", "not wanted");

        var captured = Gather(fs, Tiers()).Files.Select(f => f.RelativePath).ToList();

        Assert.Equal(["wavelink-backups/Settings.json.bak.a1b2.c3d4"], captured);
    }

    [Fact]
    public void A_locked_copy_is_left_out_and_the_capture_still_happens()
    {
        // Wave Link rotates these while it runs, which is exactly when captures fire.
        var fs = Rig()
            .AddFile(Backup + @"\AutoBackup\Settings.auto.1.json", "locked")
            .AddFile(Backup + @"\AutoBackup\Settings.auto.2.json", "fine");
        fs.ReadFailures[Backup + @"\AutoBackup\Settings.auto.1.json"] =
            new Queue<Exception>([new IOException("in use by another process")]);

        var captured = Gather(fs, Tiers()).Files.Select(f => f.RelativePath).ToList();

        Assert.Equal(["wavelink-backups/AutoBackup/Settings.auto.2.json"], captured);
    }

    [Fact]
    public void A_rig_with_no_Backup_folder_captures_nothing_extra_and_does_not_fail()
    {
        Assert.Empty(Gather(Rig(), Tiers()).Files);
    }

    // ------------------------------------------------------------------------ tier 3: presets

    [Fact]
    public void Presets_come_from_the_plugins_own_folder_when_there_is_one()
    {
        var fs = Rig()
            .AddFile(Roaming + @"\FabFilter\Pro-Q 4\My curve.ffp", "curve")
            .AddFile(Roaming + @"\FabFilter\Pro-Q 4\Vocals\Bright.ffp", "bright");

        var payload = Gather(fs, Tiers(presets: true));

        Assert.Contains("presets/appdata/FabFilter/Pro-Q 4/My curve.ffp", payload.Files.Select(f => f.RelativePath));
        Assert.Contains("presets/appdata/FabFilter/Pro-Q 4/Vocals/Bright.ffp", payload.Files.Select(f => f.RelativePath));
        Assert.Contains(SnapshotManifest.PresetsTier, payload.Tiers);
    }

    [Fact]
    public void A_vendor_that_keeps_presets_flat_is_captured_from_the_vendor_folder()
    {
        // Vendors agree on nothing, so the lookup widens rather than giving up. ADR-006 calls
        // this heuristic imperfect by design.
        var fs = Rig().AddFile(Roaming + @"\Supertone\preset.json", "flat");

        var payload = Gather(fs, Tiers(presets: true));

        Assert.Contains("presets/appdata/Supertone/preset.json", payload.Files.Select(f => f.RelativePath));
    }

    [Fact]
    public void Each_plugin_records_where_its_presets_came_from_and_how_many()
    {
        // A heuristic whose result cannot be inspected is a heuristic nobody can improve.
        var fs = Rig()
            .AddFile(Roaming + @"\FabFilter\Pro-Q 4\one.ffp", "1")
            .AddFile(Roaming + @"\FabFilter\Pro-Q 4\two.ffp", "22");

        var proQ = Gather(fs, Tiers(presets: true)).Plugins.Plugins
            .Single(p => p.Name == "Pro-Q 4");

        Assert.Equal([Roaming + @"\FabFilter\Pro-Q 4"], proQ.PresetSources);
        Assert.Equal(2, proQ.PresetFileCount);
        Assert.Equal(3, proQ.PresetBytes);
    }

    // ------------------------------------------------- tier 3: the two roots (technical-debt §4.18)

    [Fact]
    public void Presets_in_Documents_are_captured_as_well_as_the_ones_in_AppData()
    {
        // The defect §4.18 found on a real rig. FabFilter keeps the MIDI map and the interface
        // default in %APPDATA% and the 172 actual .ffp presets in Documents\FabFilter\Presets;
        // reading only the first captured three files and called them the user's EQ curves.
        var fs = Rig()
            .AddFile(Roaming + @"\FabFilter\Pro-Q 4\MidiControllerMap.ffm", "midi")
            .AddFile(Documents + @"\FabFilter\Presets\Pro-Q 4\Vocal Air.ffp", "curve");

        var payload = Gather(fs, Tiers(presets: true));
        var captured = payload.Files.Select(f => f.RelativePath).ToList();

        Assert.Contains("presets/appdata/FabFilter/Pro-Q 4/MidiControllerMap.ffm", captured);
        Assert.Contains("presets/documents/FabFilter/Presets/Pro-Q 4/Vocal Air.ffp", captured);

        var proQ = payload.Plugins.Plugins.Single(p => p.Name == "Pro-Q 4");
        Assert.Equal(
            [Roaming + @"\FabFilter\Pro-Q 4", Documents + @"\FabFilter\Presets\Pro-Q 4"],
            proQ.PresetSources);
        Assert.Equal(2, proQ.PresetFileCount);
    }

    [Fact]
    public void A_vendor_folder_in_Documents_is_never_taken_whole()
    {
        // %APPDATA%\<Vendor> is config-sized whatever it holds. Documents\<Vendor> is as likely to
        // be a project library - sessions, renders, sample packs - so the widest Documents
        // candidate is <Vendor>\Presets, a folder that says what it is. Without this rule tier 3
        // would quietly grow by whatever the user keeps beside their presets.
        var fs = Rig()
            .AddFile(Documents + @"\FabFilter\Sessions\huge project.wav", "not a preset");

        Assert.Empty(Gather(fs, Tiers(presets: true)).Files);
    }

    [Fact]
    public void The_Documents_folder_falls_back_to_the_Presets_folder_itself()
    {
        // A vendor that does not separate per plugin still gets its presets read - just not the
        // whole vendor folder around them.
        var fs = Rig().AddFile(Documents + @"\Supertone\Presets\voice.json", "flat");

        Assert.Contains(
            "presets/documents/Supertone/Presets/voice.json",
            Gather(fs, Tiers(presets: true)).Files.Select(f => f.RelativePath));
    }

    [Fact]
    public void Crash_reports_are_not_presets_and_are_never_captured()
    {
        // Supertone Clear on the reference rig: %APPDATA%\Supertone\Clear holds a Reports folder
        // of crash dumps and nothing else, and tier 3 captured them, counted them, and reported
        // two saved presets to the user.
        var fs = Rig()
            .AddFile(Roaming + @"\Supertone\Clear\Reports\2025_09_07_19_32_07.txt", "Crash Time :");

        var payload = Gather(fs, Tiers(presets: true));

        Assert.Empty(payload.Files);

        // The folder is still RECORDED, with a count of zero. "We looked here and there was
        // nothing worth keeping" is something the user can act on; a silence is not.
        var clear = payload.Plugins.Plugins.First(p => p.Name == "Clear");
        Assert.Equal([Roaming + @"\Supertone\Clear"], clear.PresetSources);
        Assert.Equal(0, clear.PresetFileCount);
    }

    [Fact]
    public void A_plugin_whose_vendor_saves_nothing_is_visible_as_an_empty_capture()
    {
        // Zero with no source is "we looked and there is nothing there" - which the user can act
        // on, unlike a silence.
        var clear = Gather(Rig(), Tiers(presets: true)).Plugins.Plugins.Single(p => p.Name == "Clear");

        Assert.Null(clear.PresetSource);
        Assert.Equal(0, clear.PresetFileCount);
    }

    [Fact]
    public void A_plugin_with_no_vendor_recorded_captures_no_presets_at_all()
    {
        // Guessing a vendor folder from a plug-in name would capture somebody else's work.
        var fs = new FakeFileSystem()
            .AddFile(Settings, """
                {"MixerConfiguration":{"InputSettings":{"a":{"InputName":"Wave Mic 1",
                  "AudioPluginConfigurations":[{"Name":"Clear","FilePath":"C:\\VST3\\Clear.vst3"}]}}}}
                """)
            .AddFile(Roaming + @"\Clear\preset.json", "not ours");

        Assert.Empty(Gather(fs, Tiers(presets: true)).Files);
    }

    [Fact]
    public void Switching_presets_off_captures_none_and_claims_nothing()
    {
        var fs = Rig().AddFile(Roaming + @"\FabFilter\Pro-Q 4\one.ffp", "1");

        var payload = Gather(fs, Tiers(presets: false));

        Assert.Empty(payload.Files);
        Assert.DoesNotContain(SnapshotManifest.PresetsTier, payload.Tiers);
    }

    [Fact]
    public void The_presets_tier_is_not_claimed_when_the_capture_found_nothing()
    {
        // On, and empty. The badge says what is IN the snapshot.
        var payload = Gather(Rig(), Tiers(presets: true));

        Assert.DoesNotContain(SnapshotManifest.PresetsTier, payload.Tiers);
    }

    [Fact]
    public void One_vendor_folder_shared_by_two_plugins_is_stored_once()
    {
        var fs = new FakeFileSystem()
            .AddFile(Settings, """
                {"MixerConfiguration":{"InputSettings":{"a":{"InputName":"Wave Mic 1",
                  "AudioPluginConfigurations":[
                    {"Name":"Pro-Q 4","Vendor":"FabFilter","FilePath":"C:\\VST3\\Pro-Q 4.vst3"},
                    {"Name":"Pro-C 2","Vendor":"FabFilter","FilePath":"C:\\VST3\\Pro-C 2.vst3"}]}}}}
                """)
            .AddFile(Roaming + @"\FabFilter\shared.ffp", "shared");

        var payload = Gather(fs, Tiers(presets: true));

        Assert.Single(payload.Files);
        Assert.All(payload.Plugins.Plugins, p => Assert.Equal(1, p.PresetFileCount));
    }

    // ----------------------------------------------------------------------- tier 4: binaries

    [Fact]
    public void A_single_file_plugin_is_copied_under_plugins()
    {
        var fs = Rig().AddFile(ProQPath, "pro-q bytes").AddFile(ClearPath, "clear bytes");

        var payload = Gather(fs, Tiers(binaries: true));

        Assert.Contains("plugins/FabFilter Pro-Q 4.vst3", payload.Files.Select(f => f.RelativePath));
        Assert.Contains("plugins/Clear.vst3", payload.Files.Select(f => f.RelativePath));
        Assert.Contains(SnapshotManifest.PluginsTier, payload.Tiers);
        Assert.All(payload.Plugins.Plugins, p => Assert.True(p.BinaryCaptured));
    }

    [Fact]
    public void A_vst3_that_is_a_DIRECTORY_is_recursed_and_records_a_non_zero_size()
    {
        // THE test for this phase. All six plugins on the author's machine are single files, so
        // the bundle path can only ever be exercised by a fixture like this one - and a bundle
        // treated as a file backs up NOTHING while looking like a success.
        // [[vst3-backs-up-as-nothing]]
        var fs = Rig()
            .AddFile(ClearPath, "clear bytes")
            .AddFile(ProQPath + @"\Contents\x86_64-win\FabFilter Pro-Q 4.vst3", "the real binary")
            .AddFile(ProQPath + @"\Contents\moduleinfo.json", "{}")
            .AddDirectory(ProQPath);

        var payload = Gather(fs, Tiers(binaries: true));
        var bundled = payload.Files
            .Where(f => f.RelativePath.StartsWith("plugins/FabFilter Pro-Q 4.vst3/", StringComparison.Ordinal))
            .ToList();

        Assert.Equal(2, bundled.Count);
        Assert.Contains(
            "plugins/FabFilter Pro-Q 4.vst3/Contents/x86_64-win/FabFilter Pro-Q 4.vst3",
            bundled.Select(f => f.RelativePath));
        Assert.True(bundled.Sum(f => f.Bytes.LongLength) > 0);
        Assert.Contains(SnapshotManifest.PluginsTier, payload.Tiers);
    }

    [Fact]
    public void An_empty_bundle_directory_is_a_failure_not_a_zero_byte_success()
    {
        // The silent-success bug wearing its own clothes.
        var fs = Rig().AddFile(ClearPath, "clear bytes").AddDirectory(ProQPath);

        var payload = Gather(fs, Tiers(binaries: true));

        Assert.DoesNotContain(SnapshotManifest.PluginsTier, payload.Tiers);
        Assert.Empty(payload.Files);
    }

    [Fact]
    public void One_missing_plugin_fails_the_whole_tier_rather_than_quietly_reducing_it()
    {
        // A snapshot claiming PLUGINS with five of six cannot do what its badge promises.
        var fs = Rig().AddFile(ProQPath, "pro-q bytes");

        var payload = Gather(fs, Tiers(binaries: true));

        Assert.DoesNotContain(SnapshotManifest.PluginsTier, payload.Tiers);
        Assert.Empty(payload.Files);
        Assert.All(payload.Plugins.Plugins, p => Assert.False(p.BinaryCaptured));
    }

    [Fact]
    public void A_failed_tier_4_costs_only_tier_4()
    {
        // Tier 1's extras and tier 2 are untouched by tier 4 giving up.
        var fs = Rig()
            .AddFile(Backup + @"\AutoBackup\Settings.auto.1.json", "auto")
            .AddFile(ProQPath, "pro-q bytes");

        var payload = Gather(fs, Tiers(binaries: true));

        Assert.Equal(["wavelink-backups/AutoBackup/Settings.auto.1.json"],
            payload.Files.Select(f => f.RelativePath));
        Assert.Equal(2, payload.Plugins.Plugins.Count);
    }

    [Fact]
    public void Two_plugins_with_the_same_file_name_do_not_overwrite_each_other()
    {
        var fs = new FakeFileSystem()
            .AddFile(Settings, """
                {"MixerConfiguration":{"InputSettings":{"a":{"InputName":"Wave Mic 1",
                  "AudioPluginConfigurations":[
                    {"Name":"Clear","Vendor":"Supertone","FilePath":"C:\\VST3\\Clear.vst3"},
                    {"Name":"Clear","Vendor":"Other","FilePath":"D:\\Audio\\Clear.vst3"}]}}}}
                """)
            .AddFile(@"C:\VST3\Clear.vst3", "one")
            .AddFile(@"D:\Audio\Clear.vst3", "two");

        var captured = Gather(fs, Tiers(binaries: true)).Files.Select(f => f.RelativePath).ToList();

        Assert.Equal(["plugins/Clear.vst3", "plugins/2-Clear.vst3"], captured);
    }

    [Fact]
    public void Switching_plugin_files_off_captures_none_and_claims_nothing()
    {
        var fs = Rig().AddFile(ProQPath, "pro-q").AddFile(ClearPath, "clear");

        var payload = Gather(fs, Tiers(binaries: false));

        Assert.Empty(payload.Files);
        Assert.DoesNotContain(SnapshotManifest.PluginsTier, payload.Tiers);
    }

    // -------------------------------------------------------------------------- the estimate

    [Fact]
    public void Every_tier_is_measured_whether_or_not_it_is_switched_on()
    {
        // The Settings dialog has to price a tier that is OFF, because pricing it is how the
        // user decides. Measuring must not read the bytes to find out.
        var fs = Rig()
            .AddFile(Backup + @"\AutoBackup\Settings.auto.1.json", "auto backup bytes")
            .AddFile(Roaming + @"\FabFilter\Pro-Q 4\one.ffp", "preset")
            .AddFile(ProQPath, "pro-q bytes")
            .AddFile(ClearPath, "clear bytes");

        var estimate = new TierCapture(fs, Roaming, Documents).Measure(Live(fs));

        Assert.Equal(17, estimate.WaveLinkBackupBytes);
        Assert.Equal(6, estimate.PresetBytes);
        Assert.Equal(22, estimate.PluginBinaryBytes);
        Assert.Equal(estimate.SettingsBytes + 17, estimate.TierOneBytes);
        Assert.DoesNotContain(ProQPath, fs.ReadCounts.Keys);
    }

    [Fact]
    public void A_machine_with_nothing_installed_measures_zero_rather_than_failing()
    {
        var estimate = new TierCapture(Rig(), Roaming, Documents).Measure(Live(Rig()));

        Assert.Equal(0, estimate.WaveLinkBackupBytes);
        Assert.Equal(0, estimate.PresetBytes);
        Assert.Equal(0, estimate.PluginBinaryBytes);
    }
}
