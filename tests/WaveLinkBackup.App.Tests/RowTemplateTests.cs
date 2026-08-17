// System.IO is NOT in the implicit-usings set for a UseWPF project - see ThemeTests.
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;

namespace WaveLinkBackup.App.Tests;

/// <summary>
/// The design encoded health in SHAPE - solid rule = present, dashed = missing, dotted =
/// unknowable - precisely so that high contrast works without inventing anything. If the rules
/// stop being 2px solid / 2px solid / 2px dashed / 2px dotted, that argument quietly stops being
/// true and 11-high-contrast becomes a claim nobody is keeping.
/// </summary>
public sealed class RowTemplateTests
{
    private static readonly string SourceRoot = Assembly.GetExecutingAssembly()
        .GetCustomAttributes<AssemblyMetadataAttribute>()
        .Single(a => a.Key == "AppSourceRoot").Value!;

    private static string RowStyles() =>
        File.ReadAllText(Path.Combine(SourceRoot, "Views", "RowStyles.xaml"));

    private static string Style(string key)
    {
        var match = Regex.Match(
            RowStyles(),
            $"<(?:Style|DataTemplate)[^>]*x:Key=\"{Regex.Escape(key)}\".*?</(?:Style|DataTemplate)>",
            RegexOptions.Singleline);

        Assert.True(match.Success, $"{key} is gone or has been renamed.");

        return match.Value;
    }

    [Theory]
    [InlineData("WlSlotNamed", "WlOk")]
    [InlineData("WlSlotGeneric", "WlWarn")]
    public void A_present_slot_has_a_2px_solid_bottom_rule(string key, string brush)
    {
        var style = Style(key);

        Assert.Contains("BorderThickness=\"0,0,0,2\"", style, StringComparison.Ordinal);
        Assert.Contains(brush, style, StringComparison.Ordinal);
    }

    [Fact]
    public void A_missing_slot_has_a_dashed_rule_and_an_em_dash()
    {
        var style = Style("WlSlotMissing");

        Assert.Contains("BorderThickness=\"0,0,0,2\"", style, StringComparison.Ordinal);
        Assert.Contains("StrokeDashArray", style, StringComparison.Ordinal);
        Assert.Contains("WlLine2", style, StringComparison.Ordinal);
    }

    // "Deliberately breaking the five-slot pattern is the signal: the row stops being data." - 02
    [Fact]
    public void The_damaged_inputs_cell_is_one_dotted_full_width_cell()
    {
        var template = Style("WlContentsUnknown");

        Assert.Contains("CONTENTS UNKNOWN", template, StringComparison.Ordinal);
        Assert.Contains("StrokeDashArray", template, StringComparison.Ordinal);
    }

    // THE trap this whole plan is built around. README still specifies this pill in
    // --wl-accent-soft / --wl-accent: a red pill inside an amber row, which is both the second
    // red the rules forbid and a health state dressed up as an action.
    [Fact]
    public void The_suspect_pill_is_amber_and_never_mentions_the_accent()
    {
        var style = Style("WlSuspectPill");

        Assert.Contains("WlWarn", style, StringComparison.Ordinal);
        Assert.DoesNotContain("WlAccent", style, StringComparison.Ordinal);
    }

    [Fact]
    public void The_damaged_pill_is_neutral_and_takes_no_colour_at_all()
    {
        var style = Style("WlDamagedPill");

        Assert.Contains("WlLine2", style, StringComparison.Ordinal);
        Assert.DoesNotContain("WlWarn", style, StringComparison.Ordinal);
        Assert.DoesNotContain("WlAccent", style, StringComparison.Ordinal);
    }

    // 02-backup-health-states.md:48 - "WHY pill: transparent fill, 1px --wl-line, --wl-muted at
    // 70%", unconditionally - not just when WhyIsPrimary is false. Reverting the IsDamaged trigger
    // (leaving only the WhyIsPrimary one) would drop the IsDamaged binding and the 0.7 opacity,
    // failing this.
    [Fact]
    public void The_why_pill_goes_flat_and_dim_when_the_row_is_damaged()
    {
        var style = Style("WlWhyPill");

        Assert.Contains("Binding IsDamaged", style, StringComparison.Ordinal);
        Assert.Contains("Opacity\" Value=\"0.7\"", style, StringComparison.Ordinal);

        var damagedTrigger = style[style.IndexOf("Binding IsDamaged", StringComparison.Ordinal)..];
        Assert.Contains("WhyBorder", damagedTrigger, StringComparison.Ordinal);
        Assert.Contains("Transparent", damagedTrigger, StringComparison.Ordinal);
    }

