// System.IO is NOT in the implicit-usings set for a UseWPF project - see ThemeTests.cs's own
// comment on this.
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;

namespace WaveLinkBackup.App.Tests;

/// <summary>
/// Source-text guards for Task 10b's half of the window, in the same style RowTemplateTests uses
/// for 10a's: reading the compiled files directly rather than walking a live visual tree, because
/// the failure mode being guarded against is someone editing this XAML or this code-behind, and
/// reading the source catches that directly. See RowTemplateTests' own comment for the fuller
/// argument against a template-walk here.
/// </summary>
public sealed class MainWindowTemplateTests
{
    private static readonly string SourceRoot = Assembly.GetExecutingAssembly()
        .GetCustomAttributes<AssemblyMetadataAttribute>()
        .Single(a => a.Key == "AppSourceRoot").Value!;

    private static string MainWindowXaml() =>
        File.ReadAllText(Path.Combine(SourceRoot, "Views", "MainWindow.xaml"));

    private static string MainWindowCodeBehind() =>
        File.ReadAllText(Path.Combine(SourceRoot, "Views", "MainWindow.xaml.cs"));

    private static string AppXaml() =>
        File.ReadAllText(Path.Combine(SourceRoot, "App.xaml"));

    // 11 pins the caption row unpainted so Mica shows through it; Task 4 built it that way and
    // nothing in 10b's own edits should have added a Background to it.
    [Fact]
    public void The_caption_row_stays_unpainted_so_mica_shows_through()
    {
        var match = Regex.Match(
            MainWindowXaml(),
            "<Border x:Name=\"CaptionBar\"[^>]*>", RegexOptions.Singleline);

        Assert.True(match.Success, "CaptionBar is gone or renamed.");
        Assert.DoesNotContain("Background=", match.Value, StringComparison.Ordinal);
    }

    // Plan 3 finding A: AllowsTransparency="True" makes the window layered, which makes DWM
    // silently ignore the Mica backdrop - the call still succeeds, so nothing short of reading
    // the XAML would ever catch a regression here.
    [Fact]
    public void The_window_stays_transparent_and_never_becomes_layered()
    {
        var xaml = MainWindowXaml();

        // The comment right below <Window ...> explains why AllowsTransparency stays off, and
        // quotes the literal attribute as part of that explanation - so comments are stripped
        // before the check, or that quote alone would fail this test for saying the right thing.
        var withoutComments = Regex.Replace(xaml, "<!--.*?-->", string.Empty, RegexOptions.Singleline);

        Assert.Contains("Background=\"Transparent\"", withoutComments, StringComparison.Ordinal);
        Assert.DoesNotContain("AllowsTransparency=\"", withoutComments, StringComparison.Ordinal);
    }

    // README §Screen 1 / 11: the header and every row share one grid, or the columns cannot
    // possibly line up. Grid.IsSharedSizeScope has to sit on an ancestor common to BOTH - see
    // MainWindow.xaml's own comment on why that is the root Grid rather than the ListBox.
    [Fact]
    public void The_root_grid_is_a_shared_size_scope()
    {
        Assert.Contains("Grid.IsSharedSizeScope=\"True\"", MainWindowXaml(), StringComparison.Ordinal);
    }

    // The header's own columns (WlColumnHeaderRowTemplate, ControlStyles.xaml - reused three
    // times per MainWindow.xaml's own comment, so it lives there rather than in MainWindow.xaml
    // itself) and the row template's (RowStyles.xaml) must use the identical SharedSizeGroup
    // names, or the shared-size scope shares nothing.
    //
    // FIVE names, not six. WlColName was removed from both files on purpose: WPF measures a
    // starred column that names a shared size group as if it were Auto, so naming NAME pinned the
    // whole block to its 984px minimum and left ~156px empty to the right of every row. The five
    // fixed columns still share; NAME is starred and lines up on its own.
    [Theory]
    [InlineData("WlColTaken")]
    [InlineData("WlColWhy")]
    [InlineData("WlColInputs")]
    [InlineData("WlColContents")]
    [InlineData("WlColOverflow")]
    public void The_column_header_uses_the_same_shared_size_group_names_as_the_row(string group)
    {
        var controlStyles = File.ReadAllText(Path.Combine(SourceRoot, "Views", "ControlStyles.xaml"));

        Assert.Contains($"SharedSizeGroup=\"{group}\"", controlStyles, StringComparison.Ordinal);
    }

