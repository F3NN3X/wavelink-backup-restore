using System.Text;
using WaveLinkBackup.Core.Analysis;
using WaveLinkBackup.Core.Discovery;
using WaveLinkBackup.Core.Io;
using WaveLinkBackup.Core.Tests.Fakes;

namespace WaveLinkBackup.Core.Tests;

/// <summary>
/// Tier 2's foundation: which plugins a settings file actually references, and what the
/// scanner cache knows about them. ADR-006, SPEC.md 9.
/// </summary>
public sealed class PluginReferencesTests
{
    /// <summary>
    /// Shaped like the real file: a mic chain of third-party effects, an Elgato built-in with
    /// the empty FilePath that marks it, and the same compressor reused on a second channel.
    /// </summary>
    private const string Settings = """
    {
      "MixerConfiguration": {
        "InputSettings": {
          "BS33J1A05009\\PCM_IN_01_C_00_SD1": {
            "InputName": "Wave Mic 1",
            "AudioPluginConfigurations": [
              {
                "Name": "Pro-Q 4",
                "Vendor": "FabFilter",
                "FilePath": "C:\\Program Files\\Common Files\\VST3\\FabFilter\\FabFilter Pro-Q 4.vst3",
                "ParameterState": "ab+cd/ef=="
              },
              {
                "Name": "Pro-C 2",
                "Vendor": "FabFilter",
                "FilePath": "C:\\Program Files\\Common Files\\VST3\\FabFilter\\FabFilter Pro-C 2.vst3"
              },
              { "Name": "Elgato Noise Removal", "Vendor": "Elgato", "FilePath": "" }
            ]
          },
          "PCM_OUT_00_V_14_SD8": {
            "InputName": "Voice",
            "AudioPluginConfigurations": [
              {
                "Name": "Pro-C 2",
                "Vendor": "FabFilter",
                "FilePath": "C:\\Program Files\\Common Files\\VST3\\FabFilter\\FabFilter Pro-C 2.vst3"
              }
            ]
          },
          "PCM_OUT_00_V_04_SD3": { "InputName": "Browser", "AudioPluginConfigurations": [] }
        }
      }
    }
    """;

    /// <summary>A JUCE KNOWNPLUGINS document, trimmed to the attributes tier 2 reads.</summary>
    private const string Cache = """
    <?xml version="1.0" encoding="UTF-8"?>
    <KNOWNPLUGINS>
      <PLUGIN name="Pro-Q 4" format="VST3" category="Fx" manufacturer="FabFilter"
              version="4.1.2" file="C:\Program Files\Common Files\VST3\FabFilter\FabFilter Pro-Q 4.vst3"
              uniqueId="a1b2c3d4" isInstrument="0"/>
      <PLUGIN name="Pro-C 2" format="VST3" category="Fx" manufacturer="FabFilter"
              version="2.3.0" file="C:\Program Files\Common Files\VST3\FabFilter\FabFilter Pro-C 2.vst3"
              uniqueId="e5f6a7b8" isInstrument="0"/>
      <BLACKLIST/>
    </KNOWNPLUGINS>
    """;

    private static IReadOnlyList<ReferencedPlugin> Referenced(string json) =>
        SettingsAnalysis.Analyse(Encoding.UTF8.GetBytes(json)).Value.ReferencedPlugins;

    // -----------------------------------------------------------------------------------
    // The referenced set
    // -----------------------------------------------------------------------------------

    [Fact]
    public void Only_entries_with_a_FilePath_are_referenced()
    {
        // An empty FilePath is an Elgato built-in. It ships with Wave Link, so capturing it
        // would be paying to back up the installer. ADR-006.
        var plugins = Referenced(Settings);

        Assert.Equal(["Pro-Q 4", "Pro-C 2"], plugins.Select(p => p.Name));
        Assert.DoesNotContain(plugins, p => p.Name == "Elgato Noise Removal");
    }

    [Fact]
    public void A_plugin_on_two_channels_is_one_member_of_the_set()
    {
        // Tier 2 describes a set of plugins, not a list of placements.
        Assert.Single(Referenced(Settings), p => p.Name == "Pro-C 2");
    }

    [Fact]
    public void Name_vendor_and_path_come_through_verbatim()
    {
        var proQ = Referenced(Settings).First();

        Assert.Equal("Pro-Q 4", proQ.Name);
        Assert.Equal("FabFilter", proQ.Vendor);
        Assert.Equal(
            @"C:\Program Files\Common Files\VST3\FabFilter\FabFilter Pro-Q 4.vst3",
            proQ.FilePath);
    }

    [Fact]
    public void A_whitespace_FilePath_counts_as_absent()
    {
        // "   " is not a location, and treating it as one would put a garbage row in
        // plugins.json and a phantom name in the restore dialog's missing-plugin warning.
        Assert.Empty(Referenced("""
            {"MixerConfiguration":{"InputSettings":{"A":{"AudioPluginConfigurations":[
              {"Name":"Ghost","FilePath":"   "}]}}}}
            """));
    }

