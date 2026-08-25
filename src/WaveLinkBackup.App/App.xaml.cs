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
/// "Configured once, then ignored, so it lives in the tray and the window is the exception."
/// If closing the window stopped the backups, the app would fail its own promise and become
/// upstream's tool with extra steps. So: OnExplicitShutdown, the coordinator lives here and
/// outlives every window, and closing hides.
/// </summary>
public partial class App : Application
{
    private const string InstanceName = "WaveLinkBackup";

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
    /// Error 2 (the errors spec) asks at most once per process. The chooser fires the first time a
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

    /// <summary>
    /// The cheap half of §8.1: writes an unhandled exception beside shell.json before the process
    /// is gone. Held here (not built in the handler) so the directory is known and the writer can
    /// be reached by both the dispatcher and the AppDomain handlers.
    /// </summary>
    private CrashReportWriter? crashReports;

    /// <summary>
    /// The path of the crash report, for the one surface that can still speak after an unexpected
    /// fault: a failed restore with no designed error code points at it (technical-debt.md §8.1a).
    /// Null until a crash has been written this run. Most runs never get here.
    /// </summary>
    internal string? LastCrashReportPath { get; private set; }

    /// <summary>Read by MainWindow at construction, and updated by every SaveGeometry.</summary>
    internal ShellState ShellState { get; private set; } = ShellState.Default;

    /// <summary>
    /// The Run-key seam, held because BOTH the shell view model and the Settings dialog's
    /// WHEN WINDOWS STARTS section read it (the tray and updates spec).
    /// </summary>
    private IAutostart? autostart;

    /// <summary>
    /// The newest snapshot, for the menu readout and the tooltip.
    ///
    /// Read from the STORE rather than from BackupHost.LastBackupAt, which only knows about
    /// captures made during this run, so a freshly started app would say "no backup yet" with
    /// thirty backups on disk.
    /// </summary>
    private (DateTimeOffset? TakenAt, int? Inputs) newest;

    /// <summary>What LastBackupAt was when <see cref="newest"/> was last read.</summary>
    private DateTimeOffset? summaryAsOf;

    /// <summary>Read by MainWindow: closing hides, unless the app is on its way out.</summary>
    internal bool IsShuttingDown => shuttingDown;

    /// <summary>
    /// Whether a restore must run elevated to close Wave Link - true when a running Wave Link
    /// process sits above this one's integrity level (WavelinkSEService as System), which a
    /// non-elevated copy cannot even open a handle to. Read by MainWindow before it commits to an
    /// in-process restore, so it can tell the user WHY Windows will ask for rights instead of
    /// letting the UAC prompt appear unexplained (the elevation spec). False when nothing is
    /// running or every running process is reachable - the common case, where no prompt appears.
    /// </summary>
    internal bool RestoreCloseRequiresElevation => waveLinkProcess?.CloseRequiresElevation ?? false;

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

            // A failed swap has nowhere to report to: the window the user was looking at belonged
            // to the process that has already exited. Leaving a breadcrumb beside settings.json is
            // what stops "the update did nothing" being a silent, unexplained no-op - the next
            // launch reads it and says so.
            if (!swapped)
            {
                UpdateInstaller.RecordFailure(
                    SettingsRepository.DefaultDirectory,
                    "The new version couldn't replace the old one - something still had the app's "
                        + "folder open. Nothing changed, and your backups are untouched.",
                    DateTimeOffset.Now);
            }

            // Relaunch either way: on success the new install, on failure the old one that was put
            // back. The one thing this must never do is leave the user with nothing running.
            UpdateInstaller.Relaunch(
                target, System.IO.Path.GetFileName(Environment.ProcessPath ?? "WaveLinkBackup.exe"));

