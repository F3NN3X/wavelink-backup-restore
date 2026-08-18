// System.IO is NOT in the implicit-usings set for a UseWPF project - see ThemeTests.cs's own
// comment on this.
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;

namespace WaveLinkBackup.App.Tests;

/// <summary>
/// Fix 2 and part of fix 4: WlGhostButton, WlSecondaryButton and WlPrimaryButton
/// (ControlStyles.xaml) each set Opacity="0.4" on IsEnabled=False with no high-contrast branch -
/// 11-high-contrast.md:32 forbids that ("40% opacity is illegal here... Disabled = GrayText for
/// border and label at full opacity"). They also had no hover feedback at all in high contrast
/// (WlHover resolves to Transparent there) - 11:32's "Hover = 1px HotTrack outline, no fill."
/// Source-text guards, same idiom as FocusRingTests/RowTemplateTests: reading the compiled file
/// rather than a rendered visual tree.
/// </summary>
public sealed class ButtonHighContrastTests
{
    private static readonly string SourceRoot = Assembly.GetExecutingAssembly()
        .GetCustomAttributes<AssemblyMetadataAttribute>()
        .Single(a => a.Key == "AppSourceRoot").Value!;

    private static string ControlStyles() =>
        File.ReadAllText(Path.Combine(SourceRoot, "Views", "ControlStyles.xaml"));

    private static string MainWindowXaml() =>
        File.ReadAllText(Path.Combine(SourceRoot, "Views", "MainWindow.xaml"));

    private static string ButtonStyle(string key)
    {
        var text = ControlStyles();
        var start = text.IndexOf($"<Style x:Key=\"{key}\"", StringComparison.Ordinal);
        Assert.True(start >= 0, $"{key} is gone or renamed.");

        // Each of the three button styles closes with the same "</Setter.Value></Setter></Style>"
        // sequence right after its own ControlTemplate.Triggers - find the first </Style> after
        // the opening tag, which is this style's own (none of the three nest another Style).
        var end = text.IndexOf("</Style>", start, StringComparison.Ordinal);
        Assert.True(end >= 0, $"{key}'s closing </Style> was not found.");

        return text[start..(end + "</Style>".Length)];
    }

    [Theory]
    [InlineData("WlGhostButton")]
    [InlineData("WlSecondaryButton")]
    [InlineData("WlPrimaryButton")]
    public void Disabled_is_full_opacity_and_GrayText_in_high_contrast(string key)
    {
        var style = ButtonStyle(key);

        // Declared AFTER the plain IsEnabled=False trigger, so it wins and restores Opacity to 1.
        var plainDisabledIndex = style.IndexOf(
            "<Trigger Property=\"IsEnabled\" Value=\"False\">", StringComparison.Ordinal);
        Assert.True(plainDisabledIndex >= 0, $"{key}'s plain disabled trigger is gone or renamed.");

        var hcDisabledIndex = style.IndexOf("IsEnabled\" RelativeSource=", StringComparison.Ordinal);
        Assert.True(hcDisabledIndex >= 0, $"{key} has no high-contrast disabled trigger.");
        Assert.True(hcDisabledIndex > plainDisabledIndex,
            $"{key}'s high-contrast disabled trigger must be declared after the plain one.");

        var hcDisabledBlockStart = style.LastIndexOf("<MultiDataTrigger>", hcDisabledIndex, StringComparison.Ordinal);
        var hcDisabledBlockEnd = style.IndexOf("</MultiDataTrigger>", hcDisabledIndex, StringComparison.Ordinal);
        var block = style[hcDisabledBlockStart..hcDisabledBlockEnd];

        Assert.Contains("IsHighContrast", block, StringComparison.Ordinal);
        Assert.Contains("<Setter Property=\"Opacity\" Value=\"1\" />", block, StringComparison.Ordinal);
        Assert.Contains(
            "<Setter Property=\"Foreground\" Value=\"{DynamicResource WlMuted}\" />",
            block, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("WlGhostButton")]
    [InlineData("WlSecondaryButton")]
    [InlineData("WlPrimaryButton")]
    public void Hover_draws_a_hot_track_outline_in_high_contrast(string key)
    {
        var style = ButtonStyle(key);

        var hoverTriggerIndex = style.IndexOf("IsMouseOver\" RelativeSource=", StringComparison.Ordinal);
        Assert.True(hoverTriggerIndex >= 0, $"{key} has no high-contrast hover trigger.");

        var blockStart = style.LastIndexOf("<MultiDataTrigger>", hoverTriggerIndex, StringComparison.Ordinal);
        var blockEnd = style.IndexOf("</MultiDataTrigger>", hoverTriggerIndex, StringComparison.Ordinal);
        var block = style[blockStart..blockEnd];

        Assert.Contains("IsHighContrast", block, StringComparison.Ordinal);
        Assert.Contains("TargetName=\"Surface\" Property=\"BorderBrush\" Value=\"{DynamicResource WlHotTrack}\"",
            block, StringComparison.Ordinal);
        Assert.Contains("TargetName=\"Surface\" Property=\"BorderThickness\" Value=\"1\"",
            block, StringComparison.Ordinal);
    }

    // Rename/Delete/Restore's own icons hard-coded Stroke="{DynamicResource WlText}" - a literal
    // that never follows the button's own Foreground, so the disabled+high-contrast fix above
    // would dim the label but leave the icon bright green (GrayText in HC Black) beside it.
    [Theory]
    [InlineData("WlPencilGeometry")]
    [InlineData("WlTrashGeometry")]
    [InlineData("WlRotateCcwGeometry")]
    public void The_bottom_bar_icons_follow_their_buttons_own_foreground(string geometry)
    {
        var xaml = MainWindowXaml();

        var pathTag = Regex.Match(
            xaml, $"<Path Data=\"\\{{StaticResource {Regex.Escape(geometry)}\\}}\".*?/>",
            RegexOptions.Singleline).Value;

        Assert.True(pathTag.Length > 0, $"The Path using {geometry} is gone or renamed.");
        Assert.DoesNotContain("Stroke=\"{DynamicResource WlText}\"", pathTag, StringComparison.Ordinal);
        Assert.Contains(
            "Stroke=\"{Binding Foreground, RelativeSource={RelativeSource AncestorType=Button}}\"",
            pathTag, StringComparison.Ordinal);
    }
}