    [Fact]
    public void A_referenced_plugin_with_no_name_falls_back_to_its_file_name()
    {
        var plugin = Assert.Single(Referenced("""
            {"MixerConfiguration":{"InputSettings":{"A":{"AudioPluginConfigurations":[
              {"FilePath":"C:\\VST3\\Saturn 2.vst3"}]}}}}
            """));

        Assert.Equal("Saturn 2", plugin.Name);
        Assert.Null(plugin.Vendor);
    }

    [Fact]
    public void A_rig_of_only_built_ins_references_nothing()
    {
        Assert.Empty(Referenced("""
            {"MixerConfiguration":{"InputSettings":{"A":{"InputName":"Wave Mic 1",
              "AudioPluginConfigurations":[{"Name":"Elgato Noise Removal","FilePath":""}]}}}}
            """));
    }

    [Fact]
    public void A_malformed_effect_entry_does_not_take_the_analysis_down()
    {
        // A string where an object belongs is skipped, not thrown on: the settings file still
        // analyses, and a capture is still possible.
        var result = SettingsAnalysis.Analyse(Encoding.UTF8.GetBytes("""
            {"MixerConfiguration":{"InputSettings":{"A":{"AudioPluginConfigurations":[
              "not an object", {"Name":"Real","FilePath":"C:\\VST3\\Real.vst3"}]}}}}
            """));

        Assert.True(result.IsSuccess);
        Assert.Equal("Real", Assert.Single(result.Value.ReferencedPlugins).Name);
    }

    // -----------------------------------------------------------------------------------
    // The scanner cache
    // -----------------------------------------------------------------------------------

    [Fact]
    public void The_cache_yields_name_manufacturer_version_path_and_uniqueId()
    {
        var proQ = PluginCache.Parse(Cache).First();

        Assert.Equal("Pro-Q 4", proQ.Name);
        Assert.Equal("FabFilter", proQ.Manufacturer);
        Assert.Equal("4.1.2", proQ.Version);
        Assert.Equal("a1b2c3d4", proQ.UniqueId);
        Assert.Equal(
            @"C:\Program Files\Common Files\VST3\FabFilter\FabFilter Pro-Q 4.vst3",
            proQ.FilePath);
    }

    [Fact]
    public void An_older_scanner_uid_attribute_is_read_as_the_uniqueId()
    {
        var plugin = Assert.Single(PluginCache.Parse("""
            <KNOWNPLUGINS><PLUGIN name="Clear" file="C:\VST3\Clear.vst3" uid="9f8e"/></KNOWNPLUGINS>
            """));

        Assert.Equal("9f8e", plugin.UniqueId);
    }

    [Fact]
    public void A_blank_attribute_reads_as_absent_rather_than_as_an_empty_version()
    {
        var plugin = Assert.Single(PluginCache.Parse("""
            <KNOWNPLUGINS><PLUGIN name="Clear" file="C:\VST3\Clear.vst3" version=""/></KNOWNPLUGINS>
            """));

        Assert.Null(plugin.Version);
    }

    [Fact]
    public void Unparseable_cache_XML_yields_nothing_rather_than_throwing()
    {
        // Tier 2 is always on, so this file must never be able to fail a snapshot.
        Assert.Empty(PluginCache.Parse("<KNOWNPLUGINS><PLUGIN name="));
        Assert.Empty(PluginCache.Parse(""));
        Assert.Empty(PluginCache.Parse("   "));
    }

    [Fact]
    public void A_cache_written_with_a_byte_order_mark_still_parses()
    {
        // ReadSharedText decodes bytes verbatim, so the BOM arrives inside the string.
        var withBom = '\uFEFF' + """
            <KNOWNPLUGINS><PLUGIN name="Clear" file="C:\VST3\Clear.vst3" version="1.0"/></KNOWNPLUGINS>
            """;

        Assert.Equal("1.0", Assert.Single(PluginCache.Parse(withBom)).Version);
    }

    // -----------------------------------------------------------------------------------
    // Cross-reference
    // -----------------------------------------------------------------------------------

    [Fact]
    public void Resolving_attaches_the_version_and_uniqueId_from_the_cache()
    {
        var resolved = PluginReferences.Resolve(Referenced(Settings), PluginCache.Parse(Cache));

        Assert.Equal(["4.1.2", "2.3.0"], resolved.Select(p => p.Version));
        Assert.All(resolved, p => Assert.True(p.VersionKnown));
    }

    [Fact]
    public void A_plugin_the_cache_has_never_seen_is_still_recorded_with_no_version()
    {
        // The cache is rebuilt by rescanning and can be stale or missing; the settings
        // file's FilePath is the authority on what is in use. ADR-006.
        var resolved = PluginReferences.Resolve(Referenced(Settings), []);

        Assert.Equal(2, resolved.Count);
        Assert.All(resolved, p => Assert.Null(p.Version));
        Assert.All(resolved, p => Assert.False(p.VersionKnown));
    }

