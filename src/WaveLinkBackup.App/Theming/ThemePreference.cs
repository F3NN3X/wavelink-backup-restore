namespace WaveLinkBackup.App.Theming;

/// <summary>
/// What the USER asked for, as opposed to <see cref="AppTheme"/>, which is what gets drawn.
///
/// The two are different types on purpose. Auto has no palette of its own — it is a deferral to
/// Windows — and a single enum carrying both would let "Auto" reach
/// <see cref="ThemeManager.Load"/>, where there is no Auto.xaml and never will be.
/// </summary>
public enum ThemePreference
{
    /// <summary>Follow Windows, which is what the app did before there was a choice.</summary>
    Auto,
    Dark,
    Light,
    HighContrast,
}

/// <summary>
/// The preference and the OS reduced to the one theme to draw. Pure, so the precedence rule is a
/// table test rather than something only a screenshot catches — the same shape as
/// <see cref="AccentPalette"/>.
/// </summary>
public static class ThemeChoice
{
    /// <summary>
    /// Windows' own high contrast outranks everything, including an explicit Dark or Light.
    ///
    /// That is <see cref="Windows.ISystemTheme"/>'s existing rule ("high contrast is not a third
    /// preference sitting alongside the other two: it is Windows saying the palette is no longer
    /// ours"), and a preference that could override it would let the app paint its own colours
    /// over a scheme somebody turned on because they cannot read ours.
    /// </summary>
    public static AppTheme Resolve(ThemePreference preference, AppTheme system, bool systemIsHighContrast) =>
        systemIsHighContrast ? AppTheme.HighContrast : preference switch
        {
            ThemePreference.Dark => AppTheme.Dark,
            ThemePreference.Light => AppTheme.Light,
            ThemePreference.HighContrast => AppTheme.HighContrast,
            _ => system,
        };

    /// <summary>How the preference is spelt in shell.json. Stable — it is a persisted value.</summary>
    public static string ToStorageName(ThemePreference preference) => preference switch
    {
        ThemePreference.Dark => "dark",
        ThemePreference.Light => "light",
        ThemePreference.HighContrast => "highContrast",
        _ => "auto",
    };

    /// <summary>
    /// Tolerant, like every other field shell.json reads: an unknown or misspelt value is a
    /// preference we cannot honour, and Auto is the one answer that is never wrong.
    /// </summary>
    public static ThemePreference FromStorageName(string? name) => name?.ToLowerInvariant() switch
    {
        "dark" => ThemePreference.Dark,
        "light" => ThemePreference.Light,
        "highcontrast" => ThemePreference.HighContrast,
        _ => ThemePreference.Auto,
    };
}
