using System.Windows;
using System.Windows.Media;
using WaveLinkBackup.App.Tests.Fakes;
using WaveLinkBackup.App.Theming;

namespace WaveLinkBackup.App.Tests;

/// <summary>
/// screens/11 requires high contrast to be reacted to at runtime, "do not require a restart",
/// and the same argument applies to dark/light and the accent. This is the seam that makes that
/// assertable without changing the developer's own Windows settings mid-test.
///
/// Every test does its mutation AND its assertions inside a single Wpf.Run. Application.Current
/// .Resources is process-global, and the dispatcher runs one delegate at a time — so keeping
/// each case atomic on that thread is what stops these interfering with each other and with
/// TrayIconRendererTests.
/// </summary>
public sealed class ThemeFollowingTests
{
    private static readonly Color Accent = Color.FromRgb(0x00, 0x78, 0xD4);

    private static SolidColorBrush Brush(string key) =>
        (SolidColorBrush)Application.Current.Resources[key];

    [Fact]
    public void Applying_a_theme_puts_it_in_slot_zero()
    {
        var themeIsFirst = Wpf.Run(() =>
        {
            ThemeManager.Apply(AppTheme.Dark);

            var slot0 = Application.Current.Resources.MergedDictionaries[0];
            return slot0.Contains("WlBg");
        });

        Assert.True(themeIsFirst);
    }

    /// <summary>
    /// A re-apply that appended instead of replacing slot 0 would put the theme AFTER the tray
    /// menu's styles, and every DynamicResource in them would resolve to the wrong dictionary —
    /// or to nothing. Cheap to assert, miserable to debug.
    /// </summary>
    [Fact]
    public void Re_applying_replaces_slot_zero_and_leaves_later_dictionaries_alone()
    {
        var (count, markerStillLast) = Wpf.Run(() =>
        {
            var dictionaries = Application.Current.Resources.MergedDictionaries;
            dictionaries.Clear();

            ThemeManager.Apply(AppTheme.Dark);

            var marker = new ResourceDictionary { ["MarkerForTheTest"] = "still here" };
            dictionaries.Add(marker);

            ThemeManager.Apply(AppTheme.Light);
            ThemeManager.Apply(AppTheme.Dark);

            return (dictionaries.Count, ReferenceEquals(dictionaries[^1], marker));
        });

        Assert.Equal(2, count);
        Assert.True(markerStillLast);
    }

    [Fact]
    public void Following_applies_the_systems_theme_immediately_and_starts_listening()
    {
        var system = new FakeSystemTheme { Theme = AppTheme.Light };

        var background = Wpf.Run(() =>
        {
            ThemeManager.Follow(system);
            return Brush("WlBg").Color.ToString();
        });

        Assert.Equal("#FFF5F5F3", background);
        Assert.True(system.Started);
    }

    [Fact]
    public void A_change_re_applies_without_anything_being_restarted()
    {
        var system = new FakeSystemTheme { Theme = AppTheme.Light };

        var (before, after) = Wpf.Run(() =>
        {
            ThemeManager.Follow(system);
            var light = Brush("WlBg").Color.ToString();

            system.Theme = AppTheme.Dark;
            system.RaiseChanged();

            return (light, Brush("WlBg").Color.ToString());
        });

        Assert.Equal("#FFF5F5F3", before);
        Assert.Equal("#FF111419", after);
    }

    [Fact]
    public void High_contrast_outranks_dark_and_light()
    {
        var theme = new FakeSystemTheme { Theme = AppTheme.Light, IsHighContrast = true }.Theme;

        Assert.Equal(AppTheme.HighContrast, theme);
    }

    [Fact]
    public void The_after_apply_callback_runs_after_the_swap_not_before()
    {
        var system = new FakeSystemTheme { Theme = AppTheme.Light };

        // The tray icon is drawn from resolved brushes rather than bound to them, so it is
        // redrawn by this callback. Seeing the OLD theme here would repaint a stale icon.
        var seen = Wpf.Run(() =>
        {
            string? observed = null;
            ThemeManager.Follow(system, () => observed = Brush("WlBg").Color.ToString());

            system.Theme = AppTheme.Dark;
            system.RaiseChanged();

            return observed;
        });

        Assert.Equal("#FF111419", seen);
    }

    [Fact]
    public void The_accent_overlays_the_authored_brushes()
    {
        var system = new FakeSystemTheme { Theme = AppTheme.Dark, Accent = Accent };

        var (accent, soft, danger) = Wpf.Run(() =>
        {
            ThemeManager.Follow(system);

            return (
                Brush("WlAccent").Color.ToString(),
                Brush("WlAccentSoft").Opacity,
                Brush("WlDanger").Color.ToString());
        });

        Assert.Equal("#FF0078D4", accent);
        Assert.Equal(0.12, soft);

        // The whole point: the accent moved and danger did not.
        Assert.Equal("#FFF01616", danger);
    }

    [Fact]
    public void A_high_contrast_swap_leaves_the_accent_alone()
    {
        var system = new FakeSystemTheme { Accent = Accent, IsHighContrast = true };

        var accentIsNotTheUsers = Wpf.Run(() =>
        {
            ThemeManager.Follow(system);
            return Brush("WlAccent").Color != Accent;
        });

        Assert.True(accentIsNotTheUsers);
    }
}