    [Fact]
    public void Matching_is_by_path_before_name()
    {
        // Two builds of the same plugin share a name. Picking by name would record a version
        // the ParameterState was not written by - the drift tier 2 exists to make visible.
        var cached = PluginCache.Parse("""
            <KNOWNPLUGINS>
              <PLUGIN name="Pro-Q 4" version="3.0.0" file="C:\Old\FabFilter Pro-Q 4.vst3"/>
              <PLUGIN name="Pro-Q 4" version="4.1.2"
                      file="C:\Program Files\Common Files\VST3\FabFilter\FabFilter Pro-Q 4.vst3"/>
            </KNOWNPLUGINS>
            """);

        var proQ = PluginReferences.Resolve(Referenced(Settings), cached).First();

        Assert.Equal("4.1.2", proQ.Version);
    }

    [Fact]
    public void A_path_matches_across_separator_and_trailing_slash_differences()
    {
        // A bundle is a directory, and a scanner that writes it with a trailing separator
        // must still match the settings file's spelling of the same path.
        var referenced = Referenced("""
            {"MixerConfiguration":{"InputSettings":{"A":{"AudioPluginConfigurations":[
              {"Name":"Clear","FilePath":"C:\\VST3\\Clear.vst3"}]}}}}
            """);

        var resolved = PluginReferences.Resolve(referenced, PluginCache.Parse("""
            <KNOWNPLUGINS><PLUGIN name="Clear" version="2.0" file="C:/VST3/Clear.vst3\"/></KNOWNPLUGINS>
            """));

        Assert.Equal("2.0", Assert.Single(resolved).Version);
    }

    [Fact]
    public void A_moved_plugin_falls_back_to_a_name_match()
    {
        // The user moved the .vst3 and Wave Link rescanned; the version is still worth
        // recording, and the settings file's path stays the one that gets captured.
        var referenced = Referenced("""
            {"MixerConfiguration":{"InputSettings":{"A":{"AudioPluginConfigurations":[
              {"Name":"Clear","FilePath":"D:\\Audio\\VST3\\Clear.vst3"}]}}}}
            """);

        var resolved = Assert.Single(PluginReferences.Resolve(referenced, PluginCache.Parse("""
            <KNOWNPLUGINS><PLUGIN name="Clear" version="2.0" file="C:\VST3\Clear.vst3"/></KNOWNPLUGINS>
            """)));

        Assert.Equal("2.0", resolved.Version);
        Assert.Equal(@"D:\Audio\VST3\Clear.vst3", resolved.FilePath);
    }

    [Fact]
    public void A_vendor_missing_from_the_settings_is_taken_from_the_cache()
    {
        var referenced = Referenced("""
            {"MixerConfiguration":{"InputSettings":{"A":{"AudioPluginConfigurations":[
              {"Name":"Clear","FilePath":"C:\\VST3\\Clear.vst3"}]}}}}
            """);

        var resolved = Assert.Single(PluginReferences.Resolve(referenced, PluginCache.Parse("""
            <KNOWNPLUGINS>
              <PLUGIN name="Clear" manufacturer="Supertone" file="C:\VST3\Clear.vst3"/>
            </KNOWNPLUGINS>
            """)));

        Assert.Equal("Supertone", resolved.Vendor);
    }

    // -----------------------------------------------------------------------------------
    // Reading it off disk
    // -----------------------------------------------------------------------------------

    private static SettingsLocation Location(string localState) =>
        new(Path.Combine(localState, "Settings.json"), "Elgato.WaveLink_g54w8ztgkx496",
            localState, Path.Combine(localState, "Logs"));

    [Fact]
    public void The_cache_sits_beside_the_settings_in_AudioPluginCache()
    {
        Assert.Equal(
            @"C:\LocalState\AudioPluginCache\AvailablePlugins.cache",
            Location(@"C:\LocalState").PluginCachePath);
    }

    [Fact]
    public void Reading_a_present_cache_returns_its_plugins()
    {
        var location = Location(@"C:\LocalState");
        var fileSystem = new FakeFileSystem().AddFile(location.PluginCachePath, Cache);

        Assert.Equal(2, new PluginCacheReader(fileSystem).Read(location).Count);
    }

    [Fact]
    public void An_absent_cache_reads_as_empty_rather_than_as_a_failure()
    {
        // A rig with no third-party plugins has never made the scanner write this file.
        var location = Location(@"C:\LocalState");

        Assert.Empty(new PluginCacheReader(new FakeFileSystem()).Read(location));
    }

    [Fact]
    public void A_locked_cache_reads_as_empty_rather_than_as_a_failure()
    {
        // Wave Link holds it while it rescans, which is exactly when a capture may fire.
        var location = Location(@"C:\LocalState");
        var fileSystem = new FakeFileSystem().AddFile(location.PluginCachePath, Cache);
        fileSystem.ReadFailures[location.PluginCachePath] =
            new Queue<Exception>([new IOException("in use by another process")]);

        Assert.Empty(new PluginCacheReader(fileSystem).Read(location));
    }
}
