// System.IO is NOT in the implicit-usings set for a UseWPF project - see ThemeTests.cs's own
// comment on this.
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;

namespace WaveLinkBackup.App.Tests;

/// <summary>
/// The focus ring's colour is the one judgment call Task 11's brief leaves open: its own snippet
/// sets Stroke="{DynamicResource WlAccent}" unconditionally, but its own comment says high
/// contrast wants WindowText, and HighlightText on a Highlight-filled SELECTED row - which
/// disagree, since WlAccent already IS Highlight in HighContrast.xaml. Selected + high contrast
/// is exactly the combination the plan's acceptance list calls out ("the focus ring is visible on
/// a selected row"), so it is pinned here the same source-text way RowTemplateTests pins
/// WlRowTemplate's own selected+high-contrast trigger, rather than trusted to a by-eye pass alone.
/// </summary>
public sealed class FocusRingTests
{
    private static readonly string SourceRoot = Assembly.GetExecutingAssembly()
        .GetCustomAttributes<AssemblyMetadataAttribute>()
        .Single(a => a.Key == "AppSourceRoot").Value!;

    private static string ControlStyles() =>
        File.ReadAllText(Path.Combine(SourceRoot, "Views", "ControlStyles.xaml"));

    private static string FocusVisualStyle()
    {
        var text = ControlStyles();
        var start = text.IndexOf("<Style x:Key=\"WlFocusVisual\">", StringComparison.Ordinal);
        Assert.True(start >= 0, "WlFocusVisual is gone or renamed.");

        // The very next </Style> after this one's own opening tag - there is no nesting inside it.
        var end = text.IndexOf("</Style>", start, StringComparison.Ordinal);
        Assert.True(end >= 0, "WlFocusVisual's closing </Style> was not found.");

        return text[start..(end + "</Style>".Length)];
    }

    [Fact]
    public void The_ring_is_2px_with_a_2px_offset()
    {
        var style = FocusVisualStyle();

        Assert.Contains("StrokeThickness=\"2\"", style, StringComparison.Ordinal);
        Assert.Contains("Margin=\"-2\"", style, StringComparison.Ordinal);
    }

    [Fact]
    public void The_ring_defaults_to_the_accent()
    {
        Assert.Contains("Stroke=\"{DynamicResource WlAccent}\"", FocusVisualStyle(), StringComparison.Ordinal);
    }

    // WlAccent already IS Highlight in HighContrast.xaml, so leaving the ring on WlAccent there
    // would put a Highlight ring on a row Task 10 already fills with Highlight - invisible. WlText
    // is WindowText in every theme (Dark.xaml, Light.xaml, HighContrast.xaml alike), so switching
    // to it in high contrast is what keeps the ring visible on every UNSELECTED focusable element.
    [Fact]
    public void High_contrast_switches_the_ring_to_window_text()
    {
        var style = FocusVisualStyle();

        var hcTrigger = Regex.Match(
            style, "<DataTrigger Value=\"True\">.*?</DataTrigger>", RegexOptions.Singleline).Value;

        Assert.Contains("IsHighContrast", hcTrigger, StringComparison.Ordinal);
        Assert.Contains(
            "TargetName=\"Ring\" Property=\"Stroke\" Value=\"{DynamicResource WlText}\"",
            hcTrigger, StringComparison.Ordinal);
    }

    // The plan's own acceptance line: "the focus ring is visible on a selected row" in high
    // contrast. WlAccentInk is HighlightText there (HighContrast.xaml) - the readable-on-Highlight
    // counterpart Task 10's own selected+HC row trigger already uses for the same reason.
    [Fact]
    public void High_contrast_plus_selected_switches_the_ring_to_highlight_text()
    {
        var style = FocusVisualStyle();

        var selectedTrigger = Regex.Match(
            style, "<MultiDataTrigger>.*?</MultiDataTrigger>", RegexOptions.Singleline).Value;

        Assert.Contains("IsHighContrast", selectedTrigger, StringComparison.Ordinal);
        Assert.Contains("Path=\"IsSelected\"", selectedTrigger, StringComparison.Ordinal);
        Assert.Contains("RelativeSource=\"{RelativeSource TemplatedParent}\"", selectedTrigger, StringComparison.Ordinal);
        Assert.Contains(
            "TargetName=\"Ring\" Property=\"Stroke\" Value=\"{DynamicResource WlAccentInk}\"",
            selectedTrigger, StringComparison.Ordinal);
    }

    // Without this alias the ring only ever applies where a control's FocusVisualStyle is set
    // explicitly - WPF's own default resolves against SystemParameters.FocusVisualStyleKey, and
    // that is the mechanism that reaches every generated ListBoxItem row without MainWindow.xaml
    // setting FocusVisualStyle on each one by hand.
    [Fact]
    public void The_ring_is_registered_as_the_windows_own_default_focus_visual()
    {
        Assert.Contains(
            "<Style x:Key=\"{x:Static SystemParameters.FocusVisualStyleKey}\"",
            ControlStyles(), StringComparison.Ordinal);
    }
}
