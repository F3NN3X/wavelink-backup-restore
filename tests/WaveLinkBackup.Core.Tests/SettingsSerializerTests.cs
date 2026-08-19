using System.Text;
using WaveLinkBackup.Core.Automation;

namespace WaveLinkBackup.Core.Tests;

/// <summary>
/// settings.json in and out. Read is deliberately tolerant: this is a preferences file, so a
/// broken one should cost defaults rather than a refusal to start, and one broken field must
/// not cost the user the other three.
/// </summary>
public sealed class SettingsSerializerTests
{
    [Fact]
    public void Round_trips_every_field()
    {
        var settings = new BackupSettings(
            StorePath: @"D:\Backups\WaveLink",
            AutoBackupEnabled: false,
            AutoBackupKeepCount: 7,
            ChosenWaveLinkPath: @"C:\Program Files\Elgato\WaveLink\Settings.json",
            IncludePresets: false,
            IncludePluginFiles: true);

        var read = SettingsSerializer.Read(SettingsSerializer.Write(settings));

        Assert.Equal(settings, read);
    }

    [Fact]
    public void Writes_a_schema_version()
    {
        var json = Encoding.UTF8.GetString(SettingsSerializer.Write(BackupSettings.Default));

        Assert.Contains("\"schemaVersion\": 1", json, StringComparison.Ordinal);
    }

    [Fact]
    public void A_null_chosen_installation_survives_the_round_trip()
    {
        var read = SettingsSerializer.Read(SettingsSerializer.Write(BackupSettings.Default));

        Assert.Null(read.ChosenWaveLinkPath);
    }

    [Fact]
    public void The_tier_toggles_default_to_presets_on_and_plugin_files_off()
    {
        // ADR-006's defaults, and the reason: presets are the user's own irreplaceable work at
        // ~10 MB; the binaries are re-downloadable at ~40 MB and carry no licence.
        Assert.True(BackupSettings.Default.IncludePresets);
        Assert.False(BackupSettings.Default.IncludePluginFiles);
    }

    [Fact]
    public void A_settings_file_written_before_the_tier_toggles_existed_still_reads()
    {
        // The two booleans were ADDED with no schema bump: a field whose absence means its
        // default is exactly what the tolerant read already handles. Bumping the version is for
        // a field whose MEANING changes, which is a different and much rarer event.
        var read = SettingsSerializer.Read("""
            {"schemaVersion":1,"storePath":"D:\\B","autoBackupEnabled":true,"autoBackupKeepCount":30}
            """u8);

        Assert.Equal(@"D:\B", read.StorePath);
        Assert.True(read.IncludePresets);
        Assert.False(read.IncludePluginFiles);
    }

    [Fact]
    public void A_wrong_typed_tier_toggle_falls_back_on_its_own()
    {
        var read = SettingsSerializer.Read("""
            {"storePath":"D:\\B","includePresets":"yes","includePluginFiles":true}
            """u8);

        Assert.True(read.IncludePresets);
        Assert.True(read.IncludePluginFiles);
    }

    [Fact]
    public void Unparseable_bytes_fall_back_to_defaults()
    {
        Assert.Equal(BackupSettings.Default, SettingsSerializer.Read("this is not json"u8));
    }

    [Fact]
    public void Empty_bytes_fall_back_to_defaults()
    {
        Assert.Equal(BackupSettings.Default, SettingsSerializer.Read([]));
    }

    [Fact]
    public void A_json_array_falls_back_to_defaults()
    {
        Assert.Equal(BackupSettings.Default, SettingsSerializer.Read("[1,2,3]"u8));
    }

    /// <summary>
    /// One broken field must not cost the user the other three. This is the whole reason Read
    /// is tolerant per field rather than all-or-nothing.
    /// </summary>
    [Fact]
    public void A_wrongly_typed_field_falls_back_alone()
    {
        var json = """
            {
              "schemaVersion": 1,
              "storePath": "D:\\Backups",
              "autoBackupEnabled": "yes please",
              "autoBackupKeepCount": 12
            }
            """u8;

        var read = SettingsSerializer.Read(json);

        Assert.Equal(@"D:\Backups", read.StorePath);
        Assert.True(read.AutoBackupEnabled);        // defaulted
        Assert.Equal(12, read.AutoBackupKeepCount); // kept
    }

    [Fact]
    public void Unknown_fields_are_ignored()
    {
        var json = """
            {"schemaVersion": 1, "storePath": "D:\\B", "somethingFromTheFuture": 42}
            """u8;

        Assert.Equal(@"D:\B", SettingsSerializer.Read(json).StorePath);
    }
}