    // screens/11-high-contrast.md: "Selected = full Highlight fill with HighlightText throughout."
    // Without a selected+HC trigger, the plain IsSelected trigger's WlCard fill maps to Transparent
    // in high contrast and the "every row" HC trigger zeroes BorderThickness - a selected row would
    // have no fill and no border at all. Reverting the MultiDataTrigger below removes that fill,
    // failing this.
    [Fact]
    public void A_selected_row_gets_a_full_highlight_fill_in_high_contrast()
    {
        var template = RowStyles();

        var multiTriggerIndex = template.IndexOf(
            "Path=IsSelected", StringComparison.Ordinal);
        Assert.True(multiTriggerIndex >= 0, "The selected+high-contrast trigger is gone or renamed.");

        var blockStart = template.LastIndexOf("<MultiDataTrigger>", multiTriggerIndex, StringComparison.Ordinal);
        Assert.True(blockStart >= 0, "Could not find the enclosing MultiDataTrigger.");
        var blockEnd = template.IndexOf("</MultiDataTrigger>", multiTriggerIndex, StringComparison.Ordinal);
        Assert.True(blockEnd >= 0, "Could not find the closing </MultiDataTrigger>.");

        var block = template[blockStart..blockEnd];

        Assert.Contains("IsHighContrast", block, StringComparison.Ordinal);
        Assert.Contains("RowSurface", block, StringComparison.Ordinal);
        Assert.Contains("WlAccent", block, StringComparison.Ordinal);

        // Declared before the health triggers so IsSuspect/IsDamaged (still declared after) keep
        // out-ranking selection even in high contrast. Search from ControlTemplate.Triggers only -
        // "Binding IsSuspect" also appears earlier, inside the HealthPillHost's own nested Style,
        // which is a different trigger entirely (pill visibility, not row surface colour).
        var triggersSectionIndex = template.IndexOf("<ControlTemplate.Triggers>", StringComparison.Ordinal);
        Assert.True(triggersSectionIndex >= 0, "ControlTemplate.Triggers section is gone or renamed.");
        var isSuspectIndex = template.IndexOf("Binding IsSuspect", triggersSectionIndex, StringComparison.Ordinal);
        Assert.True(isSuspectIndex >= 0, "IsSuspect health trigger is gone or renamed.");
        Assert.True(blockEnd < isSuspectIndex,
            "The selected+high-contrast trigger must be declared before IsSuspect/IsDamaged so health still outranks selection.");
    }

    // 02-backup-health-states.md:53 - "CONTENTS column: all three tier slots present but dashed
    // ghosts at 50% opacity" regardless of IsPresent. Reverting the IsDamaged trigger on the tier
    // ContentControl's Style would leave a present tier showing WlTierPresent (real data) on a
    // damaged row, failing this.
    [Fact]
    public void A_damaged_row_forces_every_tier_badge_to_the_absent_ghost_treatment()
    {
        // WlRowTemplate is a Style with several nested Style blocks of its own, so the Style()
        // helper's non-greedy match (built for leaf DataTemplates) would stop at the first nested
        // </Style> long before reaching the CONTENTS column - read the raw source instead.
        var template = RowStyles();

        // The tier ItemTemplate's own Style block: from its "IsPresent" DataTrigger to the
        // ItemsControl's closing tag, so this does not accidentally match the WHY pill's own
        // IsDamaged trigger or the row-level IsDamaged health trigger.
        var isPresentIndex = template.IndexOf("Binding IsPresent", StringComparison.Ordinal);
        Assert.True(isPresentIndex >= 0, "Tier badge's IsPresent trigger is gone or renamed.");

        var tierEndIndex = template.IndexOf("</ItemsControl>", isPresentIndex, StringComparison.Ordinal);
        Assert.True(tierEndIndex >= 0, "Tier ItemsControl close tag not found after IsPresent trigger.");

        var tierItemStyle = template[isPresentIndex..tierEndIndex];

        Assert.Contains("DataContext.IsDamaged", tierItemStyle, StringComparison.Ordinal);
        Assert.Contains("AncestorType=ListBoxItem", tierItemStyle, StringComparison.Ordinal);

        // Declared AFTER IsPresent so it wins and forces WlTierAbsent regardless of IsPresent.
        var isDamagedIndex = tierItemStyle.IndexOf("DataContext.IsDamaged", StringComparison.Ordinal);
        var isDamagedSetter = tierItemStyle[isDamagedIndex..];
        Assert.Contains("WlTierAbsent", isDamagedSetter, StringComparison.Ordinal);
    }

    // 10-decisions section 5: "No element in this app has a 2px border on all four sides." The
    // health slots use a 2px bottom RULE, which is a different thing. The focus ring is the one
    // legitimate 2px rectangle and it lives in ControlStyles.xaml, not here.
    [Fact]
    public void Nothing_in_the_row_has_a_2px_border_on_all_four_sides()
    {
        var offenders = Regex.Matches(RowStyles(), "BorderThickness=\"([^\"]+)\"")
            .Select(m => m.Groups[1].Value)
            .Where(v => v is "2" || v.Split(',').Distinct().SequenceEqual(["2"]))
            .ToArray();

        Assert.True(offenders.Length == 0,
            $"2px on all four sides is a rule the design does not use: {string.Join(", ", offenders)}");
    }

    // Every brush in the row must be one of the 22 theme keys. The colour-literal guard catches
    // a #RRGGBB; this catches a slot bound to WlLine instead of WlOk, which is a perfectly legal
    // colour and completely wrong.
    [Fact]
    public void Every_brush_the_row_uses_is_a_theme_key()
    {
        var known = Theming.ThemeManager.BrushKeys.ToHashSet(StringComparer.Ordinal);

        var used = Regex.Matches(RowStyles(), @"(?:Dynamic|Static)Resource\s+(Wl[A-Za-z]+)")
            .Select(m => m.Groups[1].Value)
            .Where(name => name.StartsWith("Wl", StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal);

        var unknown = used
            .Where(name => !known.Contains(name))
            // Type styles, geometries and the row's own templates are keys too, and are not brushes.
            .Where(name => !name.EndsWith("Text", StringComparison.Ordinal)
                        && !name.EndsWith("Geometry", StringComparison.Ordinal)
                        && !name.EndsWith("Font", StringComparison.Ordinal)
                        && !name.EndsWith("Pill", StringComparison.Ordinal)
                        && !name.StartsWith("WlSlot", StringComparison.Ordinal))
            .ToArray();

        Assert.True(unknown.Length == 0,
            $"Not theme brushes: {string.Join(", ", unknown)}");
    }
}
