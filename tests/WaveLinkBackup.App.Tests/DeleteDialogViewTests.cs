using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WaveLinkBackup.App.Theming;
using WaveLinkBackup.App.ViewModels;
using WaveLinkBackup.App.Views;
using WaveLinkBackup.Core.Snapshots;

namespace WaveLinkBackup.App.Tests;

/// <summary>
/// The DeleteDialog view, forced through a real layout pass in both themes - the repo's stand-in
/// for a screenshot (a WPF window can't be browser-QA'd). AppResourceOrderTests and RowTemplateTests
/// established the Measure/Arrange idiom for a DETACHED ContentControl; but a Window's content tree
/// is materialized LAZILY - named elements inside it are not reachable via VisualTreeHelper until the
/// window is actually shown (MainWindowSelectionTests' own header records that Show(), not
/// Measure/Arrange, is what builds a full visual tree). So this test shows the dialog off-screen,
/// reads the tree, then closes it. A StaticResource that cannot resolve throws a XamlParseException
/// during that pass; a DynamicResource that resolves to nothing leaves a visually broken dialog
/// nobody catches - so each variant is asserted to actually build its visual tree and to show/hide
/// the right pieces, not merely "did not throw".
///
/// What this pins beyond the model tests:
///   - every StaticResource in DeleteDialog.xaml resolves (WlGhostButton/Secondary/Danger, text styles,
///     TrackedText) under the real merge order;
///   - the context block and the ghost "Back up now instead" button collapse to zero when their bound
///     value is null (Normal variant) and appear when present (OnlyBackup / PreRestore);
///   - the card is 480px wide in every theme.
/// </summary>
public sealed class DeleteDialogViewTests
{
    private static readonly string SourceRoot = Assembly.GetExecutingAssembly()
        .GetCustomAttributes<AssemblyMetadataAttribute>()
        .Single(a => a.Key == "AppSourceRoot").Value!;

    // Same hand-built snapshot the model tests use: the view only renders what the model computed.
    private static Snapshot Snapshot(string name, SnapshotTrigger trigger) => new(
        "2026-08-11T2136-a3f81c",
        @"C:\Users\test\AppData\Local\WaveLinkBackup\2026-08-11T2136-a3f81c",
        new SnapshotManifest(
            SchemaVersion: SnapshotManifest.CurrentSchemaVersion,
            DisplayName: name,
            Notes: string.Empty,
            CreatedUtc: new DateTimeOffset(2026, 8, 11, 19, 36, 0, TimeSpan.Zero),
            Trigger: trigger,
            SettingsSha256: new string('0', 64),
            WaveLinkVersion: null,
            InputCount: 3,
            InputNames: ["Wave Mic 1"],
            EffectCount: 0,
            EffectChannelCount: 0,
            HasDuplicateKeys: false,
            Tiers: [],
            Files: new Dictionary<string, SnapshotFile>
            {
                [SnapshotManifest.SettingsFileName] = new(new string('0', 64), 12_582_912),
            }));

    /// <summary>
    /// Merges App.xaml's non-theme dictionaries in its own order and applies the given theme to slot
    /// 0 - the same startup path App.OnStartup takes, so DynamicResource brushes (WlSunken, WlScrim,
    /// WlCard, WlLine, WlBg) resolve exactly as they do in a real run.
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

    private static FrameworkElement? FindByName(IEnumerable<FrameworkElement> tree, string name) =>
        tree.FirstOrDefault(e => e.Name == name);