    // The regression this replaced a sixth InlineData with. A shared starred column is silently
    // demoted to Auto, and nothing about that shows up in a source-text check for a group NAME -
    // only in the width the column ends up with. Both files, because the header and the row have
    // to agree.
    [Theory]
    [InlineData("ControlStyles.xaml")]
    [InlineData("RowStyles.xaml")]
    public void The_name_column_is_starred_and_never_in_a_shared_size_group(string file)
    {
        var xaml = File.ReadAllText(Path.Combine(SourceRoot, "Views", file));
        var withoutComments = Regex.Replace(xaml, "<!--.*?-->", string.Empty, RegexOptions.Singleline);

        Assert.DoesNotContain("SharedSizeGroup=\"WlColName\"", withoutComments, StringComparison.Ordinal);
        Assert.Contains("<ColumnDefinition Width=\"*\" MinWidth=\"220\" />", withoutComments, StringComparison.Ordinal);
    }

    // The five fixed columns carry the design's width PLUS the 20px gap, because the gap is a
    // right Margin on each cell's content rather than a seventh column. Dropping back to the bare
    // 120/124/300/200 would leave every cell 20px narrower than the design draws it - most
    // visibly the five-slot INPUTS strip, which is the row's whole information design.
    //
    // CONTENTS is the one exception, at 248 rather than 200 + 20. Three tier badges at the
    // design's own type role and padding measure 224.2px, so 200 clipped the third one mid-word;
    // the design's own render draws them at about 224 as well. The measurement behind that number
    // is RowTemplateTests.The_three_tier_badges_fit_inside_the_contents_column, which reads the
    // rendered width rather than trusting this arithmetic.
    [Theory]
    [InlineData("ControlStyles.xaml")]
    [InlineData("RowStyles.xaml")]
    public void The_fixed_columns_carry_the_twenty_pixel_gap(string file)
    {
        var xaml = File.ReadAllText(Path.Combine(SourceRoot, "Views", file));

        foreach (var (width, group) in new[]
                 {
                     (140, "WlColTaken"), (144, "WlColWhy"),
                     (320, "WlColInputs"), (248, "WlColContents"),
                 })
        {
            Assert.Contains(
                $"Width=\"{width}\" SharedSizeGroup=\"{group}\"", xaml, StringComparison.Ordinal);
        }
    }

    // The header sits outside GroupsHost and the rows sit inside it, so the scroll bar's 10px comes
    // off the rows' available width and not the header's. Both resolve NAME's star independently, so
    // without this gutter the header drifts 10px right of the cells it heads the moment the list is
    // long enough to scroll. GroupsHost owns its scrolling (Option A), so the gutter binds to its own
    // ScrollViewer's ComputedVerticalScrollBarVisibility.
    [Fact]
    public void The_column_header_reserves_the_lists_scroll_bar_gutter()
    {
        var xaml = MainWindowXaml();

        Assert.Contains("ElementName=\"GroupsHost\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Padding\" Value=\"20,11,30,9\"", xaml, StringComparison.Ordinal);
    }

    // 10-decisions section 6: "Enter fires the primary button - except Delete and Restore, where
    // focus starts on Cancel and the destructive button must be reached deliberately (Tab or
    // click)." IsDefault gives a button Enter from ANYWHERE in the dialog, focus on Cancel
    // included - which made Enter, on a dialog that opens focused on Cancel, the most destructive
    // key in the app. Empty trash is in here for the same reason: 08 gives it the delete dialog's
    // shape and its focus rule, and it is irreversible on the volumes it asks about at all.
    [Theory]
    [InlineData("DeleteDialog.xaml", "DeleteButton")]
    [InlineData("RestoreDialog.xaml", "RestoreButton")]
    [InlineData("EmptyTrashDialog.xaml", "ConfirmButton")]
    public void The_destructive_button_is_never_the_default_button(string file, string button)
    {
        var xaml = File.ReadAllText(Path.Combine(SourceRoot, "Views", file));
        var match = Regex.Match(xaml, $"<Button x:Name=\"{button}\"[^>]*>", RegexOptions.Singleline);

        Assert.True(match.Success, $"{button} is gone or has been renamed in {file}.");
        Assert.DoesNotContain("IsDefault=\"True\"", match.Value, StringComparison.Ordinal);
    }

