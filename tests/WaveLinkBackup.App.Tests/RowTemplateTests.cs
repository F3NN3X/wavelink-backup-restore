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

    private static string HighContrastTheme() =>
        File.ReadAllText(Path.Combine(SourceRoot, "Theming", "HighContrast.xaml"));

    // screens/11-high-contrast.md: "Selected = full Highlight fill with HighlightText throughout."
    // and "Suspect and damaged no longer tint the row; the verdict word does the work." Together
    // those mean a selected row must win over health TOO in high contrast - unlike the normal
    // themes, where IsSuspect/IsDamaged's real tints must win over selection. Reverting the
    // MultiDataTrigger, or moving it back before IsSuspect/IsDamaged (an earlier, wrong attempt at
    // this same fix), both remove the fill from a selected+unwell row in HC, failing this.
    [Fact]
    public void A_selected_row_gets_a_full_highlight_fill_in_high_contrast_even_when_unwell()
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

        // "...throughout" (11-high-contrast.md) includes MetaText: it carries the verdict word once
        // the row tint is gone, and its Foreground is never touched by Health (only its Text swaps,
        // via the plain HC trigger). Reverting just this one setter would leave the verdict word in
        // WlMuted (GrayText) on a Highlight-filled row - readable or not depending on the user's HC
        // scheme, which is exactly the risk HighlightText exists to remove.
        Assert.Contains(
            "TargetName=\"MetaText\" Property=\"Foreground\" Value=\"{DynamicResource WlAccentInk}\"",
            block, StringComparison.Ordinal);

        // Must be declared LAST in ControlTemplate.Triggers - after IsSuspect, IsDamaged, and the
        // plain "every row" HC trigger - so it wins over all three. Search from
        // ControlTemplate.Triggers only: "Binding IsSuspect" also appears earlier, inside the
        // HealthPillHost's own nested Style, a different trigger entirely (pill visibility, not
        // row surface colour).
        var triggersSectionIndex = template.IndexOf("<ControlTemplate.Triggers>", StringComparison.Ordinal);
        Assert.True(triggersSectionIndex >= 0, "ControlTemplate.Triggers section is gone or renamed.");
        var isSuspectIndex = template.IndexOf("Binding IsSuspect", triggersSectionIndex, StringComparison.Ordinal);
        Assert.True(isSuspectIndex >= 0, "IsSuspect health trigger is gone or renamed.");
        var isDamagedIndex = template.IndexOf("Binding IsDamaged", triggersSectionIndex, StringComparison.Ordinal);
        Assert.True(isDamagedIndex >= 0, "IsDamaged health trigger is gone or renamed.");
        var plainHcBorderIndex = template.IndexOf(
            "TargetName=\"RowSurface\" Property=\"BorderThickness\" Value=\"0\"",
            triggersSectionIndex, StringComparison.Ordinal);
        Assert.True(plainHcBorderIndex >= 0, "The plain every-row high-contrast trigger is gone or renamed.");

        Assert.True(blockStart > isSuspectIndex && blockStart > isDamagedIndex && blockStart > plainHcBorderIndex,
            "The selected+high-contrast trigger must be declared LAST - after IsSuspect, IsDamaged, " +
            "and the plain high-contrast trigger - so a selected row stays filled even when it is " +
            "also suspect or damaged. It must NOT be positioned before them (that was tried and is " +
            "wrong): in high contrast health does not tint the row at all, so there is no tint here " +
            "for selection to out-rank; declaring this last is what makes selection win instead.");

        // Pin WHY this ordering is safe, not just that it happens to work: IsSuspect/IsDamaged set
        // RowSurface.Background to WlWarnSoft/WlSunken, and this trigger only gets to win over them
        // harmlessly because HighContrast.xaml maps BOTH to Transparent - there is no real tint
        // being clobbered. If a future edit gave either of those keys a real high-contrast colour,
        // this ordering would start hiding a genuine signal, and this assertion would catch that
        // before the trigger-order test above could go stale silently.
        var hcTheme = HighContrastTheme();
        Assert.Contains("x:Key=\"WlWarnSoft\" Color=\"Transparent\"", hcTheme, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"WlSunken\" Color=\"Transparent\"", hcTheme, StringComparison.Ordinal);
    }

    // 11-high-contrast.md:32 - "...throughout" names glyphs explicitly. VerdictGlyph IS the verdict
    // word's icon, and it is a named element in the same ControlTemplate scope as RowSurface (unlike
    // the deferred nested pills/badges), so it is in scope for this fix. Reverting either of the two
    // property-specific setters below would leave that one Health's glyph in WlText (WindowText) on
    // a Highlight-filled selected row - not guaranteed to contrast, failing this.
    [Theory]
    [InlineData("Whole", "Stroke")]
    [InlineData("Suspect", "Fill")]
    [InlineData("Damaged", "Fill")]
    public void A_selected_rows_verdict_glyph_inverts_to_highlight_text_in_high_contrast(
        string health, string property)
    {
        var template = RowStyles();

        var triggersSectionIndex = template.IndexOf("<ControlTemplate.Triggers>", StringComparison.Ordinal);
        Assert.True(triggersSectionIndex >= 0, "ControlTemplate.Triggers section is gone or renamed.");
        var triggersSection = template[triggersSectionIndex..];

        // Each MultiDataTrigger for a given Health value is unique in combination with IsSelected -
        // the setter TEXT alone ("Fill" -> WlAccentInk) is shared between Suspect and Damaged, so
        // the block has to be picked out by BOTH conditions together, not by the setter alone.
        var blocks = Regex.Matches(triggersSection, "<MultiDataTrigger>.*?</MultiDataTrigger>", RegexOptions.Singleline)
            .Select(m => m.Value)
            .Where(b => b.Contains("Path=IsSelected", StringComparison.Ordinal)
                     && b.Contains($"Value=\"{health}\"", StringComparison.Ordinal))
            .ToArray();

        Assert.True(blocks.Length == 1,
            $"Expected exactly one selected+high-contrast+{health} MultiDataTrigger, found {blocks.Length}.");
        var block = blocks[0];

        Assert.Contains("IsHighContrast", block, StringComparison.Ordinal);
        Assert.Contains(
            $"TargetName=\"VerdictGlyph\" Property=\"{property}\" Value=\"{{DynamicResource WlAccentInk}}\"",
            block, StringComparison.Ordinal);

        // "Check which of Fill/Stroke the glyph actually uses ... set what it has" - the check mark
        // (Whole) is an open, stroke-only geometry; filling it too would paint a triangular sliver
        // WPF's implicit fill-closes-open-paths behaviour would add across a shape that has no fill
        // anywhere else in the app. Suspect/Damaged are fill-only glyphs, so the reverse must hold.
        var otherProperty = property == "Stroke" ? "Fill" : "Stroke";
        Assert.DoesNotContain(
            $"TargetName=\"VerdictGlyph\" Property=\"{otherProperty}\" Value=\"{{DynamicResource WlAccentInk}}\"",
            block, StringComparison.Ordinal);

        // Declared after that Health's OWN base HC-only trigger (the one with the same Health value
        // but no IsSelected condition), so it overrides WlText with WlAccentInk rather than the
        // other way round. Picked out by the same both-conditions technique, since "Fill -> WlText"
        // is likewise shared text between Suspect's and Damaged's base triggers.
        var baseBlocks = Regex.Matches(triggersSection, "<MultiDataTrigger>.*?</MultiDataTrigger>", RegexOptions.Singleline)
            .Select(m => m.Value)
            .Where(b => !b.Contains("Path=IsSelected", StringComparison.Ordinal)
                     && b.Contains($"Value=\"{health}\"", StringComparison.Ordinal))
            .ToArray();
        Assert.True(baseBlocks.Length == 1,
            $"Expected exactly one base (non-selected) high-contrast MultiDataTrigger for {health}, found {baseBlocks.Length}.");

        var blockStart = triggersSectionIndex + triggersSection.IndexOf(block, StringComparison.Ordinal);
        var baseBlockStart = triggersSectionIndex + triggersSection.IndexOf(baseBlocks[0], StringComparison.Ordinal);
        Assert.True(blockStart > baseBlockStart,
            $"The selected+high-contrast {health} glyph trigger must be declared AFTER {health}'s own " +
            "base high-contrast trigger so it wins and overrides WlText with WlAccentInk.");
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

    // 10b fix: the selected-row expansion moved OUT of MainWindow.xaml's hand-placed sibling
    // Border and INTO WlRowTemplate itself - "a second Grid.Row inside the same WlCard block,
    // visible only when IsSelected" (the fix brief, quoting the original 10b brief). Reverting
    // that move (putting the expansion back in MainWindow.xaml) would make this fail.
    //
    // Reads the raw source rather than going through the Style() helper: WlRowTemplate has
    // several nested Style blocks of its own (the CONTENTS-badge test above hits the same thing),
    // so Style()'s non-greedy match would stop at the first nested </Style> long before reaching
    // ExpansionRow, which is declared after all of them.
    [Fact]
    public void The_selected_row_expansion_lives_inside_the_row_template()
    {
        var template = RowStyles();

        Assert.Contains("x:Name=\"ExpansionRow\"", template, StringComparison.Ordinal);
        Assert.Contains("Binding DetailLine", template, StringComparison.Ordinal);
        Assert.Contains("Binding DetailFileName", template, StringComparison.Ordinal);
        Assert.Contains("Binding DamagedSentence", template, StringComparison.Ordinal);
        Assert.Contains("Binding DamagedDetail", template, StringComparison.Ordinal);

        // Collapsed by default, and shown only by a Setter added to the EXISTING
        // Trigger Property="IsSelected" - not a new trigger, and not a Binding-driven Visibility
        // on the element itself (which would sidestep the "no new trigger" constraint the other
        // way, by not using a trigger at all).
        var expansionIndex = template.IndexOf("x:Name=\"ExpansionRow\"", StringComparison.Ordinal);
        var expansionTag = template[expansionIndex..template.IndexOf('>', expansionIndex)];
        Assert.Contains("Visibility=\"Collapsed\"", expansionTag, StringComparison.Ordinal);

        var isSelectedTriggerIndex = template.IndexOf(
            "<Trigger Property=\"IsSelected\" Value=\"True\">", StringComparison.Ordinal);
        Assert.True(isSelectedTriggerIndex >= 0, "The plain IsSelected trigger is gone or reworded.");
        var isSelectedTriggerEnd = template.IndexOf("</Trigger>", isSelectedTriggerIndex, StringComparison.Ordinal);
        var isSelectedTrigger = template[isSelectedTriggerIndex..isSelectedTriggerEnd];
        Assert.Contains(
            "TargetName=\"ExpansionRow\" Property=\"Visibility\" Value=\"Visible\"",
            isSelectedTrigger, StringComparison.Ordinal);
    }

    // 10b fix: WlRowTemplate's trigger ORDER took three review rounds to get right (see the
    // selected+high-contrast tests above) and the fix brief is explicit that restoring native
    // list selection must not disturb it. This does not re-check every trigger's position (the
    // two tests above already do that in full) - it only proves no trigger was inserted ahead of
    // the last one, which is the shape a careless "add a new IsSelected trigger for the
    // expansion" fix would have taken instead of reusing the existing one.
    [Fact]
    public void The_selected_plus_high_contrast_trigger_is_still_the_last_trigger_declared()
    {
        var template = RowStyles();

        var triggersSectionIndex = template.IndexOf("<ControlTemplate.Triggers>", StringComparison.Ordinal);
        var triggersSectionEnd = template.IndexOf("</ControlTemplate.Triggers>", StringComparison.Ordinal);
        Assert.True(triggersSectionIndex >= 0 && triggersSectionEnd >= 0,
            "ControlTemplate.Triggers section is gone or renamed.");

        var lastMultiTriggerStart = template.LastIndexOf(
            "<MultiDataTrigger>", triggersSectionEnd, StringComparison.Ordinal);
        Assert.True(lastMultiTriggerStart > triggersSectionIndex);

        var lastTrigger = template[lastMultiTriggerStart..triggersSectionEnd];
        Assert.Contains("Path=IsSelected", lastTrigger, StringComparison.Ordinal);
        Assert.Contains("IsHighContrast", lastTrigger, StringComparison.Ordinal);
        Assert.Contains("WlAccent", lastTrigger, StringComparison.Ordinal);
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
