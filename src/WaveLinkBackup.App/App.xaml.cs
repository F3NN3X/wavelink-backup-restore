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
        systemTheme = new UiSettingsTheme();
        chrome = new DwmWindowChrome();
        ThemeManager.Apply(systemTheme.Theme, systemTheme.Accent);

        fileSystem = new FileSystem();
        settingsRepository = new SettingsRepository(fileSystem, SettingsRepository.DefaultDirectory);

        shellStateRepository = new ShellStateRepository(fileSystem, SettingsRepository.DefaultDirectory);
        ShellState = shellStateRepository.Read();

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

        // Now that there is an icon to repaint, follow the OS. screens/11 requires high contrast
        // to be reacted to at runtime rather than needing a restart, and the same is true of
        // dark/light and the accent. This first call is also what builds the menu.
        ThemeManager.Follow(systemTheme, OnThemeChanged);

        timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(15),
        };
        timer.Tick += (_, _) => { host.Tick(); RefreshTray(); RefreshShellFacts(); shell.RefreshAutostart(); };
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

        var vm = SettingsViewModel.Build(
            settings,
            ApplySettings,
            whereLive,
            whichLive,
            startup);

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

        return vm;
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

        var dialog = new ErrorDialog(ErrorDialogModel.Build(inspection.Error))
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
