using System.Text;
using WaveLinkBackup.Core.Analysis;
using WaveLinkBackup.Core.Results;

namespace WaveLinkBackup.Core.Tests;

public sealed class SettingsAnalysisTests
{
    /// <summary>Shaped like the real file: five named inputs, effects on the mic chain.</summary>
    private const string Healthy = """
    {
      "MixerConfiguration": {
        "InputSettings": {
          "BS33J1A05009\\PCM_IN_01_C_00_SD1": {
            "InputName": "Wave Mic 1",
            "AudioPluginConfigurations": [
              { "Name": "Pro-Q 4", "ParameterState": "ab+cd/ef==" },
              { "Name": "Pro-C 2" }
            ]
          },
          "PCM_OUT_00_V_14_SD8": { "InputName": "Voice", "AudioPluginConfigurations": [] },
          "PCM_OUT_00_V_04_SD3": { "InputName": "Browser", "AudioPluginConfigurations": [] },
          "PCM_OUT_00_V_00_SD1": { "InputName": "Music", "AudioPluginConfigurations": [] },
          "PCM_OUT_00_V_12_SD7": { "InputName": "System", "AudioPluginConfigurations": [] }
        }
      }
    }
    """;

    /// <summary>What a reset looks like: two inputs, generic names. See SPEC.md 3.</summary>
    private const string Collapsed = """
    {
      "MixerConfiguration": {
        "InputSettings": {
          "Elgato Wave:3": { "InputName": "Elgato Wave:3" },
          "System": { "InputName": "System" }
        }
      }
    }
    """;

    private static Result<SettingsAnalysisResult> Analyse(string json) =>
        SettingsAnalysis.Analyse(Encoding.UTF8.GetBytes(json));

    [Fact]
    public void Counts_inputs_and_reads_their_names_in_document_order()
    {
        var result = Analyse(Healthy);

        Assert.True(result.IsSuccess);
        Assert.Equal(5, result.Value.Fingerprint.InputCount);
        Assert.Equal(
            ["Wave Mic 1", "Voice", "Browser", "Music", "System"],
            result.Value.Fingerprint.InputNames);
    }

    [Fact]
    public void Counts_effects_and_the_channels_carrying_them()
    {
        var fingerprint = Analyse(Healthy).Value.Fingerprint;

        Assert.Equal(2, fingerprint.EffectCount);
        Assert.Equal(1, fingerprint.EffectChannelCount);
    }

    [Fact]
    public void Records_size_and_a_stable_content_hash()
    {
        var bytes = Encoding.UTF8.GetBytes(Healthy);
        var fingerprint = SettingsAnalysis.Analyse(bytes).Value.Fingerprint;

        Assert.Equal(bytes.Length, fingerprint.SizeBytes);
        Assert.Equal(64, fingerprint.Sha256.Length);
        Assert.Equal(fingerprint.Sha256, SettingsAnalysis.Analyse(bytes).Value.Fingerprint.Sha256);
    }

    [Fact]
    public void A_collapsed_configuration_analyses_successfully_and_simply_reads_as_two_inputs()
    {
        // Health is relative, never absolute (SPEC.md 11). Core reports; it does not judge.
        var result = Analyse(Collapsed);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.Fingerprint.InputCount);
    }

    [Fact]
    public void Duplicate_keys_are_a_finding_not_a_failure()
    {
        // A suspect snapshot may be the only one there is, so it must still analyse.
        var result = Analyse("""
            {"MixerConfiguration":{"InputSettings":{"A":{"InputName":"x"}}},"Dup":1,"dup":2}
            """);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.Report.HasCaseInsensitiveDuplicateKeys);
        Assert.Single(result.Value.Report.DuplicateKeys);
    }

    [Fact]
    public void A_clean_document_reports_no_duplicates()
    {
        Assert.False(Analyse(Healthy).Value.Report.HasCaseInsensitiveDuplicateKeys);
    }

    [Fact]
    public void Unparseable_bytes_fail_with_MalformedSettings()
    {
        var result = Analyse("{ this is not json");

        Assert.False(result.IsSuccess);
        Assert.IsType<MalformedSettings>(result.Error);
    }

    [Fact]
    public void Empty_input_fails_rather_than_reporting_zero_inputs()
    {
        Assert.IsType<MalformedSettings>(SettingsAnalysis.Analyse([]).Error);
    }

    [Fact]
    public void A_document_without_InputSettings_is_malformed_not_empty()
    {
        // Silently reporting zero inputs would let a wrong file look like a collapsed one.
        var result = Analyse("""{"General":{}}""");

        Assert.False(result.IsSuccess);
        Assert.IsType<MalformedSettings>(result.Error);
    }

    [Fact]
    public void Inputs_missing_a_name_fall_back_rather_than_throwing()
    {
        var result = Analyse("""{"MixerConfiguration":{"InputSettings":{"key-only":{}}}}""");

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value.Fingerprint.InputCount);
        Assert.Single(result.Value.Fingerprint.InputNames);
    }
}
