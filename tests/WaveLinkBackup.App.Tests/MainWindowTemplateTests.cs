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

    // The header's own six columns (WlColumnHeaderRowTemplate, ControlStyles.xaml - reused three
    // times per MainWindow.xaml's own comment, so it lives there rather than in MainWindow.xaml
    // itself) and the row template's (RowStyles.xaml) six columns must use the identical
    // SharedSizeGroup names, or the shared-size scope shares nothing.
    [Theory]
    [InlineData("WlColName")]
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
                        && name != "WlCaptionButton" && name != "WlCaptionCloseButton"
                        && name != "WlShieldCheckGeometry" && name != "WlFocusVisual")
            .ToArray();

        Assert.True(unknown.Length == 0, $"Not theme brushes: {string.Join(", ", unknown)}");
    }

    // 10b fix: each date group's Rows is now hosted by a real ListBox (Selector), not a
    // hand-placed ListBoxItem per row in a plain ItemsControl - that is what gives arrow-key
    // selection movement and Selector.SelectedItem back. ItemContainerStyle is what makes
    // WlRowTemplate (RowStyles.xaml) generate the actual row containers.
    [Fact]
    public void The_row_list_is_a_real_ListBox_using_the_row_template_as_its_container_style()
    {
        var xaml = MainWindowXaml();

        Assert.Contains("<ListBox ItemsSource=\"{Binding Rows}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("ItemContainerStyle=\"{StaticResource WlRowTemplate}\"", xaml, StringComparison.Ordinal);
    }

    // The exact binding the fix brief specifies - reaching the shared ShellViewModel.List.Selected
    // from inside the group DataTemplate's own DataContext (a Row, then a DateGroup) via the
    // window's own DataContext, two-way so a click OR an arrow-key move writes back through it.
    [Fact]
    public void The_row_lists_SelectedItem_is_two_way_bound_to_the_shared_selection()
    {
        Assert.Contains(
            "SelectedItem=\"{Binding DataContext.List.Selected, "
                + "RelativeSource={RelativeSource AncestorType=Window}, Mode=TwoWay}\"",
            MainWindowXaml(), StringComparison.Ordinal);
    }

    // "No border, no background, no focus rectangle of its own - the row template owns the whole
    // visual." A regression here would show a second, ListBox-drawn selection/focus chrome
    // wrapped around WlRowTemplate's own.
    [Fact]
    public void The_row_list_carries_none_of_its_own_default_chrome()
    {
        var listBoxTag = Regex.Match(
            MainWindowXaml(), "<ListBox ItemsSource=\"\\{Binding Rows\\}\".*?>", RegexOptions.Singleline).Value;

        Assert.Contains("BorderThickness=\"0\"", listBoxTag, StringComparison.Ordinal);
        Assert.Contains("Background=\"Transparent\"", listBoxTag, StringComparison.Ordinal);
        Assert.Contains("FocusVisualStyle=\"{x:Null}\"", listBoxTag, StringComparison.Ordinal);
    }

    // Per-row virtualization, restored: a VirtualizingStackPanel inside the ListBox itself (one
    // per group, alongside GroupsHost's own group-level VirtualizingStackPanel), with
    // CanContentScroll left True so the panel keeps hooking up as a real IScrollInfo provider
    // rather than measuring every row unconditionally.
    [Fact]
    public void The_row_list_virtualizes_with_content_scrolling_enabled()
    {
        var xaml = MainWindowXaml();

        Assert.Equal(2, Regex.Matches(xaml, "<VirtualizingStackPanel />").Count);
        Assert.Contains("ScrollViewer.CanContentScroll=\"True\"", xaml, StringComparison.Ordinal);
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

    // Fix 5: GroupsHost's own VirtualizingStackPanel defaulted to ScrollUnit="Item" - each item is
    // an entire date group (header plus every row under it), so one wheel notch jumped a whole
    // day's worth of rows at once on a store with many backups on one day. Pixel scrolling is what
    // a user expects from a list like this.
    [Fact]
    public void The_groups_host_scrolls_by_pixel_not_by_whole_date_group()
    {
        var groupsHostTag = Regex.Match(
            MainWindowXaml(), "<ItemsControl x:Name=\"GroupsHost\".*?>", RegexOptions.Singleline).Value;

        Assert.Contains("VirtualizingPanel.ScrollUnit=\"Pixel\"", groupsHostTag, StringComparison.Ordinal);
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
}
