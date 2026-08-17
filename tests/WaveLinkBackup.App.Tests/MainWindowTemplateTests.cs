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
                        && name != "WlShieldCheckGeometry")
            .ToArray();

        Assert.True(unknown.Length == 0, $"Not theme brushes: {string.Join(", ", unknown)}");
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
