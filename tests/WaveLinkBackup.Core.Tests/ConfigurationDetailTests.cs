using System.Text;
using WaveLinkBackup.Core.Analysis;
using WaveLinkBackup.Core.Results;

namespace WaveLinkBackup.Core.Tests;

/// <summary>
/// The read behind the details dialog: channels, their effect chains in order, the mixes and
/// where they play out.
///
/// The fixture is cut from a REAL 47 KB settings file (a nine-channel rig, 2026-08-20), reduced
/// to the shapes that matter and with the parameter blobs dropped - so the key names, the id
/// format, the built-in-versus-VST3 distinction and the mix ids are the file's own, not invented.
/// </summary>
public sealed class ConfigurationDetailTests
{
    private const string NineChannelRig = """
    {
      "MixerConfiguration": {
        "MixSettings": {
          "PCM_IN_01_V_00_SD1": {
            "Name": "Headphones", "IsMuted": false,
            "OutputDevices": [
              {
                "Name": "Headphones", "FriendlyName": "Headphones (Elgato Wave:3)",
                "DeviceType": "WaveHardwareOutputDevice"
              }
            ]
          },
          "PCM_IN_01_V_02_SD2": { "Name": "MicMix", "IsMuted": true, "OutputDevices": [] },
          "PCM_IN_01_V_04_SD3": { "Name": "Stream Mix", "IsMuted": false, "OutputDevices": [] }
        },
        "MainOutputDeviceSettings": {
          "Name": "Headphones", "AudioDeviceType": "WaveHardwareOutputDevice"
        },
        "InputSettings": {
          "BS33J1A05009\\PCM_IN_01_C_00_SD1": {
            "InputName": "Wave Mic 1",
            "WaveDeviceType": "Wave3",
            "IsHiddenFromMixes": false,
            "MixerIds": ["PCM_IN_01_V_02_SD2", "PCM_IN_01_V_04_SD3"],
            "AudioPluginConfigurations": [
              {
                "Name": "ElgatoSampleRecorder", "Vendor": "Elgato", "Category": "Fx",
                "FilePath": "", "BypassState": true
              },
              {
                "Name": "WaveCompressor", "Vendor": "Elgato", "Category": "Fx",
                "FilePath": "", "BypassState": false
              },
              {
                "Name": "Pro-Q 4", "Vendor": "FabFilter", "Category": "EQ",
                "FilePath": "C:\\Program Files\\Common Files\\VST3\\FabFilter\\FabFilter Pro-Q 4.vst3",
                "BypassState": false, "CustomName": "Broadcast curve"
              }
            ]
          },
          "PCM_OUT_00_V_14_SD8": {
            "InputName": "Voice", "WaveDeviceType": "NoWaveDevice",
            "MixerIds": ["PCM_IN_01_V_00_SD1"], "AudioPluginConfigurations": []
          },
          "PCM_OUT_00_V_20_SD11": {
            "InputName": "Meld Studio", "WaveDeviceType": "NoWaveDevice",
            "MixerIds": [], "AudioPluginConfigurations": []
          },
          "PCM_OUT_00_V_22_SD12": {
            "InputName": "Aux 1", "IsHiddenFromMixes": true,
            "MixerIds": ["PCM_IN_01_V_99_SD9"]
          }
        }
      }
    }
    """;

    private static ConfigurationDetail Read(string json) =>
        ConfigurationDetail.Read(Encoding.UTF8.GetBytes(json)).Value;

    private static ChannelDetail Channel(string name) =>
        Read(NineChannelRig).Channels.Single(c => c.Name == name);

    [Fact]
    public void Every_channel_is_read_in_the_files_own_order()
    {
        Assert.Equal(
            ["Wave Mic 1", "Voice", "Meld Studio", "Aux 1"],
            Read(NineChannelRig).Channels.Select(c => c.Name));
    }

    /// <summary>
    /// The chain order IS the configuration: an EQ before a compressor is a different sound from
    /// the same two the other way round, so a details view that sorted them alphabetically would
    /// be describing a rig the user does not have.
    /// </summary>
    [Fact]
    public void An_effect_chain_keeps_its_order_and_numbers_from_one()
    {
        var effects = Channel("Wave Mic 1").Effects;

        Assert.Equal([1, 2, 3], effects.Select(e => e.Position));
        Assert.Equal(["ElgatoSampleRecorder", "WaveCompressor", "Pro-Q 4"], effects.Select(e => e.Name));
    }

    /// <summary>
    /// An empty FilePath is an Elgato built-in ([[ADR-006]]) - it ships with Wave Link, so a
    /// restore on a new machine always finds it. A path is a third-party VST3, which is the set
    /// that can go missing.
    /// </summary>
    [Fact]
    public void A_built_in_is_told_apart_from_a_third_party_plugin_by_its_path()
    {
        var effects = Channel("Wave Mic 1").Effects;

        Assert.True(effects[1].IsBuiltIn);
        Assert.Null(effects[1].FilePath);

        Assert.False(effects[2].IsBuiltIn);
        Assert.EndsWith("FabFilter Pro-Q 4.vst3", effects[2].FilePath!, StringComparison.Ordinal);
    }

    [Fact]
    public void A_renamed_effect_shows_the_users_own_name_and_keeps_the_plugins()
    {
        var proQ = Channel("Wave Mic 1").Effects[2];

        Assert.Equal("Broadcast curve", proQ.DisplayName);
        Assert.Equal("Pro-Q 4", proQ.Name);
    }

