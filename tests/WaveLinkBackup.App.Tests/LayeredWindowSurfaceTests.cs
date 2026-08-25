// System.IO is NOT in the implicit-usings set for a UseWPF project - see ThemeTests.cs's own
// comment on this.
using System.IO;
using System.Reflection;
using System.Windows.Media;
using WaveLinkBackup.App.Theming;

namespace WaveLinkBackup.App.Tests;

/// <summary>
/// A layered window has no backdrop but the one it paints.
///
/// <para>
/// Every dialog here is <c>WindowStyle=None</c>, <c>AllowsTransparency=True</c>,
/// <c>Background="Transparent"</c>, with <c>WlScrim</c> edge to edge and a <c>WlCard</c> card
/// centred on top. In light and dark that works because the scrim is a real fill. In high contrast
/// the scrim is transparent by design — a dialog is separated by a border, not by dimming — so
/// <c>WlCard</c> is the only thing standing between the dialog's text and the desktop.
/// </para>
///
/// <para>
/// It was <c>Transparent</c>, and every dialog rendered as a hole with a border round it. The
/// high-contrast dictionary's own rule is "every fill goes transparent", which is right for a card
/// drawn on <c>WlBg</c> and wrong for a card that IS the window.
/// </para>
/// </summary>
public sealed class LayeredWindowSurfaceTests
{
    private static readonly string SourceRoot = Assembly.GetExecutingAssembly()
        .GetCustomAttributes<AssemblyMetadataAttribute>()
        .Single(a => a.Key == "AppSourceRoot").Value!;

    /// <summary>
    /// Every view whose root window is layered, and therefore has no opaque backdrop of its own.
    /// Discovered rather than listed: a tenth dialog added next year is covered without anyone
    /// remembering this file exists.
    /// </summary>
    private static IEnumerable<(string Name, string Text)> LayeredViews()
    {
        foreach (var file in Directory.EnumerateFiles(
                     Path.Combine(SourceRoot, "Views"), "*.xaml"))
        {
            // Comments first, same as SourceGuardTests and ToolScriptGuardTests: the rule is about
            // markup, not about the prose explaining the rule. MainWindow.xaml carries two
            // comment lines about AllowsTransparency saying it stays FALSE there, and a raw scan
            // reads those as the attribute itself.
            var text = StripComments(File.ReadAllText(file));

            if (text.Contains("AllowsTransparency=\"True\"", StringComparison.Ordinal))
            {
                yield return (Path.GetFileName(file), text);
            }
        }
    }

    internal static string StripComments(string xaml) =>
        System.Text.RegularExpressions.Regex.Replace(
            xaml, "<!--.*?-->", "", System.Text.RegularExpressions.RegexOptions.Singleline);

    private static byte Alpha(AppTheme theme, string key) => Wpf.Run(
        () => ((SolidColorBrush)ThemeManager.Load(theme)[key]!).Color.A);

    [Theory]
    [InlineData(AppTheme.HighContrast)]
    [InlineData(AppTheme.Light)]
    [InlineData(AppTheme.Dark)]
    public void The_card_surface_is_opaque_in_every_theme(AppTheme theme)
    {
        // Not just high contrast. Whatever a future theme does with its fills, a dialog card is
        // load-bearing in all of them, and the failure is silent: the dialog still lays out, still
        // takes focus, still closes. It is only unreadable.
        Assert.Equal(255, Alpha(theme, "WlCard"));
    }

    [Fact]
    public void The_high_contrast_card_is_the_system_window_colour_rather_than_a_literal()
    {
        // "Nothing here is a literal" - the palette in high contrast is Windows', not ours. A
        // hardcoded white would look correct on the usual black-on-white scheme and wrong on
        // every other one.
        var text = File.ReadAllText(Path.Combine(SourceRoot, "Theming", "HighContrast.xaml"));
        var line = text.Split('\n').Single(l => l.Contains("x:Key=\"WlCard\"", StringComparison.Ordinal));

        Assert.Contains("SystemColors.WindowColorKey", line, StringComparison.Ordinal);

        // WindowColorKey, not WindowColor. The latter is the Color VALUE, and a value used as a
        // DynamicResource key never resolves - the brush renders black, in the one theme where
        // that is unsurvivable.
        Assert.DoesNotContain("SystemColors.WindowColor}", line, StringComparison.Ordinal);
    }

    [Fact]
    public void Every_layered_view_paints_an_opaque_surface_somewhere()
    {
        // The structural half of the same rule: a layered window that never references WlCard is
        // relying on a fill this test does not know is opaque.
        var offenders = LayeredViews()
            .Where(v => !v.Text.Contains("DynamicResource WlCard", StringComparison.Ordinal))
            .Select(v => $"  {v.Name}")
            .ToArray();

        Assert.True(offenders.Length == 0,
            "A layered window (AllowsTransparency=True) has no backdrop but the one it paints, so " +
            "it must sit on WlCard - the one surface brush that is opaque in every theme. " +
            $"Found:{Environment.NewLine}{string.Join(Environment.NewLine, offenders)}");
    }

    [Fact]
    public void The_scan_found_the_dialogs_it_is_supposed_to_be_guarding()
    {
        // A discovery-based guard that discovers nothing passes forever.
        var found = LayeredViews().Select(v => v.Name).ToArray();

        Assert.True(found.Length >= 8,
            $"Expected the dialog set to be layered; found only {found.Length}: " +
            string.Join(", ", found));
        Assert.Contains("DeleteDialog.xaml", found);
        Assert.Contains("SettingsDialog.xaml", found);

        // MainWindow is NOT layered - AllowsTransparency stays false there so DWM still draws
        // Mica. It names the attribute twice in comments saying exactly that, which is what the
        // comment stripper is for; without it this scan reads the prose as the markup.
        Assert.DoesNotContain("MainWindow.xaml", found);
    }
}
