using System.Security.Cryptography;
using System.Text;
using WaveLinkBackup.Core.Analysis;
using WaveLinkBackup.Core.Automation;
using WaveLinkBackup.Core.Capture;
using WaveLinkBackup.Core.Io;
using WaveLinkBackup.Core.Snapshots;
using WaveLinkBackup.Core.Tests.Fakes;

namespace WaveLinkBackup.Core.Tests;

/// <summary>
/// Tier 2's payload: plugins.json, and the hash it attaches to each binary.
///
/// Every test here is really one assertion said twice: tier 2 is always on (ADR-006), so
/// nothing in it may fail a snapshot. A cache that lied, a file that vanished, a plugins.json
/// someone truncated - each has to come out as "unknown", never as an exception.
/// </summary>
public sealed class PluginManifestTests
{
    private const string ProQPath = @"C:\Program Files\Common Files\VST3\FabFilter\FabFilter Pro-Q 4.vst3";

    private static PluginManifest Sample => new(PluginManifest.CurrentSchemaVersion, [
        new("Pro-Q 4", "FabFilter", "4.1.2", "a1b2c3d4", ProQPath, "9f86d0",
            Channels: ["Wave Mic 1", "Voice"],
            PresetSource: @"C:\Users\test\AppData\Roaming\FabFilter\Pro-Q 4",
            PresetFileCount: 12, PresetBytes: 4096, BinaryPath: "plugins/FabFilter Pro-Q 4.vst3"),
        new("Saturn 2", null, null, null, @"C:\VST3\Saturn 2.vst3", null, Channels: []),
    ]);

    private static PluginManifest RoundTrip(PluginManifest manifest) =>
        PluginManifestSerializer.Read(PluginManifestSerializer.Write(manifest));

    private static PluginManifest Read(string json) =>
        PluginManifestSerializer.Read(Encoding.UTF8.GetBytes(json));

    // ----------------------------------------------------------------------- the shape

    [Fact]
    public void Records_name_vendor_version_uniqueId_path_and_hash_for_every_plugin()
    {
        // The six fields ADR-006 asks for. Together they turn "my effects are gone and I
        // don't know why" into "install FabFilter Pro-Q 4 v4.1.2".
        var plugin = RoundTrip(Sample).Plugins.First();

        Assert.Equal("Pro-Q 4", plugin.Name);
        Assert.Equal("FabFilter", plugin.Vendor);
        Assert.Equal("4.1.2", plugin.Version);
        Assert.Equal("a1b2c3d4", plugin.UniqueId);
        Assert.Equal(ProQPath, plugin.FilePath);
        Assert.Equal("9f86d0", plugin.Sha256);
    }

    [Fact]
    public void Round_trips_every_field_including_the_unknown_ones()
    {
        Assert.Equal(Sample, RoundTrip(Sample));
    }

    [Fact]
    public void A_rig_of_only_built_ins_round_trips_as_an_empty_capture()
    {
        // Distinct from a snapshot that never looked: this one looked and found none.
        Assert.Empty(RoundTrip(PluginManifest.Empty).Plugins);
        Assert.Equal(PluginManifest.CurrentSchemaVersion, RoundTrip(PluginManifest.Empty).SchemaVersion);
    }

    // --------------------------------------------------------------- the tolerant read

    [Fact]
    public void A_truncated_file_reads_as_no_plugins_rather_than_throwing()
    {
        // Tier 2 cannot be switched off, so a malformed plugins.json has to degrade. The
        // alternative is a snapshot nobody can restore because of a file nobody reads.
        Assert.Empty(Read("""{"schemaVersion": 1, "plugins": [{"name": "Pro-Q""").Plugins);
    }

    [Fact]
    public void Empty_bytes_read_as_no_plugins()
    {
        Assert.Empty(PluginManifestSerializer.Read([]).Plugins);
    }

    [Fact]
    public void A_file_that_is_not_an_object_reads_as_no_plugins()
    {
        Assert.Empty(Read("[1, 2, 3]").Plugins);
        Assert.Empty(Read("\"plugins\"").Plugins);
    }

