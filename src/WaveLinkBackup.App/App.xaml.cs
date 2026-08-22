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

    /// <summary>
    /// The OS palette WITH the user's preference applied - a <see cref="PreferredTheme"/> wrapped
    /// around the real <see cref="UiSettingsTheme"/>. Everything downstream (ThemeManager.Follow,
    /// the window's chrome, the tray icon and menu, every IsHighContrast) reads this one, so the
    /// preference reaches all of them without any of them knowing it exists.
    /// </summary>
    private PreferredTheme? systemTheme;
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
    /// Error 2 (06-errors.md) asks at most once per process. The chooser fires the first time a
    /// tick finds more than one Wave Link installation and none has been chosen yet; after the user
    /// picks, cancels, or an install is later found uniquely, this stays set so the dialog never
    /// re-appears on every 15-second tick.
    /// </summary>
    private bool error2Prompted;

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
    /// The Run-key seam, held because BOTH the shell view model and the Settings dialog's
    /// WHEN WINDOWS STARTS section read it (screens/12).
    /// </summary>
    private IAutostart? autostart;

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

        // BEFORE the mutex and before ANY file is read, for the same reason the headless restore
        // is - and one more. This copy is running from a staging directory that is about to be
        // renamed into place, so it must do nothing but wait, swap and relaunch. Reading settings
        // here would be reading them from a path that is about to stop existing.
        if (arguments.IsApplyingUpdate)
        {
            var installer = new UpdateInstaller();
            var target = arguments.ApplyUpdateInstallDirectory!;

            var swapped = installer.Apply(
                arguments.ApplyUpdateForProcessId!.Value, target, TimeSpan.FromSeconds(30));

            // Relaunch either way: on success the new install, on failure the old one that was put
            // back. The one thing this must never do is leave the user with nothing running.
            UpdateInstaller.Relaunch(
                target, System.IO.Path.GetFileName(Environment.ProcessPath ?? "WaveLinkBackup.exe"));

            Shutdown(swapped ? 0 : 1);
            return;
        }

        // BEFORE the mutex, because this copy is not an instance of the app - it is one restore,
        // started elevated by the copy the user is looking at, and the mutex would make it exit
        // without doing the thing it was started for (screens/13-elevation.md).
        if (arguments.IsHeadlessRestore)
        {
            var elevatedFileSystem = new FileSystem();
            var elevatedSettings = arguments.ApplyTo(
                new SettingsRepository(elevatedFileSystem, SettingsRepository.DefaultDirectory).Read());

            Shutdown(HeadlessRestore.Run(
                arguments, elevatedSettings, elevatedFileSystem, new WaveLinkProcess(), new SystemClock()));
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

        // BeginInvoke, not Invoke: the listener thread has no reason to block on the window
        // being shown, and a queued activation that arrives before the dispatcher is ready must
        // not deadlock waiting for it. ShowMainWindow already de-minimizes and activates.
        instance.ActivationRequested += (_, _) => Dispatcher.BeginInvoke(ShowMainWindow);
        instance.StartListening();

        // Set before anything exists that could close.
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        // Applied before anything is drawn; Follow (below, once the tray exists) starts the
        // listening and re-applies on every OS change.
        chrome = new DwmWindowChrome();

        fileSystem = new FileSystem();
        settingsRepository = new SettingsRepository(fileSystem, SettingsRepository.DefaultDirectory);

        // Read BEFORE the first Apply: the theme preference lives in here, and applying the OS
        // theme first would paint the window once in the palette the user did not ask for.
        shellStateRepository = new ShellStateRepository(fileSystem, SettingsRepository.DefaultDirectory);
        ShellState = shellStateRepository.Read();

        systemTheme = new PreferredTheme(new UiSettingsTheme(), () => ShellState.Theme);
        ThemeManager.Apply(systemTheme.Theme, systemTheme.Accent);

        settings = arguments.ApplyTo(settingsRepository.Read());

        (host, service, store, waveLinkProcess, restoreService, shell, autostart) =
            Compose(fileSystem, settings, GatherPayload);
        host.AutoBackupEnabled = settings.AutoBackupEnabled;
        host.Start();

        // Once, before anything is shown: the readout and the tooltip must be right on the first
        // frame, not after the first capture of this run. The shell's own facts are the same
        // idea, one level up - Screen 1's status strip must be right before the window ever
        // shows, not after the first 15-second tick.
        RefreshNewest();
        RefreshShellFacts();
        shell.RefreshAutostart();

        tray = BuildTray();

        // A monitor being plugged in, unplugged, or rescaled can move the taskbar to a screen with
        // a different DPI, and the tray icon is a BITMAP - it does not reflow, it just gets soft.
        // Re-rendering on the change is the whole of technical-debt.md §4.8 minor 1's second half.
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;

        // Now that there is an icon to repaint, follow the OS. screens/11 requires high contrast
        // to be reacted to at runtime rather than needing a restart, and the same is true of
        // dark/light and the accent. This first call is also what builds the menu.
        ThemeManager.Follow(systemTheme, OnThemeChanged);

        timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(15),
        };
        // The tick captures to disk when due; the list must re-read so a new automatic snapshot
        // appears without a restart or an F5. Only a REAL capture re-reads - a deduped or skipped
        // tick changed nothing, and RefreshAsync would cancel-and-rerun the health probe for no reason.
        timer.Tick += (_, _) =>
        {
            var tick = host.Tick();
            if (tick.Captured) _ = shell.List.RefreshAsync();
            RefreshTray();
            RefreshShellFacts();
            shell.RefreshAutostart();
        };
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
        IRestoreService RestoreService, ShellViewModel Shell, IAutostart Autostart)
        Compose(
            IFileSystem fileSystem,
            BackupSettings settings,
            Func<SettingsInspection, SnapshotPayload?> gatherPayload)
    {
        var clock = new SystemClock();
        var inspector = SettingsInspector.For(fileSystem, SettingsLocator.SystemLocalAppData);
        var store = new SnapshotStore(fileSystem, clock, settings.StorePath);

        // Tiers 1-extra, 2, 3 and 4. A closure rather than a held record, so switching a tier in
        // the Settings dialog takes effect on the next capture instead of the next launch.
        var service = new BackupService(
            inspector, store, settings.AutoBackupKeepCount, settings.ChosenWaveLinkPath,
            gatherPayload);

        // Watch the installation we actually found. Falling back to LocalAppData means the
        // watcher still starts when Wave Link is missing — it just never fires, and the tray
        // says why rather than the process refusing to run.
        var live = inspector.Inspect(settings.ChosenWaveLinkPath);
        var watchPath = live.IsSuccess
            ? live.Value.Location.LocalStatePath
            : SettingsLocator.SystemLocalAppData;

        // The user's own timings (screens/14-backup-timing.md): the interval cap they chose, and
        // the daily copy if they switched one on. AutoBackupPolicy.Default is the pair of constants
        // these two settings replaced.
        var coordinator = new AutoBackupCoordinator(
            new FileSystemSettingsWatcher(watchPath), service, clock, AutoBackupPolicy.For(settings));

        // The window's own data model. Built here rather than in MainWindow's constructor so it
        // exists (and RefreshShellFacts can reach it) even before any window is ever shown - the
        // app can start hidden in the tray. Marshal is NOT set here: SnapshotListViewModel
        // defaults to running inline, which is correct for every caller except the one that
        // calls RefreshAsync, and that caller (MainWindow) sets it itself before its own first
        // RefreshAsync - see MainWindow.xaml.cs.
        var list = new SnapshotListViewModel(store, new HealthProbe(store, fileSystem, clock), fileSystem, clock);

        // The autostart seam (screens/12): the Run key for THIS executable. ProcessPath is null
        // until the process has started, so it is resolved at composition time - by then
        // App.xaml.cs has run and Environment.ProcessPath points at the real exe.
        var runKeyAutostart = new RunKeyAutostart(
            new WindowsRegistryKeys(), Environment.ProcessPath ?? string.Empty);
        var shell = new ShellViewModel(list, runKeyAutostart);

        // The shell-facing restore seam. Built here rather than in MainWindow so it exists even
        // before any window is shown - the same reason the shell VM is built here. It wraps Core's
        // RestoreOrchestrator; the view-model never touches a Wave Link process API itself.
        var waveLinkProcess = new WaveLinkProcess();
        var restoreService = new RestoreService(fileSystem, waveLinkProcess, store, gatherPayload);

        return (new BackupHost(coordinator, clock), service, store, waveLinkProcess, restoreService, shell, runKeyAutostart);
    }

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
                case "Help": item.Click += (_, _) => OpenHelp(); break;
                case "About": item.Click += (_, _) => OpenAbout(); break;
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
    /// <param name="progress">
    /// Byte-level progress for the window's backing-up strip. Null from the tray menu, which has
    /// no strip to drive.
    /// </param>
    internal Result<Snapshot> BackUpNow(IProgress<SnapshotWriteProgress>? progress = null)
    {
        var result = service!.BackUpNow("Manual", progress: progress);

        // No message box here: MainWindow.BackUpNowAsync renders the failure as one of the twelve
        // designed errors (06-errors.md) - inline strip for 3/5, message box otherwise. The tray
        // menu's own entry point discards this Result and needs no reporting of its own.

        RefreshTray();
        RefreshShellFacts();

        return result;
    }

    // Internal so the settings dialog's "Open" button launches Explorer at the same folder the tray
    // menu's "Open folder" item does - one seam, two entry points.
    internal void OpenStoreFolder() =>
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{settings.StorePath}\""));

    private void ToggleAutoBackup() => SetAutoBackup(!host!.AutoBackupEnabled);

    /// <summary>
    /// Whether the watcher is running, set to an absolute value rather than flipped.
    ///
    /// Internal because Screen 4's own checkbox — "Keep backing up on its own when my settings
    /// change" — is the first-run screen's one setting, and it needs to say what it means rather
    /// than what the last press did. It was `IsChecked="True"` in the XAML and wired to nothing:
    /// a control that showed a state it did not read and changed a setting it did not write.
    /// </summary>
    internal void SetAutoBackup(bool enabled)
    {
        if (host is null || settingsRepository is null) return;
        if (host.AutoBackupEnabled == enabled) return;

        host.AutoBackupEnabled = enabled;

        settings = settings with { AutoBackupEnabled = enabled };
        settingsRepository.Save(settings);

        RefreshTray();
        RefreshShellFacts();
    }

    /// <summary>The current value, so a control can start out showing the truth.</summary>
    internal bool AutoBackupEnabled => host?.AutoBackupEnabled ?? settings.AutoBackupEnabled;

    private void TogglePause()
    {
        if (host!.IsPaused) host.Resume();
        else host.PauseFor(TimeSpan.FromHours(1));

        RefreshTray();
    }

    /// <summary>
    /// Internal so MainWindow's own gear button can open the same dialog. Builds a fresh view-model
    /// each time (the file may have changed while the window was closed) and shows it modally over
    /// the main window when one is open, otherwise as a standalone modal.
    /// </summary>
    /// <summary>
    /// Error 8's "Get the update": Settings, at UPDATES, with a check already running so the new
    /// version's row is showing by the time the user looks at it (screens/12).
    ///
    /// The check is the ONLY thing started automatically here. Installing is still a press — "It
    /// never installs anything without you" holds on this path exactly as it does on every other.
    /// </summary>
    internal void OpenSettingsAtUpdates() => OpenSettings(scrollToUpdates: true);

    internal void OpenSettings() => OpenSettings(scrollToUpdates: false);

    /// <summary>
    /// "What's in this backup": read the snapshot's own settings file, describe it, show it.
    ///
    /// The READ lives here rather than in the window because the window has neither the store nor
    /// the file system. It is a single read of a file that is typically 47 KB and always local, on
    /// a press - so it is synchronous, and a store on a sleeping network drive is the only case
    /// where that is felt, which is the same trade every other row action already makes.
    ///
    /// Every failure - a snapshot that has gone, a file that cannot be read, one that is not
    /// settings at all - becomes a sentence INSIDE the dialog rather than a refusal to open it.
    /// A damaged backup is precisely when someone wants to know what was in it.
    /// </summary>
    internal void OpenSnapshotDetails(Window owner, string snapshotId)
    {
        if (store is null || fileSystem is null) return;

        var found = store.Get(snapshotId);
        if (!found.IsSuccess) return;

        var snapshot = found.Value;

        Result<ConfigurationDetail> read;
        try
        {
            read = ConfigurationDetail.Read(fileSystem.ReadSharedBytes(snapshot.SettingsPath));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            read = Result<ConfigurationDetail>.Fail(
                new SettingsUnreadable(snapshot.SettingsPath, ex.Message));
        }

        var dialog = new Views.SnapshotDetailsDialog(SnapshotDetailsModel.For(snapshot, read));

        if (owner.IsLoaded)
        {
            dialog.Owner = owner;
            dialog.ShowDialog();
        }
        else
        {
            dialog.Show();
        }
    }

    private void OpenSettings(bool scrollToUpdates)
    {
        var vm = BuildSettingsViewModel();
        var dialog = new Views.SettingsDialog(vm) { ScrollToUpdates = scrollToUpdates };
        ShowOverMainWindow(dialog);
    }

    /// <summary>
    /// Internal so MainWindow's own "?" button can open the same dialog. The content is static copy
    /// (HelpDialogModel.Default), so there is nothing to build fresh each time - but the owner
    /// handling is the same as Settings': modal over the main window when one is open, standalone
    /// otherwise (the tray menu's entry point runs with no window at all).
    /// </summary>
    internal void OpenHelp() => ShowOverMainWindow(new Views.HelpDialog(ViewModels.HelpDialogModel.Build()));

    /// <summary>
    /// Internal so MainWindow can open it if a future revision adds an entry point there. The model
    /// is built fresh each time because its version and links are read from the assembly and
    /// environment at construction - the same "build a fresh view-model each time" rule as Settings.
    /// </summary>
    internal void OpenAbout() => ShowOverMainWindow(new Views.AboutDialog(ViewModels.AboutDialogModel.Build()));

    /// <summary>
    /// Shows a dialog modally over the main window when one is open, standalone otherwise. The two
    /// lines every "open a dialog" seam used to repeat - Settings above, Help and About below - in
    /// one place, so a third dialog does not copy them again.
    /// </summary>
    private static void ShowOverMainWindow(Window dialog)
    {
        var owner = (Application.Current as App)?.MainWindow is { } main && main.IsLoaded ? main : null;
        if (owner is not null)
        {
            dialog.Owner = owner;
            dialog.ShowDialog();
        }
        else
        {
            dialog.Show();
        }
    }

    /// <summary>
    /// Save a settings change AND make it true of the running app. The Settings dialog has no Save
    /// button - every control commits immediately - so this is what "commits immediately" has to
    /// mean: written to disk, and reflected in the objects already running.
    ///
    /// Only writing the file was the old behaviour, and it made every control on that screen a
    /// control that appeared not to work until the next launch. The tier toggles were the visible
    /// case (App holds the record GatherPayload closes over, and it stayed stale), the automatic-
    /// backup switch the quiet one.
    /// </summary>
    private bool ApplySettings(BackupSettings next)
    {
        if (!settingsRepository!.Save(next).IsSuccess) return false;

        settings = next;

        if (host is not null)
        {
            host.AutoBackupEnabled = next.AutoBackupEnabled;
            host.Policy = AutoBackupPolicy.For(next);
        }

        return true;
    }

    /// <summary>
    /// Remember the theme the user picked, and repaint.
    ///
    /// <see cref="PreferredTheme.Refresh"/> rather than a call to ThemeManager.Apply: the wrapper
    /// raises the same Changed event an OS switch raises, and every subscriber - ThemeManager's own
    /// Follow, the main window's chrome and IsHighContrast, the tray menu and icon - is already
    /// wired to that. One route, so a preference change cannot repaint less than a Windows change
    /// does.
    /// </summary>
    internal void SetThemePreference(ThemePreference preference)
    {
        if (ShellState.Theme == preference) return;

        ShellState = ShellState with { Theme = preference };
        shellStateRepository?.Save(ShellState);

        systemTheme?.Refresh();
    }

    /// <summary>
    /// The settings view-model, built from the live store and current settings. Exposed separately
    /// so tests can drive the two sections (folder + when-to-back-up) without a window: they read
    /// the same seams the dialog binds to - the trash row and the free-space figure - and write
    /// through <see cref="SetStorePath"/> on a folder change.
    /// </summary>
    internal SettingsViewModel BuildSettingsViewModel()
    {
        var repo = settingsRepository!;
        // The size is the file's own byte count - read it through the same seam the rest of the
        // app uses (ReadSharedBytes), so a Wave Link lock can't make the figure lie. "not written
        // yet" when the file has never been saved: honest, and matches the mono line's tone.
        var settingsFileBytes = fileSystem!.FileExists(repo.FilePath)
            ? fileSystem.ReadSharedBytes(repo.FilePath).Length
            : 0L;

        var whereLive = new WhereSettingsLiveModel(
            repo.FilePath,
            settingsFileBytes > 0 ? Readable.Bytes(settingsFileBytes) : "not written yet");

        // WHICH WAVE LINK (Task 4): shown only when more than one installation exists and one has
        // been chosen. The locator never guesses between two installs (it returns
        // MultiplePackagesFound instead), so a successful inspect of the CHOSEN path is proof that
        // exactly one install is present - which is precisely when the section must hide itself,
        // because there is nothing to choose. A failure with no candidate is "not installed" or
        // "unreadable", also not this section's business; only MultiplePackagesFound + a chosen
        // path earns it. The version and path come from that live inspection; the CHOSEN date is
        // when our own settings file last recorded the choice (the moment the user picked).
        var whichLive = BuildWhichWaveLink(repo);

        // WHEN WINDOWS STARTS (screens/12): the Run key and the shell's own close behaviour. Both
        // seams were built and tested phases ago with nothing bound to either of them
        // (technical-debt.md §4.21 item 4). Null when autostart is unavailable, which hides the
        // whole section rather than drawing two toggles that write nowhere.
        var startup = autostart is null ? null : new StartupSeam(
            autostart,
            () => ShellState.ClosingHidesToTray,
            hides =>
            {
                ShellState = ShellState with { ClosingHidesToTray = hides };
                shellStateRepository?.Save(ShellState);
            });

        // HOW IT LOOKS: the theme preference. Shell state, not settings.json - and applied through
        // the same PreferredTheme the OS's own changes come through, so a pick here re-themes the
        // window, the dialogs, the tray menu and the tray icon by exactly the route a Windows
        // dark/light switch already did.
        var appearance = new AppearanceSeam(
            () => ShellState.Theme,
            SetThemePreference,
            () => systemTheme?.IsHighContrast ?? SystemParameters.HighContrast);

        var vm = SettingsViewModel.Build(
            settings,
            ApplySettings,
            whereLive,
            whichLive,
            startup,
            appearance);

        // WHAT GOES IN A BACKUP: every figure MEASURED from what a capture would take on this
        // machine right now (phase 6 §7) - never the design mock's 470 KB / 4 KB / 10 MB / 40 MB.
        // A number the user is asked to decide on has to be their number ([[ADR-006]]).
        //
        // Tier 1 is the settings file PLUS Wave Link's own backup copies; the effects list rides
        // inside the settings file, so it carries no separate byte count. The first two rows are
        // locked ON - they have no switch, deliberately - and the other two are the real toggles.
        var estimate = MeasureTiers();

        vm.EstimatedBackupBytes = estimate.TierOneBytes
            + (settings.IncludePresets ? estimate.PresetBytes : 0)
            + (settings.IncludePluginFiles ? estimate.PluginBinaryBytes : 0);

        vm.WhatGoesIn = new WhatGoesInModel(
            setup: new WhatGoesInRow("Your setup", "Every channel, routing and effect chain - the whole file, plus Wave Link's own copies.", estimate.TierOneBytes, true, true),
            effectsList: new WhatGoesInRow("A list of your effects", "The names of the effects in use. Travels inside the settings file above.", 0, true, true),
            presets: new WhatGoesInRow("Effect presets", "Your saved preset values for each effect.", estimate.PresetBytes, settings.IncludePresets, false),
            pluginFiles: new WhatGoesInRow("The effect plug-ins themselves", "The .vst3 files, so a new machine can load the effects.", estimate.PluginBinaryBytes, settings.IncludePluginFiles, false));

        // The toggle and stepper carry high-contrast triggers that bind to this through the
        // window's DataContext - the same value MainWindow hands ShellViewModel.
        vm.IsHighContrast = systemTheme?.IsHighContrast ?? SystemParameters.HighContrast;

        // WHERE BACKUPS ARE KEPT: the trash row is computed BEFORE anything is shown (Plan 6's
        // projection), re-detected per volume - never cached across a folder move.
        if (store is not null)
        {
            var (count, bytes) = store.TrashSize();
            vm.TrashRow = TrashRowModel.Build(
                count, bytes, store.TrashPath,
                store.TrashGoesToRecycleBin(new RecycleBin()));
            vm.FreeSpaceBytes = fileSystem!.GetAvailableFreeBytes(settings.StorePath);

            // The other two thirds of the design's stats line, from the same read (audit §2.9a).
            var snapshots = store.List();
            vm.BackupCount = snapshots.Count;
            vm.UsedBytes = snapshots.Sum(s => s.Manifest.TotalSizeBytes);
        }

        // UPDATES (screens/12). Built here because it needs the release feed and the HTTP client,
        // and hidden entirely when no feed is configured - a "Check now" that cannot reach anything
        // is worse than no button.
        vm.Updates = BuildUpdateViewModel();

        return vm;
    }

    /// <summary>
    /// Where releases are looked for. Read from the environment rather than compiled in, because
    /// it is a fact about a DEPLOYMENT and not about the program (technical-debt.md §5): this repo
    /// has no remote yet, and a hard-coded owner/repo would be a constant that is wrong the moment
    /// one exists. Absent means the UPDATES section hides itself.
    /// </summary>
    internal static UpdateSource ReleaseSource => new(
        Environment.GetEnvironmentVariable("WLBACKUP_UPDATE_OWNER") ?? string.Empty,
        Environment.GetEnvironmentVariable("WLBACKUP_UPDATE_REPO") ?? string.Empty);

    /// <summary>
    /// One client for the life of the process. A new HttpClient per check is the classic way to
    /// exhaust sockets, and this one is used at most weekly.
    /// </summary>
    private static readonly System.Net.Http.HttpClient updateHttp = new()
    {
        Timeout = TimeSpan.FromSeconds(30),
    };

    private UpdateViewModel BuildUpdateViewModel()
    {
        var source = ReleaseSource;
        var feed = new GitHubReleaseFeed(source, updateHttp);

        return new UpdateViewModel(
            check: ct => feed.CheckAsync(ReleaseVersion.Current, ct),
            install: (release, progress, ct) => InstallUpdateAsync(release, progress, ct),
            persist: (checkForUpdates, checkedAt) => ApplySettings(
                settings with { CheckForUpdates = checkForUpdates, LastUpdateCheckUtc = checkedAt }),
            autoCheckEnabled: settings.CheckForUpdates,
            lastCheckedAt: settings.LastUpdateCheckUtc,
            isConfigured: source.IsConfigured);
    }

    /// <summary>
    /// Download, verify, stage, hand over. Returns null when the hand-over started — at which
    /// point this process is on its way out and has nothing left to report — or the mono line for
    /// the failed-update block.
    ///
    /// **The shutdown is deliberate and complete.** The staged copy cannot rename a directory this
    /// process holds a handle on, so the watcher, the tray and the store all have to go first, and
    /// a last backup is taken on the way out exactly as a Quit would.
    /// </summary>
    private async Task<string?> InstallUpdateAsync(
        UpdateRelease release, IProgress<double> progress, CancellationToken ct)
    {
        var staging = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "WaveLinkBackup-update");

        var download = await new UpdateDownloader(updateHttp)
            .DownloadAsync(release, staging, progress, ct)
            .ConfigureAwait(true);

        if (!download.Succeeded) return download.FailureDetail;

        var install = System.IO.Path.GetDirectoryName(Environment.ProcessPath);
        if (install is null) return "COULDN'T FIND THIS APP'S OWN FOLDER · NOTHING CHANGED";

        var started = new UpdateInstaller().Begin(download.Path!, install);
        if (!started.Started) return started.FailureDetail;

        // Everything that holds a file handle inside the install directory, and the last backup a
        // Quit would take. ShutdownEverything ends the process, so nothing after this runs.
        ShutdownEverything();
        return null;
    }

    /// <summary>
    /// What one backup would cost on this machine, per tier. Zero for every tier when Wave Link
    /// cannot be inspected — the dialog then prints dashes rather than a guess, which is the same
    /// answer the bottom bar gives when it cannot read free space.
    /// </summary>
    /// <summary>
    /// What every capture puts in a snapshot beyond the settings file, read against the settings
    /// as they stand at that moment. Shared by the manual button, the watcher and the pre-restore
    /// snapshot, so all three produce the same shape of backup.
    /// </summary>
    private SnapshotPayload? GatherPayload(SettingsInspection live) =>
        fileSystem is null
            ? null
            : TierCapture.For(fileSystem).Gather(live, settings, NewestPluginManifest());

    /// <summary>
    /// The newest snapshot's plugins.json, or null when there is no snapshot, no store, or
    /// nothing readable in it. Handed to the capture for one purpose: letting tier 2 skip
    /// re-hashing a plug-in binary nothing has touched (technical-debt.md §4.16).
    ///
    /// Null on every doubt. The cost of a null is a capture that hashes as it always did.
    /// </summary>
    private PluginManifest? NewestPluginManifest()
    {
        if (fileSystem is null || store is null) return null;

        var newest = store.List().FirstOrDefault();
        return newest is null ? null : new SnapshotPluginReader(fileSystem).Read(newest);
    }

    private CaptureEstimate MeasureTiers()
    {
        if (fileSystem is null) return CaptureEstimate.Nothing;

        var live = SettingsInspector.For(fileSystem, SettingsLocator.SystemLocalAppData)
            .Inspect(settings.ChosenWaveLinkPath);

        return live.IsSuccess
            ? TierCapture.For(fileSystem).Measure(live.Value)
            : CaptureEstimate.Nothing;
    }

    /// <summary>
    /// The WHICH WAVE LINK section's model, or null when the section must hide itself. The rule is
    /// "more than one installation AND one has been chosen": the locator never guesses between two
    /// (it returns MultiplePackagesFound), so a successful inspect of the CHOSEN path proves exactly
    /// one install is present - which is exactly when there is nothing to choose and the section
    /// stays hidden. A failure with no candidate is "not installed" or "unreadable", also not this
    /// section's business; only MultiplePackagesFound + a chosen path earns it (08-settings-
    /// persistence.md: hide the whole section when only one installation exists).
    /// </summary>
    private WhichWaveLinkModel? BuildWhichWaveLink(SettingsRepository repo)
    {
        if (fileSystem is null || settings.ChosenWaveLinkPath is null) return null;

        var inspection = SettingsInspector.For(fileSystem, SettingsLocator.SystemLocalAppData)
            .Inspect(settings.ChosenWaveLinkPath);

        // Only a genuine "more than one" finding shows the section. One install or none is the
        // ordinary found / not-found fact the status strip already reports - not a choice.
        if (inspection.Error is not MultiplePackagesFound { Candidates.Count: > 1 }) return null;

        var chosen = settings.ChosenWaveLinkPath;
        if (!fileSystem.FileExists(chosen)) return null;

        // The version and path come from the live inspection of the CHOSEN install - the same seam
        // RefreshShellFacts uses, so the section never disagrees with the status strip. The version
        // is null-tolerant: a file without Update.LastUpdateVersion still shows its path and date.
        var version = inspection.Value.Analysis.WaveLinkVersion ?? "version unknown";

        // CHOSEN date = when our own settings file last recorded the choice (the moment the user
        // picked it), not when Wave Link's file was written. The repo file is the honest source:
        // it is what stores ChosenWaveLinkPath, so its last-write IS the choice time.
        var chosenAt = new DateTimeOffset(
            fileSystem.GetLastWriteTimeUtc(repo.FilePath), TimeSpan.Zero).ToLocalTime();

        return new WhichWaveLinkModel(version, chosen, chosenAt, Visible: true);
    }

    /// <summary>
    /// The WHICH WAVE LINK "Change…" action (Task 4): re-opens the error-2 chooser. It is the same
    /// dialog that fires at startup when two installations are found and none is chosen - here it
    /// is reached deliberately, so a user who picked the wrong install can correct it without
    /// uninstalling one. The pick persists through vm.ChooseWaveLink (which stores
    /// ChosenWaveLinkPath), which is what stops the chooser asking again on the next launch.
    /// </summary>
    internal void ChangeWaveLink(Window owner)
    {
        if (fileSystem is null || settingsRepository is null) return;

        var inspection = SettingsInspector.For(fileSystem, SettingsLocator.SystemLocalAppData)
            .Inspect(settings.ChosenWaveLinkPath);

        // Only a genuine "more than one" finding offers a choice. One install or none has nothing
        // to switch between - the button's section is hidden in those cases anyway, but this is
        // the seam's own guard against a stale reference re-entering it.
        if (inspection.Error is not MultiplePackagesFound { Candidates: var candidates }
            || candidates.Count <= 1)
            return;

        var dialog = new ErrorDialog(ErrorDialogModel.Build(inspection.Error, DescribeInstall(candidates)))
        {
            Owner = owner,
        };
        dialog.ShowDialog();

        if (dialog.Confirmed && dialog.SelectedInstallPath is not null)
        {
            settings = settings with { ChosenWaveLinkPath = dialog.SelectedInstallPath };
            settingsRepository.Save(settings);
        }
    }

    /// <summary>
    /// The settings dialog's "Change folder…": opens the picker, and on pick writes the new folder
    /// through (re-pointing every consumer that holds a store reference) then re-detects the trash
    /// row's volume - the Plan-6 rule that a folder move must never reuse a cached Recycle-Bin answer.
    /// </summary>
    internal void ChangeBackupFolder(Window owner, SettingsViewModel vm)
    {
        var picker = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Choose a folder for Wave Link backups",
            InitialDirectory = settings.StorePath,
        };

        if (picker.ShowDialog(owner) != true) return;

        // Error 9 (06-errors.md §9), raised in place rather than as a modal. A folder holding
        // files but no snapshot is almost always a mis-click - a Recordings folder, a project
        // directory - and pointing the store at it silently would hide every existing backup
        // behind an empty list. An EMPTY folder is fine: that is "start fresh in an empty one",
        // which the error's own copy invites.
        if (NotABackupFolder(picker.FolderName) is { } fileCount)
        {
            vm.ShowNotABackupFolder(picker.FolderName, fileCount);
            return;
        }

        vm.ClearNotABackupFolder();
        SetStorePath(picker.FolderName);

        // Re-detect: the trash row and free-space figure both describe the NEW volume.
        if (store is not null)
        {
            var (count, bytes) = store.TrashSize();
            vm.TrashRow = TrashRowModel.Build(
                count, bytes, store.TrashPath,
                store.TrashGoesToRecycleBin(new RecycleBin()));
            vm.FreeSpaceBytes = fileSystem!.GetAvailableFreeBytes(settings.StorePath);
        }
    }

    /// <summary>
    /// The settings dialog's "Empty trash" (Plan 6's action, hosted in Plan 8). Local volumes run
    /// straight through - the Recycle Bin makes it reversible, and a confirmation guarding a
    /// reversible action is exactly the noise that teaches people to click through the ones that
    /// matter. Network/removable confirm first via <see cref="Views.EmptyTrashDialog"/>: there is no
    /// Recycle Bin to catch them, so emptying deletes for good. After either path the row and the
    /// free-space figure are re-read - both describe the volume's current state, not a cached one.
    /// </summary>
    internal void EmptyTrash(Window owner, SettingsViewModel vm)
    {
        if (store is null || vm.TrashRow is not { } row) return;

        if (row.RequiresConfirmation)
        {
            var (count, bytes) = store.TrashSize();
            var dialog = new Views.EmptyTrashDialog(
                EmptyTrashDialogModel.Build(count, bytes, store.TrashPath));
            dialog.Owner = owner;
            if (dialog.ShowDialog() != true) return;
        }

        store.EmptyTrash(new RecycleBin());

        // Re-detect: the row now reports whatever is left (usually "the trash is empty"), and the
        // free-space figure may have moved. Never reuse the pre-empty numbers.
        var (newCount, newBytes) = store.TrashSize();
        vm.TrashRow = TrashRowModel.Build(
            newCount, newBytes, store.TrashPath,
            store.TrashGoesToRecycleBin(new RecycleBin()));
        vm.FreeSpaceBytes = fileSystem!.GetAvailableFreeBytes(settings.StorePath);
    }

    /// <summary>
    /// Error 12's "Choose a folder…". Persists the new path and re-points every consumer that
    /// holds a store reference - the list, the service (next backup writes here), the host's
    /// coordinator, and the tray readout. Without the re-point the app would keep reading and
    /// writing the dead path after the user has told it where to go.
    /// </summary>
    internal void SetStorePath(string path)
    {
        // All three are set in OnStartup before any window exists, so this is a belt-and-braces
        // guard rather than an expected branch - but the fields are nullable and the compiler
        // will not see through the composition, so we narrow them to locals here.
        if (fileSystem is null || host is null || shell is null) return;

        settings = settings with { StorePath = path };
        settingsRepository?.Save(settings);

        var clock = new SystemClock();
        var inspector = SettingsInspector.For(fileSystem, SettingsLocator.SystemLocalAppData);
        var newStore = new SnapshotStore(fileSystem, clock, path);
        store = newStore;

        // Rebuilt with the NEW store so that a backup taken after the folder change writes to
        // where the user pointed it - not the dead path. The coordinator's reference is swapped
        // inside the host; the watcher and its two timestamps survive (a pending write is still
        // a pending write, even if the destination moved).
        service = new BackupService(
            inspector, newStore, settings.AutoBackupKeepCount, settings.ChosenWaveLinkPath, GatherPayload);
        host.SetStore(newStore, service);

        shell.List.SetStorePath(path);

        RefreshTray();
        RefreshShellFacts();
    }

    /// <summary>
    /// "Copy diagnostics" (technical-debt.md §6): everything the app knows about itself, redacted,
    /// on the clipboard.
    ///
    /// **On the clipboard and nowhere else.** There is no upload here and no setting that would
    /// create one — the whole point is to give the user something safe to paste, not to collect
    /// anything. <see cref="Diagnostics.Report"/> runs every field through
    /// <see cref="Redaction"/>, so the promise printed beside the button is kept by construction
    /// rather than by whoever adds the next field remembering.
    /// </summary>
    internal void CopyDiagnostics(SettingsViewModel vm)
    {
        if (fileSystem is null) return;

        var live = SettingsInspector.For(fileSystem, SettingsLocator.SystemLocalAppData)
            .Inspect(settings.ChosenWaveLinkPath);

        var report = Core.Analysis.Diagnostics.Report(
            ReleaseVersion.Display(ReleaseVersion.Current),
            settings,
            live.IsSuccess ? live.Value : null,
            store?.List() ?? [],
            DateTimeOffset.Now);

        try
        {
            Clipboard.SetText(report);
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
            // Another process holds the clipboard - common, transient, and not worth an error
            // screen of its own on a diagnostic action. The button is pressable again.
            return;
        }

        vm.DiagnosticsCopied = true;
    }

    /// <summary>
    /// What each error-2 candidate turns out to be, so its row can say more than a path
    /// (06 §2, technical-debt.md §4.21 item 7).
    ///
    /// Each candidate is inspected on its own — the whole point of the dialog is that discovery
    /// could not choose between them, so nothing here may lean on the "current" one. A candidate
    /// that cannot be read yields null, and its row falls back to a path and a radio.
    ///
    /// The RUNNING chip is an approximation the model documents: Windows gives no mapping from a
    /// running MSIX process back to its package, so it goes to whichever candidate's settings file
    /// was written most recently, and only while Wave Link is actually up.
    /// </summary>
    private Func<string, ErrorInstallDetail?> DescribeInstall(IReadOnlyList<string> candidates)
    {
        var running = waveLinkProcess?.IsRunning ?? false;

        var newest = running
            ? candidates
                .Where(c => fileSystem!.FileExists(c))
                .OrderByDescending(c => fileSystem!.GetLastWriteTimeUtc(c))
                .FirstOrDefault()
            : null;

        return path =>
        {
            if (fileSystem is null) return null;

            var inspection = SettingsInspector.For(fileSystem, SettingsLocator.SystemLocalAppData)
                .Inspect(path);

            if (!inspection.IsSuccess) return null;

            var saved = fileSystem.FileExists(path)
                ? new DateTimeOffset(fileSystem.GetLastWriteTimeUtc(path), TimeSpan.Zero).ToLocalTime()
                : (DateTimeOffset?)null;

            return new ErrorInstallDetail(
                inspection.Value.Analysis.WaveLinkVersion,
                inspection.Value.Analysis.Fingerprint.InputCount,
                inspection.Value.Bytes.LongLength,
                saved,
                IsRunning: string.Equals(path, newest, StringComparison.OrdinalIgnoreCase));
        };
    }

    /// <summary>
    /// How many files a folder holds when it holds files but no backups, or null when it is a
    /// usable store — empty, or already holding snapshots.
    ///
    /// The test is for a snapshot directory rather than for a <c>manifest.json</c> at the top
    /// level: the store's own shape is one directory per snapshot, so a top-level manifest would
    /// never be there even in a perfectly good store.
    /// </summary>
    private int? NotABackupFolder(string path)
    {
        if (fileSystem is null) return null;

        var files = fileSystem.EnumerateFiles(path, "*");
        var directories = fileSystem.EnumerateDirectories(path, "*");

        if (files.Count == 0 && directories.Count == 0) return null;

        var holdsASnapshot = directories.Any(d =>
            fileSystem.FileExists(System.IO.Path.Combine(d, SnapshotManifest.ManifestFileName)));

        return holdsASnapshot ? null : files.Count;
    }

    /// <summary>Error 12's "Use the default folder": same as SetStorePath with the default.</summary>
    internal void UseDefaultStore() => SetStorePath(SnapshotStore.DefaultStorePath);

    /// <summary>
    /// Error 1's "Choose the settings file…" — the escape hatch for a Wave Link discovery cannot
    /// find.
    ///
    /// This is the only route a **non-MSIX install** has into the app at all
    /// (technical-debt.md §2.2): <c>SettingsLocator.Locate(explicitSettingsPath)</c> bypasses
    /// discovery entirely, so an explicit path makes the program useful on a machine where every
    /// automatic answer is "not found". Persisted, so it is asked once.
    /// </summary>
    internal void ChooseSettingsFile(Window owner)
    {
        var picker = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Choose Wave Link's Settings.json",
            Filter = "Wave Link settings (Settings.json)|Settings.json|JSON files (*.json)|*.json",
            CheckFileExists = true,
        };

        if (picker.ShowDialog(owner) != true) return;

        settings = settings with { ChosenWaveLinkPath = picker.FileName };
        settingsRepository?.Save(settings);

        // Rebuild the capture path around the chosen file, exactly as a folder change does — the
        // watcher and every later capture must read the file the user just pointed at, not the one
        // discovery failed to find.
        ApplyChosenWaveLink();
    }

    /// <summary>
    /// Re-point everything that reads Wave Link's settings at <c>settings.ChosenWaveLinkPath</c>,
    /// then re-read the facts so the not-found screen collapses on its own.
    /// </summary>
    private void ApplyChosenWaveLink()
    {
        if (fileSystem is null || host is null || store is null) return;

        service = new BackupService(
            SettingsInspector.For(fileSystem, SettingsLocator.SystemLocalAppData),
            store,
            settings.AutoBackupKeepCount,
            settings.ChosenWaveLinkPath,
            GatherPayload);

        host.SetStore(store, service);

        RefreshTray();
        RefreshShellFacts();
    }

    /// <summary>
    /// Error 12's "Look again": re-probe the CURRENT path. No settings change - the user is
    /// asking whether the drive came back, not where to put it. If the folder now exists the
    /// list re-reads and the full screen collapses on its own (State flips off FolderMissing).
    /// </summary>
    internal void RecheckStore()
    {
        RefreshTray();
        RefreshShellFacts();
    }

    /// <summary>
    /// The two designed tray notifications, and their rules. Held rather than constructed per call
    /// because the nine-day notice fires ONCE per episode, which is state.
    /// </summary>
    private readonly TrayNotifications notifications = new();

    /// <summary>
    /// Show one, with its action wired.
    ///
    /// **Its action is the whole notification, not a button.** The design draws each notice with a
    /// labelled action; a classic tray balloon has no buttons, and Windows renders one as a toast
    /// that drops them. Real toast buttons need an AppUserModelID and a Start-menu shortcut - an
    /// installer concern this app does not have yet. So the label is stated in the body and
    /// clicking anywhere on the notice does the thing, which keeps the action reachable and says
    /// what it is. Recorded in technical-debt.md §4.21 item 6 rather than left as a silent
    /// difference from the design.
    /// </summary>
    private void Notify(TrayNotification? notification)
    {
        if (notification is null || tray is null) return;

        pendingNotificationAction = notification.Kind switch
        {
            TrayNotificationKind.NothingBackedUp => () =>
            {
                ShowMainWindow();
                OpenSettings();
            },
            TrayNotificationKind.WaveLinkReset => ShowMainWindow,
            _ => null,
        };

        tray.ShowNotification(
            notification.Title,
            $"{notification.Body}\n\n{notification.ActionLabel}");
    }

    /// <summary>What clicking the most recent notification does. Null when there is nothing to do.</summary>
    private Action? pendingNotificationAction;

    /// <summary>
    /// The second designed notification: Wave Link rejected a restored backup and reset the live
    /// configuration. Raised by the window when a restore comes back Rejected, because the window
    /// may well be hidden at that point — a headless restore is a supported path, and the strip it
    /// draws is no use to somebody who is not looking at it.
    /// </summary>
    internal void NotifyWaveLinkReset() =>
        Notify(TrayNotifications.WaveLinkReset(Core.Restore.RestoreOrchestrator.PreRestoreName));

    /// <summary>
    /// The DPI scale of the screen holding the taskbar, which is the screen the tray icon is drawn
    /// on — and not necessarily the one holding the window, or the primary one.
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

        // The nine-day notice (screens/12, "Notifications - exactly two"). Checked on every tray
        // refresh, which is every host tick, and fired at most once per episode - the decision and
        // the once-ness both live in TrayNotifications so they can be asserted from a table.
        Notify(notifications.NothingBackedUp(newest.TakenAt, DateTimeOffset.Now));

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
        // Same rule as RefreshTray, and the same caller: this one raises PropertyChanged on a
        // bound view model and can open the error-2 chooser, neither of which belongs on a
        // thread-pool thread.
        if (UiThread.Marshal(Dispatcher, RefreshShellFacts)) return;

        if (shell is null || fileSystem is null) return;

        // Error 2 (06-errors.md): more than one Wave Link installation and none chosen yet is a
        // dialog, not a status-strip fact. It fires once per process - the chooser persists the
        // answer (or the user cancels), so it must never re-ask on every 15-second tick.
        if (!error2Prompted && settings.ChosenWaveLinkPath is null)
            PromptForInstallationChoice();

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
            WaveLinkInputs: inspection.IsSuccess ? inspection.Value.Analysis.Fingerprint.InputCount : 0,
            WaveLinkSettingsPath: inspection.IsSuccess ? inspection.Value.Location.SettingsPath : null,
            AutoBackupEnabled: host?.AutoBackupEnabled ?? false,
            FolderMissing: !fileSystem.DirectoryExists(settings.StorePath),
            StorePath: settings.StorePath,
            FreeBytes: fileSystem.GetAvailableFreeBytes(settings.StorePath),
            WaveLinkVersion: inspection.IsSuccess ? inspection.Value.Analysis.WaveLinkVersion : null,
            LogsPath: inspection.IsSuccess ? inspection.Value.Location.LogsPath : null));
    }

    /// <summary>
    /// Error 2 (06-errors.md): the chooser. It fires only when a live inspection finds more than
    /// one Wave Link installation and none has been chosen yet, so it is the FIRST thing the user
    /// sees in that situation - before any backup or restore can act on the wrong install. The
    /// answer (or a cancel) marks <see cref="error2Prompted"/> so the dialog never re-asks; picking
    /// an install also persists it, which is what stops the chooser asking again on every launch
    /// (10-decisions.md 4).
    /// </summary>
    private void PromptForInstallationChoice()
    {
        if (fileSystem is null) return;

        var inspection = SettingsInspector.For(fileSystem, SettingsLocator.SystemLocalAppData)
            .Inspect(settings.ChosenWaveLinkPath);

        // Only a genuine "more than one" finding opens the dialog. One install or none is not an
        // error 2 - it is the ordinary found / not-found fact the status strip already reports.
        if (inspection.Error is not MultiplePackagesFound { Candidates: var candidates }
            || candidates.Count <= 1)
            return;

        error2Prompted = true;

        var dialog = new ErrorDialog(ErrorDialogModel.Build(inspection.Error, DescribeInstall(candidates)))
        {
            Owner = MainWindow,
        };
        dialog.ShowDialog();

        if (dialog.Confirmed && dialog.SelectedInstallPath is not null)
        {
            settings = settings with { ChosenWaveLinkPath = dialog.SelectedInstallPath };
            settingsRepository?.Save(settings);
        }
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

    /// <summary>
    /// Called from the window's Closing, so geometry survives a hide as well as an exit.
    ///
    /// Writes the field as well as the file. The other two writers (the close-behaviour toggle and
    /// the theme preference) save <see cref="ShellState"/> as they find it - so a field left
    /// holding the geometry this app STARTED with would have them write that stale rectangle back
    /// over the one this method had just saved.
    /// </summary>
    internal void SaveGeometry(Views.MainWindow window)
    {
        ShellState = window.CurrentGeometry(ShellState);
        shellStateRepository?.Save(ShellState);
    }

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
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        systemTheme?.Dispose();
        instance?.Dispose();

        Shutdown(0);
    }
}
