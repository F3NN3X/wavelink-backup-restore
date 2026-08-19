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

    /// <summary>
    /// Internal so MainWindow's own gear button can open the same dialog. Builds a fresh view-model
    /// each time (the file may have changed while the window was closed) and shows it modally over
    /// the main window when one is open, otherwise as a standalone modal.
    /// </summary>
    internal void OpenSettings()
    {
        var vm = BuildSettingsViewModel();
        var dialog = new Views.SettingsDialog(vm);
        var owner = MainWindow != null ? (Window)MainWindow : null;
        if (owner is not null && owner.IsLoaded)
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
        var whereLive = new WhereSettingsLiveModel(
            repo.FilePath,
            fileSystem!.FileExists(repo.FilePath)
                ? Readable.Bytes(fileSystem!.ReadSharedBytes(repo.FilePath).Length)
                : "not written yet");

        var vm = SettingsViewModel.Build(
            settings,
            s => repo.Save(s).IsSuccess,
            whereLive);

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
        }

        return vm;
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
        service = new BackupService(inspector, newStore, settings.AutoBackupKeepCount, settings.ChosenWaveLinkPath);
        host.SetStore(newStore, service);

        shell.List.SetStorePath(path);

        RefreshTray();
        RefreshShellFacts();
    }

    /// <summary>Error 12's "Use the default folder": same as SetStorePath with the default.</summary>
    internal void UseDefaultStore() => SetStorePath(SnapshotStore.DefaultStorePath);

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
            FreeBytes: fileSystem.GetAvailableFreeBytes(settings.StorePath)));
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

        var dialog = new ErrorDialog(ErrorDialogModel.Build(inspection.Error))
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