    [Fact]
    public void Each_missing_field_degrades_on_its_own()
    {
        // Per-field, not per-file. A plugin whose version the writer could not establish is
        // still worth naming - that is the whole tier.
        var plugin = Assert.Single(
            Read("""{"plugins":[{"name":"Pro-Q 4","filePath":"C:\\a.vst3"}]}""").Plugins);

        Assert.Equal("Pro-Q 4", plugin.Name);
        Assert.Equal(@"C:\a.vst3", plugin.FilePath);
        Assert.Null(plugin.Vendor);
        Assert.Null(plugin.Version);
        Assert.Null(plugin.UniqueId);
        Assert.Null(plugin.Sha256);
        Assert.False(plugin.VersionKnown);
    }

    [Fact]
    public void A_wrong_typed_field_reads_as_unknown_rather_than_failing_the_entry()
    {
        var plugin = Assert.Single(Read("""
            {"plugins":[{"name":"Pro-Q 4","filePath":"C:\\a.vst3","version":4.12,"vendor":null}]}
            """).Plugins);

        Assert.Equal("Pro-Q 4", plugin.Name);
        Assert.Null(plugin.Version);
        Assert.Null(plugin.Vendor);
    }

    [Fact]
    public void A_plugin_entry_that_is_not_an_object_is_skipped_and_its_neighbours_survive()
    {
        var plugins = Read("""
            {"plugins":[7,{"name":"Pro-Q 4","filePath":"C:\\a.vst3"},null]}
            """).Plugins;

        Assert.Equal(["Pro-Q 4"], plugins.Select(p => p.Name));
    }

    [Fact]
    public void An_entry_naming_neither_a_file_nor_a_plugin_is_dropped()
    {
        // A row of blanks in the restore dialog's missing-plugin warning is worse than no row.
        Assert.Empty(Read("""{"plugins":[{"vendor":"FabFilter","version":"4.1.2"}]}""").Plugins);
    }

    [Fact]
    public void An_entry_with_a_path_but_no_name_falls_back_to_its_file_name()
    {
        Assert.Equal("Saturn 2", Read("""{"plugins":[{"filePath":"C:\\VST3\\Saturn 2.vst3"}]}""")
            .Plugins.Single().Name);
    }

    [Fact]
    public void A_newer_schema_version_is_read_rather_than_rejected()
    {
        // The opposite of ManifestSerializer, deliberately. That result decides what gets
        // written to disk; this one decides what a warning says, and a partial warning
        // beats none.
        var manifest = Read("""
            {"schemaVersion":99,"plugins":[{"name":"Pro-Q 4","filePath":"C:\\a.vst3","futureField":true}]}
            """);

        Assert.Equal(99, manifest.SchemaVersion);
        Assert.Equal("Pro-Q 4", manifest.Plugins.Single().Name);
    }

    // ------------------------------------------------------------------- the binary hash

    private static PluginBinaryFiles Binaries(FakeFileSystem fs) => new(fs);

    private static ResolvedPlugin Resolved(string path) =>
        new("Pro-Q 4", "FabFilter", path, "4.1.2", "a1b2c3d4", ["Wave Mic 1"]);

