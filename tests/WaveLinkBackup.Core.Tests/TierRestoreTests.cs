using WaveLinkBackup.Core.Automation;
using WaveLinkBackup.Core.Capture;
using WaveLinkBackup.Core.Io;
using WaveLinkBackup.Core.Restore;
using WaveLinkBackup.Core.Snapshots;
using WaveLinkBackup.Core.Tests.Fakes;

namespace WaveLinkBackup.Core.Tests;

/// <summary>
/// Putting tiers 3 and 4 back — and the privilege rule that decides how much of this an ordinary
/// account can do.
///
/// **Tiers 1–3 must never need administrator rights.** They write to LocalState and %APPDATA%,
/// both of which the user owns. Tier 4 writes into `C:\Program Files\Common Files\VST3`, which is
/// the one thing in this program that can need elevation — so it is opt-in, and it says so
/// instead of failing with an access-denied trace ([[ADR-006]]).
/// </summary>
public sealed class TierRestoreTests
{
    private const string LocalAppData = @"C:\Users\test\AppData\Local";
    private const string Roaming = @"C:\Users\test\AppData\Roaming";

    /// <summary>On another drive on purpose - see TierCaptureTests.Documents.</summary>
    private const string Documents = @"G:\win_user-folders\Documents";
    private const string LocalState =
        LocalAppData + @"\Packages\Elgato.WaveLink_g54w8ztgkx496\LocalState";
    private const string Settings = LocalState + @"\Settings.json";
    private const string Store = LocalAppData + @"\WaveLinkBackup";

    private const string ProQPath = @"C:\Program Files\Common Files\VST3\FabFilter Pro-Q 4.vst3";

    private const string Rig = """
        {"MixerConfiguration":{"InputSettings":{"a":{"InputName":"Wave Mic 1",
          "AudioPluginConfigurations":[{"Name":"Pro-Q 4","Vendor":"FabFilter",
            "FilePath":"C:\\Program Files\\Common Files\\VST3\\FabFilter Pro-Q 4.vst3"}]}}}}
        """;

    /// <summary>A machine with presets and a plug-in, captured with every tier on.</summary>
    private static (Snapshot Snapshot, FakeFileSystem Fs) Captured(
        Action<FakeFileSystem>? arrange = null, bool binaries = true)
    {
        var fs = new FakeFileSystem()
            .AddFile(Settings, Rig)
            .AddFile(Roaming + @"\FabFilter\Pro-Q 4\Vocals\Bright.ffp", "bright")
            .AddFile(Roaming + @"\FabFilter\Pro-Q 4\My curve.ffp", "curve");

        arrange?.Invoke(fs);

        var live = SettingsInspector.For(fs, LocalAppData).Inspect().Value;
        var payload = new TierCapture(fs, Roaming, Documents).Gather(
            live, BackupSettings.Default with { IncludePresets = true, IncludePluginFiles = binaries });

        var snapshot = new SnapshotStore(fs, new FakeClock(), Store)
            .Write(live.Bytes, live.Analysis, SnapshotTrigger.Manual, "x", payload: payload).Value;

        return (snapshot, fs);
    }

    private static PluginManifest? Plugins(FakeFileSystem fs, Snapshot snapshot) =>
        new SnapshotPluginReader(fs).Read(snapshot);

    private static TierRestoreResult Restore(
        FakeFileSystem fs, Snapshot snapshot, RestoreOptions? options = null) =>
        new TierRestore(fs, Roaming, Documents).Restore(
            snapshot, Plugins(fs, snapshot), options ?? RestoreOptions.Default);

    // ------------------------------------------------------------------------ tier 3 back

    [Fact]
    public void Presets_go_back_where_they_came_from()
    {
        var (snapshot, fs) = Captured(f => f.AddFile(ProQPath, "plugin bytes"));
        fs.DeleteDirectory(Roaming + @"\FabFilter");

        var result = Restore(fs, snapshot);

        Assert.Equal(2, result.PresetFilesRestored);
        Assert.Equal("bright"u8.ToArray(), fs.Read(Roaming + @"\FabFilter\Pro-Q 4\Vocals\Bright.ffp"));
        Assert.Equal("curve"u8.ToArray(), fs.Read(Roaming + @"\FabFilter\Pro-Q 4\My curve.ffp"));
    }

