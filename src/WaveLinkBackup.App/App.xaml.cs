using System.Diagnostics;
using System.Globalization;
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
using WaveLinkBackup.App.Theming;
using WaveLinkBackup.App.ViewModels;
using WaveLinkBackup.App.Views;
using WaveLinkBackup.App.Windows;
using WaveLinkBackup.Core.Abstractions;
using WaveLinkBackup.Core.Automation;
using WaveLinkBackup.Core.Discovery;
using WaveLinkBackup.Core.Io;
using WaveLinkBackup.Core.Process;
using WaveLinkBackup.Core.Results;
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
    private ISystemTheme? systemTheme;
    private IWindowChrome? chrome;
    private BackupHost? host;
    private BackupService? service;
    private SnapshotStore? store;
    private IWaveLinkProcess? waveLinkProcess;
    private TaskbarIcon? tray;
    private ContextMenu? trayMenu;
    private System.Drawing.Icon? trayIcon;
    private DispatcherTimer? timer;
    private SettingsRepository? settingsRepository;
    private ShellStateRepository? shellStateRepository;
    private IRestoreService? restoreService;
    private BackupSettings settings = BackupSettings.Default;
    private bool shuttingDown;

    /// <summary>
    /// The window's whole data model - held here, not just handed to MainWindow, because
    /// RefreshShellFacts (the 15-second tick) has to reach ShellViewModel.Apply even while no
    /// window is open (the app starts hidden when launched with --start-in-tray).
    /// </summary>
    private ShellViewModel? shell;

    /// <summary>Kept for RefreshShellFacts, which re-checks the live installation on every tick.</summary>
    private IFileSystem? fileSystem;

    /// <summary>Read by MainWindow at construction, and updated by every SaveGeometry.</summary>
    internal ShellState ShellState { get; private set; } = ShellState.Default;

    /// <summary>
    /// The newest snapshot, for the menu readout and the tooltip.
    ///
    /// Read from the STORE rather than from BackupHost.LastBackupAt, which only knows about
    /// captures made during this run — so a freshly started app would say "no backup yet" with
    /// thirty backups on disk.
    /// </summary>
    private (DateTimeOffset? TakenAt, int? Inputs) newest;

    /// <summary>What LastBackupAt was when <see cref="newest"/> was last read.</summary>
    private DateTimeOffset? summaryAsOf;

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

        // Applied before anything is drawn; Follow (below, once the tray exists) starts the
        // listening and re-applies on every OS change.
        systemTheme = new UiSettingsTheme();
        chrome = new DwmWindowChrome();
        ThemeManager.Apply(systemTheme.Theme, systemTheme.Accent);

        fileSystem = new FileSystem();
        settingsRepository = new SettingsRepository(fileSystem, SettingsRepository.DefaultDirectory);

        shellStateRepository = new ShellStateRepository(fileSystem, SettingsRepository.DefaultDirectory);
        ShellState = shellStateRepository.Read();

        settings = arguments.ApplyTo(settingsRepository.Read());

        (host, service, store, waveLinkProcess, restoreService, shell) = Compose(fileSystem, settings);
        host.AutoBackupEnabled = settings.AutoBackupEnabled;
        host.Start();

        // Once, before anything is shown: the readout and the tooltip must be right on the first
        // frame, not after the first capture of this run. The shell's own facts are the same
        // idea, one level up - Screen 1's status strip must be right before the window ever
        // shows, not after the first 15-second tick.
        RefreshNewest();
        RefreshShellFacts();

        tray = BuildTray();

        // Now that there is an icon to repaint, follow the OS. screens/11 requires high contrast
        // to be reacted to at runtime rather than needing a restart, and the same is true of
        // dark/light and the accent. This first call is also what builds the menu.
        ThemeManager.Follow(systemTheme, OnThemeChanged);

        timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(15),
        };
        timer.Tick += (_, _) => { host.Tick(); RefreshTray(); RefreshShellFacts(); };
        timer.Start();

        // Windows shutting down is a shutdown path too — and the ORIGINAL INCIDENT happened
        // during an update, while the machine was restarting. A shell that only captures on a
        // deliberate Quit misses the exact case CaptureOnShutdown was written for.
        SessionEnding += (_, _) => ShutdownEverything();

        if (!arguments.StartInTray) ShowMainWindow();

        RefreshTray();
    }

    private static (
        BackupHost Host, BackupService Service, SnapshotStore Store, IWaveLinkProcess WaveLinkProcess,
        IRestoreService RestoreService, ShellViewModel Shell)
        Compose(IFileSystem fileSystem, BackupSettings settings)
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

        // The window's own data model. Built here rather than in MainWindow's constructor so it
        // exists (and RefreshShellFacts can reach it) even before any window is ever shown - the
        // app can start hidden in the tray. Marshal is NOT set here: SnapshotListViewModel
        // defaults to running inline, which is correct for every caller except the one that
        // calls RefreshAsync, and that caller (MainWindow) sets it itself before its own first
        // RefreshAsync - see MainWindow.xaml.cs.
        var list = new SnapshotListViewModel(store, new HealthProbe(store, fileSystem, clock), fileSystem, clock);
        var shell = new ShellViewModel(list);

        // The shell-facing restore seam. Built here rather than in MainWindow so it exists even
        // before any window is shown - the same reason the shell VM is built here. It wraps Core's
        // RestoreOrchestrator; the view-model never touches a Wave Link process API itself.
        var waveLinkProcess = new WaveLinkProcess();
        var restoreService = new RestoreService(fileSystem, waveLinkProcess, store);

        return (new BackupHost(coordinator, clock), service, store, waveLinkProcess, restoreService, shell);
    }

    private TaskbarIcon BuildTray()
    {
        var icon = new TaskbarIcon
        {
            Id = TrayIconId,
            MenuActivation = PopupActivationMode.RightClick,
        };

        icon.TrayLeftMouseUp += (_, _) => ShowMainWindow();

        // Built in code rather than declared in a window's XAML, so nothing loads it into a
        // visual tree and the icon would never appear without this.
        icon.ForceCreate();

        return icon;
    }

    /// <summary>
    /// Builds the menu from scratch, and does so again on every theme change.
    ///
    /// A tray icon's ContextMenu has no parent in ANY visual tree, so the resources-changed
    /// notification that an Application.Resources swap raises never reaches it — its
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
        // What DWM contributes is the rounded corner and a frame that matches the OS theme —
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

    /// <summary>
    /// Internal, and returning the Result, so MainWindow's own "Back up now" button can select
    /// the row the capture just wrote (README: "Back up now inserts a row at the top of TODAY
    /// and selects it") - the tray menu's own entry point keeps discarding it, same as before.
    /// </summary>
    internal Result<Snapshot> BackUpNow()
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
        RefreshShellFacts();

        return result;
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

    /// <summary>Internal so MainWindow's own gear button can call the same placeholder.</summary>
    internal static void OpenSettings() =>
        MessageBox.Show("Settings arrive in the next plan.", "Wave Link Backup",
            MessageBoxButton.OK, MessageBoxImage.Information);

    private void RefreshTray()
    {
        if (tray is null || host is null) return;

        var status = TrayState.From(host.Conditions);
        var colour = TrayIconRenderer.ColourFor(
            status, systemTheme?.IsHighContrast ?? SystemParameters.HighContrast);

        // The new icon is installed before the old one is disposed: the shell has taken a copy
        // by then, and freeing it first would flash an empty slot.
        var previous = trayIcon;
        trayIcon = TrayIconRenderer.Render(status, colour);
        tray.Icon = trayIcon;
        previous?.Dispose();

        // A capture during this run is the only thing that can have changed the store under us.
        if (summaryAsOf != host.LastBackupAt)
        {
            summaryAsOf = host.LastBackupAt;
            RefreshNewest();
        }

        tray.ToolTipText = TrayState.Tooltip(host.Conditions, newest.TakenAt);

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
    /// screens/12: "LAST BACKUP (mono 10px label + TODAY 23:07 · 5 INPUTS)".
    ///
    /// The day qualifier is not decoration — "23:07" alone is ambiguous the moment a backup is
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

    /// <summary>
    /// The status strip's five facts, re-read from the live installation and re-applied to the
    /// shell. Called once before the window is ever shown and again on every 15-second tick,
    /// alongside RefreshTray - the tray icon and the status strip are two readouts of the same
    /// underlying state and neither should be able to go stale while the other updates.
    /// </summary>
    private void RefreshShellFacts()
    {
        if (shell is null || fileSystem is null) return;

        var inspection = SettingsInspector.For(fileSystem, SettingsLocator.SystemLocalAppData)
            .Inspect(settings.ChosenWaveLinkPath);

        var savedAt = inspection.IsSuccess
            ? new DateTimeOffset(
                fileSystem.GetLastWriteTimeUtc(inspection.Value.Location.SettingsPath), TimeSpan.Zero)
                .ToLocalTime()
            : (DateTimeOffset?)null;

        shell.Apply(new ShellFacts(
            WaveLinkFound: inspection.IsSuccess,
            WaveLinkRunning: waveLinkProcess?.IsRunning ?? false,
            SettingsLastSavedLocal: savedAt,
            AutoBackupEnabled: host?.AutoBackupEnabled ?? false,
            FolderMissing: !fileSystem.DirectoryExists(settings.StorePath),
            StorePath: settings.StorePath,
            FreeBytes: fileSystem.GetAvailableFreeBytes(settings.StorePath)));
    }

    private void ShowMainWindow()
    {
        // The window inspects live settings itself at the moment of a restore, so the plan and the
        // write describe the same "what is on disk right now". This closure reads them fresh from
        // the locator + chosen path rather than trusting a copy held by the 15-second tick. It
        // returns the Result, not a bare inspection: Wave Link may be missing or its file unreadable
        // at that moment, and the window must surface that rather than crash on an unwrap.
        var inspectLive = () => SettingsInspector.For(fileSystem!, SettingsLocator.SystemLocalAppData)
            .Inspect(settings.ChosenWaveLinkPath);

        MainWindow ??= new Views.MainWindow(
            chrome!, systemTheme!, ShellState, shell!, restoreService!, inspectLive);

        // Closing HIDES it, so a window that exists may simply be invisible.
        MainWindow.Show();
        if (MainWindow.WindowState == WindowState.Minimized) MainWindow.WindowState = WindowState.Normal;
        MainWindow.Activate();
    }

    /// <summary>Called from the window's Closing, so geometry survives a hide as well as an exit.</summary>
    internal void SaveGeometry(Views.MainWindow window) =>
        shellStateRepository?.Save(window.CurrentGeometry(ShellState.ClosingHidesToTray));

    /// <summary>
    /// The single exit. Three entrances reach it: the tray's Quit, closing the window when
    /// "closing hides it" is off, and Windows ending the session.
    /// </summary>
    internal void ShutdownEverything()
    {
        if (shuttingDown) return;
        shuttingDown = true;

        if (MainWindow is Views.MainWindow main) SaveGeometry(main);

        // First, not last: shell.List owns a HealthProbe run on a background thread that reports
        // back through Marshal (MainWindow.xaml.cs: `action => Dispatcher.Invoke(action)`), a
        // delegate closed over a window whose dispatcher this very method is about to tear down.
        // Disposing cancels HealthProbe's CancellationTokenSource, which HealthProbe.ProbeAsync
        // checks immediately before every report() call - so cancelling as early as possible here
        // gives that check the most time to land before Shutdown(0) below actually stops the
        // dispatcher pumping, shrinking the window where a probe callback could try to marshal
        // through a dispatcher that is no longer there.
        shell?.List.Dispose();

        timer?.Stop();
        host?.Stop();
        host?.CaptureOnShutdown();

        tray?.Dispose();
        trayIcon?.Dispose();
        host?.Dispose();

        // SystemEvents holds a static subscription; leaving it attached keeps the process alive
        // past Shutdown, which on a tray app is indistinguishable from a leak.
        systemTheme?.Dispose();
        instance?.Dispose();

        Shutdown(0);
    }
}