    [Fact]
    public void The_hash_is_of_the_binary_as_it_stood_at_capture_time()
    {
        var fs = new FakeFileSystem().AddFile(ProQPath, "plugin bytes");

        Assert.Equal(
            Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes("plugin bytes"))),
            Binaries(fs).HashOf(ProQPath));
    }

    [Fact]
    public void A_plugin_missing_from_this_machine_hashes_to_nothing_rather_than_throwing()
    {
        // Uninstalled since the settings last named it. The settings' FilePath is still the
        // authority on what is in use, so the row stays - with no hash against it.
        Assert.Null(Binaries(new FakeFileSystem()).HashOf(ProQPath));
    }

    [Fact]
    public void A_vst3_bundle_records_no_hash_rather_than_the_hash_of_nothing()
    {
        // A .vst3 that is a DIRECTORY is a bundle, and what identifies it is its whole tree -
        // tier 4's problem. [[vst3-backs-up-as-nothing]]
        var fs = new FakeFileSystem()
            .AddFile(ProQPath + @"\Contents\x86_64-win\Pro-Q 4.vst3", "real bytes");

        Assert.Null(Binaries(fs).HashOf(ProQPath));
    }

    [Fact]
    public void A_locked_binary_leaves_the_hash_unknown_instead_of_failing()
    {
        var fs = new FakeFileSystem().AddFile(ProQPath, "plugin bytes");
        fs.ReadFailures[ProQPath] = new Queue<Exception>([new IOException("in use")]);

        Assert.Null(Binaries(fs).HashOf(ProQPath));
    }

    // ------------------------------------------------------- through the capture path

    private const string LocalAppData = @"C:\Users\test\AppData\Local";
    private const string LocalState =
        LocalAppData + @"\Packages\Elgato.WaveLink_g54w8ztgkx496\LocalState";

    [Fact]
    public void A_capture_taken_the_normal_way_carries_tier_2_end_to_end()
    {
        // The wiring test: the settings name the plugin, the cache names its version, the
        // binary is on disk, and one BackUpNow lands all three in the snapshot.
        var fs = new FakeFileSystem()
            .AddFile(LocalState + @"\Settings.json", """
                {"MixerConfiguration":{"InputSettings":{"a":{"InputName":"Wave Mic 1",
                  "AudioPluginConfigurations":[{"Name":"Pro-Q 4",
                    "FilePath":"C:\\Program Files\\Common Files\\VST3\\FabFilter\\FabFilter Pro-Q 4.vst3"}]}}}}
                """)
            .AddFile(LocalState + @"\AudioPluginCache\AvailablePlugins.cache", $"""
                <KNOWNPLUGINS>
                  <PLUGIN name="Pro-Q 4" manufacturer="FabFilter" version="4.1.2"
                          file="{ProQPath}" uniqueId="a1b2c3d4"/>
                </KNOWNPLUGINS>
                """)
            .AddFile(ProQPath, "plugin bytes");

        var store = new SnapshotStore(fs, new FakeClock(), LocalAppData + @"\WaveLinkBackup");
        var capture = new TierCapture(fs, LocalAppData + @"\Roaming");
        var snapshot = new BackupService(
                SettingsInspector.For(fs, LocalAppData), store,
                gatherPayload: live => capture.Gather(live, BackupSettings.Default))
            .BackUpNow("Before 3.3 beta").Value;

        Assert.Contains(SnapshotManifest.PluginManifestTier, snapshot.Manifest.Tiers);

        var plugin = PluginManifestSerializer.Read(fs.Read(snapshot.PluginsPath)).Plugins.Single();
        Assert.Equal("Pro-Q 4", plugin.Name);
        Assert.Equal("FabFilter", plugin.Vendor);
        Assert.Equal("4.1.2", plugin.Version);
        Assert.Equal("a1b2c3d4", plugin.UniqueId);
        Assert.Equal(ProQPath, plugin.FilePath);
        Assert.NotNull(plugin.Sha256);
    }

    [Fact]
    public void A_caller_with_no_capture_wired_writes_no_plugins_json_at_all()
    {
        // Not "looked and found none" - never looked. SnapshotPayload's null case from outside.
        var fs = new FakeFileSystem().AddFile(LocalState + @"\Settings.json", """
            {"MixerConfiguration":{"InputSettings":{"a":{"InputName":"Wave Mic 1"}}}}
            """);

        var store = new SnapshotStore(fs, new FakeClock(), LocalAppData + @"\WaveLinkBackup");
        var snapshot = new BackupService(SettingsInspector.For(fs, LocalAppData), store)
            .BackUpNow("x").Value;

        Assert.Equal([SnapshotManifest.SettingsTier], snapshot.Manifest.Tiers);
        Assert.False(fs.FileExists(snapshot.PluginsPath));
    }
}
