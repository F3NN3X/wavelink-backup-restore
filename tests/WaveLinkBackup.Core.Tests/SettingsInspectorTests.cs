using System.Text;
using WaveLinkBackup.Core.Io;
using WaveLinkBackup.Core.Discovery;
using WaveLinkBackup.Core.Results;
using WaveLinkBackup.Core.Tests.Fakes;

namespace WaveLinkBackup.Core.Tests;

public sealed class SettingsInspectorTests
{
    private const string LocalAppData = @"C:\Users\test\AppData\Local";
    private const string Settings =
        LocalAppData + @"\Packages\Elgato.WaveLink_g54w8ztgkx496\LocalState\Settings.json";

    private const string Valid = """
        {"MixerConfiguration":{"InputSettings":{"a":{"InputName":"Wave Mic 1"}}}}
        """;

    private const string Cache =
        LocalAppData + @"\Packages\Elgato.WaveLink_g54w8ztgkx496\LocalState"
        + @"\AudioPluginCache\AvailablePlugins.cache";

    private const string WithPlugin = """
        {"MixerConfiguration":{"InputSettings":{"a":{"InputName":"Wave Mic 1",
          "AudioPluginConfigurations":[{"Name":"Pro-Q 4","FilePath":"C:\\VST3\\Pro-Q 4.vst3"}]}}}}
        """;

    private static SettingsInspector Inspector(FakeFileSystem fs) =>
        new(new SettingsLocator(fs, LocalAppData), new SettingsReader(fs));

    private static SettingsInspector WithCache(FakeFileSystem fs) =>
        SettingsInspector.For(fs, LocalAppData);

    [Fact]
    public void Locates_reads_and_analyses_in_one_call()
    {
        var fs = new FakeFileSystem().AddFile(Settings, Valid);

        var result = Inspector(fs).Inspect();

        Assert.True(result.IsSuccess);
        Assert.Equal(Settings, result.Value.Location.SettingsPath);
        Assert.Equal(1, result.Value.Analysis.Fingerprint.InputCount);
        Assert.Equal(Encoding.UTF8.GetBytes(Valid).Length, result.Value.Bytes.Length);
    }

    [Fact]
    public void A_torn_read_is_retried_exactly_once()
    {
        // A single read is not atomic against Wave Link's own save, so a capture taken
        // mid-write can catch a half-written file. That is a retry, not a broken config.
        var fs = new FakeFileSystem().AddFile(Settings, Valid);
        fs.ReadSequence[Settings] = new Queue<byte[]>([Encoding.UTF8.GetBytes("{ torn")]);

        var result = Inspector(fs).Inspect();

        Assert.True(result.IsSuccess);
        Assert.Equal(2, fs.ReadCounts[Settings]);
    }

    [Fact]
    public void A_file_malformed_on_both_reads_fails_and_is_not_retried_again()
    {
        var fs = new FakeFileSystem().AddFile(Settings, "{ definitely not json");

        var result = Inspector(fs).Inspect();

        Assert.IsType<MalformedSettings>(result.Error);
        Assert.Equal(2, fs.ReadCounts[Settings]);
    }

    [Fact]
    public void A_locked_file_is_not_retried()
    {
        // Retrying a lock turns an immediate, clearly-worded failure into a slow one
        // reported as a timeout. The lock is Wave Link's steady state, not a window.
        var fs = new FakeFileSystem().AddFile(Settings, Valid);
        fs.ReadFailures[Settings] = new Queue<Exception>([new IOException("used by another process")]);

        var result = Inspector(fs).Inspect();

        Assert.IsType<SettingsUnreadable>(result.Error);
        Assert.Equal(1, fs.ReadCounts[Settings]);
    }

    [Fact]
    public void A_discovery_failure_short_circuits_before_any_read()
    {
        var fs = new FakeFileSystem();

        Assert.IsType<WaveLinkNotInstalled>(Inspector(fs).Inspect().Error);
        Assert.Empty(fs.ReadCounts);
    }

    [Fact]
    public void An_inspection_carries_the_referenced_plugins_with_versions_from_the_cache()
    {
        // Tier 2 is built from this: the settings say what is in use, the scanner cache says
        // which version it is. ADR-006.
        var fs = new FakeFileSystem()
            .AddFile(Settings, WithPlugin)
            .AddFile(Cache, """
                <KNOWNPLUGINS>
                  <PLUGIN name="Pro-Q 4" manufacturer="FabFilter" version="4.1.2"
                          file="C:\VST3\Pro-Q 4.vst3" uniqueId="a1b2c3d4"/>
                </KNOWNPLUGINS>
                """);

        var plugin = Assert.Single(WithCache(fs).Inspect().Value.Plugins);

        Assert.Equal("Pro-Q 4", plugin.Name);
        Assert.Equal("4.1.2", plugin.Version);
        Assert.Equal("a1b2c3d4", plugin.UniqueId);
    }

    [Fact]
    public void An_absent_cache_leaves_the_version_unknown_and_the_plugin_recorded()
    {
        var fs = new FakeFileSystem().AddFile(Settings, WithPlugin);

        var plugin = Assert.Single(WithCache(fs).Inspect().Value.Plugins);

        Assert.Equal(@"C:\VST3\Pro-Q 4.vst3", plugin.FilePath);
        Assert.False(plugin.VersionKnown);
    }

    [Fact]
    public void An_inspector_built_without_a_cache_reader_still_reports_the_referenced_plugins()
    {
        // The cache only enriches; the settings file is the authority on what is in use.
        var fs = new FakeFileSystem().AddFile(Settings, WithPlugin);

        Assert.Single(Inspector(fs).Inspect().Value.Plugins);
    }

    [Fact]
    public void Duplicate_keys_inspect_successfully_and_are_reported()
    {
        var fs = new FakeFileSystem().AddFile(Settings,
            """{"MixerConfiguration":{"InputSettings":{"a":{}}},"D":1,"d":2}""");

        var result = Inspector(fs).Inspect();

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.Analysis.Report.HasCaseInsensitiveDuplicateKeys);
    }
}
