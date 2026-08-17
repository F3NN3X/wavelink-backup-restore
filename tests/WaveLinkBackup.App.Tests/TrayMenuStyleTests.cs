using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WaveLinkBackup.App.Theming;

namespace WaveLinkBackup.App.Tests;

/// <summary>
/// A broken ControlTemplate is a runtime failure that the compiler is perfectly happy with, and
/// the tray menu is the one surface nobody sees until they right-click it. These force the
/// templates to actually instantiate.
///
/// What they do NOT check is whether it looks right — that is a human's job, and the plan says so.
/// </summary>
public sealed class TrayMenuStyleTests
{
    private static ContextMenu LoadMenu()
    {
        var dictionary = new ResourceDictionary
        {
            Source = new Uri(
                "pack://application:,,,/WaveLinkBackup;component/Views/TrayIcon.xaml",
                UriKind.Absolute),
        };

        return (ContextMenu)dictionary["TrayMenu"];
    }

    /// <summary>
    /// The order is the design's (screens/12) and it is not arbitrary: "Back up now" is the
    /// default item and sits first, and Quit sits last and alone under a rule because it stops
    /// the backups.
    /// </summary>
    [Fact]
    public void The_menu_is_the_designed_items_in_the_designed_order()
    {
        var names = Wpf.Run(() => LoadMenu().Items
            .OfType<FrameworkElement>()
            .Select(i => i is Separator ? "-" : i.Name)
            .ToList());

        Assert.Equal(
        [
            "LastBackupHeader",
            "-",
            "BackUpNow", "OpenApp", "OpenFolder",
            "-",
            "AutoBackup", "PauseResume",
            "-",
            "OpenSettings", "Quit",
        ], names);
    }

    /// <summary>
    /// HasDropShadow false is load-bearing, not cosmetic. It is what stops the popup being a
    /// layered window, and a layered window ignores every DWM backdrop attribute silently — the
    /// call succeeds and the menu looks untouched. Plan 3, finding A.
    /// </summary>
    [Fact]
    public void The_menu_refuses_a_drop_shadow_so_a_backdrop_can_apply_at_all()
    {
        Assert.False(Wpf.Run(() => LoadMenu().HasDropShadow));
    }

    [Fact]
    public void The_readout_is_not_clickable_and_every_other_item_is()
    {
        var (header, others) = Wpf.Run(() =>
        {
            var menu = LoadMenu();
            var items = menu.Items.OfType<MenuItem>().ToList();

            return (
                items.Single(i => i.Name == "LastBackupHeader").IsEnabled,
                items.Where(i => i.Name != "LastBackupHeader").All(i => i.IsEnabled));
        });

        Assert.False(header);
        Assert.True(others);
    }

    /// <summary>Only one item is checkable — the automatic-backup toggle.</summary>
    [Fact]
    public void Exactly_one_item_is_checkable()
    {
        var checkable = Wpf.Run(() => LoadMenu().Items
            .OfType<MenuItem>()
            .Where(i => i.IsCheckable)
            .Select(i => i.Name)
            .ToList());

        Assert.Equal(["AutoBackup"], checkable);
    }

    /// <summary>
    /// The templates instantiate. Opening the menu is what forces the ControlTemplates to be
    /// applied, and a typo in one throws here rather than the first time a user right-clicks.
    /// </summary>
    [Fact]
    public void Every_template_in_the_menu_instantiates()
    {
        var applied = Wpf.Run(() =>
        {
            ThemeManager.Apply(AppTheme.Dark);

            var menu = LoadMenu();
            menu.IsOpen = true;      // realises the popup, and with it every template
            menu.UpdateLayout();

            var items = menu.Items.OfType<MenuItem>().Count(i => i.Template is not null);

            menu.IsOpen = false;
            return items;
        });

        Assert.Equal(8, applied);
    }

    /// <summary>
    /// The colours come from the theme, so a swap moves them. This is the difference between
    /// "styled" and "styled to follow Windows".
    /// </summary>
    [Fact]
    public void The_menus_colours_follow_the_theme_rather_than_being_baked_in()
    {
        var (dark, light) = Wpf.Run(() =>
        {
            ThemeManager.Apply(AppTheme.Dark);
            var inDark = ((SolidColorBrush)LoadMenu().Background).Color.ToString();

            // A FRESH menu, not the same one reopened. A ContextMenu that belongs to a tray icon
            // has no parent in any visual tree, so it never receives the resources-changed
            // notification an Application.Resources swap raises — its DynamicResources resolve
            // once, at load, and then never again. Rebuilding is what makes the swap reach it;
            // see App.RebuildTrayMenu.
            ThemeManager.Apply(AppTheme.Light);
            var inLight = ((SolidColorBrush)LoadMenu().Background).Color.ToString();

            return (inDark, inLight);
        });

        Assert.Equal("#FF1A1E20", dark);   // WlChrome, dark
        Assert.Equal("#FFEAEAE6", light);  // WlChrome, light
        Assert.NotEqual(dark, light);
    }
}
