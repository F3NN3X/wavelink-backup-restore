using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using H.NotifyIcon;
using H.NotifyIcon.Core;
using WaveLinkBackup.App.Hosting;
using WaveLinkBackup.App.Services;
using WaveLinkBackup.App.Startup;
using WaveLinkBackup.App.Updates;
using WaveLinkBackup.App.Theming;
using WaveLinkBackup.App.ViewModels;
using WaveLinkBackup.App.Views;
using WaveLinkBackup.App.Windows;
using WaveLinkBackup.Core.Abstractions;
using WaveLinkBackup.Core.Analysis;
using WaveLinkBackup.Core.Automation;
using WaveLinkBackup.Core.Capture;
using WaveLinkBackup.Core.Discovery;
using WaveLinkBackup.Core.Io;
using WaveLinkBackup.Core.Process;
using WaveLinkBackup.Core.Results;
using WaveLinkBackup.Core.Snapshots;

namespace WaveLinkBackup.App;

/// <summary>The tray icon and its menu: what the app looks like when there is no window.</summary>
public partial class App
{

    /// <summary>
    /// Fixed, and pinned here rather than left to the library. H.NotifyIcon derives its default
    /// GUID from the executable path, so the icon's registered settings, position, "show in
    /// taskbar", reset the first time the exe moves.
    /// </summary>
    private static readonly Guid TrayIconId = new("2f8b6f4e-9d3a-4c17-9b52-6a1d4f0e7c38");

    private TaskbarIcon BuildTray()
    {
        var icon = new TaskbarIcon
        {
            Id = TrayIconId,
            MenuActivation = PopupActivationMode.RightClick,
        };

        icon.TrayLeftMouseUp += (_, _) => ShowMainWindow();

        // The notification's action. Both designed notices carry one, and neither can be a button
        // on a classic balloon - see Notify. Consumed on click so a stale action cannot fire from
        // a later, unrelated notification.
        icon.TrayBalloonTipClicked += (_, _) =>
        {
            var action = pendingNotificationAction;
            pendingNotificationAction = null;
            action?.Invoke();
        };

        // Built in code rather than declared in a window's XAML, so nothing loads it into a
        // visual tree and the icon would never appear without this.
        icon.ForceCreate();

        return icon;
    }

    /// <summary>
    /// Builds the menu from scratch, and does so again on every theme change.
    ///
    /// A tray icon's ContextMenu has no parent in ANY visual tree, so the resources-changed
    /// notification that an Application.Resources swap raises never reaches it: its
    /// DynamicResources resolve once, when it is loaded, and then never again. Neither closing
    /// and reopening the menu nor an UpdateLayout refreshes them. Rebuilding is what makes a
    /// live theme change actually visible in the menu, which is the whole point of following the
    /// OS rather than reading it once at startup.
    /// </summary>
    private void RebuildTrayMenu()
    {
        if (tray is null) return;

        var dictionary = new ResourceDictionary
        {
            Source = new Uri(
                "pack://application:,,,/WaveLinkBackup;component/Views/TrayIcon.xaml",
                UriKind.Absolute),
        };

        trayMenu = (ContextMenu)dictionary["TrayMenu"];
        WireMenu(trayMenu);

        // Opened, not Opening: the popup's HWND does not exist yet at Opening. And every time
        // rather than once, because WPF does not guarantee the same HWND on the next open.
        trayMenu.Opened += (_, _) => ApplyMenuChrome();

        tray.ContextMenu = trayMenu;
    }

    /// <summary>
    /// Rebuild first: <see cref="RefreshTray"/> writes into the menu's items, so refreshing a
    /// menu that is about to be replaced would put the state on the discarded one.
    /// </summary>
    private void OnThemeChanged()
    {
        RebuildTrayMenu();
        RefreshTray();
    }

