// System.IO is NOT in the implicit-usings set for a UseWPF project — that set replaces the
// default one rather than adding to it, which is why Core.Tests needs no such line.
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Windows.Media;
using WaveLinkBackup.App.Theming;

namespace WaveLinkBackup.App.Tests;

/// <summary>
/// Theme switching is a resource swap, which is what makes it testable at all. Pixels are what
/// the handoff and a human eye are for; these are the two rules that can rot silently.
/// </summary>
public sealed class ThemeTests
{
    [Theory]
    [InlineData(AppTheme.Dark)]
    [InlineData(AppTheme.Light)]
    [InlineData(AppTheme.HighContrast)]
    public void Every_theme_declares_every_brush(AppTheme theme)
    {
        // Reduced to plain data on the WPF thread; the dictionary itself belongs to it.
        var missing = Wpf.Run(() =>
        {
            var dictionary = ThemeManager.Load(theme);
            return ThemeManager.BrushKeys.Where(k => !dictionary.Contains(k)).ToList();
        });

        Assert.True(missing.Count == 0,
            $"{theme} is missing: {string.Join(", ", missing)}. A missing key is a control that " +
            $"renders with WPF's default colours in one theme only.");
    }

    /// <summary>
    /// "--wl-danger must not follow the OS accent. Two different reds in one window is a bug the
    /// design calls out explicitly." Dark and light both pin it to the red they specify.
    /// </summary>
    [Theory]
    [InlineData(AppTheme.Dark, "#FFF01616")]
    [InlineData(AppTheme.Light, "#FFAA0000")]
    public void Danger_is_the_specified_red_and_not_the_accent(AppTheme theme, string expected)
    {
        var danger = Wpf.Run(
            () => ((SolidColorBrush)ThemeManager.Load(theme)["WlDanger"]!).Color.ToString());

        Assert.Equal(expected, danger);
    }

    /// <summary>
    /// The guard that keeps "no colour literals in XAML" true in six months rather than only
    /// today. Mirrors Core's SourceGuardTests.
    /// </summary>
    [Fact]
    public void No_xaml_outside_the_theme_dictionaries_contains_a_colour_literal()
    {
        var root = Assembly.GetExecutingAssembly()
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .Single(a => a.Key == "AppSourceRoot").Value!;

        var themingFolder = $"{Path.DirectorySeparatorChar}Theming{Path.DirectorySeparatorChar}";
        var literal = new Regex("#[0-9A-Fa-f]{3,8}\\b");

        var offenders = new List<string>();

        foreach (var file in Directory.EnumerateFiles(root, "*.xaml", SearchOption.AllDirectories))
        {
            if (file.Contains(themingFolder, StringComparison.OrdinalIgnoreCase)) continue;
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")) continue;
            if (file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")) continue;

            foreach (Match match in literal.Matches(File.ReadAllText(file)))
            {
                offenders.Add($"  {Path.GetFileName(file)}: {match.Value}");
            }
        }

        Assert.True(offenders.Count == 0,
            $"Colours belong in Theming/*.xaml and are referenced by key, which is what makes " +
            $"a theme switch a resource swap rather than a window rebuild. Found:" +
            $"{Environment.NewLine}{string.Join(Environment.NewLine, offenders)}");
    }

    /// <summary>
    /// The companion to the test above: that one keeps literals OUT of the view XAML, this one
    /// keeps them out of HighContrast.xaml specifically. In a high-contrast theme the palette is
    /// not ours — Windows forces the colours and we bind to SystemColors.*ColorKey so they track
    /// whichever scheme (Black or White) the user has chosen. A literal hex here would freeze one
    /// scheme's values into the dictionary, and the other scheme would render wrong with nothing
    /// in the suite to catch it. The only legal values are a SystemColors static or Transparent.
    /// </summary>
    [Fact]
    public void High_contrast_carries_no_hard_coded_colour()
    {
        var root = Assembly.GetExecutingAssembly()
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .Single(a => a.Key == "AppSourceRoot").Value!;

        var path = Path.Combine(root, "Theming", "HighContrast.xaml");
        Assert.True(File.Exists(path), $"Expected the HC dictionary at {path}");

        var xaml = File.ReadAllText(path);

        // 1. No hex literal anywhere — #RGB, #RRGGBB or #AARRGGBB. The existing guard skips the
        //    theme folder; this is the one that watches it.
        var hex = new Regex("#[0-9A-Fa-f]{3,8}\\b");
        var hexOffenders = hex.Matches(xaml).Select(m => m.Value).ToList();

        Assert.True(hexOffenders.Count == 0,
            $"HighContrast.xaml must bind to SystemColors.*ColorKey or be Transparent — a literal " +
            $"hex freezes one scheme's palette and the other renders wrong. Found:" +
            $"{Environment.NewLine}{string.Join(Environment.NewLine, hexOffenders)}");

        // 2. Every SolidColorBrush in the file resolves to exactly one of the two legal shapes:
        //    Color="{DynamicResource {x:Static SystemColors.*ColorKey}}" or Color="Transparent".
        //    Anything else — a named colour (Red), a raw value, a DynamicResource that is not a
        //    SystemColors static — is a third category sneaking in.
        var brush = new Regex(
            "<SolidColorBrush\\b[^>]*/>", RegexOptions.Singleline);
        var legalValue = new Regex(
            "Color=\"(Transparent|\\{DynamicResource \\{x:Static SystemColors\\.\\w+ColorKey\\}\\})\"");

        var offenders = new List<string>();
        foreach (Match m in brush.Matches(xaml))
        {
            if (!legalValue.IsMatch(m.Value))
                offenders.Add($"  {m.Value}");
        }

        Assert.True(offenders.Count == 0,
            $"Every HC brush must be a SystemColors.*ColorKey static or Transparent. Found:" +
            $"{Environment.NewLine}{string.Join(Environment.NewLine, offenders)}");
    }
}
