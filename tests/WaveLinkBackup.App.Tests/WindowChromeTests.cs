using WaveLinkBackup.App.Tests.Fakes;
using WaveLinkBackup.App.Windows;

namespace WaveLinkBackup.App.Tests;

/// <summary>
/// The window's half of the contract ChromeChoice holds. The interop is not testable - design
/// section E lists Mica under "not tested" - but the DECISION is, and it is the part a
/// refactor loses quietly.
/// </summary>
public sealed class WindowChromeTests
{
    [Fact]
    public void The_main_window_asks_for_mica_and_the_default_corners()
    {
        var (backdrop, corners) = ChromeChoice.ForMainWindow(highContrast: false);

        Assert.Equal(Backdrop.Mica, backdrop);
        Assert.Equal(Corners.Default, corners);
    }

    // 11-high-contrast: every tint and fill is removed, and surfaces are told apart by 1px
    // WindowText borders. A translucent backdrop is a tint.
    [Fact]
    public void High_contrast_takes_no_backdrop()
    {
        var (backdrop, _) = ChromeChoice.ForMainWindow(highContrast: true);

        Assert.Equal(Backdrop.None, backdrop);
    }

    // The window and the tray menu take DIFFERENT chrome from the same seam. Plan 3's finding A
    // is only half right now, and this is the half that is still right.
    [Fact]
    public void The_window_and_the_tray_menu_do_not_take_the_same_chrome()
    {
        Assert.NotEqual(ChromeChoice.ForTrayMenu(false), ChromeChoice.ForMainWindow(false));
    }

    // The caption bar paints WlChrome only when the backdrop did not land, so the fake has to be
    // able to say it did not.
    [Fact]
    public void A_failed_backdrop_is_reported_rather_than_thrown()
    {
        var chrome = new FakeWindowChrome { BackdropSucceeds = false };

        Assert.False(chrome.Apply(IntPtr.Zero, Backdrop.Mica, Corners.Default, dark: true));
    }

    [Fact]
    public void A_successful_backdrop_is_reported_too()
    {
        Assert.True(new FakeWindowChrome().Apply(IntPtr.Zero, Backdrop.Mica, Corners.Default, dark: true));
    }
}
