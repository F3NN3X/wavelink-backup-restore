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

    /// <summary>
    /// The trap that took the restore dialog off the air: every style in Typography.xaml is
    /// TargetType="TextBlock", TrackedText is a FrameworkElement and deliberately not a TextBlock,
    /// and WPF throws when the two meet - at Style-application time, inside InitializeComponent,
    /// so the whole window fails to construct rather than one label rendering wrong.
    ///
    /// It is invisible on inspection: WlColumnHeaderText and WlColumnHeaderTrackedText are the same
    /// role, described by the same words, four characters apart, and the tracked pair lives in a
    /// different file. RestoreDialog.xaml reached for the wrong one and no test opened that window.
    /// This is the cheap guard for the class - the view tests catch it per window, this catches it
    /// per line, including in a window nobody has written a test for yet.
    /// </summary>
    [Fact]
    public void No_TrackedText_wears_a_TextBlock_style()
    {
        var textBlockRoles = new[]
        {
            "WlDialogTitleText", "WlRowNameText", "WlBodyText", "WlSecondaryText",
            "WlMonoReadoutText", "WlMonoMetaText", "WlStatusStripText", "WlColumnHeaderText",
            "WlTierBadgeText", "WlSlotLabelText",
        };

        // <views:TrackedText ...> up to its closing bracket, then any Style= naming a TextBlock role.
        var element = new System.Text.RegularExpressions.Regex(
            "<views:TrackedText\\b[^>]*>",
            System.Text.RegularExpressions.RegexOptions.Singleline);

        var offenders = new List<string>();

        foreach (var file in System.IO.Directory.EnumerateFiles(
                     AppResources.SourceRoot, "*.xaml", System.IO.SearchOption.AllDirectories))
        {
            if (file.Contains($"{System.IO.Path.DirectorySeparatorChar}obj{System.IO.Path.DirectorySeparatorChar}")) continue;
            if (file.Contains($"{System.IO.Path.DirectorySeparatorChar}bin{System.IO.Path.DirectorySeparatorChar}")) continue;

            foreach (System.Text.RegularExpressions.Match match in
                     element.Matches(System.IO.File.ReadAllText(file)))
            {
                foreach (var role in textBlockRoles)
                {
                    if (!match.Value.Contains($"StaticResource {role}}}", StringComparison.Ordinal)) continue;

                    offenders.Add($"  {System.IO.Path.GetFileName(file)}: TrackedText styled {role}");
                }
            }
        }

        Assert.True(offenders.Count == 0,
            $"A TargetType=\"TextBlock\" style applied to a TrackedText throws when WPF applies it, " +
            $"taking the whole window down at construction. Use the parallel *TrackedText style " +
            $"(RowStyles.xaml). Found:{Environment.NewLine}{string.Join(Environment.NewLine, offenders)}");
    }
}
