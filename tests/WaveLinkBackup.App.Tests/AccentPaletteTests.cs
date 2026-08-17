using System.Windows.Media;
using WaveLinkBackup.App.Theming;

namespace WaveLinkBackup.App.Tests;

/// <summary>
/// The percentages are the design's, and they are a table test precisely because a derived tint
/// is the kind of thing that otherwise only a screenshot catches — and then only if someone
/// happens to have the right accent set that day.
///
/// No WPF thread needed: Color is a value type and Derive touches nothing else.
/// </summary>
public sealed class AccentPaletteTests
{
    private static readonly Color Accent = Color.FromRgb(0x00, 0x78, 0xD4);

    private static double OpacityOf(string key, AppTheme theme) =>
        AccentPalette.Derive(Accent, theme).Single(b => b.Key == key).Opacity;

    [Theory]
    [InlineData(AppTheme.Dark, 0.12)]
    [InlineData(AppTheme.Light, 0.07)]
    public void Accent_soft_is_the_designs_percentage_for_its_theme(AppTheme theme, double expected)
    {
        Assert.Equal(expected, OpacityOf("WlAccentSoft", theme));
    }

    [Theory]
    [InlineData(AppTheme.Dark, 0.32)]
    [InlineData(AppTheme.Light, 0.24)]
    public void Accent_line_is_the_designs_percentage_for_its_theme(AppTheme theme, double expected)
    {
        Assert.Equal(expected, OpacityOf("WlAccentLine", theme));
    }

    [Theory]
    [InlineData(AppTheme.Dark)]
    [InlineData(AppTheme.Light)]
    public void The_accent_itself_is_fully_opaque_and_unmodified(AppTheme theme)
    {
        var accent = AccentPalette.Derive(Accent, theme).Single(b => b.Key == "WlAccent");

        Assert.Equal(Accent, accent.Colour);
        Assert.Equal(1.0, accent.Opacity);
    }

    /// <summary>
    /// The one the design calls out by name: "two different reds in one window is a bug". The
    /// accent is the user's; danger is ours.
    /// </summary>
    [Theory]
    [InlineData(AppTheme.Dark)]
    [InlineData(AppTheme.Light)]
    public void Danger_is_never_derived_from_the_accent(AppTheme theme)
    {
        Assert.DoesNotContain("WlDanger", AccentPalette.Derive(Accent, theme).Select(b => b.Key));
    }

    /// <summary>
    /// AccentInk is the ink drawn ON the accent. Deriving it from the accent is how you arrive
    /// at white on yellow.
    /// </summary>
    [Theory]
    [InlineData(AppTheme.Dark)]
    [InlineData(AppTheme.Light)]
    public void The_ink_on_the_accent_is_never_derived_from_it(AppTheme theme)
    {
        Assert.DoesNotContain("WlAccentInk", AccentPalette.Derive(Accent, theme).Select(b => b.Key));
    }

    /// <summary>
    /// screens/11: in high contrast the accent is gone — primary becomes Highlight, and nothing
    /// is red, so nothing needs protecting from a second red either.
    /// </summary>
    [Fact]
    public void High_contrast_ignores_the_accent_entirely()
    {
        Assert.Empty(AccentPalette.Derive(Accent, AppTheme.HighContrast));
    }

    /// <summary>
    /// Exactly three keys move. A whitelist rather than a WlDanger-shaped hole, so the next role
    /// that must not follow the accent is caught without anyone remembering to add a test.
    /// </summary>
    [Theory]
    [InlineData(AppTheme.Dark)]
    [InlineData(AppTheme.Light)]
    public void Only_the_three_accent_roles_move_when_the_accent_does(AppTheme theme)
    {
        var moved = AccentPalette.Derive(Accent, theme).Select(b => b.Key).Order().ToList();

        Assert.Equal(["WlAccent", "WlAccentLine", "WlAccentSoft"], moved);
        Assert.All(moved, key => Assert.Contains(key, ThemeManager.BrushKeys));
    }
}
