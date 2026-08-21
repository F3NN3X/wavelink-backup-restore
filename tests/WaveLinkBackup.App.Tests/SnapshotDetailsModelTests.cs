using System.Text;
using WaveLinkBackup.App.ViewModels;
using WaveLinkBackup.Core.Analysis;
using WaveLinkBackup.Core.Results;
using WaveLinkBackup.Core.Snapshots;

namespace WaveLinkBackup.App.Tests;

/// <summary>
/// The details dialog's model: a snapshot plus the read of its settings file, turned into the
/// sentences the dialog renders. Everything the view shows is computed here, so the view can stay
/// a renderer and every line of copy is assertable without a window.
/// </summary>
public sealed class SnapshotDetailsModelTests
{
    private const string Rig = """
    {
      "MixerConfiguration": {
        "MixSettings": {
          "m1": {
            "Name": "Headphones", "IsMuted": false,
            "OutputDevices": [{ "Name": "Headphones", "FriendlyName": "Headphones (Elgato Wave:3)" }]
          },
          "m2": { "Name": "Stream Mix", "IsMuted": true, "OutputDevices": [] }
        },
        "MainOutputDeviceSettings": { "Name": "Headphones" },
        "InputSettings": {
          "BS33J1A05009\\PCM_IN_01_C_00_SD1": {
            "InputName": "Wave Mic 1", "WaveDeviceType": "Wave3", "MixerIds": ["m1", "m2"],
            "AudioPluginConfigurations": [
              { "Name": "WaveCompressor", "Vendor": "Elgato", "Category": "Fx", "FilePath": "" },
              {
                "Name": "Pro-Q 4", "Vendor": "FabFilter", "Category": "EQ",
                "FilePath": "C:\\VST3\\FabFilter Pro-Q 4.vst3", "BypassState": true
              }
            ]
          },
          "b": {
            "InputName": "Browser", "WaveDeviceType": "NoWaveDevice", "MixerIds": ["m1"],
            "AudioPluginConfigurations": []
          },
          "c": { "InputName": "Meld Studio", "MixerIds": [] }
        }
      }
    }
    """;

    private static Snapshot Snapshot(
        string name = "Full rig",
        SnapshotTrigger trigger = SnapshotTrigger.Manual,
        long bytes = 3_400_000) =>
        new(
            "2026-08-20T1041-6b38a6",
            @"C:\store\2026-08-20T1041-6b38a6",
            new SnapshotManifest(
                SchemaVersion: SnapshotManifest.CurrentSchemaVersion,
                DisplayName: name,
                Notes: string.Empty,
                CreatedUtc: new DateTimeOffset(2026, 8, 20, 8, 41, 0, TimeSpan.Zero),
                Trigger: trigger,
                SettingsSha256: new string('0', 64),
                WaveLinkVersion: "3.3.0.4108",
                InputCount: 3,
                InputNames: ["Wave Mic 1", "Browser", "Meld Studio"],
                EffectCount: 2,
                EffectChannelCount: 1,
                HasDuplicateKeys: false,
                Tiers: ["settings"],
                Files: new Dictionary<string, SnapshotFile>(StringComparer.Ordinal)
                {
                    ["settings.json"] = new(new string('0', 64), bytes),
                }));

    private static SnapshotDetailsModel Model(string json = Rig, Snapshot? snapshot = null) =>
        SnapshotDetailsModel.For(
            snapshot ?? Snapshot(), ConfigurationDetail.Read(Encoding.UTF8.GetBytes(json)));

    private static ChannelRow Channel(string name) => Model().Channels.Single(c => c.Name == name);

    // ---------------------------------------------------------------- the header

    [Fact]
    public void The_title_names_the_backup()
    {
        Assert.Equal("What's in “Full rig”", Model().Title);
    }

    /// <summary>
    /// The row calls a pre-restore backup PRE-RESTORE, so the dialog the row opens has to as well -
    /// PRERESTORE is not a word, and a backup should not change its name on the way to a dialog.
    /// </summary>
    [Fact]
    public void The_meta_line_uses_the_lists_own_vocabulary()
    {
        var meta = Model(snapshot: Snapshot(trigger: SnapshotTrigger.PreRestore)).MetaLine;

        Assert.StartsWith("PRE-RESTORE · ", meta, StringComparison.Ordinal);
        Assert.Contains("20 AUG", meta, StringComparison.Ordinal);
        // 3,400,000 bytes is 3.2 MB the way this app counts them - binary, like Explorer.
        Assert.Contains("3.2 MB", meta, StringComparison.Ordinal);
    }

    [Fact]
    public void The_summary_counts_channels_effects_and_mixes()
    {
        Assert.Equal("3 CHANNELS · 2 EFFECTS ON 1 CHANNEL · 2 MIXES", Model().SummaryLine);
    }

