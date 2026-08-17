using System.Windows.Media;
using WaveLinkBackup.App.Hosting;
using WaveLinkBackup.App.Theming;
using WaveLinkBackup.App.Views;

namespace WaveLinkBackup.App.Tests;

/// <summary>
/// The renderer hands Windows a hand-assembled ICO container, and every way of getting that
/// wrong fails at runtime with the compiler perfectly happy — which is how the first two
/// attempts at this shipped an app that threw on its first tray refresh. These load the icon
/// back, which is the only assertion that would have caught it.
///
/// The glyphs themselves are for a human eye; this is about the container and the colour rule.
/// </summary>
public sealed class TrayIconRendererTests
{
    [Theory]
    [InlineData(TrayStatus.Watching)]
    [InlineData(TrayStatus.BackingUp)]
    [InlineData(TrayStatus.NeedsYou)]
    [InlineData(TrayStatus.Paused)]
    public void Every_state_renders_an_icon_windows_can_load(TrayStatus status)
    {
        var size = Wpf.Run(() =>
        {
            using var icon = TrayIconRenderer.Render(status, Colors.White);
            return (icon.Width, icon.Height);
        });

        Assert.Equal((32, 32), size);
    }

    [Fact]
    public void The_icon_can_be_rendered_at_a_larger_size_for_a_dense_display()
    {
        var size = Wpf.Run(() =>
        {
            using var icon = TrayIconRenderer.Render(TrayStatus.Watching, Colors.White, pixelSize: 64);
            return (icon.Width, icon.Height);
        });

        Assert.Equal((64, 64), size);
    }

    /// <summary>
    /// Amber is the only colour the icon ever takes, and NEEDS YOU is the only state that takes
    /// it. Everything else is the ordinary text colour.
    /// </summary>
    [Fact]
    public void Needs_you_is_the_only_state_that_takes_a_colour()
    {
        var (warn, watching, needsYou) = Wpf.Run(() =>
        {
            ThemeManager.Apply(AppTheme.Dark);

            return (
                ((SolidColorBrush)System.Windows.Application.Current.Resources["WlWarn"]).Color,
                TrayIconRenderer.ColourFor(TrayStatus.Watching, highContrast: false),
                TrayIconRenderer.ColourFor(TrayStatus.NeedsYou, highContrast: false));
        });

        Assert.Equal(warn, needsYou);
        Assert.NotEqual(warn, watching);
    }

    /// <summary>
    /// The deliberate exception to the 40%-disabled rule — the icon is a state, not a disabled
    /// control — and the exception does NOT survive into high contrast, where transparency is
    /// not a contrast guarantee.
    /// </summary>
    [Fact]
    public void Paused_is_dimmed_normally_and_fully_opaque_in_high_contrast()
    {
        var (normal, contrast) = Wpf.Run(() =>
        {
            ThemeManager.Apply(AppTheme.Dark);

            return (
                TrayIconRenderer.ColourFor(TrayStatus.Paused, highContrast: false).A,
                TrayIconRenderer.ColourFor(TrayStatus.Paused, highContrast: true).A);
        });

        Assert.Equal((byte)(255 * 0.55), normal);
        Assert.Equal(255, contrast);
    }
}
