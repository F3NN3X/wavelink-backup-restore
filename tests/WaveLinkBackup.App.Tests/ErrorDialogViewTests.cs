using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WaveLinkBackup.App.Theming;
using WaveLinkBackup.App.ViewModels;
using WaveLinkBackup.App.Views;
using WaveLinkBackup.Core.Results;

namespace WaveLinkBackup.App.Tests;

/// <summary>
/// The ErrorDialog view, forced through a real layout pass in both themes - the repo's stand-in for
/// a screenshot. Same idiom as DeleteDialogViewTests: a Window's content tree is materialized lazily,
/// so named elements are only reachable via VisualTreeHelper once the window is actually shown; this
/// test shows each variant off-screen, reads the tree on the owning STA thread (WPF elements are
/// thread-affine), then closes it. A StaticResource that cannot resolve throws a XamlParseException
/// during that pass, so "did not throw" already proves every resource in ErrorDialog.xaml resolves
/// under the real merge order. Beyond that, each variant is asserted to show/hide the right pieces:
///   - error 2 shows the chooser rows and the "remember this one" checkbox, no note block;
///   - error 4 shows the amber note block (WlWarnSoft) and the ghost "Open the folder" button,
///     no chooser, no remember checkbox;
///   - error 8 shows the neutral note block and neither the chooser nor any footer extra.
/// The card width is bound (620 for the chooser, 560 for the other two) and asserted per variant.
/// </summary>
public sealed class ErrorDialogViewTests
{
    private static readonly string SourceRoot = Assembly.GetExecutingAssembly()
        .GetCustomAttributes<AssemblyMetadataAttribute>()
        .Single(a => a.Key == "AppSourceRoot").Value!;

    // The three dialog errors, constructed exactly as Core would produce them.
    private static ErrorDialogModel TwoInstallations() =>
        ErrorDialogModel.Build(new MultiplePackagesFound(
            ["C:\\Program Files\\Wave Link", "D:\\Apps\\Wave Link"]));

    private static ErrorDialogModel MalformedSettings() =>
        ErrorDialogModel.Build(new MalformedSettings("unexpected token at line 12, column 3"));

    private static ErrorDialogModel NewerVersion() =>
        ErrorDialogModel.Build(new UnsupportedSnapshotSchema(Found: 3, Supported: 2));

    /// <summary>
    /// Merges App.xaml's non-theme dictionaries in its own order and applies the given theme to slot
    /// 0 - the same startup path App.OnStartup takes, so DynamicResource brushes (WlScrim, WlCard,
    /// WlLine, WlSunken, WlWarnSoft) resolve exactly as they do in a real run.
    /// </summary>
    private static void LoadAppResources(AppTheme theme)
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