    [Fact]
    public void A_rig_with_no_effects_says_so_rather_than_counting_zero()
    {
        var model = Model("""
        {"MixerConfiguration":{"InputSettings":{"a":{"InputName":"A"}}}}
        """);

        Assert.Contains("NO EFFECTS", model.SummaryLine, StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------- channels

    [Fact]
    public void Every_channel_is_listed_in_the_files_own_order()
    {
        Assert.Equal(
            ["Wave Mic 1", "Browser", "Meld Studio"],
            Model().Channels.Select(c => c.Name));
    }

    /// <summary>
    /// The badge is for Elgato hardware. "NoWaveDevice" is the file's way of saying "an application
    /// or a virtual channel" - the ordinary case, and badging every ordinary channel would make the
    /// badge mean nothing.
    /// </summary>
    [Fact]
    public void Only_elgato_hardware_carries_a_device_badge()
    {
        Assert.Equal("WAVE:3", Channel("Wave Mic 1").DeviceLabel);
        Assert.Null(Channel("Browser").DeviceLabel);
        Assert.Null(Channel("Meld Studio").DeviceLabel);
    }

    [Fact]
    public void A_channel_says_which_mixes_it_is_heard_in()
    {
        Assert.Equal("IN HEADPHONES, STREAM MIX", Channel("Wave Mic 1").RoutingLine);
        Assert.False(Channel("Wave Mic 1").IsInNoMix);
    }

    /// <summary>
    /// A channel routed nowhere is audible nowhere. Nothing else in the app would tell you, and
    /// it is usually a surprise - so it is called out in words rather than left as an empty list.
    /// </summary>
    [Fact]
    public void A_channel_in_no_mix_says_so_in_words()
    {
        Assert.Equal("NOT IN ANY MIX", Channel("Meld Studio").RoutingLine);
        Assert.True(Channel("Meld Studio").IsInNoMix);
    }

    [Fact]
    public void A_channel_counts_its_effects_and_a_bare_one_says_none()
    {
        Assert.Equal("2 EFFECTS", Channel("Wave Mic 1").EffectsLabel);
        Assert.Equal("NO EFFECTS", Channel("Browser").EffectsLabel);
    }

    // ---------------------------------------------------------------- the chain

    [Fact]
    public void The_chain_keeps_its_order_and_shows_its_position()
    {
        var effects = Channel("Wave Mic 1").Effects;

        Assert.Equal(["WaveCompressor", "Pro-Q 4"], effects.Select(e => e.Name));
        Assert.Equal(["01", "02"], effects.Select(e => e.PositionLabel));
    }

    /// <summary>
    /// The vendor and the category as the plug-in describes itself, plus the one thing this app
    /// knows and the plug-in does not: whether a restore on another machine would find it.
    /// </summary>
    [Fact]
    public void An_effect_says_who_made_it_what_it_is_and_whether_it_ships_with_wave_link()
    {
        var effects = Channel("Wave Mic 1").Effects;

        Assert.Equal("ELGATO · FX · BUILT IN", effects[0].Meta);
        Assert.True(effects[0].IsBuiltIn);

        Assert.Equal("FABFILTER · EQ · VST3", effects[1].Meta);
        Assert.False(effects[1].IsBuiltIn);
    }

    [Fact]
    public void A_bypassed_effect_is_shown_and_marked()
    {
        var effects = Channel("Wave Mic 1").Effects;

        Assert.False(effects[0].Bypassed);
        Assert.True(effects[1].Bypassed);
    }

    // ---------------------------------------------------------------- mixes and outputs

    [Fact]
    public void Mixes_name_their_output_device()
    {
        var mixes = Model().Mixes;

        Assert.Equal("Headphones", mixes[0].Name);
        Assert.Equal("HEADPHONES (ELGATO WAVE:3)", mixes[0].OutputLine);
        Assert.False(mixes[0].IsMuted);
    }

    /// <summary>
    /// Normal, not broken: on a stock rig only the monitor mix carries a hardware output. It reads
    /// as a fact, in the same muted treatment as every other output line.
    /// </summary>
    [Fact]
    public void A_mix_with_no_device_reads_as_a_fact()
    {
        Assert.Equal("NO OUTPUT DEVICE", Model().Mixes[1].OutputLine);
        Assert.True(Model().Mixes[1].IsMuted);
    }

    [Fact]
    public void The_main_output_is_named_when_the_file_has_one()
    {
        Assert.Equal("WAVE LINK PLAYS OUT OF HEADPHONES", Model().MainOutputLine);
    }

    [Fact]
    public void There_is_no_main_output_line_when_the_file_does_not_say()
    {
        Assert.Null(Model("""{"MixerConfiguration":{"InputSettings":{}}}""").MainOutputLine);
    }

    // ---------------------------------------------------------------- the unreadable case

    /// <summary>
    /// A damaged backup is exactly when someone asks what was in it, so the dialog opens and says
    /// why it cannot answer - it does not refuse to open, and it still names the backup.
    /// </summary>
    [Fact]
    public void An_unreadable_backup_still_names_itself_and_explains()
    {
        var model = SnapshotDetailsModel.For(
            Snapshot(),
            Result<ConfigurationDetail>.Fail(new SettingsUnreadable(@"C:\store\x", "the file is gone")));

        Assert.False(model.IsReadable);
        Assert.Equal("What's in “Full rig”", model.Title);
        Assert.Contains("the file is gone", model.Unreadable!, StringComparison.Ordinal);
        Assert.Empty(model.Channels);
        Assert.Empty(model.Mixes);
    }

    [Fact]
    public void A_backup_holding_something_that_is_not_settings_reads_as_unreadable()
    {
        Assert.False(Model("not json at all").IsReadable);
    }
}
