using System.Windows;
using WaveLinkBackup.App.Hosting;
using WaveLinkBackup.App.Theming;
using WaveLinkBackup.Core.Tests.Fakes;

namespace WaveLinkBackup.App.Tests;

/// <summary>
/// The App-owned half of persistence. settings.json describes itself in the Settings dialog as
/// the folder, the automatic-backup switch, how many to keep and which Wave Link you picked;
/// adding a window rectangle to it would make that sentence false.
/// </summary>
public sealed class ShellStateTests
{
    private const string Directory = @"C:\Users\t\AppData\Local\WaveLinkBackup";
    private const string File = @"C:\Users\t\AppData\Local\WaveLinkBackup\shell.json";

    private static ShellStateRepository Repository(FakeFileSystem fileSystem) => new(fileSystem, Directory);

    [Fact]
    public void Reads_defaults_when_the_file_does_not_exist()
    {
        Assert.Equal(ShellState.Default, Repository(new FakeFileSystem()).Read());
    }

    // Closing hides by default: the app is the process, not the window.
    [Fact]
    public void Closing_hides_to_tray_by_default()
    {
        Assert.True(ShellState.Default.ClosingHidesToTray);
    }

    [Fact]
    public void The_default_has_no_remembered_geometry()
    {
        Assert.Null(ShellState.Default.Left);
        Assert.Null(ShellState.Default.Width);
        Assert.False(ShellState.Default.IsMaximized);
    }

    [Fact]
    public void Saves_then_reads_the_same_state()
    {
        var repository = Repository(new FakeFileSystem());
        var state = new ShellState(120, 80, 1240, 800, IsMaximized: true, ClosingHidesToTray: false,
            Theme: ThemePreference.Light);

        repository.Save(state);

        Assert.Equal(state, repository.Read());
    }

    [Fact]
    public void The_file_sits_beside_settings_json()
    {
        Assert.Equal(File, Repository(new FakeFileSystem()).FilePath);
    }

    // Same tolerance as SettingsSerializer, for the same reason: this is a preferences file.
    [Fact]
    public void Unparseable_content_falls_back_to_defaults()
    {
        var fileSystem = new FakeFileSystem().AddFile(File, "not json at all");

        Assert.Equal(ShellState.Default, Repository(fileSystem).Read());
    }

    [Fact]
    public void A_broken_field_falls_back_alone()
    {
        var fileSystem = new FakeFileSystem().AddFile(File, """
            {"schemaVersion":1,"left":120,"top":"eighty","width":1240,"closingHidesToTray":false}
            """);

        var state = Repository(fileSystem).Read();

        Assert.Equal(120, state.Left);
        Assert.Null(state.Top);
        Assert.Equal(1240, state.Width);
        Assert.False(state.ClosingHidesToTray);
    }

    /// <summary>
    /// The theme is written as a WORD. A number would be unreadable in a file whose whole reason
    /// for being hand-editable is that losing it costs nothing.
    /// </summary>
    [Fact]
    public void The_theme_preference_is_saved_by_name()
    {
        var fileSystem = new FakeFileSystem();
        Repository(fileSystem).Save(ShellState.Default with { Theme = ThemePreference.HighContrast });

        Assert.Contains("\"theme\": \"highContrast\"", fileSystem.ReadSharedText(File), StringComparison.Ordinal);
    }

    [Fact]
    public void A_theme_nobody_can_honour_falls_back_to_following_windows()
    {
        var fileSystem = new FakeFileSystem().AddFile(File, """
            {"schemaVersion":1,"closingHidesToTray":true,"theme":"sepia"}
            """);

        Assert.Equal(ThemePreference.Auto, Repository(fileSystem).Read().Theme);
    }

    // A shell.json written before there was a choice. Auto is what the app did then, so this is
    // the value that changes nothing for anyone upgrading.
    [Fact]
    public void A_file_from_before_the_theme_existed_reads_as_auto()
    {
        var fileSystem = new FakeFileSystem().AddFile(File, """
            {"schemaVersion":1,"left":120,"top":80,"closingHidesToTray":false}
            """);

        Assert.Equal(ThemePreference.Auto, Repository(fileSystem).Read().Theme);
    }

    [Fact]
    public void Saving_never_throws_when_the_directory_cannot_be_created()
    {
        var fileSystem = new FakeFileSystem { FailDirectoryCreation = true };

        Repository(fileSystem).Save(ShellState.Default);
    }

    // The trap this file exists to avoid: a window remembered on a monitor that has since been
    // unplugged opens where nobody can see it, and a tray app whose window "won't open" reads
    // exactly like one that has crashed.
    [Fact]
    public void Geometry_entirely_off_every_screen_is_rejected()
    {
        var screens = new[] { new Rect(0, 0, 1920, 1080) };

        Assert.False(ShellState.IsOnScreen(new ShellState(3200, 200, 1180, 760, false, true, ThemePreference.Auto), screens));
    }

    [Fact]
    public void Geometry_overlapping_a_screen_is_accepted()
    {
        var screens = new[] { new Rect(0, 0, 1920, 1080) };

        Assert.True(ShellState.IsOnScreen(new ShellState(1800, 900, 1180, 760, false, true, ThemePreference.Auto), screens));
    }

    [Fact]
    public void Geometry_on_a_second_monitor_is_accepted()
    {
        var screens = new[] { new Rect(0, 0, 1920, 1080), new Rect(1920, 0, 2560, 1440) };

        Assert.True(ShellState.IsOnScreen(new ShellState(2400, 100, 1180, 760, false, true, ThemePreference.Auto), screens));
    }

    [Fact]
    public void State_with_no_geometry_is_not_on_screen_and_is_not_meant_to_be()
    {
        Assert.False(ShellState.IsOnScreen(ShellState.Default, [new Rect(0, 0, 1920, 1080)]));
    }
}
