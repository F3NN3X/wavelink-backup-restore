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

    // ------------------------------ DPI sizing (technical-debt.md §4.8 minor 1)

    /// <summary>
    /// The fixed 32px this replaced was right at 100% and 150% scaling and soft above. Windows
    /// asks the notification area for a 16px logical icon, so the render size is 16 × the DPI of
    /// the screen holding the taskbar.
    /// </summary>
    [Theory]
    [InlineData(1.00, 16)]
    [InlineData(1.25, 20)]
    [InlineData(1.50, 24)]
    [InlineData(1.75, 32)]
    [InlineData(2.00, 32)]
    [InlineData(3.00, 48)]
    [InlineData(4.00, 64)]
    public void The_render_size_follows_the_taskbars_dpi(double scale, int expected)
    {
        Assert.Equal(expected, TrayIconRenderer.PixelSizeFor(scale));
    }

    /// <summary>
    /// A DPI that cannot be read falls back to what the app already drew, rather than to something
    /// smaller. The icon being slightly soft is survivable; the icon being absent is not.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    public void An_unreadable_dpi_falls_back_to_the_size_that_was_always_used(double scale)
    {
        Assert.Equal(32, TrayIconRenderer.PixelSizeFor(scale));
    }

    [Fact]
    public void Every_snapped_size_actually_renders()
    {
        foreach (var size in (int[])[16, 20, 24, 32, 48, 64])
        {
            using var icon = Wpf.Run(() => TrayIconRenderer.Render(
                TrayStatus.Watching, System.Windows.Media.Colors.White, size));

            Assert.Equal(size, icon.Width);
        }
    }
}
