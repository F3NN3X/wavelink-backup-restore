using WaveLinkBackup.Core.Analysis;
using WaveLinkBackup.Core.Restore;
using WaveLinkBackup.Core.Snapshots;
using WaveLinkBackup.Core.Tests.Fakes;

namespace WaveLinkBackup.Core.Tests;

/// <summary>
/// Phase 6 section 5: what a restore would find for the plug-ins a snapshot recorded.
///
/// This is where tier 2 pays for itself. Without it, restoring a settings file onto a machine
/// missing FabFilter Pro-Q 4 loads that channel with the effect switched OFF and says nothing -
/// which looks exactly like an incomplete backup. With it, the dialog names the plug-in before
/// the user presses the button.
/// </summary>
public sealed class PluginResolutionTests
{
    private const string ProQPath = @"C:\Program Files\Common Files\VST3\FabFilter\FabFilter Pro-Q 4.vst3";
    private const string ClearPath = @"C:\Program Files\Common Files\VST3\Clear.vst3";

    private static PluginManifestEntry Entry(
        string name, string vendor, string path, string? version = "4.1.2", params string[] channels) =>
        new(name, vendor, version, "a1b2c3d4", path, Sha256: null, Channels: channels);

    private static PluginManifest Snapshot(params PluginManifestEntry[] entries) =>
        new(PluginManifest.CurrentSchemaVersion, entries);

    private static CachedPlugin Cached(string name, string path, string? version) =>
        new(name, "FabFilter", version, path, "a1b2c3d4");

    private static PluginRestoreCheck Check(
        FakeFileSystem fs, PluginManifest snapshot, params CachedPlugin[] installed) =>
        new PluginResolution(fs).Check(snapshot, installed);

    // ------------------------------------------------------------------------- the finding

    [Fact]
    public void A_plugin_that_is_not_here_is_missing()
    {
        var check = Check(new FakeFileSystem(), Snapshot(Entry("Pro-Q 4", "FabFilter", ProQPath)));

        Assert.Equal(PluginPresence.Missing, check.Plugins.Single().Presence);
        Assert.True(check.HasMissing);
    }

    [Fact]
    public void A_plugin_at_its_recorded_path_and_version_says_nothing()
    {
        var fs = new FakeFileSystem().AddFile(ProQPath, "bytes");

        var check = Check(fs, Snapshot(Entry("Pro-Q 4", "FabFilter", ProQPath)),
            Cached("Pro-Q 4", ProQPath, "4.1.2"));

        Assert.Equal(PluginPresence.Installed, check.Plugins.Single().Presence);
        Assert.False(check.HasMissing);
        Assert.Null(check.MissingLead);
        Assert.Null(check.DriftNote);
    }

    [Fact]
    public void A_bundle_on_disk_is_installed_rather_than_missing()
    {
        // A bundle is a DIRECTORY. Testing only for a file reports every bundled plug-in as
        // missing and sends the user to reinstall something they already have.
        // [[vst3-backs-up-as-nothing]]
        var fs = new FakeFileSystem().AddFile(ProQPath + @"\Contents\x86_64-win\p.vst3", "real");

        var check = Check(fs, Snapshot(Entry("Pro-Q 4", "FabFilter", ProQPath)),
            Cached("Pro-Q 4", ProQPath, "4.1.2"));

        Assert.Equal(PluginPresence.Installed, check.Plugins.Single().Presence);
    }

    [Fact]
    public void A_plugin_the_user_moved_is_found_through_the_cache_by_name()
    {
        // Still installed, just not where the snapshot last saw it.
        var fs = new FakeFileSystem().AddFile(@"D:\Audio\VST3\FabFilter Pro-Q 4.vst3", "bytes");

        var check = Check(fs, Snapshot(Entry("Pro-Q 4", "FabFilter", ProQPath)),
            Cached("Pro-Q 4", @"D:\Audio\VST3\FabFilter Pro-Q 4.vst3", "4.1.2"));

        Assert.Equal(PluginPresence.Installed, check.Plugins.Single().Presence);
    }

    [Fact]
    public void A_different_version_is_drift()
    {
        var fs = new FakeFileSystem().AddFile(ProQPath, "bytes");

        var check = Check(fs, Snapshot(Entry("Pro-Q 4", "FabFilter", ProQPath)),
            Cached("Pro-Q 4", ProQPath, "4.2.0"));

        Assert.Equal(PluginPresence.VersionDrift, check.Plugins.Single().Presence);
        Assert.Contains("4.1.2", check.DriftNote);
        Assert.Contains("4.2.0", check.DriftNote);
    }

