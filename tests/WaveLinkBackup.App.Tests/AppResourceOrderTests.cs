using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WaveLinkBackup.App.Theming;
using WaveLinkBackup.App.Views;

namespace WaveLinkBackup.App.Tests;

/// <summary>
/// App.xaml merges Theming/Dark.xaml (slot 0, replaced by ThemeManager.Apply), Typography.xaml,
/// RowStyles.xaml, then ControlStyles.xaml. That order is load-bearing for one specific reason:
/// ControlStyles.xaml's WlColumnHeaderRowTemplate sets
/// <c>Style="{StaticResource WlColumnHeaderTrackedText}"</c> five times, and that key is defined
/// only in RowStyles.xaml. A plain StaticResource cannot resolve FORWARD across
/// MergedDictionaries order - only back to a dictionary already merged before it - so
/// RowStyles.xaml has to be merged before ControlStyles.xaml, not after.
///
/// This slips past every source-text guard (the RowTemplateTests/MainWindowTemplateTests style of
/// "does the key exist somewhere in this file" check) because a DataTemplate's body is parsed
/// LAZILY, at first instantiation, not at dictionary-load time - a bad merge order fails silently
/// right up until something actually asks WPF to build the template's visual tree.
/// MainWindowSelectionTests' own header comment records that an earlier attempt to drive the real
/// MainWindow through Show() hit exactly this exception and had to route around it by merging
/// only Typography.xaml + RowStyles.xaml, deliberately leaving ControlStyles.xaml out.
///
/// This test forces the one template that crosses the RowStyles/ControlStyles boundary through a
/// real layout pass - Measure/Arrange/UpdateLayout on a detached ContentControl, no Show() - the
/// same headless idiom MainWindowListStateTests and MainWindowSelectionTests already use for a
/// single non-virtualized element (Show() is only needed there for a virtualizing ItemsControl's
/// container generation, which nothing here does - WlColumnHeaderRowTemplate is a flat Grid with
/// no ItemsControl in it at all). A StaticResource that cannot resolve throws a
/// XamlParseException during that layout pass.
/// </summary>
public sealed class AppResourceOrderTests
{
    private static readonly string SourceRoot = Assembly.GetExecutingAssembly()
        .GetCustomAttributes<AssemblyMetadataAttribute>()
        .Single(a => a.Key == "AppSourceRoot").Value!;

    /// <summary>
    /// Reads App.xaml itself and merges every ResourceDictionary it declares, in ITS OWN order -
    /// not a hand-copied list here that could quietly drift from the real file. Theming/Dark.xaml
    /// (slot 0) goes through ThemeManager.Apply instead, exactly like App.OnStartup's real
    /// startup path, since that IS how slot 0 is populated in a real run.
    /// </summary>
    private static void LoadAppResourcesInAppXamlOrder()
    {
        var dictionaries = Application.Current.Resources.MergedDictionaries;
        dictionaries.Clear();
        ThemeManager.Apply(AppTheme.Dark);

        var appXaml = File.ReadAllText(Path.Combine(SourceRoot, "App.xaml"));
        var withoutComments = Regex.Replace(appXaml, "<!--.*?-->", string.Empty, RegexOptions.Singleline);

        var sources = Regex.Matches(withoutComments, "<ResourceDictionary Source=\"([^\"]+)\"\\s*/>")
            .Select(m => m.Groups[1].Value)
            .Where(s => !s.StartsWith("Theming/", StringComparison.Ordinal))
            .ToArray();

        Assert.True(sources.Length >= 2,
            "Could not find App.xaml's non-theme merged dictionaries - the regex above no longer " +
            "matches the file, which would silently turn this whole test into a no-op.");

        foreach (var source in sources)
        {
            dictionaries.Add(new ResourceDictionary
            {
                Source = new Uri(
                    $"pack://application:,,,/WaveLinkBackup;component/{source}", UriKind.Absolute),
            });
        }
    }

    private static IEnumerable<T> FindDescendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) yield return match;
            foreach (var grandchild in FindDescendants<T>(child)) yield return grandchild;
        }
    }

    /// <summary>
    /// THE regression test. Forces WlColumnHeaderRowTemplate (ControlStyles.xaml) through a real
    /// layout pass using App.xaml's actual merge order, and checks that its five TrackedText
    /// labels actually picked up WlColumnHeaderTrackedText's FontSize (10.5 - nothing else in the
    /// template sets FontSize) - not merely "did not throw". A template that silently failed to
    /// apply the cross-dictionary style at all would still not throw and would still leave a
    /// visually broken header nobody caught, which is no better a guard than the source-text
    /// "key exists somewhere" checks this is meant to improve on.
    /// </summary>
    [Fact]
    public void The_column_header_template_instantiates_and_its_five_labels_pick_up_the_shared_style()
    {
        var fontSizes = Wpf.Run(() =>
        {
            LoadAppResourcesInAppXamlOrder();

            var template = (DataTemplate)Application.Current.Resources["WlColumnHeaderRowTemplate"];
            var host = new ContentControl { ContentTemplate = template, Content = new object() };

            host.Measure(new Size(1000, 100));
            host.Arrange(new Rect(0, 0, 1000, 100));
            host.UpdateLayout();

            return FindDescendants<TrackedText>(host).Select(t => t.FontSize).ToList();
        });

        Assert.Equal(5, fontSizes.Count);
        Assert.All(fontSizes, size => Assert.Equal(10.5, size));
    }
}
