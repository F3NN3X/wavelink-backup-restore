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

    // Fix 3: the selected+high-contrast trigger inverted RowSurface, OverflowText, MetaText and
    // VerdictGlyph to HighlightText, but not TakenTimeText/TakenDateText - both named elements in
    // the SAME ControlTemplate scope, unlike the nested name segments/pills/badges that stay out
    // of reach. Without this, the backup's own time and date stayed WindowText on a Highlight
    // fill - unreadable on at least one of the two illustrative HC schemes 11-high-contrast.md
    // gives (HC White: WindowText black on Highlight dark purple).
    [Fact]
    public void A_selected_rows_taken_time_and_date_invert_to_highlight_text_in_high_contrast()
    {
        var template = RowStyles();

        var triggersSectionIndex = template.IndexOf("<ControlTemplate.Triggers>", StringComparison.Ordinal);
        Assert.True(triggersSectionIndex >= 0, "ControlTemplate.Triggers section is gone or renamed.");

        var blocks = Regex.Matches(template[triggersSectionIndex..], "<MultiDataTrigger>.*?</MultiDataTrigger>", RegexOptions.Singleline)
            .Select(m => m.Value)
            .Where(b => b.Contains("Path=IsSelected", StringComparison.Ordinal)
                     && b.Contains("TargetName=\"RowSurface\" Property=\"Background\"", StringComparison.Ordinal))
            .ToArray();

        Assert.True(blocks.Length == 1, $"Expected exactly one selected+high-contrast RowSurface trigger, found {blocks.Length}.");
        var block = blocks[0];

        Assert.Contains(
            "TargetName=\"TakenTimeText\" Property=\"Foreground\" Value=\"{DynamicResource WlAccentInk}\"",
            block, StringComparison.Ordinal);
        Assert.Contains(
            "TargetName=\"TakenDateText\" Property=\"Foreground\" Value=\"{DynamicResource WlAccentInk}\"",
            block, StringComparison.Ordinal);
    }

    // Fix 3: 11-high-contrast.md's per-cell table puts the overflow "···" glyph at WindowText in
    // high contrast (every other normal theme keeps it WlMuted) - it was left on its WlMuted
    // default with no high-contrast override, under-stating a column every other cell in the
    // scheme renders at full strength.
    [Fact]
    public void The_overflow_glyph_is_window_text_in_high_contrast()
    {
        var template = RowStyles();

        // Located by its own known Setter (RowSurface's BorderThickness="0") rather than the
        // DataTrigger's opening tag alone - "DataContext.IsHighContrast" also opens WlSlotMissing's
        // and WlTierAbsent's own high-contrast triggers earlier in the file, and a non-greedy match
        // from the file's FIRST such opening tag would stop at one of those instead.
        var anchorIndex = template.IndexOf(
            "TargetName=\"RowSurface\" Property=\"BorderThickness\" Value=\"0\"", StringComparison.Ordinal);
        Assert.True(anchorIndex >= 0, "The plain every-row high-contrast trigger is gone or renamed.");

        var blockStart = template.LastIndexOf("<DataTrigger", anchorIndex, StringComparison.Ordinal);
        var blockEnd = template.IndexOf("</DataTrigger>", anchorIndex, StringComparison.Ordinal);
        var plainHcTrigger = template[blockStart..blockEnd];

        Assert.Contains(
            "TargetName=\"OverflowText\" Property=\"Foreground\" Value=\"{DynamicResource WlText}\"",
            plainHcTrigger, StringComparison.Ordinal);
    }

    // Fix 4: WlHover resolves to Transparent in high contrast (HighContrast.xaml), so hovering a
    // row gave no feedback at all there. 11-high-contrast.md:32: "Hover = 1px HotTrack outline,
    // no fill."
    [Fact]
    public void Hovering_a_row_draws_a_hot_track_outline_in_high_contrast()
    {
        var template = RowStyles();

        var hoverTriggerIndex = template.IndexOf("IsMouseOver\" RelativeSource=", StringComparison.Ordinal);
        Assert.True(hoverTriggerIndex >= 0, "The row's high-contrast hover trigger is gone or renamed.");

        var blockStart = template.LastIndexOf("<MultiDataTrigger>", hoverTriggerIndex, StringComparison.Ordinal);
        var blockEnd = template.IndexOf("</MultiDataTrigger>", hoverTriggerIndex, StringComparison.Ordinal);
        var block = template[blockStart..blockEnd];

        Assert.Contains("IsHighContrast", block, StringComparison.Ordinal);
        Assert.Contains(
            "TargetName=\"RowSurface\" Property=\"BorderThickness\" Value=\"1\"",
            block, StringComparison.Ordinal);
        Assert.Contains(
            "TargetName=\"RowSurface\" Property=\"BorderBrush\" Value=\"{DynamicResource WlHotTrack}\"",
            block, StringComparison.Ordinal);

        // Must sit between the plain every-row high-contrast trigger (which it overrides
        // BorderThickness="0" back to "1" from) and the load-bearing selected+high-contrast
        // trigger (which must stay the LAST trigger that paints RowSurface - see that trigger's
        // own comment and the test above pinning it).
        var plainHcBorderIndex = template.IndexOf(
            "TargetName=\"RowSurface\" Property=\"BorderThickness\" Value=\"0\"", StringComparison.Ordinal);
        var selectedHcIndex = template.IndexOf(
            "TargetName=\"RowSurface\" Property=\"Background\" Value=\"{DynamicResource WlAccent}\"", StringComparison.Ordinal);
        Assert.True(plainHcBorderIndex >= 0 && selectedHcIndex >= 0,
            "One of the two ordering anchors (plain high-contrast trigger, selected+high-contrast trigger) is gone or renamed.");
        Assert.True(blockStart > plainHcBorderIndex && blockStart < selectedHcIndex,
            "The row's high-contrast hover trigger must sit after the plain every-row high-contrast " +
            "trigger and before the selected+high-contrast trigger.");
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
    // two tests above already do that in full) - it only proves nothing that ALSO paints
    // RowSurface was inserted ahead of, or after, the load-bearing selected+high-contrast trigger.
    //
    // An earlier version of this test picked out the template's literal LAST <MultiDataTrigger>
    // and asserted it contained "Path=IsSelected", "IsHighContrast" and "WlAccent". That is NOT
    // the load-bearing trigger: the file's actual last MultiDataTrigger is one of the three
    // selected+HC+Health VerdictGlyph-colour triggers below it (declared after it on purpose - see
    // that trigger's own comment), and it satisfies all three substrings by accident (its own
    // conditions include Path=IsSelected and IsHighContrast, and its Setter value WlAccentInk
    // contains the substring "WlAccent"). That version would have stayed green even if the real
    // RowSurface-filling trigger were moved back before IsSuspect/IsDamaged - precisely the
    // regression the big comment above it (~line 660) documents as already having been tried and
    // found wrong. This version picks the trigger out by its actual Setter (TargetName="RowSurface"
    // Property="Background"), which only the load-bearing trigger has.
    [Fact]
    public void The_selected_plus_high_contrast_trigger_is_the_last_one_that_paints_RowSurface()
    {
        var template = RowStyles();

        var triggersSectionIndex = template.IndexOf("<ControlTemplate.Triggers>", StringComparison.Ordinal);
        var triggersSectionEnd = template.IndexOf("</ControlTemplate.Triggers>", StringComparison.Ordinal);
        Assert.True(triggersSectionIndex >= 0 && triggersSectionEnd >= 0,
            "ControlTemplate.Triggers section is gone or renamed.");

        var triggersSection = template[triggersSectionIndex..triggersSectionEnd];

        var blocks = Regex.Matches(triggersSection, "<MultiDataTrigger>.*?</MultiDataTrigger>", RegexOptions.Singleline)
            .Select(m => m.Value)
            .Where(b => b.Contains("Path=IsSelected", StringComparison.Ordinal)
                     && b.Contains("IsHighContrast", StringComparison.Ordinal)
                     && b.Contains("TargetName=\"RowSurface\" Property=\"Background\"", StringComparison.Ordinal))
            .ToArray();

        Assert.True(blocks.Length == 1,
            $"Expected exactly one selected+high-contrast trigger that fills RowSurface, found {blocks.Length}.");

        var block = blocks[0];
        var blockStart = triggersSectionIndex + triggersSection.IndexOf(block, StringComparison.Ordinal);

        // Must come after every OTHER trigger that tints RowSurface - IsSuspect, IsDamaged, and
        // the plain every-row high-contrast trigger - or health/plain-HC would win back over
        // selection in high contrast.
        var isSuspectIndex = template.IndexOf("Binding IsSuspect", triggersSectionIndex, StringComparison.Ordinal);
        var isDamagedIndex = template.IndexOf("Binding IsDamaged", triggersSectionIndex, StringComparison.Ordinal);
        var plainHcBorderIndex = template.IndexOf(
            "TargetName=\"RowSurface\" Property=\"BorderThickness\" Value=\"0\"",
            triggersSectionIndex, StringComparison.Ordinal);

        Assert.True(isSuspectIndex >= 0, "IsSuspect health trigger is gone or renamed.");
        Assert.True(isDamagedIndex >= 0, "IsDamaged health trigger is gone or renamed.");
        Assert.True(plainHcBorderIndex >= 0, "The plain every-row high-contrast trigger is gone or renamed.");

        Assert.True(blockStart > isSuspectIndex && blockStart > isDamagedIndex && blockStart > plainHcBorderIndex,
            "The selected+high-contrast RowSurface trigger must be declared AFTER IsSuspect, " +
            "IsDamaged, and the plain every-row high-contrast trigger, so selection wins over " +
            "health in high contrast.");

        // And nothing declared AFTER it may also touch RowSurface - the three glyph-colour
        // triggers that follow it in the file all target VerdictGlyph instead, which is fine and
        // is what this asserts stays true.
        var rest = template[(blockStart + block.Length)..triggersSectionEnd];
        Assert.DoesNotContain("TargetName=\"RowSurface\"", rest, StringComparison.Ordinal);
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

    // Design section C makes five-always-five structural in the view model precisely so it
    // cannot become an accident of a template. This asserts the template agrees, on a row that
    // only has two inputs - the case where a template that skips empties would look fine.
    //
    // The brief's own printed snippet built the ItemsControl via a plain object initializer and
    // asserted items.Items.Count, which mirrors ItemsSource.Count unconditionally regardless of
    // ItemTemplate - checked empirically, it stayed green even with WlSlotTemplate's key renamed
    // to something that does not exist (the ResourceDictionary indexer returns null for a missing
    // key, which casts to a null ItemTemplate without throwing, and Items.Count never looks at
    // what a template did with the data). ItemContainerGenerator.ContainerFromIndex has the same
    // blind spot for a different reason: WPF allocates one container per data item independently
    // of whether ItemTemplate resolves to anything - also checked empirically, container count
    // stayed 5 with the same renamed key. Neither number depends on WlSlotTemplate actually being
    // the thing that ran.
    //
    // What DOES depend on it: WlSlotTemplate's own root element is a
    // <ContentControl Content="{Binding}"> (RowStyles.xaml) that a Style then hands one of
    // WlSlotNamed/WlSlotGeneric/WlSlotMissing as its ContentTemplate - so a real application of
    // WlSlotTemplate puts exactly one extra ContentControl into the visual tree per slot, nested
    // under the container ItemsControl.ItemContainerGenerator already produces. Counting those is
    // what proves WlSlotTemplate specifically, not just "a" template, rendered five times: a
    // renamed/missing key leaves ItemTemplate null, WPF falls back to each item's own ToString(),
    // and zero ContentControls appear - confirmed below, this is what "make the guard fail" showed.
    //
    // Getting even this far needed one correction to the printed snippet: a plain C# object
    // initializer never calls ISupportInitialize.BeginInit/EndInit (only the XAML parser does that
    // automatically), and without EndInit an ItemsControl's IsInitialized stays false and it never
    // resolves ANY default Style/Template, Measure/Arrange/UpdateLayout notwithstanding - confirmed
    // empirically: with the brief's exact object-initializer code, Template stayed null and the
    // generator status stayed NotStarted straight through a full layout pass. BeginInit/EndInit
    // (still no Show(), still no live PresentationSource - the same headless idiom
    // AppResourceOrderTests already uses for a ContentControl) is what makes the control resolve
    // its own default template and actually build containers at all.
    [Fact]
    public void The_slot_strip_renders_five_containers_for_a_two_input_row()
    {
        var rendered = Wpf.Run(() =>
        {
            var dictionary = new System.Windows.ResourceDictionary
            {
                Source = new Uri("pack://application:,,,/WaveLinkBackup;component/Views/RowStyles.xaml"),
            };

            var items = new System.Windows.Controls.ItemsControl();
            items.BeginInit();
            items.ItemsSource = ViewModels.InputSlots.Build(["Elgato Wave:3", "System"], peakInputCount: 5);
            items.ItemTemplate = (System.Windows.DataTemplate)dictionary["WlSlotTemplate"];
            items.EndInit();

            // A container per item only exists once the control has been through a layout pass.
            items.Measure(new System.Windows.Size(300, 40));
            items.Arrange(new System.Windows.Rect(0, 0, 300, 40));
            items.UpdateLayout();

            // Counts every ContentControl WlSlotTemplate's own root element puts into the visual
            // tree - one per slot, iff WlSlotTemplate is the thing that actually rendered each one.
            var slotContentControls = 0;
            void CountSlotRoots(System.Windows.DependencyObject node)
            {
                for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(node); i++)
                {
                    var child = System.Windows.Media.VisualTreeHelper.GetChild(node, i);
                    if (child is System.Windows.Controls.ContentControl) slotContentControls++;
                    CountSlotRoots(child);
                }
            }
            CountSlotRoots(items);

            return slotContentControls;
        });

        Assert.Equal(ViewModels.InputSlots.SlotCount, rendered);
    }
}
