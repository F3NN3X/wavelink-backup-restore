using System.Windows;
using System.Windows.Media;

namespace WaveLinkBackup.App.Tests;

/// <summary>
/// The embedding, and the trap underneath it: WPF cannot use a variable font. A [wght] file
/// resolves to ONE instance, so Rubik 500 and Rubik 700 would render identically and nothing
/// would look obviously broken - it would just look flat.
/// </summary>
public sealed class TypographyTests
{
    private static readonly Uri Base = new("pack://application:,,,/WaveLinkBackup;component/");

    private static FontFamily Family(string key) => Wpf.Run(() =>
    {
        var dictionary = new ResourceDictionary
        {
            Source = new Uri(Base, "Views/Typography.xaml"),
        };

        return (FontFamily)dictionary[key];
    });

    [Fact]
    public void The_display_family_is_rubik_and_is_embedded()
    {
        var family = Family("WlDisplayFont");

        Assert.Contains("Rubik", family.Source, StringComparison.Ordinal);
        Assert.NotEmpty(family.GetTypefaces());
    }

    [Fact]
    public void The_mono_family_is_jetbrains_mono_and_is_embedded()
    {
        var family = Family("WlMonoFont");

        Assert.Contains("JetBrains", family.Source, StringComparison.Ordinal);
        Assert.NotEmpty(family.GetTypefaces());
    }

    // The variable-font trap, pinned. Three DISTINCT glyph typefaces means three real weights
    // were embedded; a variable file would collapse them onto one.
    [Fact]
    public void Rubik_ships_regular_medium_and_bold_as_separate_faces()
    {
        var family = Family("WlDisplayFont");

        var faces = Wpf.Run(() => new[] { FontWeights.Regular, FontWeights.Medium, FontWeights.Bold }
            .Select(w => new Typeface(family, FontStyles.Normal, w, FontStretches.Normal))
            .Select(t => t.TryGetGlyphTypeface(out var g) ? g.FontUri.ToString() : null)
            .ToArray());

        Assert.All(faces, f => Assert.NotNull(f));
        Assert.Equal(3, faces.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void Jetbrains_mono_ships_regular_and_medium_as_separate_faces()
    {
        var family = Family("WlMonoFont");

        var faces = Wpf.Run(() => new[] { FontWeights.Regular, FontWeights.Medium }
            .Select(w => new Typeface(family, FontStyles.Normal, w, FontStretches.Normal))
            .Select(t => t.TryGetGlyphTypeface(out var g) ? g.FontUri.ToString() : null)
            .ToArray());

        Assert.All(faces, f => Assert.NotNull(f));
        Assert.Equal(2, faces.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    // Every size in README's type table has a style. A missing one is a control that quietly
    // renders at WPF's 12px default, which looks like a spacing bug rather than a type bug.
    [Theory]
    [InlineData("WlDialogTitleText")]
    [InlineData("WlRowNameText")]
    [InlineData("WlBodyText")]
    [InlineData("WlSecondaryText")]
    [InlineData("WlMonoReadoutText")]
    [InlineData("WlMonoMetaText")]
    [InlineData("WlStatusStripText")]
    [InlineData("WlColumnHeaderText")]
    [InlineData("WlTierBadgeText")]
    [InlineData("WlSlotLabelText")]
    public void Every_type_role_has_a_style(string key)
    {
        var (style, targetType) = Wpf.Run(() =>
        {
            var dictionary = new ResourceDictionary { Source = new Uri(Base, "Views/Typography.xaml") };
            var s = dictionary[key] as Style;
            return (s, s?.TargetType);
        });

        Assert.NotNull(style);
        Assert.Equal(typeof(System.Windows.Controls.TextBlock), targetType);
    }
}
