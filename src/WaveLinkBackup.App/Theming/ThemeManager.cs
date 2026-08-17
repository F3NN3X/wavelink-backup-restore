using System.Windows;

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
/// Live following of the OS — UISettings.ColorValuesChanged, SystemEvents.UserPreferenceChanged
/// and the accent derivation — arrives in plan 3. This picks a theme once, at startup.
/// </summary>
public static class ThemeManager
{
    /// <summary>
    /// The 21 roles from screens/01-tokens-and-mapping.md. Named here so a missing key in one
    /// theme is a failing test rather than a control that renders wrong in light mode only.
    ///
    /// The design's own list says 20 and omits WlRaised, which the dark surface set needs.
    /// </summary>
    public static IReadOnlyList<string> BrushKeys { get; } =
    [
        "WlBg", "WlChrome", "WlCard", "WlRaised", "WlSunken",
        "WlText", "WlStrong", "WlMuted",
        "WlLine", "WlLine2", "WlHover",
        "WlAccent", "WlAccentInk", "WlAccentSoft", "WlAccentLine", "WlDanger",
        "WlOk", "WlOkSoft", "WlWarn", "WlWarnSoft",
        "WlScrim",
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
    /// High contrast wins over dark/light: it is not a preference sitting alongside them, it is
    /// Windows saying the palette is no longer ours.
    /// </summary>
    public static AppTheme DetectFromSystem()
    {
        if (SystemParameters.HighContrast) return AppTheme.HighContrast;

        return IsSystemInLightMode() ? AppTheme.Light : AppTheme.Dark;
    }

    public static void Apply(AppTheme theme)
    {
        var dictionaries = Application.Current.Resources.MergedDictionaries;

        // Slot 0 is the theme by convention; everything merged after it may reference these
        // keys. Replacing in place keeps that ordering.
        if (dictionaries.Count == 0) dictionaries.Add(Load(theme));
        else dictionaries[0] = Load(theme);
    }

    private static bool IsSystemInLightMode()
    {
        // Registry rather than UISettings for now: UISettings arrives with the live-following
        // work in plan 3, and this keeps the WinRT surface out of the startup path until then.
        using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
            @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");

        return key?.GetValue("AppsUseLightTheme") is int light && light != 0;
    }
}
