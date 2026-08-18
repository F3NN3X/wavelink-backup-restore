// System.IO is NOT in the implicit-usings set for a UseWPF project - see ThemeTests.cs's own
// comment on this.
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;

namespace WaveLinkBackup.App.Tests;

/// <summary>
/// Task 11's key map is data (ShellCommands, ShellCommandTests), but WIRING that data into the
/// window is code - a CommandBinding per command, a CanExecute per gated one, and Escape bound
/// narrowly rather than at the window. None of that is reachable from ShellCommandTests, so it
/// is pinned here the same source-text way MainWindowTemplateTests pins Task 10b's own wiring:
/// the failure mode being guarded against is someone editing this XAML/code-behind, and reading
/// the source catches that directly without needing a live window and a real HealthProbe.
/// </summary>
public sealed class ShellCommandWiringTests
{
    private static readonly string SourceRoot = Assembly.GetExecutingAssembly()
        .GetCustomAttributes<AssemblyMetadataAttribute>()
        .Single(a => a.Key == "AppSourceRoot").Value!;

    private static string MainWindowXaml() =>
        File.ReadAllText(Path.Combine(SourceRoot, "Views", "MainWindow.xaml"));

    private static string MainWindowCodeBehind() =>
        File.ReadAllText(Path.Combine(SourceRoot, "Views", "MainWindow.xaml.cs"));

    [Theory]
    [InlineData("Refresh", false)]
    [InlineData("Search", false)]
    [InlineData("BackUpNow", true)]
    [InlineData("Rename", true)]
    [InlineData("Delete", true)]
    [InlineData("Restore", true)]
    public void Every_window_bound_command_has_a_CommandBinding(string command, bool gated)
    {
        var bindingsSection = Regex.Match(
            MainWindowXaml(), "<Window.CommandBindings>.*?</Window.CommandBindings>",
            RegexOptions.Singleline).Value;

        Assert.True(bindingsSection.Length > 0, "Window.CommandBindings is gone or renamed.");

        var binding = Regex.Match(
            bindingsSection,
            $@"<CommandBinding Command=""\{{x:Static vm:ShellCommands\.{command}\}}""[^/]*/>",
            RegexOptions.Singleline).Value;

        Assert.True(binding.Length > 0, $"No CommandBinding for {command}.");
        Assert.Contains($"Executed=\"{command}_Executed\"", binding, StringComparison.Ordinal);

        if (gated)
        {
            Assert.Contains($"CanExecute=\"{command}_CanExecute\"", binding, StringComparison.Ordinal);
        }
    }

    // 10-decisions section 6: Escape only clears the search when the list or the search field has
    // focus - a window-wide binding would swallow Escape in the dialogs a later session adds.
    [Fact]
    public void ClearSearch_is_not_bound_on_the_window()
    {
        var bindingsSection = Regex.Match(
            MainWindowXaml(), "<Window.CommandBindings>.*?</Window.CommandBindings>",
            RegexOptions.Singleline).Value;

        Assert.DoesNotContain("ShellCommands.ClearSearch", bindingsSection, StringComparison.Ordinal);
    }

    [Fact]
    public void ClearSearch_is_bound_on_the_search_box_and_the_row_list()
    {
        var xaml = MainWindowXaml();

        var searchBox = Regex.Match(
            xaml, "<TextBox x:Name=\"SearchBox\".*?</TextBox>", RegexOptions.Singleline).Value;
        Assert.Contains("ShellCommands.ClearSearch", searchBox, StringComparison.Ordinal);
        Assert.Contains("ClearSearch_Executed", searchBox, StringComparison.Ordinal);

        // Fix 1 moved the Loaded/Collapsed visibility trigger off ListScrollViewer itself and onto
        // the wrapping ListLoadedRegion Grid (so the new search-footer strip shows and hides
        // alongside the scroll region) - the end marker here follows that move, from
        // <ScrollViewer.Style> to </ScrollViewer.CommandBindings>, which is still unique to this
        // element and still closes before GroupsHost begins.
        var listScrollViewer = Regex.Match(
            xaml, "<ScrollViewer x:Name=\"ListScrollViewer\".*?</ScrollViewer.CommandBindings>",
            RegexOptions.Singleline).Value;
        Assert.Contains("ShellCommands.ClearSearch", listScrollViewer, StringComparison.Ordinal);
        Assert.Contains("ClearSearch_Executed", listScrollViewer, StringComparison.Ordinal);
    }

    // The trap the brief names explicitly: a guard INSIDE the Executed handler still leaves the
    // command looking live (WPF greys nothing out on its own). CanExecute is the only mechanism
    // that actually disables Enter on a row that cannot be restored.
    [Theory]
    [InlineData("BackUpNow", "shell.CanBackUpNow")]
    [InlineData("Rename", "shell.CanRename")]
    [InlineData("Delete", "shell.CanDelete")]
    [InlineData("Restore", "shell.CanRestore")]
    public void CanExecute_reads_the_matching_shell_property(string command, string property)
    {
        var code = MainWindowCodeBehind();

        var method = Regex.Match(
            code,
            $@"private void {command}_CanExecute\(object sender, CanExecuteRoutedEventArgs e\) =>\s*\n?\s*e\.CanExecute = ([^;]+);").Groups[1].Value.Trim();

        Assert.Equal(property, method);
    }

    // Enter must not restore anything without a row selected and CanRestore true - the CanExecute
    // property above is what encodes that; this proves the Executed handler itself carries no
    // second (redundant, and easy to drift out of sync) guard of its own.
    [Fact]
    public void Restore_Executed_carries_no_guard_of_its_own()
    {
        var code = MainWindowCodeBehind();

        var executed = Regex.Match(
            code, @"private void Restore_Executed\(object sender, ExecutedRoutedEventArgs e\) => ([^;]+);")
            .Groups[1].Value.Trim();

        Assert.Equal("ShowRestorePlaceholder()", executed);
    }
}