    /// <summary>
    /// Gives the tray menu the Windows 11 treatment: Acrylic, rounded, and a frame that matches
    /// the OS theme.
    ///
    /// Acrylic rather than Mica because the menu is a TRANSIENT surface, and that is the material
    /// Windows itself uses for one. Mica here would read as an effect someone applied.
    /// </summary>
    private void ApplyMenuChrome()
    {
        if (trayMenu is null || chrome is null) return;

        if (PresentationSource.FromVisual(trayMenu) is not HwndSource source) return;

        var highContrast = systemTheme?.IsHighContrast ?? SystemParameters.HighContrast;
        var dark = (systemTheme?.Theme ?? AppTheme.Dark) != AppTheme.Light;

        var (material, corners) = ChromeChoice.ForTrayMenu(highContrast);

        // The background is the STYLE's business and stays opaque, so nothing here touches it.
        // What DWM contributes is the rounded corner and a frame that matches the OS theme,
        // two things the app genuinely cannot draw for itself.
        chrome.Apply(source.Handle, material, corners, dark);
    }

    private MenuItem Item(string name) =>
        trayMenu!.Items.OfType<MenuItem>().Single(i => i.Name == name);

    private void WireMenu(ContextMenu menu)
    {
        foreach (var item in menu.Items.OfType<MenuItem>())
        {
            // LastBackupHeader is deliberately absent: it is a readout, not an item.
            switch (item.Name)
            {
                case "UpdateAvailableHeader":
                    item.Click += (_, _) => { ShowMainWindow(); OpenSettings(); };
                    break;
                case "BackUpNow": item.Click += (_, _) => BackUpNow(); break;
                case "OpenApp": item.Click += (_, _) => ShowMainWindow(); break;
                case "OpenFolder": item.Click += (_, _) => OpenStoreFolder(); break;
                case "AutoBackup": item.Click += (_, _) => ToggleAutoBackup(); break;
                case "PauseResume": item.Click += (_, _) => TogglePause(); break;
                case "Help": item.Click += (_, _) => OpenHelp(); break;
                case "About": item.Click += (_, _) => OpenAbout(); break;
                case "OpenSettings": item.Click += (_, _) => OpenSettings(); break;
                case "Quit": item.Click += (_, _) => ShutdownEverything(); break;
                default: break;
            }
        }
    }