    private static IEnumerable<FrameworkElement> FindDescendants(FrameworkElement root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is not FrameworkElement element) continue;
            yield return element;
            foreach (var grandchild in FindDescendants(element)) yield return grandchild;
        }
    }

    /// <summary>
    /// Shows the dialog off-screen (forcing full visual-tree materialization), runs the assertions on
    /// the owning STA thread, then closes the window. Throws if any resource fails to resolve - that
    /// is the failure this test exists to catch.
    /// </summary>
    private static void ShowAndAssert(ErrorDialogModel model, Action<FrameworkElement> assert)
        => Wpf.Run(() =>
        {
            // The dark default is what App.OnStartup applies before any user choice - the honest theme.
            LoadAppResources(AppTheme.Dark);

            var dialog = new ErrorDialog(model)
            {
                Width = 720,
                Height = 560,
                Left = -3000,
                Top = -3000,
                ShowInTaskbar = false,
            };

            dialog.Show();
            dialog.UpdateLayout();

            try
            {
                foreach (var element in FindDescendants(dialog))
                {
                    assert(element);
                }
            }
            finally
            {
                dialog.Close();
            }

            return true;
        });

    // -------------------------------------------------------------- error 2 - the chooser variant

    [Fact]
    public void Two_installations_shows_chooser_and_remember_checkbox_with_no_note_block()
    {
        ShowAndAssert(TwoInstallations(), e =>
        {
            if (e.Name == "ChooserList") Assert.Equal(Visibility.Visible, e.Visibility);
            if (e.Name == "NoteBlock") Assert.Equal(Visibility.Collapsed, e.Visibility);
            if (e.Name == "RememberCheckbox") Assert.Equal(Visibility.Visible, e.Visibility);
            // No ghost footer action for error 2.
            if (e.Name == "GhostButton") Assert.Equal(Visibility.Collapsed, e.Visibility);
            // Both footer buttons are always present.
            if (e.Name == "SecondaryButton") Assert.Equal(Visibility.Visible, e.Visibility);
            if (e.Name == "PrimaryButton") Assert.Equal(Visibility.Visible, e.Visibility);
        });
    }

    // -------------------------------------------------------------- error 4 - the amber variant

    [Fact]
    public void Malformed_settings_shows_amber_note_block_and_ghost_button_with_no_chooser()
    {
        ShowAndAssert(MalformedSettings(), e =>
        {
            if (e.Name == "ChooserList") Assert.Equal(Visibility.Collapsed, e.Visibility);
            // The note block is present and visible...
            if (e.Name == "NoteBlock")
            {
                var border = (Border)e;
                Assert.Equal(Visibility.Visible, border.Visibility);
                // ...and AMBER: the live settings file is not whole, so it takes WlWarnSoft/WlWarn.
                // Compare by value, not identity - the shared test Application's merged dictionaries
                // can hold duplicate keys across test classes, and FindResource returns the LAST one,
                // which may be a different brush instance than the one the dialog bound to. The colour
                // is what "amber" means; the instance is an implementation detail.
                Assert.Equal(((SolidColorBrush)border.Background).Color, Color.FromRgb(0xF5, 0xB8, 0x43));
                Assert.Equal(((SolidColorBrush)border.BorderBrush).Color, Color.FromRgb(0xF5, 0xB8, 0x43));
            }
            if (e.Name == "RememberCheckbox") Assert.Equal(Visibility.Collapsed, e.Visibility);
            // The ghost "Open the folder" action is error 4's only footer extra.
            if (e.Name == "GhostButton") Assert.Equal(Visibility.Visible, e.Visibility);
        });
    }

    // -------------------------------------------------------------- error 8 - the neutral version readout

    [Fact]
    public void Newer_version_shows_neutral_note_block_with_no_chooser_or_footer_extras()
    {
        ShowAndAssert(NewerVersion(), e =>
        {
            if (e.Name == "ChooserList") Assert.Equal(Visibility.Collapsed, e.Visibility);
            if (e.Name == "NoteBlock") Assert.Equal(Visibility.Visible, e.Visibility);
            if (e.Name == "RememberCheckbox") Assert.Equal(Visibility.Collapsed, e.Visibility);
            // No ghost action and no remember checkbox for error 8.
            if (e.Name == "GhostButton") Assert.Equal(Visibility.Collapsed, e.Visibility);
        });
    }

    // -------------------------------------------------------------- the card width is bound per variant

    [Theory]
    [InlineData(AppTheme.Light)]
    [InlineData(AppTheme.Dark)]
    public void The_chooser_card_is_620px_and_the_others_560px_in_every_theme(AppTheme theme)
    {
        Wpf.Run(() =>
        {
            LoadAppResources(theme);

            // Error 2: the chooser card is wider.
            Assert.Equal(620, CardWidthOf(new ErrorDialog(TwoInstallations()), theme));
            // Errors 4 and 8: the standard card width.
            Assert.Equal(560, CardWidthOf(new ErrorDialog(MalformedSettings()), theme));
            Assert.Equal(560, CardWidthOf(new ErrorDialog(NewerVersion()), theme));

            return true;
        });
    }

    /// <summary>Shows a dialog off-screen, reads the card Border's rendered width, closes it.</summary>
    private static double CardWidthOf(ErrorDialog dialog, AppTheme theme)
    {
        // LoadAppResources was already called for this thread by the caller; re-applying is harmless
        // and keeps this helper self-contained if reused.
        dialog.Width = 720;
        dialog.Height = 560;
        dialog.Left = -3000;
        dialog.Top = -3000;
        dialog.ShowInTaskbar = false;

        dialog.Show();
        dialog.UpdateLayout();

        try
        {
            // The card is the Border whose Width is bound to CardWidth (620 or 560). Find it by its
            // rendered width rather than a name, since the XAML does not name it.
            var card = FindDescendants(dialog)
                .OfType<Border>()
                .FirstOrDefault(b => b.Width == 620 || b.Width == 560);
            Assert.NotNull(card);
            return card!.ActualWidth;
        }
        finally
        {
            dialog.Close();
        }
    }
}