    [Fact]
    public void A_preset_from_Documents_goes_back_to_Documents_and_not_to_AppData()
    {
        // The whole reason the snapshot names its roots. Documents is on another volume here, so
        // a mapping that lost the root would not merely misfile the presets - it would write them
        // to a drive the vendor never looks at, silently, and report a successful restore.
        var (snapshot, fs) = Captured(f => f
            .AddFile(Documents + @"\FabFilter\Presets\Pro-Q 4\Vocal Air.ffp", "air"));

        fs.DeleteDirectory(Documents + @"\FabFilter");
        fs.DeleteDirectory(Roaming + @"\FabFilter");

        var result = Restore(fs, snapshot);

        Assert.Equal(3, result.PresetFilesRestored);
        Assert.Equal("air"u8.ToArray(), fs.Read(Documents + @"\FabFilter\Presets\Pro-Q 4\Vocal Air.ffp"));
        Assert.False(fs.FileExists(Roaming + @"\FabFilter\Presets\Pro-Q 4\Vocal Air.ffp"));
    }

    [Fact]
    public void A_snapshot_written_before_the_roots_existed_still_restores_into_AppData()
    {
        // Schema 1 wrote presets/<Vendor>/... with no root segment, and everything in one came
        // from %APPDATA%. Reading it as AppData is what keeps every snapshot already on disk
        // restorable - this layout change must cost nobody their existing backups.
        var (snapshot, fs) = Captured();
        fs.DeleteDirectory(Roaming + @"\FabFilter");

        var legacy = Legacy(fs, snapshot);
        var result = new TierRestore(fs, Roaming, Documents)
            .Restore(legacy, Plugins(fs, legacy), RestoreOptions.Default);

        Assert.Equal(2, result.PresetFilesRestored);
        Assert.Equal("curve"u8.ToArray(), fs.Read(Roaming + @"\FabFilter\Pro-Q 4\My curve.ffp"));
    }

    /// <summary>
    /// The same snapshot with its preset files moved back to the schema-1 spelling - the shape
    /// every snapshot on a user's disk today has.
    /// </summary>
    private static Snapshot Legacy(FakeFileSystem fs, Snapshot snapshot)
    {
        var files = snapshot.Manifest.Files.ToDictionary(
            e => Rename(e.Key), e => e.Value, StringComparer.OrdinalIgnoreCase);

        foreach (var relative in snapshot.Manifest.Files.Keys)
        {
            var renamed = Rename(relative);
            if (renamed == relative) continue;

            fs.AddFile(
                SnapshotManifest.PathIn(snapshot.Directory, renamed),
                System.Text.Encoding.UTF8.GetString(
                    fs.Read(SnapshotManifest.PathIn(snapshot.Directory, relative))));
        }

        return snapshot with { Manifest = snapshot.Manifest with { Files = files } };

        static string Rename(string relative) =>
            relative.StartsWith("presets/appdata/", StringComparison.Ordinal)
                ? "presets/" + relative["presets/appdata/".Length..]
                : relative;
    }

    [Fact]
    public void Restoring_presets_needs_no_elevation_and_touches_nothing_in_Program_Files()
    {
        // The privilege model, stated as a test: everything that matters restores on an ordinary
        // account.
        var (snapshot, fs) = Captured(f => f.AddFile(ProQPath, "plugin bytes"));
        fs.Delete(ProQPath);

        var result = Restore(fs, snapshot, RestoreOptions.Default);

        Assert.False(result.NeedsElevation);
        Assert.Empty(result.Skipped);
        Assert.Equal(0, result.PluginFilesRestored);
        Assert.False(fs.FileExists(ProQPath));
    }

    [Fact]
    public void Switching_presets_off_restores_nothing_at_all()
    {
        var (snapshot, fs) = Captured(f => f.AddFile(ProQPath, "plugin bytes"));
        fs.DeleteDirectory(Roaming + @"\FabFilter");

        var result = Restore(fs, snapshot, RestoreOptions.SettingsOnly);

        Assert.False(result.RestoredAnything);
        Assert.False(fs.FileExists(Roaming + @"\FabFilter\Pro-Q 4\My curve.ffp"));
    }