            Shutdown(swapped ? 0 : 1);
            return;
        }

        // BEFORE the mutex, because this copy is not an instance of the app - it is one restore,
        // started elevated by the copy the user is looking at, and the mutex would make it exit
        // without doing the thing it was started for (the elevation spec).
        if (arguments.IsHeadlessRestore)
        {
            var elevatedFileSystem = new FileSystem();
            var elevatedSettings = arguments.ApplyTo(
                new SettingsRepository(elevatedFileSystem, SettingsRepository.DefaultDirectory).Read());

            // The elevated copy holds the rights to start WavelinkSEService (it just closed one),
            // so it brings the service back before relaunching - this is the path that actually
            // keeps Wave Link's "Start Service" box from appearing.
            Shutdown(HeadlessRestore.Run(
                arguments, elevatedSettings, elevatedFileSystem, new WaveLinkProcess(), new SystemClock(),
                service: new WaveLinkService()));
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

        // §8.1: a fault must leave a report, not just an event-log entry on a machine where
        // someone knows to look. The dispatcher handler catches UI-thread faults (the common case,
        // the original incident was a button click); the AppDomain handler is the backstop for
        // anything that escapes it. Both write through the same writer, so a fault that reaches
        // both leaves one coherent report rather than two interleaved ones. Installed once the
        // file system exists, because the writer needs it and there is no reason to wait past
        // this line. Every later startup step can throw.
        crashReports = new CrashReportWriter(fileSystem, SettingsRepository.DefaultDirectory);
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnAppDomainUnhandledException;

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

        // Now that there is an icon to repaint, follow the OS. The high-contrast spec requires high contrast
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

            // Daily, while the app is running. The tick is every 15 seconds and this is a date
            // comparison until the day is up, so the cost of asking here is nothing - and it is
            // the only thing that makes the interval real for an app designed to sit in the tray
            // for weeks without being restarted.
            _ = CheckForUpdateInBackground();
        };
        timer.Start();

        // Windows shutting down is a shutdown path too, and the ORIGINAL INCIDENT happened
        // during an update, while the machine was restarting. A shell that only captures on a
        // deliberate Quit misses the exact case CaptureOnShutdown was written for.
        SessionEnding += (_, _) => ShutdownEverything();

        if (!arguments.StartInTray) ShowMainWindow();

        RefreshTray();

        // A swap that failed last time. Read once - it is news exactly once - and said on the
        // strip, which is the surface already carrying facts about this app's state.
        if (UpdateInstaller.TakeFailure(SettingsRepository.DefaultDirectory) is { } failed)
        {
            updateFailureNotice = failed;
            RefreshShellFacts();
            Notify(TrayNotifications.UpdateFailed(failed));
        }

        // The check that makes "on its own, on by default" true. It used to run from the Settings
        // dialog's Loaded handler, which meant a user who never opened Settings was never told a
        // fix existed - a cadence attached to a surface almost nobody visits. Daily now rather
        // than the design's weekly; [[ADR-018]] carries why.
        _ = CheckForUpdateInBackground();
    }

    /// <summary>
    /// UI-thread fault. Writes the report first. That is the whole point of §8.1's cheap half,
    /// then lets the process end. Handling it (e.Handled = true) would keep a broken app running,
    /// which is a design decision this entry explicitly defers; what is owed right now is the
    /// report, and the report must be written before anything else happens on the way down.
    /// </summary>
    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        WriteCrashReport(e.Exception);
        e.Handled = false;
    }

    /// <summary>
    /// Backstop for faults that escape the dispatcher (a background thread, a finalizer). It runs
    /// on whatever thread faulted, which is why the writer takes an IFileSystem and serializes
    /// itself rather than assuming a UI thread. The process ends after this regardless. There is
    /// nothing to do but leave the report.
    /// </summary>
    private void OnAppDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception exception)
        {
            WriteCrashReport(exception);
        }
    }

    /// <summary>
    /// Both handlers write through here so the report carries the same context regardless of which
    /// one fired: the running build's version and the username to strip from the stack. The version
    /// is read on the way down, not cached at startup: a fault during early startup can hit before
    /// anything else exists, and the assembly attribute is still there then.
    /// </summary>
    private void WriteCrashReport(Exception exception)
    {
        var path = crashReports?.Write(exception, ReleaseVersion.Display(ReleaseVersion.Current), Redaction.CurrentUserName);
        if (path is not null)
        {
            LastCrashReportPath = path;
        }
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
        // watcher still starts when Wave Link is missing. It just never fires, and the tray
        // says why rather than the process refusing to run.
        var live = inspector.Inspect(settings.ChosenWaveLinkPath);
        var watchPath = live.IsSuccess
            ? live.Value.Location.LocalStatePath
            : SettingsLocator.SystemLocalAppData;

        // The user's own timings (the backup-timing spec): the interval cap they chose, and
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

        // The autostart seam (the tray and updates spec): the Run key for THIS executable. ProcessPath is null
        // until the process has started, so it is resolved at composition time - by then
        // App.xaml.cs has run and Environment.ProcessPath points at the real exe.
        var runKeyAutostart = new RunKeyAutostart(
            new WindowsRegistryKeys(), Environment.ProcessPath ?? string.Empty);
        var shell = new ShellViewModel(list, runKeyAutostart);

        // The shell-facing restore seam. Built here rather than in MainWindow so it exists even
        // before any window is shown - the same reason the shell VM is built here. It wraps Core's
        // RestoreOrchestrator; the view-model never touches a Wave Link process API itself.
        var waveLinkProcess = new WaveLinkProcess();
        // Brings WavelinkSEService back before the app relaunches, so a restore never leaves Wave
        // Link staring at its own "Start Service" box. The in-process path runs unelevated, where
        // starting a System service is denied - that is reported, not fatal, and Wave Link falls
        // back to its own prompt exactly as before this seam existed.
        var restoreService = new RestoreService(
            fileSystem, waveLinkProcess, store, gatherPayload, new WaveLinkService());

        return (new BackupHost(coordinator, clock), service, store, waveLinkProcess, restoreService, shell, runKeyAutostart);
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
        // designed errors (the errors spec) - inline strip for 3/5, message box otherwise. The tray
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
    /// Internal because Screen 4's own checkbox, "Keep backing up on its own when my settings
    /// change", is the first-run screen's one setting, and it needs to say what it means rather
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

    /// <summary>What clicking the most recent notification does. Null when there is nothing to do.</summary>
    private Action? pendingNotificationAction;

    /// <summary>
    /// The version a check found and this build does not have, display-formatted, or null.
    ///
    /// ONE field feeding three surfaces - the status strip, the tray menu line, and the balloon -
    /// rather than each asking the feed itself. The check is a network call on a timer; three
    /// callers would mean three answers that can disagree, and a strip that says one thing while
    /// the menu says another is worse than either alone.
    /// </summary>
    private string? updateAvailableVersion;

    /// <summary>
    /// Whether a check is in flight. The timer ticks every 15 seconds and a check is a network
    /// call that can outlive several ticks - without this, a slow or hanging feed would stack a
    /// new request on every tick until something gave out.
    /// </summary>
    private bool updateCheckInFlight;

    /// <summary>Why the last update did not go in, said once on the next launch.</summary>
    private string? updateFailureNotice;

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