    /// <summary>
    /// Shows the dialog off-screen (forcing full visual-tree materialization - a Window's content is
    /// lazy until shown), runs the assertions on the owning STA thread (WPF elements are
    /// thread-affine - reading Name/Visibility from another thread throws), then closes the window.
    /// Throws if any resource fails to resolve - that is the failure this test exists to catch.
    /// </summary>
    private static void ShowAndAssert(DeleteDialogModel model, Action<FrameworkElement> assert)
        => Wpf.Run(() =>
        {
            // The variant tests do not pin a theme - the dark default is what App.OnStartup applies
            // before any user choice, so it is the honest one to load here.
            LoadAppResources(AppTheme.Dark);

            var dialog = new DeleteDialog(model)
            {
                Width = 720,
                Height = 560,
                // Off-screen: the test needs the visual tree materialized, not a visible window.
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

    // -------------------------------------------------------------- the three variants build

    [Fact]
    public void Normal_variant_builds_with_no_context_block_and_no_ghost_button()
    {
        var model = DeleteDialogModel.Build(Snapshot("Before 3.3 beta", SnapshotTrigger.Manual), totalBackups: 4);

        // The context block Border is present in the tree but collapsed (Context is null).
        ShowAndAssert(model, e =>
        {
            if (e.Name == "ContextBlock") Assert.Equal(Visibility.Collapsed, e.Visibility);
            // The ghost button collapses when BackUpNowInstead is null.
            if (e.Name == "BackUpNowButton") Assert.Equal(Visibility.Collapsed, e.Visibility);
            // Cancel and Delete are always present and visible.
            if (e.Name == "CancelButton") Assert.Equal(Visibility.Visible, e.Visibility);
            if (e.Name == "DeleteButton") Assert.Equal(Visibility.Visible, e.Visibility);
        });
    }

    [Fact]
    public void Only_backup_variant_builds_with_context_block_and_ghost_button()
    {
        var model = DeleteDialogModel.Build(Snapshot("Before 3.3 beta", SnapshotTrigger.Manual), totalBackups: 1);

        // The context block is visible (Context is present) and the ghost button too.
        ShowAndAssert(model, e =>
        {
            if (e.Name == "ContextBlock") Assert.Equal(Visibility.Visible, e.Visibility);
            if (e.Name == "BackUpNowButton") Assert.Equal(Visibility.Visible, e.Visibility);
            if (e.Name == "CancelButton") Assert.Equal(Visibility.Visible, e.Visibility);
            if (e.Name == "DeleteButton") Assert.Equal(Visibility.Visible, e.Visibility);
        });
    }

    [Fact]
    public void Pre_restore_variant_builds_with_context_block_and_no_ghost_button()
    {
        var model = DeleteDialogModel.Build(Snapshot("Before restore", SnapshotTrigger.PreRestore), totalBackups: 4);

        // The context block is visible (Context is present); no ghost button for PreRestore.
        ShowAndAssert(model, e =>
        {
            if (e.Name == "ContextBlock") Assert.Equal(Visibility.Visible, e.Visibility);
            if (e.Name == "BackUpNowButton") Assert.Equal(Visibility.Collapsed, e.Visibility);
            if (e.Name == "CancelButton") Assert.Equal(Visibility.Visible, e.Visibility);
            if (e.Name == "DeleteButton") Assert.Equal(Visibility.Visible, e.Visibility);
        });
    }

    // -------------------------------------------------------------- the card is 480px in every theme

    [Theory]
    [InlineData(AppTheme.Light)]
    [InlineData(AppTheme.Dark)]
    public void The_card_is_480px_wide_in_every_theme(AppTheme theme)
    {
        Wpf.Run(() =>
        {
            LoadAppResources(theme);

            var model = DeleteDialogModel.Build(Snapshot("A", SnapshotTrigger.Manual), totalBackups: 3);
            var dialog = new DeleteDialog(model)
            {
                Width = 720,
                Height = 560,
                Left = -3000,
                Top = -3000,
                ShowInTaskbar = false,
            };

            dialog.Show();
            dialog.UpdateLayout();

            // The card Border is the one with Width=480 set in XAML. Find it by its rendered width.
            var card = FindDescendants(dialog).OfType<Border>().FirstOrDefault(b => b.Width == 480);
            Assert.NotNull(card);
            Assert.Equal(480, card!.ActualWidth);

            dialog.Close();
            return true;
        });
    }
}
