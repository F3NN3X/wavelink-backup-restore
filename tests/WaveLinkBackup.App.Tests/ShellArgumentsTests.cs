using WaveLinkBackup.App.Startup;
using WaveLinkBackup.Core.Automation;

namespace WaveLinkBackup.App.Tests;

/// <summary>
/// Flags win for one run and are never written back
/// (operations/design/screens/08-settings-persistence.md), so these produce an overlay over
/// BackupSettings rather than something that gets saved.
/// </summary>
public sealed class ShellArgumentsTests
{
    [Fact]
    public void No_arguments_means_show_the_window()
    {
        var args = ShellArguments.Parse([]);

        Assert.True(args.IsValid);
        Assert.False(args.StartInTray);
    }

    [Fact]
    public void Tray_starts_windowless()
    {
        Assert.True(ShellArguments.Parse(["--tray"]).StartInTray);
    }

    [Fact]
    public void Every_value_flag_is_captured()
    {
        var args = ShellArguments.Parse(
            ["--store", @"D:\B", "--settings", @"C:\WL\Settings.json", "--keep", "12"]);

        Assert.True(args.IsValid);
        Assert.Equal(@"D:\B", args.StorePath);
        Assert.Equal(@"C:\WL\Settings.json", args.SettingsPath);
        Assert.Equal(12, args.KeepCount);
    }

    [Fact]
    public void An_unknown_flag_is_an_error_rather_than_being_ignored()
    {
        var args = ShellArguments.Parse(["--destroy-everything"]);

        Assert.False(args.IsValid);
        Assert.Contains("--destroy-everything", args.Error!, StringComparison.Ordinal);
    }

    [Fact]
    public void A_value_flag_with_no_value_is_an_error()
    {
        Assert.False(ShellArguments.Parse(["--store"]).IsValid);
        Assert.False(ShellArguments.Parse(["--keep"]).IsValid);
    }

    [Fact]
    public void A_non_numeric_keep_count_is_an_error()
    {
        Assert.False(ShellArguments.Parse(["--keep", "loads"]).IsValid);
    }

    [Fact]
    public void Flags_overlay_the_settings_they_are_given()
    {
        var settings = new BackupSettings(@"D:\from-file", AutoBackupKeepCount: 30);

        var overlaid = ShellArguments.Parse(["--store", @"D:\from-flag"]).ApplyTo(settings);

        Assert.Equal(@"D:\from-flag", overlaid.StorePath);
        Assert.Equal(30, overlaid.AutoBackupKeepCount); // untouched
    }

    [Fact]
    public void Absent_flags_leave_the_settings_alone()
    {
        var settings = new BackupSettings(@"D:\from-file", AutoBackupKeepCount: 30,
                                          ChosenWaveLinkPath: @"C:\WL\Settings.json");

        Assert.Equal(settings, ShellArguments.Parse([]).ApplyTo(settings));
    }
}