    // ...and Enter still has to work once the user HAS tabbed onto it, or the rule above would
    // just be a keyboard dead end on the confirm button of three dialogs.
    [Theory]
    [InlineData("DeleteDialog.xaml.cs", "DeleteButton")]
    [InlineData("RestoreDialog.xaml.cs", "RestoreButton")]
    [InlineData("EmptyTrashDialog.xaml.cs", "ConfirmButton")]
    public void The_destructive_button_handles_enter_itself(string file, string button)
    {
        var code = File.ReadAllText(Path.Combine(SourceRoot, "Views", file));

        Assert.Contains($"{button}.KeyDown +=", code, StringComparison.Ordinal);
    }

    // Task 12's guards look RowStyles.xaml's keys up by name - none of that matters if the
    // dictionary is never merged, which would fail silently (a StaticResource that happens not to
    // be hit yet) rather than loudly.
    [Fact]
    public void App_xaml_merges_the_row_dictionary()
    {
        Assert.Contains("Views/RowStyles.xaml", AppXaml(), StringComparison.Ordinal);
    }

    // The brief's own trap: HealthProbe reports on its own thread, and SnapshotListViewModel.Marshal
    // defaults to running inline. Setting Marshal AFTER the first RefreshAsync has already been
    // wired means the very first verdict can race a background PropertyChanged before Marshal is
    // in place. This test proves the ORDER in the source text, not just that both lines exist.
    [Fact]
    public void Marshal_is_set_before_the_first_refresh_is_wired()
    {
        var code = MainWindowCodeBehind();

        var marshalSet = code.IndexOf("shell.List.Marshal = action => Dispatcher.Invoke(action);", StringComparison.Ordinal);
        var refreshWired = code.IndexOf("await shell.List.RefreshAsync()", StringComparison.Ordinal);

        Assert.True(marshalSet >= 0, "The Marshal assignment is gone or reworded.");
        Assert.True(refreshWired >= 0, "The RefreshAsync wiring is gone or reworded.");
        Assert.True(marshalSet < refreshWired,
            "Marshal must be set before RefreshAsync is ever reachable, or the first verdict " +
            "can land on the probe's own thread and throw.");
    }