    // ------------------------------------------------------------------------ tier 4 back

    [Fact]
    public void A_plugin_binary_goes_back_to_its_recorded_path_when_asked_for()
    {
        var (snapshot, fs) = Captured(f => f.AddFile(ProQPath, "plugin bytes"));
        fs.Delete(ProQPath);

        var result = Restore(fs, snapshot, new RestoreOptions(Presets: true, PluginBinaries: true));

        Assert.Equal(1, result.PluginFilesRestored);
        Assert.Equal("plugin bytes"u8.ToArray(), fs.Read(ProQPath));
    }

    [Fact]
    public void A_bundle_goes_back_with_its_whole_tree()
    {
        // The other half of [[vst3-backs-up-as-nothing]]: capturing a bundle is pointless if the
        // restore flattens it back into one file.
        var (snapshot, fs) = Captured(f => f
            .AddFile(ProQPath + @"\Contents\x86_64-win\FabFilter Pro-Q 4.vst3", "the real binary")
            .AddFile(ProQPath + @"\Contents\moduleinfo.json", "{}")
            .AddDirectory(ProQPath));

        fs.DeleteDirectory(ProQPath);

        var result = Restore(fs, snapshot, new RestoreOptions(PluginBinaries: true));

        Assert.Equal(2, result.PluginFilesRestored);
        Assert.Equal(
            "the real binary"u8.ToArray(),
            fs.Read(ProQPath + @"\Contents\x86_64-win\FabFilter Pro-Q 4.vst3"));
        Assert.True(fs.FileExists(ProQPath + @"\Contents\moduleinfo.json"));
    }

    [Fact]
    public void An_access_denied_binary_reports_that_it_needs_elevation_rather_than_throwing()
    {
        // Program Files without administrator rights. "Try again elevated" is a different answer
        // from "something else has the file", so the two are distinguished.
        var (snapshot, fs) = Captured(f => f.AddFile(ProQPath, "plugin bytes"));
        fs.WriteFailures[ProQPath] = new Queue<Exception>([new UnauthorizedAccessException("denied")]);

        var result = Restore(fs, snapshot, new RestoreOptions(PluginBinaries: true));

        Assert.True(result.NeedsElevation);
        Assert.Equal([ProQPath], result.Skipped);
        Assert.Equal(0, result.PluginFilesRestored);
    }

    [Fact]
    public void A_locked_binary_is_skipped_without_claiming_elevation_would_help()
    {
        var (snapshot, fs) = Captured(f => f.AddFile(ProQPath, "plugin bytes"));
        fs.WriteFailures[ProQPath] = new Queue<Exception>([new IOException("in use")]);

        var result = Restore(fs, snapshot, new RestoreOptions(PluginBinaries: true));

        Assert.False(result.NeedsElevation);
        Assert.Single(result.Skipped);
    }

    [Fact]
    public void A_snapshot_without_the_plugin_tier_restores_no_binaries_even_when_asked()
    {
        var (snapshot, fs) = Captured(f => f.AddFile(ProQPath, "plugin bytes"), binaries: false);
        fs.Delete(ProQPath);

        var result = Restore(fs, snapshot, new RestoreOptions(PluginBinaries: true));

        Assert.Equal(0, result.PluginFilesRestored);
        Assert.False(fs.FileExists(ProQPath));
    }

    [Fact]
    public void A_snapshot_from_before_the_tiers_existed_restores_nothing_extra_and_does_not_fail()
    {
        var fs = new FakeFileSystem().AddFile(Settings, Rig);
        var live = SettingsInspector.For(fs, LocalAppData).Inspect().Value;
        var snapshot = new SnapshotStore(fs, new FakeClock(), Store)
            .Write(live.Bytes, live.Analysis, SnapshotTrigger.Manual, "old").Value;

        var result = new TierRestore(fs, Roaming, Documents).Restore(
            snapshot, Plugins(fs, snapshot), new RestoreOptions(PluginBinaries: true));

        Assert.False(result.RestoredAnything);
        Assert.False(result.NeedsElevation);
    }
}
