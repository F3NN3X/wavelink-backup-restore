using System.Windows.Media;

namespace WaveLinkBackup.App.Theming;

/// <param name="Key">The brush resource key this replaces.</param>
/// <param name="Opacity">
/// Carried separately rather than folded into the colour's alpha, because that is how the
/// authored dictionaries express it: SolidColorBrush.Opacity, not a premultiplied ARGB, and
/// the derived brushes have to be interchangeable with the authored ones.
/// </param>
public readonly record struct AccentBrush(string Key, Color Colour, double Opacity);

/// <summary>
/// The accent enters the app HERE and nowhere else.
///
/// 01-tokens-and-mapping.md: "When the user's accent is set, --wl-accent-soft = accent at 12%
/// (dark) / 7% (light) and --wl-accent-line = accent at 32% / 24%." Deriving rather than
/// authoring is what keeps that true: four authored values would drift the first time one of
/// them was edited.
///
/// Pure, so the percentages are a table test rather than something only a screenshot catches.
/// </summary>
public static class AccentPalette
{
    /// <summary>
    /// Two keys are deliberately absent.
    ///
    /// WlDanger, because the design calls out two different reds in one window as a bug: the
    /// accent is the user's, and danger is ours. WlAccentInk, because it is the ink drawn ON the
    /// accent: deriving it from the accent is how you arrive at white on yellow.
    /// </summary>
    public static IReadOnlyList<AccentBrush> Derive(Color accent, AppTheme theme)
    {
        // In high contrast the accent is gone: screens/11 replaces primary with Highlight and
        // says nothing is red, so nothing needs protecting from a second red either.
        if (theme == AppTheme.HighContrast) return [];

        var (soft, line) = theme == AppTheme.Light ? (0.07, 0.24) : (0.12, 0.32);

        return
        [
            new AccentBrush("WlAccent", accent, 1.0),
            new AccentBrush("WlAccentSoft", accent, soft),
            new AccentBrush("WlAccentLine", accent, line),
        ];
    }
}
