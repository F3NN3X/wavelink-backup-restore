using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;
using WaveLinkBackup.App.Theming;

namespace WaveLinkBackup.App.Tests;

/// <summary>
/// Merges App.xaml's non-theme dictionaries in App.xaml's own order and applies a theme to slot 0 -
/// the same startup path App.OnStartup takes, so a view under test resolves every StaticResource and
/// DynamicResource exactly as it does in a real run.
///
/// Reading the order out of App.xaml rather than restating it is the point: merge order decides which
/// of two same-keyed resources wins, so a test that hard-codes its own order can pass while the real
/// window fails.
///
/// DeleteDialogViewTests and ErrorDialogViewTests each carry their own copy of this, written before
/// there was a third caller. New view tests use this one.
/// </summary>
internal static class AppResources
{
    public static readonly string SourceRoot = Assembly.GetExecutingAssembly()
        .GetCustomAttributes<AssemblyMetadataAttribute>()
        .Single(a => a.Key == "AppSourceRoot").Value!;

    public static void Load(AppTheme theme)
    {
        var dictionaries = Application.Current.Resources.MergedDictionaries;
        dictionaries.Clear();
        ThemeManager.Apply(theme);

        var appXaml = File.ReadAllText(Path.Combine(SourceRoot, "App.xaml"));
        var withoutComments = Regex.Replace(appXaml, "<!--.*?-->", string.Empty, RegexOptions.Singleline);

        foreach (var source in Regex.Matches(withoutComments, "<ResourceDictionary Source=\"([^\"]+)\"\\s*/>")
                 .Select(m => m.Groups[1].Value)
                 .Where(s => !s.StartsWith("Theming/", StringComparison.Ordinal)))
        {
            dictionaries.Add(new ResourceDictionary
            {
                Source = new Uri($"pack://application:,,,/WaveLinkBackup;component/{source}", UriKind.Absolute),
            });
        }
    }

    /// <summary>Every FrameworkElement below <paramref name="root"/>, depth first.</summary>
    public static IEnumerable<FrameworkElement> Descendants(FrameworkElement root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            if (VisualTreeHelper.GetChild(root, i) is not FrameworkElement element) continue;

            yield return element;
            foreach (var grandchild in Descendants(element)) yield return grandchild;
        }
    }
}