    [Fact]
    public void An_unknown_version_on_either_side_is_never_reported_as_a_change()
    {
        // A warning that fires on every restore is a warning nobody reads by the third time.
        var fs = new FakeFileSystem().AddFile(ProQPath, "bytes");

        var recordedUnknown = Check(fs, Snapshot(Entry("Pro-Q 4", "FabFilter", ProQPath, version: null)),
            Cached("Pro-Q 4", ProQPath, "4.2.0"));
        var currentUnknown = Check(fs, Snapshot(Entry("Pro-Q 4", "FabFilter", ProQPath)),
            Cached("Pro-Q 4", ProQPath, null));

        Assert.Equal(PluginPresence.VersionUnknown, recordedUnknown.Plugins.Single().Presence);
        Assert.Equal(PluginPresence.VersionUnknown, currentUnknown.Plugins.Single().Presence);
        Assert.Null(recordedUnknown.DriftNote);
        Assert.Null(currentUnknown.DriftNote);
    }

    [Fact]
    public void A_snapshot_that_never_recorded_its_plugins_says_nothing_either_way()
    {
        Assert.False(PluginRestoreCheck.Unknown.HasMissing);
        Assert.Null(PluginRestoreCheck.Unknown.MissingLead);
    }

    // -------------------------------------------------------------------------- the wording

    [Fact]
    public void One_missing_plugin_is_named_with_its_vendor_and_its_channel()
    {
        // The design's exact shape: a naming clause in strong text, then the consequence and the
        // way out. (design README, Screen 2 item 4)
        var check = Check(new FakeFileSystem(),
            Snapshot(Entry("Pro-Q 4", "FabFilter", ProQPath, "4.1.2", "Voice")));

        Assert.Equal("FabFilter Pro-Q 4 isn't installed on this computer.", check.MissingLead);
        Assert.Equal(
            "The Voice channel will load with that effect switched off. "
            + "Install it and restore again to get it back.",
            check.MissingRest);
    }

    [Fact]
    public void Two_missing_plugins_read_as_a_list_and_take_the_plural()
    {
        var check = Check(new FakeFileSystem(), Snapshot(
            Entry("Pro-Q 4", "FabFilter", ProQPath, "4.1.2", "Voice"),
            Entry("Clear", "Supertone", ClearPath, "2.0", "Wave Mic 1")));

        Assert.Equal("FabFilter Pro-Q 4 and Supertone Clear aren't installed on this computer.",
            check.MissingLead);
        Assert.Contains("The Voice and Wave Mic 1 channels will load with those effects switched off.",
            check.MissingRest);
        Assert.Contains("Install them and restore again to get them back.", check.MissingRest);
    }

    [Fact]
    public void A_plugin_on_two_channels_names_both_once_each()
    {
        var check = Check(new FakeFileSystem(),
            Snapshot(Entry("Clear", "Supertone", ClearPath, "2.0", "Wave Mic 1", "Voice")));

        Assert.Contains("The Wave Mic 1 and Voice channels", check.MissingRest);
    }

    [Fact]
    public void A_snapshot_from_before_channels_were_recorded_still_says_what_is_missing()
    {
        // Older plugins.json files carry no channels. Naming a channel we do not have would be
        // worse than saying it plainly.
        var check = Check(new FakeFileSystem(), Snapshot(Entry("Clear", "Supertone", ClearPath)));

        Assert.Equal("Supertone Clear isn't installed on this computer.", check.MissingLead);
        Assert.Contains("The channels using that effect will load with it switched off.",
            check.MissingRest);
    }

    [Fact]
    public void A_plugin_with_no_vendor_is_named_by_itself()
    {
        var check = Check(new FakeFileSystem(),
            Snapshot(new PluginManifestEntry("Saturn 2", null, "2.0", null, @"C:\VST3\Saturn 2.vst3",
                null, Channels: ["Music"])));

        Assert.Equal("Saturn 2 isn't installed on this computer.", check.MissingLead);
    }

    [Fact]
    public void Drift_and_missing_are_reported_separately()
    {
        // Different severities: one is amber and blocks nothing; the other is a quiet line.
        var fs = new FakeFileSystem().AddFile(ClearPath, "bytes");

        var check = Check(fs,
            Snapshot(
                Entry("Pro-Q 4", "FabFilter", ProQPath, "4.1.2", "Voice"),
                Entry("Clear", "Supertone", ClearPath, "2.0", "Wave Mic 1")),
            Cached("Clear", ClearPath, "2.1"));

        Assert.Single(check.Missing);
        Assert.Single(check.Drifted);
        Assert.Contains("FabFilter Pro-Q 4", check.MissingLead);
        Assert.Contains("Supertone Clear 2.0 → 2.1", check.DriftNote);
    }
}
