using WaveLinkBackup.App.Tests.Fakes;
using WaveLinkBackup.App.Windows;

namespace WaveLinkBackup.App.Tests;

/// <summary>
/// Windows 11 uses Mica for long-lived window backgrounds and Acrylic for transient surfaces.
/// The distinction is invisible to the compiler and obvious to a user, which makes it exactly
/// the sort of thing a refactor loses. Design §E puts the interop under "not tested"; the
/// decision is a different matter.
/// </summary>
public sealed class ChromeChoiceTests
{
    /// <summary>
    /// The menu takes no backdrop and paints its own WlCard surface. A translucent menu shows the
    /// DESKTOP's palette through it, and this menu's job is to look like Wave Link Backup.
    /// </summary>
    [Fact]
    public void The_tray_menu_paints_its_own_surface_and_stays_rounded()
    {
        Assert.Equal((Backdrop.None, Corners.Rounded), ChromeChoice.ForTrayMenu(highContrast: false));
    }

    /// <summary>
    /// The window is where a backdrop earns its keep: it is large, long-lived, and the design
    /// asks for Mica on the caption bar by name. The menu and the window differ on purpose, and
    /// this is the guard against "they are both surfaces, surely one rule will do".
    /// </summary>
    [Fact]
    public void The_main_window_is_mica_and_keeps_the_corners_windows_would_give_it()
    {
        Assert.Equal((Backdrop.Mica, Corners.Default), ChromeChoice.ForMainWindow(highContrast: false));
    }

    /// <summary>
    /// screens/11: surfaces are told apart by borders, not by translucency, and Windows suppresses
    /// the materials in high contrast anyway. Asking for one would be a call that does nothing on
    /// a good day and fights the scheme on a bad one.
    /// </summary>
    [Fact]
    public void High_contrast_asks_for_no_backdrop_on_either_surface()
    {
        Assert.Equal(Backdrop.None, ChromeChoice.ForTrayMenu(highContrast: true).Backdrop);
        Assert.Equal(Backdrop.None, ChromeChoice.ForMainWindow(highContrast: true).Backdrop);
    }

    /// <summary>
    /// The corner preference is not part of the material, so it survives everything: a square
    /// menu on Windows 11 looks broken rather than plain.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void The_menu_is_rounded_in_every_scheme(bool highContrast)
    {
        Assert.Equal(Corners.Rounded, ChromeChoice.ForTrayMenu(highContrast).Corners);
    }

    [Fact]
    public void A_chrome_that_cannot_do_backdrops_still_reports_the_attempt()
    {
        var chrome = new FakeWindowChrome { BackdropSucceeds = false };

        var took = chrome.Apply(new IntPtr(1), Backdrop.Acrylic, Corners.Rounded, dark: true);

        Assert.False(took);
        Assert.Equal((new IntPtr(1), Backdrop.Acrylic, Corners.Rounded, true), chrome.Calls.Single());
    }
}