    /// <summary>
    /// A bypassed effect is restored bypassed, so hiding it would describe a chain the backup does
    /// not hold - and "why is my de-esser doing nothing" is answered by this flag.
    /// </summary>
    [Fact]
    public void A_bypassed_effect_is_reported_rather_than_dropped()
    {
        var effects = Channel("Wave Mic 1").Effects;

        Assert.True(effects[0].Bypassed);
        Assert.False(effects[1].Bypassed);
    }

    [Fact]
    public void A_channels_mixes_are_resolved_to_their_names()
    {
        Assert.Equal(["MicMix", "Stream Mix"], Channel("Wave Mic 1").Mixes);
    }

    /// <summary>
    /// A channel routed nowhere is audible nowhere - a real state of a real rig, and one nothing
    /// else in the app would tell you about.
    /// </summary>
    [Fact]
    public void A_channel_in_no_mix_says_so()
    {
        Assert.True(Channel("Meld Studio").IsInNoMix);
        Assert.False(Channel("Wave Mic 1").IsInNoMix);
    }

    /// <summary>
    /// An id with no MixSettings entry keeps the id. The channel IS routed somewhere and dropping
    /// it would report the channel as unrouted, which is a different and worse claim.
    /// </summary>
    [Fact]
    public void A_mix_id_with_no_entry_is_kept_as_the_id()
    {
        Assert.Equal(["PCM_IN_01_V_99_SD9"], Channel("Aux 1").Mixes);
    }

    [Fact]
    public void A_hidden_channel_is_marked_hidden()
    {
        Assert.True(Channel("Aux 1").HiddenFromMixes);
        Assert.False(Channel("Voice").HiddenFromMixes);
    }

    [Fact]
    public void Mixes_carry_their_name_mute_state_and_output_device()
    {
        var mixes = Read(NineChannelRig).Mixes;

        Assert.Equal(["Headphones", "MicMix", "Stream Mix"], mixes.Select(m => m.Name));
        Assert.True(mixes[1].IsMuted);

        var output = Assert.Single(mixes[0].Outputs);
        Assert.Equal("Headphones (Elgato Wave:3)", output.DisplayName);
    }

    /// <summary>
    /// Normal, not broken: on a stock rig only the monitor mix carries a hardware output and the
    /// rest are consumed by the stream software over the virtual device.
    /// </summary>
    [Fact]
    public void A_mix_with_no_output_device_is_not_an_error()
    {
        Assert.Empty(Read(NineChannelRig).Mixes[2].Outputs);
    }

    [Fact]
    public void The_main_output_is_read()
    {
        Assert.Equal("Headphones", Read(NineChannelRig).MainOutput!.Name);
    }

    [Fact]
    public void The_totals_match_the_chains()
    {
        var detail = Read(NineChannelRig);

        Assert.Equal(3, detail.EffectCount);
        Assert.Equal(1, detail.ChannelsWithEffectsCount);
    }

    // ---------------------------------------------------------------- tolerance

    /// <summary>
    /// Every field below is missing on some real file - an older Wave Link, a channel added by a
    /// beta, a key Elgato renamed. A details view that refuses to open because one channel has no
    /// WaveDeviceType is worse than one that shows the channel with its type blank.
    /// </summary>
    [Fact]
    public void A_channel_missing_every_optional_field_still_reads()
    {
        var detail = Read("""
        {"MixerConfiguration":{"InputSettings":{"PCM_OUT_00_V_00_SD1":{"InputName":"Bare"}}}}
        """);

        var channel = Assert.Single(detail.Channels);

        Assert.Equal("Bare", channel.Name);
        Assert.Null(channel.DeviceType);
        Assert.False(channel.HiddenFromMixes);
        Assert.Empty(channel.Mixes);
        Assert.Empty(channel.Effects);
    }

    /// <summary>The key is the Core Audio endpoint id - ugly, and better than losing the channel.</summary>
    [Fact]
    public void A_channel_with_no_name_falls_back_to_its_key()
    {
        var detail = Read("""
        {"MixerConfiguration":{"InputSettings":{"PCM_OUT_00_V_00_SD1":{}}}}
        """);

        Assert.Equal("PCM_OUT_00_V_00_SD1", Assert.Single(detail.Channels).Name);
    }

    [Fact]
    public void An_effect_with_no_name_is_still_a_slot_in_the_chain()
    {
        var detail = Read("""
        {"MixerConfiguration":{"InputSettings":{"a":{"InputName":"A",
          "AudioPluginConfigurations":[{"BypassState":false}]}}}}
        """);

        var effect = Assert.Single(detail.Channels[0].Effects);

        Assert.Equal("Unnamed effect", effect.Name);
        Assert.Equal(1, effect.Position);
    }

    [Fact]
    public void A_file_with_no_mixes_reads_its_channels_anyway()
    {
        var detail = Read("""
        {"MixerConfiguration":{"InputSettings":{"a":{"InputName":"A","MixerIds":["m1"]}}}}
        """);

        Assert.Empty(detail.Mixes);
        Assert.Equal(["m1"], detail.Channels[0].Mixes);
        Assert.Null(detail.MainOutput);
    }

    // ---------------------------------------------------------------- refusals

    [Theory]
    [InlineData("")]
    [InlineData("not json at all")]
    [InlineData("{}")]
    [InlineData("""{"MixerConfiguration":{}}""")]
    [InlineData("""{"MixerConfiguration":{"InputSettings":[]}}""")]
    public void Anything_that_is_not_a_settings_file_fails_as_malformed(string json)
    {
        var result = ConfigurationDetail.Read(Encoding.UTF8.GetBytes(json));

        Assert.False(result.IsSuccess);
        Assert.IsType<MalformedSettings>(result.Error);
    }
}