    // Every DynamicResource/StaticResource brush this window and its controls use must be one of
    // the 23 theme keys - the same guard RowTemplateTests runs over RowStyles.xaml, applied to
    // the other two files 10b actually touched. A brush bound to the wrong (but legal) key is a
    // colour mistake the literal-colour guard cannot see.
    [Theory]
    [InlineData("MainWindow.xaml")]
    [InlineData("ControlStyles.xaml")]
    public void Every_brush_this_file_uses_is_a_theme_key(string file)
    {
        var known = Theming.ThemeManager.BrushKeys.ToHashSet(StringComparer.Ordinal);
        var text = File.ReadAllText(Path.Combine(SourceRoot, "Views", file));

        var used = Regex.Matches(text, @"(?:Dynamic|Static)Resource\s+(Wl[A-Za-z]+)")
            .Select(m => m.Groups[1].Value)
            .Where(name => name.StartsWith("Wl", StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal);

        var unknown = used
            .Where(name => !known.Contains(name))
            // Styles, templates and geometries are keys too, and are not brushes.
            .Where(name => !name.EndsWith("Text", StringComparison.Ordinal)
                        && !name.EndsWith("Button", StringComparison.Ordinal)
                        && !name.EndsWith("Box", StringComparison.Ordinal)
                        && !name.EndsWith("Geometry", StringComparison.Ordinal)
                        && !name.EndsWith("Font", StringComparison.Ordinal)
                        && !name.EndsWith("Template", StringComparison.Ordinal)
                        && !name.EndsWith("Visibility", StringComparison.Ordinal)
                        // WlScrollBarThumb is a Style; WlDialogShadow is a DropShadowEffect.
                        // Neither is a brush, and both are referenced the same way one would be.
                        && !name.EndsWith("Thumb", StringComparison.Ordinal)
                        && !name.EndsWith("Shadow", StringComparison.Ordinal)
                        // WlStandardEase is an easing function (Motion.xaml).
                        && !name.EndsWith("Ease", StringComparison.Ordinal)
                        // Value converters are keys too - WlFractionWidthConverter drives the
                        // backing-up strip's determinate bar.
                        && !name.EndsWith("Converter", StringComparison.Ordinal)
                        && name != "WlCaptionButton" && name != "WlCaptionCloseButton"
                        && name != "WlShieldCheckGeometry" && name != "WlFocusVisual")
            .ToArray();

        Assert.True(unknown.Length == 0, $"Not theme brushes: {string.Join(", ", unknown)}");
    }

    /// <summary>
    /// A markup extension is evaluated in ATTRIBUTE syntax only. Written as a property element -
    /// <c>&lt;Run.Text&gt;{Binding X}&lt;/Run.Text&gt;</c> - the braces are just characters, the
    /// binding never happens, and the expression prints itself to the user. It builds and it
    /// parses, so nothing but a scan or a pair of eyes catches it.
    ///
    /// It reached a shipped screen once: the settings dialog showed
    /// "{Binding WhatGoesIn.NoteOneLead}" under WHAT GOES IN A BACKUP. SettingsDialogViewTests
    /// catches it at render time for that one window; this catches it in every XAML file, including
    /// the ones with no view test.
    /// </summary>
    [Fact]
    public void No_xaml_puts_a_markup_extension_in_property_element_syntax()
    {
        var offenders = new List<string>();

        foreach (var file in Directory.EnumerateFiles(SourceRoot, "*.xaml", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")) continue;
            if (file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")) continue;

            var withoutComments = Regex.Replace(
                File.ReadAllText(file), "<!--.*?-->", string.Empty, RegexOptions.Singleline);

            // A '>' closing some property-element start tag, immediately followed by a brace that
            // opens a markup extension. Whitespace between them is still the same mistake.
            foreach (Match match in Regex.Matches(withoutComments, @"<\w[\w.]*\.\w+>\s*\{(Binding|DynamicResource|StaticResource)"))
            {
                offenders.Add($"  {Path.GetFileName(file)}: {match.Value}");
            }
        }

        Assert.True(offenders.Count == 0,
            $"A markup extension only evaluates in attribute syntax. In a property element it is " +
            $"literal text and the user reads it. Found:" +
            $"{Environment.NewLine}{string.Join(Environment.NewLine, offenders)}");
    }

    /// <summary>
    /// Run.Text is registered BindsTwoWayByDefault - a Run is editable inside a RichTextBox - so a
    /// plain binding to a computed, get-only view-model property throws "A TwoWay or
    /// OneWayToSource binding cannot work on the read-only property". TextBlock.Text is one-way,
    /// so the same expression is fine there and fails only on a Run.
    ///
    /// The failure mode is what makes this worth a guard: the throw lands on the WPF thread while
    /// the window is being built, so it presents as the dialog never opening - not as a message.
    /// It cost a bisect to find once.
    /// </summary>
    [Fact]
    public void Every_Run_that_binds_its_text_asks_for_a_one_way_binding()
    {
        var offenders = new List<string>();

        foreach (var file in Directory.EnumerateFiles(SourceRoot, "*.xaml", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")) continue;
            if (file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")) continue;

            var withoutComments = Regex.Replace(
                File.ReadAllText(file), "<!--.*?-->", string.Empty, RegexOptions.Singleline);

            foreach (Match run in Regex.Matches(withoutComments, @"<Run\b[^>]*?/>", RegexOptions.Singleline))
            {
                var text = Regex.Match(run.Value, @"Text=""\{Binding[^""]*""");
                if (!text.Success) continue;
                if (text.Value.Contains("Mode=OneWay", StringComparison.Ordinal)) continue;

                offenders.Add($"  {Path.GetFileName(file)}: {Regex.Replace(text.Value, @"\s+", " ")}");
            }
        }

        Assert.True(offenders.Count == 0,
            $"Run.Text binds TwoWay by default and every model property behind one of these is " +
            $"read-only. Add Mode=OneWay. Found:" +
            $"{Environment.NewLine}{string.Join(Environment.NewLine, offenders)}");
    }

    /// <summary>
    /// ONE ListBox over the grouped view, not one per date.
    ///
    /// The per-group shape is what gave native row selection at all, and it made the list several
    /// Selectors — so a selection could not span them and arrow keys stopped at every date
    /// boundary (technical-debt.md §4.14). A single Selector is single-select and continuous by
    /// construction.
    /// </summary>
    [Fact]
    public void The_row_list_is_one_ListBox_over_the_grouped_view()
    {
        var xaml = MainWindowXaml();

        Assert.Contains("ItemsSource=\"{Binding List.View}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("ItemContainerStyle=\"{StaticResource WlRowTemplate}\"", xaml, StringComparison.Ordinal);

        // Exactly one, or the restructure did not happen.
        Assert.Single(Regex.Matches(xaml, "<ListBox "));
    }

    /// <summary>
    /// The date header comes from a GroupStyle, not from inside the item template.
    ///
    /// A group container is NOT a ListBoxItem, which is what keeps ↓ from stopping on a date on
    /// its way between two backups. A header rendered inside the item template would be selectable
    /// and would put the boundary back.
    /// </summary>
    [Fact]
    public void The_date_header_is_a_group_header_and_not_a_selectable_row()
    {
        var xaml = MainWindowXaml();

        Assert.Contains("<ListBox.GroupStyle>", xaml, StringComparison.Ordinal);
        Assert.Contains("<GroupStyle.HeaderTemplate>", xaml, StringComparison.Ordinal);
    }

    /// <summary>
    /// SelectedItem is an ordinary TwoWay binding again — the inverse of what this file asserted
    /// while the list was several Selectors.
    ///
    /// It used to pin the binding's ABSENCE, because a shared TwoWay SelectedItem across several
    /// Selectors is actively harmful: one handed an item its own Items collection does not contain
    /// declines the write and writes its own row back, and two of them ping-pong. With one Selector
    /// the binding is simply correct, and its presence is what carries a click to the view model.
    /// </summary>
    [Fact]
    public void The_list_binds_its_selection_two_way()
    {
        var withoutComments = Regex.Replace(
            MainWindowXaml(), "<!--.*?-->", string.Empty, RegexOptions.Singleline);

        Assert.Contains(
            "SelectedItem=\"{Binding List.Selected, Mode=TwoWay}\"",
            withoutComments,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// GroupSelection is GONE, not merely unused. It existed only to carry a rule the structure
    /// now enforces, and a lingering copy would be a second answer to a question with one.
    /// </summary>
    [Fact]
    public void The_group_selection_workaround_is_gone()
    {
        var code = MainWindowCodeBehind();

        Assert.DoesNotContain("GroupSelection", code, StringComparison.Ordinal);
        Assert.DoesNotContain("Selector.SelectionChangedEvent", code, StringComparison.Ordinal);
        Assert.False(
            File.Exists(Path.Combine(SourceRoot, "Views", "GroupSelection.cs")),
            "GroupSelection.cs is still there. The structure it worked around is gone.");
    }

    /// <summary>
    /// Home and End have no code-behind any more either. They were hand-handled because neither
    /// could reach past its own group's Selector; one Selector gives both for free, and a
    /// hand-rolled version would now be a second implementation racing WPF's.
    /// </summary>
    [Fact]
    public void Home_and_End_are_left_to_the_selector()
    {
        var code = MainWindowCodeBehind();

        Assert.DoesNotContain("Key.Home", code, StringComparison.Ordinal);
        Assert.DoesNotContain("Key.End", code, StringComparison.Ordinal);
    }

    // "No border, no background, no focus rectangle of its own - the row template owns the whole
    // visual." A regression here would show a second, ListBox-drawn selection/focus chrome
    // wrapped around WlRowTemplate's own.
    [Fact]
    public void The_row_list_carries_none_of_its_own_default_chrome()
    {
        var listBoxTag = Regex.Match(
            MainWindowXaml(), "<ListBox x:Name=\"GroupsHost\".*?>", RegexOptions.Singleline).Value;

        Assert.Contains("BorderThickness=\"0\"", listBoxTag, StringComparison.Ordinal);
        Assert.Contains("Background=\"Transparent\"", listBoxTag, StringComparison.Ordinal);
        Assert.Contains("FocusVisualStyle=\"{x:Null}\"", listBoxTag, StringComparison.Ordinal);
    }

    // Per-row virtualization. One VirtualizingStackPanel now, not two: there is one ListBox rather
    // than a group-level ItemsControl wrapping a ListBox per date.
    //
    // CanContentScroll must be False (pixel scrolling), not True. The rows are grouped by date, and
    // with content scrolling (True) WPF treats each group as one scroll unit - the inner
    // ScrollViewer's extent collapses to ~1px and nothing scrolls (dotnet/wpf#8687,
    // MaterialDesignInXAML#1220). Pixel scrolling measures the real pixel height and scrolls
    // correctly with or without grouping; the VirtualizingStackPanel still virtualizes because it
    // gets its viewport through IViewportProvider even in pixel mode.
    [Fact]
    public void The_row_list_virtualizes_with_pixel_scrolling_for_grouped_rows()
    {
        var xaml = MainWindowXaml();

        Assert.Single(Regex.Matches(xaml, "<VirtualizingStackPanel />"));
        Assert.Contains("ScrollViewer.CanContentScroll=\"False\"", xaml, StringComparison.Ordinal);
    }

    // The old hand-placed structure (task-10b-report.md's own documented deviation) is gone, not
    // just superseded - a leftover bare ListBoxItem or the manual click handler would mean the fix
    // was layered on top of the old approach rather than replacing it.
    [Fact]
    public void The_hand_placed_row_and_its_click_handler_are_gone()
    {
        Assert.DoesNotContain("<ListBoxItem", MainWindowXaml(), StringComparison.Ordinal);
        Assert.DoesNotContain("PreviewMouseLeftButtonDown", MainWindowXaml(), StringComparison.Ordinal);
        Assert.DoesNotContain("Row_PreviewMouseLeftButtonDown", MainWindowCodeBehind(), StringComparison.Ordinal);
    }

    // The selected-row expansion moved into WlRowTemplate (RowStyles.xaml) - it must not still be
    // sitting in MainWindow.xaml as well, which would mean it renders twice or the move never
    // actually happened.
    [Fact]
    public void The_selected_row_expansion_no_longer_lives_in_MainWindow_xaml()
    {
        var xaml = MainWindowXaml();

        Assert.DoesNotContain("DamagedSentence", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("DamagedDetail", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("DetailFileName", xaml, StringComparison.Ordinal);
    }

    // Fix 1: 07-search.md's footer strip ("SHOWING 3 OF 14 · 11 HIDDEN BY THE SEARCH", right a
    // ghost button "Show all 14") was computed on SnapshotListViewModel (SearchFooter,
    // ShowAllLabel) and unit-tested there, but Task 10 never built a template that consumed it -
    // it rendered nowhere. This proves the strip actually exists in the window and is wired to
    // both properties and to ClearSearch, rather than trusting the view-model tests alone to mean
    // it is on screen.
    [Fact]
    public void The_search_footer_strip_shows_what_the_search_hides_and_offers_to_clear_it()
    {
        var xaml = MainWindowXaml();

        var footer = Regex.Match(
            xaml, "<Border x:Name=\"SearchFooterBorder\".*?</Border>", RegexOptions.Singleline).Value;
        Assert.True(footer.Length > 0, "SearchFooterBorder is gone or renamed.");

        Assert.Contains("Binding List.SearchFooter", footer, StringComparison.Ordinal);
        Assert.Contains("Binding List.ShowAllLabel", footer, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ShowAllButton\"", footer, StringComparison.Ordinal);

        // Collapsed only when there is nothing to show, not merely when the list is not Loaded -
        // ListLoadedRegion's own trigger already handles the Loaded/not-Loaded split, so this one
        // gates on SearchFooter itself being null.
        Assert.Contains("Binding List.SearchFooter}\" Value=\"{x:Null}\"", footer, StringComparison.Ordinal);

        Assert.Contains(
            "ShowAllButton.Click += (_, _) => shell.List.ClearSearch();",
            MainWindowCodeBehind(), StringComparison.Ordinal);
    }

    // Fix 5, still true and now for a smaller reason: the panel defaults to ScrollUnit="Item",
    // and an item used to be an entire date group - one wheel notch jumped a whole day's worth of
    // rows. An item is one ROW now, so the default would merely be coarse rather than wild, but
    // pixel scrolling is what a user expects from a list like this either way.
    [Fact]
    public void The_row_list_scrolls_by_pixel()
    {
        var listTag = Regex.Match(
            MainWindowXaml(), "<ListBox x:Name=\"GroupsHost\".*?>", RegexOptions.Singleline).Value;

        Assert.Contains("VirtualizingPanel.ScrollUnit=\"Pixel\"", listTag, StringComparison.Ordinal);
    }

    // The four ListState-driven regions the brief's Step 4 asks for. A source-text check rather
    // than a rendered-visibility check (that is MainWindowListStateTests' job) - this one just
    // proves each state is actually WIRED to something, not left out by a typo in the Value.
    [Theory]
    [InlineData("Loaded")]
    [InlineData("NoResults")]
    [InlineData("Empty")]
    [InlineData("FolderMissing")]
    public void Every_list_state_drives_at_least_one_visibility_trigger(string state)
    {
        Assert.Contains(
            $"Binding List.State}}\" Value=\"{state}\"", MainWindowXaml(), StringComparison.Ordinal);
    }

    // ------------------------ 03 §3's rejected-restore recovery (technical-debt.md §4.21 item 1)

    /// <summary>
    /// The exact §4.20 lesson: <c>AcknowledgeReject</c> was implemented, correct and tested, and
    /// nothing in the app called it — so the bar was permanent once shown. This asserts the
    /// button EXISTS and that its Click reaches the model, because either half alone was the bug.
    /// </summary>
    [Fact]
    public void The_rejected_strips_primary_action_exists_and_is_wired()
    {
        var xaml = MainWindowXaml();

        Assert.Contains("x:Name=\"StripPrimaryActionButton\"", xaml, StringComparison.Ordinal);
        Assert.Contains(
            "Content=\"{Binding Strip.PrimaryActionLabel}\"", xaml, StringComparison.Ordinal);
        Assert.Contains(
            "StripPrimaryActionButton.Click", MainWindowCodeBehind(), StringComparison.Ordinal);
    }

    [Fact]
    public void Something_in_the_app_actually_calls_AcknowledgeReject()
    {
        Assert.Contains("AcknowledgeReject()", MainWindowCodeBehind(), StringComparison.Ordinal);
    }

    /// <summary>
    /// 03 §3: "the 'Before restore' row renders selected immediately below, so the button and the
    /// row are visibly the same object."
    /// </summary>
    [Fact]
    public void A_reject_selects_the_row_its_primary_button_names()
    {
        Assert.Contains(
            "shell.List.Select(recovery)", MainWindowCodeBehind(), StringComparison.Ordinal);
    }

    /// <summary>
    /// The window must elevate only when the plan says the destinations refuse a write. It used
    /// to elevate whenever the opt-in was on, which prompted every user on every machine — this
    /// pins the condition rather than the call (technical-debt.md §7.5).
    /// </summary>
    [Fact]
    public void A_tier_four_restore_elevates_only_when_the_destinations_need_it()
    {
        var code = MainWindowCodeBehind();

        Assert.Contains("model.PluginFiles!.NeedsElevation", code, StringComparison.Ordinal);

        // And the unelevated path really does carry the opt-in through, or switching it on would
        // silently restore nothing.
        Assert.Contains("PluginBinaries: wantsPlugins", code, StringComparison.Ordinal);
    }
}
