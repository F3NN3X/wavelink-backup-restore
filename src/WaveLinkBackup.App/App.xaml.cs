using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using H.NotifyIcon;
using H.NotifyIcon.Core;
using WaveLinkBackup.App.Hosting;
using WaveLinkBackup.App.Startup;
using WaveLinkBackup.App.Theming;
using WaveLinkBackup.App.Views;
using WaveLinkBackup.Core.Abstractions;
using WaveLinkBackup.Core.Automation;
using WaveLinkBackup.Core.Discovery;
using WaveLinkBackup.Core.Io;
using WaveLinkBackup.Core.Snapshots;

namespace WaveLinkBackup.App;

/// <summary>
/// The app is the PROCESS, not the window.
///
/// "Configured once, then ignored — so it lives in the tray and the window is the exception."
/// If closing the window stopped the backups, the app would fail its own promise and become
/// upstream's tool with extra steps. So: OnExplicitShutdown, the coordinator lives here and
/// outlives every window, and closing hides.
/// </summary>
public partial class App : Application
{
    private const string InstanceName = "WaveLinkBackup";

    /// <summary>
    /// Fixed, and pinned here rather than left to the library. H.NotifyIcon derives its default
    /// GUID from the executable path, so the icon's registered settings — position, "show in
    /// taskbar" — reset the first time the exe moves.
    /// </summary>
    private static readonly Guid TrayIconId = new("2f8b6f4e-9d3a-4c17-9b52-6a1d4f0e7c38");

    private SingleInstance? instance;
    private BackupHost? host;
    private BackupService? service;
    private TaskbarIcon? tray;
    private ContextMenu? trayMenu;
    private System.Drawing.Icon? trayIcon;
    private DispatcherTimer? timer;
    private SettingsRepository? settingsRepository;
    private BackupSettings settings = BackupSettings.Default;
    private bool shuttingDown;

    /// <summary>Read by MainWindow: closing hides, unless the app is on its way out.</summary>
    internal bool IsShuttingDown => shuttingDown;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var arguments = ShellArguments.Parse(e.Args);
        if (!arguments.IsValid)
        {
            MessageBox.Show(arguments.Error, "Wave Link Backup", MessageBoxButton.OK, MessageBoxImage.Warning);
            Shutdown(1);
            return;
        }

        // BEFORE any Core object is built: a second instance must cost nothing.
        instance = SingleInstance.TryAcquire(InstanceName);
        if (!instance.IsFirst)
        {
            instance.SignalExistingInstance(wantsWindow: !arguments.StartInTray);
            Shutdown(0);
            return;
        }

        instance.ActivationRequested += (_, _) => Dispatcher.Invoke(ShowMainWindow);
        instance.StartListening();

        // Set before anything exists that could close.
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        ThemeManager.Apply(ThemeManager.DetectFromSystem());

        var fileSystem = new FileSystem();
        settingsRepository = new SettingsRepository(fileSystem, SettingsRepository.DefaultDirectory);
        settings = arguments.ApplyTo(settingsRepository.Read());

        (host, service) = Compose(fileSystem, settings);
        host.AutoBackupEnabled = settings.AutoBackupEnabled;
        host.Start();

        tray = BuildTray();

        timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(15),
        };
        timer.Tick += (_, _) => { host.Tick(); RefreshTray(); };
        timer.Start();

        // Windows shutting down is a shutdown path too — and the ORIGINAL INCIDENT happened
        // during an update, while the machine was restarting. A shell that only captures on a
        // deliberate Quit misses the exact case CaptureOnShutdown was written for.
        SessionEnding += (_, _) => ShutdownEverything();

        if (!arguments.StartInTray) ShowMainWindow();

        RefreshTray();
    }

    private static (BackupHost Host, BackupService Service) Compose(
        IFileSystem fileSystem, BackupSettings settings)
    {
        var clock = new SystemClock();
        var inspector = SettingsInspector.For(fileSystem, SettingsLocator.SystemLocalAppData);
        var store = new SnapshotStore(fileSystem, clock, settings.StorePath);

        var service = new BackupService(
            inspector, store, settings.AutoBackupKeepCount, settings.ChosenWaveLinkPath);

        // Watch the installation we actually found. Falling back to LocalAppData means the
        // watcher still starts when Wave Link is missing — it just never fires, and the tray
        // says why rather than the process refusing to run.
        var live = inspector.Inspect(settings.ChosenWaveLinkPath);
        var watchPath = live.IsSuccess
            ? live.Value.Location.LocalStatePath
            : SettingsLocator.SystemLocalAppData;

        var coordinator = new AutoBackupCoordinator(
            new FileSystemSettingsWatcher(watchPath), service, clock);

        return (new BackupHost(coordinator, clock), service);
    }

    private TaskbarIcon BuildTray()
    {
        trayMenu = (ContextMenu)Resources["TrayMenu"];
        WireMenu(trayMenu);

        var icon = new TaskbarIcon
        {
            Id = TrayIconId,
            ContextMenu = trayMenu,
            MenuActivation = PopupActivationMode.RightClick,
        };

        icon.TrayLeftMouseUp += (_, _) => ShowMainWindow();

        // Built in code rather than declared in a window's XAML, so nothing loads it into a
        // visual tree and the icon would never appear without this.
        icon.ForceCreate();

        return icon;
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
                case "BackUpNow": item.Click += (_, _) => BackUpNow(); break;
                case "OpenApp": item.Click += (_, _) => ShowMainWindow(); break;
                case "OpenFolder": item.Click += (_, _) => OpenStoreFolder(); break;
                case "AutoBackup": item.Click += (_, _) => ToggleAutoBackup(); break;
                case "PauseResume": item.Click += (_, _) => TogglePause(); break;
                case "OpenSettings": item.Click += (_, _) => OpenSettings(); break;
                case "Quit": item.Click += (_, _) => ShutdownEverything(); break;
                default: break;
            }
        }
    }

    private void BackUpNow()
    {
        var result = service!.BackUpNow("Manual");

        if (!result.IsSuccess)
        {
            // The twelve designed error screens are a later session; until then the failure is
            // reported plainly rather than swallowed.
            MessageBox.Show(result.Error!.Message, "Wave Link Backup",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        RefreshTray();
    }

    private void OpenStoreFolder() =>
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{settings.StorePath}\""));

    private void ToggleAutoBackup()
    {
        host!.AutoBackupEnabled = !host.AutoBackupEnabled;

        settings = settings with { AutoBackupEnabled = host.AutoBackupEnabled };
        settingsRepository!.Save(settings);

        RefreshTray();
    }

    private void TogglePause()
    {
        if (host!.IsPaused) host.Resume();
        else host.PauseFor(TimeSpan.FromHours(1));

        RefreshTray();
    }

    private static void OpenSettings() =>
        MessageBox.Show("Settings arrive in the next plan.", "Wave Link Backup",
            MessageBoxButton.OK, MessageBoxImage.Information);

    private void RefreshTray()
    {
        if (tray is null || host is null) return;

        var status = TrayState.From(host.Conditions);
        var colour = TrayIconRenderer.ColourFor(status, SystemParameters.HighContrast);

        // The new icon is installed before the old one is disposed: the shell has taken a copy
        // by then, and freeing it first would flash an empty slot.
        var previous = trayIcon;
        trayIcon = TrayIconRenderer.Render(status, colour);
        tray.Icon = trayIcon;
        previous?.Dispose();

        tray.ToolTipText = TrayState.Tooltip(host.Conditions, host.LastBackupAt);

        Item("LastBackupHeader").Header = host.LastBackupAt is { } at
            ? $"LAST BACKUP · {at.ToLocalTime().ToString("HH:mm", CultureInfo.CurrentCulture)}"
            : "LAST BACKUP · NEVER";

        Item("AutoBackup").IsChecked = host.AutoBackupEnabled;
        Item("PauseResume").Header = host.IsPaused ? "Resume" : "Pause for an hour";
    }

    private void ShowMainWindow()
    {
        MainWindow ??= new MainWindow();

        // Closing HIDES it, so a window that exists may simply be invisible.
        MainWindow.Show();
        if (MainWindow.WindowState == WindowState.Minimized) MainWindow.WindowState = WindowState.Normal;
        MainWindow.Activate();
    }

    /// <summary>
    /// The single exit. Three entrances reach it: the tray's Quit, closing the window when
    /// "closing hides it" is off, and Windows ending the session.
    /// </summary>
    internal void ShutdownEverything()
    {
        if (shuttingDown) return;
        shuttingDown = true;

        timer?.Stop();
        host?.Stop();
        host?.CaptureOnShutdown();

        tray?.Dispose();
        trayIcon?.Dispose();
        host?.Dispose();
        instance?.Dispose();

        Shutdown(0);
    }
}