    /// <summary>
    /// The DPI scale of the screen holding the taskbar, which is the screen the tray icon is drawn
    /// on, and not necessarily the one holding the window, or the primary one.
    ///
    /// Asked of <c>Shell_TrayWnd</c> directly, because that IS the taskbar: any indirection through
    /// a screen rectangle would have to guess which screen it sits on. Falls back to 1.0 when the
    /// window cannot be found or the API is unavailable, which reproduces the fixed 32px this
    /// replaced (technical-debt.md §4.8 minor 1).
    /// </summary>
    private static double TaskbarDpiScale()
    {
        try
        {
            var taskbar = FindWindowW("Shell_TrayWnd", null);
            if (taskbar == IntPtr.Zero) return 1.0;

            var dpi = GetDpiForWindow(taskbar);
            return dpi <= 0 ? 1.0 : dpi / 96.0;
        }
        catch (EntryPointNotFoundException)
        {
            return 1.0;
        }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern IntPtr FindWindowW(string lpClassName, string? lpWindowName);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    /// <summary>Re-render the tray icon at the new taskbar DPI. Marshalled: SystemEvents can
    /// raise on a non-UI thread, and the renderer builds WPF visuals.</summary>
    private void OnDisplaySettingsChanged(object? sender, EventArgs e) =>
        Dispatcher.BeginInvoke(RefreshTray);

    private void RefreshTray()
    {
        // Every line below this touches an object the UI thread owns - the tray icon, its tooltip,
        // three menu items. BackUpNow reaches here from a thread-pool thread (the capture runs off
        // the UI thread so the backing-up bar can move), and setting TaskbarIcon.Icon from there
        // threw and took the whole process with it. See UiThread.
        if (UiThread.Marshal(Dispatcher, RefreshTray)) return;

        if (tray is null || host is null) return;

        var status = TrayState.From(host.Conditions);
        var colour = TrayIconRenderer.ColourFor(
            status, systemTheme?.IsHighContrast ?? SystemParameters.HighContrast);

        // The new icon is installed before the old one is disposed: the shell has taken a copy
        // by then, and freeing it first would flash an empty slot.
        var previous = trayIcon;
        trayIcon = TrayIconRenderer.Render(status, colour, TrayIconRenderer.PixelSizeFor(TaskbarDpiScale()));
        tray.Icon = trayIcon;
        previous?.Dispose();

        // A capture during this run is the only thing that can have changed the store under us.
        if (summaryAsOf != host.LastBackupAt)
        {
            summaryAsOf = host.LastBackupAt;
            RefreshNewest();
        }

        tray.ToolTipText = TrayState.Tooltip(host.Conditions, newest.TakenAt);

        // The nine-day notice (the tray and updates spec, "Notifications - exactly two"). Checked on every tray
        // refresh, which is every host tick, and fired at most once per episode - the decision and
        // the once-ness both live in TrayNotifications so they can be asserted from a table.
        Notify(notifications.NothingBackedUp(newest.TakenAt, DateTimeOffset.Now));

        // The update line and the balloon read the SAME field the strip does, so the three can
        // never disagree. TrayNotifications decides the once-ness; this just asks.
        var update = Item("UpdateAvailableHeader");
        update.Header = updateAvailableVersion is { Length: > 0 } available
            ? $"UPDATE {available} AVAILABLE · INSTALL IN SETTINGS"
            : "UPDATE AVAILABLE";
        update.Visibility = updateAvailableVersion is { Length: > 0 }
            ? Visibility.Visible
            : Visibility.Collapsed;

        Notify(notifications.UpdateAvailable(updateAvailableVersion));

        Item("LastBackupHeader").Header = Readout();
        Item("AutoBackup").IsChecked = host.AutoBackupEnabled;
        Item("PauseResume").Header = host.IsPaused ? "Resume" : "Pause for an hour";
    }

    /// <summary>
    /// List() is newest-first and swallows a per-snapshot IO failure rather than throwing, so an
    /// unreadable store reads as "nothing found" here.
    /// </summary>
    private void RefreshNewest()
    {
        var all = store?.List();

        newest = all is { Count: > 0 }
            ? (all[0].Manifest.CreatedUtc, all[0].Manifest.InputCount)
            : (null, null);
    }

    /// <summary>
    /// The design's readout: a mono label plus what a machine produced.
    /// The tray and updates spec: "LAST BACKUP (mono 10px label + TODAY 23:07 · 5 INPUTS)".
    ///
    /// The day qualifier is not decoration: "23:07" alone is ambiguous the moment a backup is
    /// more than a day old, which for this app is the normal case.
    /// </summary>
    private string Readout()
    {
        if (newest.TakenAt is not { } at) return "LAST BACKUP · NEVER";

        var local = at.ToLocalTime();
        var today = DateTimeOffset.Now.Date;

        var day = local.Date == today ? "TODAY"
            : local.Date == today.AddDays(-1) ? "YESTERDAY"
            : local.ToString("d MMM", CultureInfo.CurrentCulture).ToUpper(CultureInfo.CurrentCulture);

        // A count of zero is NOT shown as "0 INPUTS": an unreadable store and a backup of nothing
        // are very different claims, and only one of them is ours to make.
        var inputs = newest.Inputs switch
        {
            null or 0 => string.Empty,
            1 => " · 1 INPUT",
            var n => $" · {n} INPUTS",
        };

        return $"LAST BACKUP · {day} {local.ToString("HH:mm", CultureInfo.CurrentCulture)}{inputs}";
    }
}
