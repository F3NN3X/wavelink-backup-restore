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
    [Fact]
    public void The_tray_menu_is_acrylic_and_rounded_because_it_is_transient()
    {
        Assert.Equal((Backdrop.Acrylic, Corners.Rounded), ChromeChoice.ForTrayMenu(highContrast: false));
    }

    /// <summary>
    /// Mica on a context menu reads as an effect someone applied, which is the opposite of what
    /// a native-feeling app wants. This is the guard against "they are both backdrops, surely
    /// either will do".
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
    /// A menu stays rounded even with no backdrop — the corner preference is not part of the
    /// material, and a square menu on Windows 11 looks broken rather than plain.
    /// </summary>
    [Fact]
    public void The_menu_stays_rounded_even_when_it_gets_no_backdrop()
    {
        Assert.Equal(Corners.Rounded, ChromeChoice.ForTrayMenu(highContrast: true).Corners);
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
