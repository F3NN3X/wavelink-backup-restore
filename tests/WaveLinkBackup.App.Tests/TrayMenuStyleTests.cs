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
    /// layered window, and a layered window ignores the DWM corner and frame attributes silently
    /// — the call succeeds and the menu looks untouched. Plan 3, finding A.
    /// </summary>
    [Fact]
    public void The_menu_refuses_a_drop_shadow_so_the_dwm_attributes_apply_at_all()
    {
        Assert.False(Wpf.Run(() => LoadMenu().HasDropShadow));
    }

    /// <summary>
    /// The menu is opaque. A translucent one shows the desktop's palette through it, which is the
    /// opposite of looking like this app.
    /// </summary>
    [Fact]
    public void The_menu_surface_is_fully_opaque()
    {
        var alpha = Wpf.Run(() =>
        {
            ThemeManager.Apply(AppTheme.Dark);
            return ((SolidColorBrush)LoadMenu().Background).Color.A;
        });

        Assert.Equal(255, alpha);
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

    /// <summary>
    /// screens/12: "Quit stops the backups, and the item says so rather than a dialog afterwards."
    /// The label carries the consequence on the face of the menu — there is no confirmation step
    /// to catch someone who did not read it. Pinning the exact string means a copy edit that drops
    /// "stops backing up" (leaving a bare "Quit") fails here instead of shipping.
    /// </summary>
    [Fact]
    public void The_quit_item_says_it_stops_backing_up()
    {
        var header = Wpf.Run(() => LoadMenu().Items
            .OfType<MenuItem>()
            .Single(i => i.Name == "Quit")
            .Header);

        Assert.Equal("Quit — stops backing up", header);
    }

    /// <summary>
    /// The PauseResume item's designed starting label. App.RefreshTray rewrites it to "Resume"
    /// while a pause is active and back to this on resume (App.xaml.cs, RefreshTray), so the XAML
    /// default is what a freshly built menu shows — the state the user sees before any tick has
    /// run. Pinning it keeps the swap's two endpoints distinct and the un-paused label exact.
    /// </summary>
    [Fact]
    public void The_pause_item_starts_labeled_as_a_pause()
    {
        var header = Wpf.Run(() => LoadMenu().Items
            .OfType<MenuItem>()
            .Single(i => i.Name == "PauseResume")
            .Header);

        Assert.Equal("Pause for an hour", header);
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

        // WlCard: the design's role for a raised panel, which is what a floating menu is. NOT
        // WlChrome — that is specifically the Mica caption/strip tint, and on an opaque menu with
        // no Mica behind it, it reads as a flat grey belonging to neither Windows nor this app.
        Assert.Equal("#FF16191A", dark);
        Assert.Equal("#FFFFFFFF", light);
        Assert.NotEqual(dark, light);
    }
}
