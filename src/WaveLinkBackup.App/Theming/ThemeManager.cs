using System.Windows;
using System.Windows.Media;
using WaveLinkBackup.App.Windows;

namespace WaveLinkBackup.App.Theming;

public enum AppTheme
{
    Dark,
    Light,
    HighContrast,
}

/// <summary>
/// Every --wl-* value is a brush resource key, declared once per theme and referenced with
/// DynamicResource. That is what makes switching a resource swap rather than a window rebuild.
///
/// Live following of the OS lives in <see cref="Follow"/>, over the <see cref="ISystemTheme"/>
/// seam; the accent is overlaid at swap time by <see cref="AccentPalette"/>.
/// </summary>
public static class ThemeManager
{
    /// <summary>
    /// The 21 roles from screens/01-tokens-and-mapping.md, plus WlHotTrack (11-high-contrast.md's
    /// hover outline, added post-review). Named here so a missing key in one theme is a failing
    /// test rather than a control that renders wrong in light mode only.
    ///
    /// The design's own list says 20 and omits WlRaised, which the dark surface set needs.
    /// </summary>
    public static IReadOnlyList<string> BrushKeys { get; } =
    [
        "WlBg", "WlChrome", "WlCard", "WlRaised", "WlSunken",
        "WlText", "WlStrong", "WlMuted",
        "WlLine", "WlLine2", "WlHover", "WlHotTrack",
        "WlAccent", "WlAccentInk", "WlAccentSoft", "WlAccentLine", "WlDanger", "WlDangerSoft",
        "WlOk", "WlOkSoft", "WlWarn", "WlWarnSoft",
        "WlScrim",
        "WlTransparent", "WlCaptionCloseHover",
    ];

    /// <summary>
    /// The pack URI names the assembly explicitly. The short "/Theming/Dark.xaml" form resolves
    /// against the ENTRY assembly, which under a test host is the runner rather than this app —
    /// so the short form works when run and fails when tested, which is the worst of both.
    /// </summary>
    public static ResourceDictionary Load(AppTheme theme) => new()
    {
        Source = new Uri(
            $"pack://application:,,,/WaveLinkBackup;component/Theming/{theme}.xaml",
            UriKind.Absolute),
    };

    /// <summary>
    /// Applies a theme, optionally overlaying the user's accent.
    ///
    /// The accent is written OVER the loaded dictionary rather than edited into the XAML, which
    /// keeps Dark.xaml and Light.xaml readable on their own and keeps them correct for the case
    /// where there is no accent to read.
    /// </summary>
    public static void Apply(AppTheme theme, Color? accent = null)
    {
        var dictionary = Load(theme);

        if (accent is { } colour)
        {
            foreach (var derived in AccentPalette.Derive(colour, theme))
            {
                dictionary[derived.Key] = new SolidColorBrush(derived.Colour)
                {
                    Opacity = derived.Opacity,
                };
            }
        }

        var dictionaries = Application.Current.Resources.MergedDictionaries;

        // Slot 0 is the theme by convention; everything merged after it — the tray menu's styles
        // — references these keys. Replacing in place rather than appending is what keeps that
        // ordering true across a swap.
        if (dictionaries.Count == 0) dictionaries.Add(dictionary);
        else dictionaries[0] = dictionary;
    }

    /// <summary>
    /// Applies now and on every change. <paramref name="afterApply"/> runs after the swap, for
    /// the things that read the dictionary rather than binding to it — the tray icon is drawn
    /// from resolved brushes, so it has to be redrawn, and doing it here rather than in a second
    /// subscriber means the ordering is guaranteed instead of depending on subscription order.
    /// </summary>
    public static void Follow(ISystemTheme system, Action? afterApply = null)
    {
        void Reapply()
        {
            Apply(system.Theme, system.Accent);
            afterApply?.Invoke();
        }

        system.Changed += (_, _) => Reapply();

        Reapply();
        system.Start();
    }
}
