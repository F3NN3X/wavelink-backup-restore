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
            ChosenWaveLinkPath: @"C:\Program Files\Elgato\WaveLink\Settings.json");

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
