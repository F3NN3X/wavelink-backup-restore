using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using WaveLinkBackup.App.Hosting;
using WaveLinkBackup.App.Services;
using WaveLinkBackup.App.Theming;
using WaveLinkBackup.App.ViewModels;
using WaveLinkBackup.App.Windows;
using WaveLinkBackup.Core.Io;
using WaveLinkBackup.Core.Restore;
using WaveLinkBackup.Core.Results;
using WaveLinkBackup.Core.Snapshots;

namespace WaveLinkBackup.App.Views;

public partial class MainWindow : Window
{
    private readonly IWindowChrome chrome;
    private readonly ISystemTheme systemTheme;
    private readonly ShellViewModel shell;
    private readonly IRestoreService restoreService;
    private readonly Func<Result<SettingsInspection>> inspectLive;
    private readonly IElevation elevation;
    private CancellationTokenSource? restoreCts;

    /// <param name="elevation">
    /// How the window asks Windows for administrator rights, for the one restore that can need
    /// them (screens/13-elevation.md). A seam because no test can answer a UAC prompt, and a
    /// restore path only a human can exercise is one nobody exercises.
    /// </param>
    public MainWindow(
        IWindowChrome chrome,
        ISystemTheme systemTheme,
        ShellState state,
        ShellViewModel shell,
        IRestoreService restoreService,
        Func<Result<SettingsInspection>> inspectLive,
        IElevation? elevation = null)
    {
        this.chrome = chrome;
        this.systemTheme = systemTheme;
        this.shell = shell;
        this.restoreService = restoreService;
        this.inspectLive = inspectLive;
        this.elevation = elevation ?? new ShellExecuteElevation();

        InitializeComponent();

        // The window's caption glyph is rendered from code, not loaded from a file. A XAML
        // Icon="app.ico" attribute fails at runtime with "Cannot locate resource 'app.ico'"
        // (dotnet/wpf#209): the .ico IS embedded in the assembly's WPF resource blob, but neither
        // the XAML type-converter path nor a pack://application:,,,/ URI through BitmapImage can
        // locate it by name - WPF's pack-URI resolution does not index .ico entries the way it
        // does .png/.jpg. Rendering the mark from the same geometry TrayIconRenderer draws (see
        // AppCaptionGlyph below) sidesteps the file entirely. The exe's own icon (taskbar, Alt-Tab,
        // file properties) is separate and comes from <ApplicationIcon> in the csproj.
        Icon = AppCaptionGlyph.Render();

        DataContext = shell;

        // MUST happen before the first RefreshAsync (wired below, on Loaded): HealthProbe
        // reports verdicts on its OWN thread and does not marshal itself, so the first
        // PropertyChanged after a verdict lands would otherwise fire off the UI thread and the
        // binding would throw. Loaded always fires strictly after the constructor returns, so
        // setting this here is early enough for every caller.
        shell.List.Marshal = action => Dispatcher.Invoke(action);

        // Read once here rather than waiting for the first Changed: ThemeManager.Follow (called
        // in App.OnStartup, before this window exists) has already applied the current palette,
        // so systemTheme.IsHighContrast is already correct by the time this constructor runs.
        shell.IsHighContrast = systemTheme.IsHighContrast;

        Restore(state);

        MinimiseButton.Click += (_, _) => WindowState = WindowState.Minimized;
        MaximiseButton.Click += (_, _) => WindowState =
            WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        CloseButton.Click += (_, _) => Close();

        Loaded += async (_, _) => await shell.List.RefreshAsync();

        // Window-level, before the list's own PreviewKeyDown: while a row is in edit mode Enter
        // commits and Escape cancels - and must NOT also fire Restore (Enter) or ClearSearch
        // (Escape), which are bound on the window / the scroll region respectively.
        PreviewKeyDown += OnWindowPreviewKeyDown;

        WireBottomBar();
        WireSearch();
        WireRestoreOutcomeStrip();

        // The HWND does not exist before SourceInitialized, and DwmSetWindowAttribute needs
        // one. Re-applied on every theme change because the dark-frame attribute is a colour
        // decision and high contrast withdraws the backdrop entirely.
        SourceInitialized += (_, _) => ApplyChrome();
        systemTheme.Changed += OnSystemThemeChanged;
    }

    /// <summary>
    /// Rename and Delete render live with correct enablement and open a placeholder naming the
    /// session that builds them - the same answer plan 3 gave Settings (App.OpenSettings). Restore
    /// and Back up now are real: Restore runs the full confirmation -> in-progress strip -> outcome
    /// flow, and Back up now calls App.BackUpNow, refreshes the list, and selects the row the
    /// capture just wrote.
    /// </summary>
    private void WireBottomBar()
    {
        RenameButton.Click += (_, _) => BeginRename();
        DeleteButton.Click += (_, _) => DeleteSelected();
        RestoreButton.Click += async (_, _) => await RestoreSelectedAsync();
        BackUpNowButton.Click += async (_, _) => await BackUpNowAsync();

        // The gear opens the real settings dialog (Plan 8) through the app instance - not the old
        // static placeholder. Application.Current is always set by the time a button can be clicked.
        SettingsButton.Click += (_, _) => (Application.Current as App)?.OpenSettings();

        // The "?" beside it opens the help dialog the same way: one two-line seam on App, no
        // command object (the same reason the gear above is code-behind rather than a command).
        HelpButton.Click += (_, _) => (Application.Current as App)?.OpenHelp();

        // Screen 4 (first-run / empty state): its own "Back up now" and "Choose where to keep
        // them" live in the stand-in region, so they are wired here rather than on the bottom bar.
        // Both reuse the exact same actions as the bottom bar - BackUpNowAsync and the folder
        // picker - so there is one code path for each.
        EmptyBackUpNowButton.Click += async (_, _) => await BackUpNowAsync();
        ChooseWhereToKeepButton.Click += (_, _) => ChooseWhereToKeep_Click();

        // Error 1's first-run variant: the one route a non-MSIX install has into the app
        // (technical-debt.md §2.2). Nothing bound FirstRunError1Label before 0.6.1, so this
        // button and the two lines above it did not exist on screen at all.
        ChooseSettingsFileButton.Click += (_, _) =>
            (Application.Current as App)?.ChooseSettingsFile(this);

        // Screen 4's checkbox - "Keep backing up on its own when my settings change" - is the
        // first-run screen's one setting. It is read from the app once here and written back on
        // every change, rather than carrying IsChecked="True" and no handler, which is what it had:
        // a control that always looked on and never turned anything on or off.
        //
        // A bare harness (no App) leaves it unchecked and inert rather than throwing, the same
        // seam the two buttons above already use.
        if (Application.Current is App settingsOwner)
        {
            KeepAutoBackupCheckbox.IsChecked = settingsOwner.AutoBackupEnabled;
            KeepAutoBackupCheckbox.Checked += (_, _) => settingsOwner.SetAutoBackup(true);
            KeepAutoBackupCheckbox.Unchecked += (_, _) => settingsOwner.SetAutoBackup(false);
        }
    }

    /// <summary>
    /// Screen 4's "Choose where to keep them": the same folder picker as error 12's
    /// "Choose a folder…", pointed at the current store path. In the empty state there is no
    /// missing-folder context, so InitialDirectory falls back to the default store location.
    /// </summary>
    private void ChooseWhereToKeep_Click()
    {
        if (Application.Current is not App app) return;

        var picker = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Choose a folder for Wave Link backups",
            InitialDirectory = string.IsNullOrEmpty(shell.List.FolderMissingPath)
                ? SnapshotStore.DefaultStorePath
                : shell.List.FolderMissingPath,
        };

        if (picker.ShowDialog(this) == true)
        {
            app.SetStorePath(picker.FolderName);
        }
    }

    private void WireSearch()
    {
        ClearSearchButton.Click += (_, _) => shell.List.ClearSearch();
        ClearSearchLinkButton.Click += (_, _) => shell.List.ClearSearch();

        // Fix 1: the footer strip's "Show all N" ghost button - same action as the other two
        // clear-search entry points above.
        ShowAllButton.Click += (_, _) => shell.List.ClearSearch();
    }

    /// <summary>
    /// Error 12's three actions (08-settings-persistence.md "The folder is gone"). The window
    /// reaches the App through Application.Current - the same seam BackUpNowAsync and OnClosing
    /// already use - so a bare test harness (no App) simply no-ops rather than exploding on the
    /// cast. Each handler does exactly one thing: point the store at a new folder, or re-probe
    /// the current one. The list's State flips off FolderMissing on its own once the folder is
    /// found again, which collapses this full screen without any explicit hide call here.
    /// </summary>
    private void ChooseFolder_Click(object sender, RoutedEventArgs e)
    {
        if (Application.Current is not App app) return;

        // Microsoft.Win32.OpenFolderDialog (WPF's own folder picker, .NET 8+) rather than the
        // WinForms FolderBrowserDialog: UseWindowsForms is deliberately off in this project -
        // pulling Forms into implicit scope collides with WPF's Color/Brush/Application and the
        // like (see SystemScreens' comment). This needs no csproj change.
        var picker = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Choose a folder for Wave Link backups",
            InitialDirectory = shell.List.FolderMissingPath,
        };

        if (picker.ShowDialog(this) == true)
        {
            app.SetStorePath(picker.FolderName);
        }
    }

    /// <summary>Error 12's "Look again": re-probe the CURRENT path. No settings change.</summary>
    private void LookAgain_Click(object sender, RoutedEventArgs e)
    {
        if (Application.Current is not App app) return;

        app.RecheckStore();
    }

    /// <summary>Error 12's "Use the default folder": same as ChooseFolder with the default.</summary>
    private void UseDefaultFolder_Click(object sender, RoutedEventArgs e)
    {
        if (Application.Current is not App app) return;

        app.UseDefaultStore();
    }

    /// <summary>
    /// The inline restore-result strip (03-restore-outcomes.md). Two things live here that do
    /// not belong in the view model:
    ///
    ///   1. Auto-dismiss. SucceededConfirmed clears itself after RestoreOutcomeStrip.AutoDismissAfter.
    ///      A DispatcherTimer is a WPF concern; the VM only declares the interval.
    ///   2. The status strip turning amber with a Rejected strip. StatusTone is derived from
    ///      ShellFacts in the VM; the strip's TurnsStatusAmber is an ADDITIONAL condition, so the
    ///      window ORs them together rather than giving the VM a second source of truth for tone.
    /// </summary>
    private void WireRestoreOutcomeStrip()
    {
        StripDismissButton.Click += (_, _) => shell.Strip.Dismiss();
        StripActionButton.Click += (_, _) => shell.Strip.OnAction?.Invoke();
        StripPrimaryActionButton.Click += (_, _) => shell.Strip.OnPrimaryAction?.Invoke();

        autoDismissTimer = new DispatcherTimer { Interval = RestoreOutcomeStrip.AutoDismissAfter };
        autoDismissTimer.Tick += (_, _) =>
        {
            autoDismissTimer.Stop();
            shell.Strip.Dismiss();
        };

        shell.Strip.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(RestoreOutcomeStrip.Kind))
            {
                // Restart the timer only for the one outcome that auto-dismisses; stop it for any
                // other kind so a later Rejected cannot be swept away by a stale tick.
                if (shell.Strip.AutoDismisses)
                {
                    autoDismissTimer.Start();
                }
                else
                {
                    autoDismissTimer.Stop();
                }
            }

            if (e.PropertyName == nameof(RestoreOutcomeStrip.TurnsStatusAmber))
            {
                // Re-raise the tone so the status strip's DataTrigger re-evaluates. StatusTone is
                // a computed property; raising it here is what tells the binding to re-read it.
                shell.RaiseStatusTone();
            }
        };
    }

    private DispatcherTimer? autoDismissTimer;

    // Shared by the bottom-bar Rename button and ShellCommands' F2 handler below - both begin an
    // in-place edit of the selected row's name (README Interactions), so there is one entry point.
    private void BeginRename()
    {
        if (shell.List.Selected is not { } row) return;

        row.BeginEdit();
        FocusRenameBox(row);
    }

    /// <summary>
    /// The delete flow, end to end (05-delete-dialogs.md): build the confirmation's model from
    /// the selected snapshot and the total count, show the 480px dialog, and on confirm move the
    /// snapshot into <c>.trash</c> via the list. Cancel or Escape leaves everything untouched; a
    /// failed move surfaces the store's reason rather than pretending it landed. Focus returns to
    /// the list either way - after a delete that is usually the empty-list state, which is fine:
    /// there is simply no row left to focus.
    /// </summary>
    private void DeleteSelected()
    {
        if (shell.List.Selected is not { } row) return;

        var snapshot = shell.List.FindSnapshot(row.Id);
        if (snapshot is null) return; // Stale selection - the list moved out from under it.

        var model = DeleteDialogModel.Build(snapshot, shell.List.TotalCount);
        var dialog = new DeleteDialog(model) { Owner = this };

        if (dialog.ShowDialog() != true)
        {
            RestoreFocusToList();
            return; // Cancel or Escape - nothing was touched.
        }

        var error = shell.List.Delete(row.Id);
        if (error is not null)
        {
            MessageBox.Show(error, "Wave Link Backup", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        RestoreFocusToList();
    }

    /// <summary>
    /// The restore flow, end to end (09-restore-dialog-additions.md + 04-in-progress.md):
    ///   1. Re-inspect live settings and build the read-only plan for the selected snapshot.
    ///   2. Show the confirmation dialog; a cancel or Escape leaves everything untouched.
    ///   3. On confirm, run the restore off-thread while the four-stage strip advances, then map
    ///      the result onto the existing outcome strip.
    ///
    /// CanExecute (shell.CanRestore) is the only gate - no selection and a restore already in
    /// flight both disable Enter here, so this handler carries no guard of its own.
    /// </summary>
    private async Task RestoreSelectedAsync()
    {
        if (shell.List.Selected is not { } row || shell.IsRestoring) return;

        // Inspect live settings at the moment of restore - the plan and the write must describe
        // the SAME "what is on disk right now", so both read it fresh here rather than trusting a
        // copy from the 15-second tick. This can fail: Wave Link may have been uninstalled, or its
        // settings file unreadable, in the moment between the tick and this click. Surface that
        // rather than proceeding to a plan against nothing.
        var liveResult = inspectLive();
        if (!liveResult.IsSuccess)
        {
            // 06-errors.md: an unreadable settings file (3) at the moment of the press is an
            // inline strip, not a message box. The catalog decides placement; only inline forwards.
            if (TryShowInlineError(liveResult.Error)) return;

            MessageBox.Show(liveResult.Error!.Message, "Wave Link Backup",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var live = liveResult.Value;

        var planResult = await restoreService.PlanAsync(row.Id, live, CancellationToken.None);
        if (!planResult.IsSuccess)
        {
            // A snapshot that is gone or unreadable (7 manifest, 11 not found) at the moment of
            // the press is an inline strip. Newer-format (8) and malformed-settings (4) are
            // dialogs - the catalog decides which is which; only those two forward to a dialog.
            if (TryShowInlineError(planResult.Error)) return;
            if (TryShowDialogError(planResult.Error)) return;

            MessageBox.Show(planResult.Error!.Message, "Wave Link Backup",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // The missing-plug-in warning, real at last (phase 6 §5). Core did the resolving and wrote
        // both clauses; the dialog renders them and decides nothing.
        var plugins = planResult.Value.Plugins;
        var model = RestoreDialogModel.Build(
            planResult.Value, row.TakenAt,
            missingPluginLead: plugins?.MissingLead,
            missingPluginRest: plugins?.MissingRest);
        var dialog = new RestoreDialog(model) { Owner = this };

        // Focus returns to the list when the dialog closes (confirm or cancel), so a keyboard-only
        // user is never stranded on a dead window. The list re-focuses its previously selected row.
        if (dialog.ShowDialog() != true)
        {
            RestoreFocusToList();
            return; // Cancel or Escape - nothing was touched.
        }

        // The tier 4 opt-in. Off unless the user moved it in the dialog just now, and never
        // remembered - the Settings dialog's plug-in-files switch decides what goes INTO a backup
        // and is deliberately not read here (screens/13-elevation.md).
        var wantsPlugins = model.PluginFiles?.Enabled == true;

        // Elevate ONLY when the destinations actually refuse this process a write. The plan
        // measured that when it was built, by probing each plug-in's own folder rather than
        // matching its path against `C:\Program Files` - and the difference is not academic:
        // several audio plug-in installers grant Everyone full control of the shared VST3 folder
        // so their own updates need no administrator, and on such a machine this restore needs no
        // prompt at all. Asking anyway would be asking for rights we do not need, which is how a
        // prompt stops meaning anything.
        if (wantsPlugins && model.PluginFiles!.NeedsElevation)
        {
            await RunElevatedRestoreAsync(row.Id, row.Name);
            return;
        }

        await RunRestoreAsync(
            row.Id, row.Name, live,
            new RestoreOptions(Presets: true, PluginBinaries: wantsPlugins));
    }

    /// <summary>
    /// The restore the shell cannot do itself: tier 4 writes into
    /// `C:\Program Files\Common Files\VST3`, which needs administrator rights this process does
    /// not have and cannot acquire in place. A second, elevated copy of this same executable does
    /// the whole restore and exits with a code (screens/13-elevation.md).
    ///
    /// The in-progress strip runs throughout, already at "Closing Wave Link". The restore has begun
    /// as far as the user is concerned, and a separate "waiting for permission" state would be a
    /// screen nobody sees for more than a second. Nothing has been written while Windows is asking:
    /// the elevated copy takes the pre-restore snapshot itself.
    /// </summary>
    private async Task RunElevatedRestoreAsync(string snapshotId, string snapshotName)
    {
        shell.BeginRestore(snapshotName);
        shell.RestoreProgress.Advance(RestoreStage.ClosingWaveLink);

        restoreCts = new CancellationTokenSource();
        var token = restoreCts.Token;

        ElevationOutcome outcome;
        try
        {
            // Off the UI thread: RunElevated blocks until the elevated copy exits, and the strip
            // has to keep animating while Windows shows its consent dialog.
            outcome = await Task.Run(
                () => elevation.RunElevated(["--restore", snapshotId, "--with-plugins"], token), token);
        }
        finally
        {
            shell.CompleteRestore();
        }

        switch (outcome.Result)
        {
            case ElevationResult.Declined:
                // Error 13. Neutral, because declining changed nothing - the settings and presets
                // went back, and the plug-ins are exactly as they were. The action re-runs the same
                // restore rather than firing a second UAC prompt on its own.
                shell.Strip.OnAction = () => _ = RunElevatedRestoreAsync(snapshotId, snapshotName);
                shell.Strip.ShowError(
                    AppError.ElevationDeclined,
                    monoMeta: "ADMINISTRATOR RIGHTS DECLINED · YOUR PLUG-INS ARE STILL IN THE BACKUP",
                    actionLabel: "Try again as administrator");
                break;

            case ElevationResult.Completed:
                // The elevated copy verified from the log and we cannot see its verdict from here,
                // so this reports the honest one: the write went through, this process did not
                // confirm it. Never Confirmed - claiming a confirmation nobody read would be the
                // exact dishonesty 03-restore-outcomes.md exists to prevent.
                shell.Strip.ShowResult(RestoreResult.Unconfirmed);
                break;

            default:
                shell.Strip.ShowFailure(
                    outcome.ExitCode is { } code
                        ? $"The restore failed (code {code}). Your current settings are still in place."
                        : "The restore could not be started with administrator rights.");
                break;
        }

        await shell.List.RefreshAsync();
    }

    /// <summary>
    /// The confirmed half of a restore: drives the four-stage strip from the service's stage
    /// reports and hands the finished result to the existing outcome strip. Kept separate from
    /// RestoreSelectedAsync so the confirmation dialog is not part of this method's lifecycle.
    /// </summary>
    private async Task RunRestoreAsync(
        string snapshotId,
        string snapshotName,
        SettingsInspection live,
        RestoreOptions? options = null)
    {
        // Begin BEFORE the first stage report: it swaps in a fresh four-stage model (stage 0 current)
        // and marks the window restoring, which is what makes the strip show instead of the outcome.
        shell.BeginRestore(snapshotName);

        var progress = new Progress<RestoreStage>(stage => shell.RestoreProgress.Advance(stage));
        restoreCts = new CancellationTokenSource();

        RestoreResultView view;
        try
        {
            view = await restoreService.RestoreAsync(
                snapshotId, live, progress, restoreCts.Token, options);
        }
        finally
        {
            // CompleteRestore marks every stage done and releases the window. The strip then shows
            // the finished result - the in-progress strip and the outcome strip never overlap.
            shell.CompleteRestore();
        }

        if (view.Result == RestoreResult.Failed)
        {
            // A failed restore is a CONSEQUENCE of the press, so 06 renders it as an inline strip
            // (not the old generic "Restore failed" danger row). The typed CoreError behind the
            // failure decides WHICH of the twelve: damaged -> 10, newer format -> 8, unreadable
            // manifest -> 7, nothing else. Anything with no designed code keeps the danger row.
            if (view.CoreError is { } error)
            {
                var appError = AppErrorMapper.FromCoreSignal(new CoreSignal(error));
                if (appError is not null && appError.Placement == ErrorPlacement.InlineStrip)
                {
                    shell.Strip.ShowError(appError, monoMeta: view.FailureMessage);
                    return;
                }
            }

            // No designed error code (or none at all): the danger row. The pointer helper appends
            // the redacted crash report's path when one was written this run - 06 has no "something
            // unexpected happened" surface, so the report is the evidence and this row points at it
            // (technical-debt.md §8.1a). Null otherwise: the row stays exactly as before.
            var failure = AppErrorMapper.CrashReportPointer(
                view.FailureMessage,
                view.CoreError,
                Application.Current is App { LastCrashReportPath: { } reportPath } ? reportPath : null);

            shell.Strip.ShowFailure(failure!);
        }
        else
        {
            ShowRestoreOutcome(view);
        }
    }

    /// <summary>
    /// The finished result, with the rejected state's recovery wired to something.
    ///
    /// 03-restore-outcomes.md §3 gives a rejected restore a ghost "Show the log" and a primary
    /// <c>Restore "Before restore"</c>, and renders that row selected below the strip "so the
    /// button and the row are visibly the same object". Until 0.6.1 the state drew none of it and
    /// <c>AcknowledgeReject</c> was called by nothing, so the bar was permanent for the life of
    /// the process — the recovery path for the only failure that costs someone their mixer
    /// (technical-debt.md §4.21 item 1).
    /// </summary>
    private void ShowRestoreOutcome(RestoreResultView view)
    {
        if (view.Result != RestoreResult.Rejected)
        {
            shell.Strip.ShowResult(view.Result);
            return;
        }

        var recovery = view.PreRestoreSnapshotId;

        shell.Strip.OnAction = ShowWaveLinkLog;
        shell.Strip.OnPrimaryAction = recovery is null ? null : () =>
        {
            // Acknowledging FIRST is what makes the bar go away at all: Dismiss() refuses while
            // the kind is Rejected, and acting on it is the only exit the design allows.
            shell.Strip.AcknowledgeReject();
            _ = RestoreRecoveryAsync(recovery);
        };

        shell.Strip.ShowResult(view.Result, recovery, RejectionMeta(view));

        // screens/12's second notification. The strip below is the full account; this is what
        // reaches somebody whose window is behind Wave Link's, which after a restore it usually is.
        (Application.Current as App)?.NotifyWaveLinkReset();

        // The "Before restore" row, selected, immediately below the strip.
        if (recovery is not null) shell.List.Select(recovery);
    }

    /// <summary>
    /// 03 §3's mono meta line: <c>WAVE LINK 3.3.0.4108 REWROTE settings.json AT 23:12 · 1 INPUT
    /// NOW</c>. Every figure is read from what the app knows at that moment; nothing here is
    /// fixed text pretending to be a measurement (technical-debt.md §5).
    /// </summary>
    private string RejectionMeta(RestoreResultView view)
    {
        var version = shell.Facts.WaveLinkVersion is { Length: > 0 } v ? $"WAVE LINK {v} " : string.Empty;
        var count = shell.Facts.WaveLinkInputs;
        var inputs = $" · {count} INPUT{(count == 1 ? string.Empty : "S")} NOW";

        return $"{version}REWROTE settings.json AT {DateTime.Now:HH:mm}{inputs}";
    }

    /// <summary>
    /// Opens the folder holding Wave Link's own log — the evidence for WHY the file was rejected,
    /// and 03's reason for making this strip persist. The folder rather than the file: the newest
    /// log's name is Wave Link's business, and a folder that opens beats a path that might not.
    /// </summary>
    private void ShowWaveLinkLog()
    {
        var logs = shell.Facts.LogsPath;
        if (string.IsNullOrWhiteSpace(logs)) return;

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(logs)
            {
                UseShellExecute = true,
            });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            // Nothing to say: the strip is still there with the rest of what it knows, and a
            // second error about a failed folder open would bury it.
        }
    }

    /// <summary>
    /// Restores the "Before restore" copy the rejected restore took on its way in. It goes through
    /// the same confirmation the list's own Restore does — this is still a restore, and 05's
    /// pre-restore dialog variant exists for exactly this moment.
    /// </summary>
    private async Task RestoreRecoveryAsync(string snapshotId)
    {
        shell.List.Select(snapshotId);
        await RestoreSelectedAsync();
    }

    /// <summary>
    /// 06-errors.md: the live-settings errors that surface at the moment of a press are inline
    /// strips (3, 5) - "the consequence of something the user just pressed" - all neutral fill.
    /// This is the ONE place a typed CoreError becomes the strip it renders as; the catalog decides
    /// placement and weight, so this only forwards when the design says inline. Returns true when
    /// an inline strip was shown (the caller then stops), false otherwise (the caller keeps its own
    /// path - e.g. error 4's malformed-settings is a dialog, not a strip).
    /// </summary>
    private bool TryShowInlineError(CoreError? error)
    {
        if (error is null) return false;

        var appError = AppErrorMapper.FromCoreSignal(new CoreSignal(error));
        if (appError is null || appError.Placement != ErrorPlacement.InlineStrip) return false;

        shell.Strip.ShowError(appError, monoMeta: error.Message);
        return true;
    }

    /// <summary>
    /// 06-errors.md "Dialogs": the three errors the design places in a dialog (2 two installations,
    /// 4 malformed settings, 8 newer version) are shown here instead of the old message box. The
    /// catalog decides placement; this only forwards when it says Dialog, building the model from
    /// the typed CoreError itself so the machine-specific mono values (a parse position, a schema
    /// version, the found install paths) are never hard-coded. Returns true when a dialog was shown
    /// (the caller then stops), false otherwise (the caller keeps its own path).
    /// </summary>
    private bool TryShowDialogError(CoreError? error)
    {
        if (error is null) return false;

        var appError = AppErrorMapper.FromCoreSignal(new CoreSignal(error));
        if (appError is null || appError.Placement != ErrorPlacement.Dialog) return false;

        var dialog = new ErrorDialog(ErrorDialogModel.Build(error)) { Owner = this };
        dialog.ShowDialog();

        // Error 8's primary is "Get the update", and 12 has it deep-linking to Settings' UPDATES
        // section "with the new version's row already showing". It landed nowhere until that
        // section existed (technical-debt.md §4.21 item 5) - this is the link.
        if (dialog.Confirmed && appError.Code == 8)
        {
            (Application.Current as App)?.OpenSettingsAtUpdates();
        }

        RestoreFocusToList();
        return true;
    }

    private async Task BackUpNowAsync()
    {
        // Not an App (e.g. a test harness running a bare Application) - do nothing rather than
        // exploding on the cast. In production Application.Current is always the App, so this
        // branch never runs there.
        if (Application.Current is not App app) return;

        // 04-in-progress.md's backing-up strip. Up before the first byte is measured and down
        // only once the outcome takes its place, so the strip is "replaced in place by the result
        // line" rather than flashing out and back (technical-debt.md §4.21 item 2).
        shell.BackupProgress.Begin();

        var progress = new Progress<SnapshotWriteProgress>(shell.BackupProgress.Report);

        Result<Snapshot> result;
        try
        {
            // Off the UI thread so the bar can actually move. A capture is usually well under a
            // second, but "usually" is doing the work there: tier 4 copies plug-in binaries, and
            // the one rig where that is slow is the one where a frozen window would be noticed.
            result = await Task.Run(() => app.BackUpNow(progress));
        }
        finally
        {
            shell.BackupProgress.Complete();
        }

        await shell.List.RefreshAsync();

        if (!result.IsSuccess)
        {
            // 06-errors.md: a failed "Back up now" is the consequence of the press, so its
            // live-settings errors (3 unreadable, 5 still running) render as inline strips.
            // AppErrorMapper decides placement; only inline forwards here.
            if (TryShowInlineError(result.Error)) return;

            // Everything else goes to the danger strip, exactly as a failed restore does. It used
            // to be a raw MessageBox carrying CoreError.Message - a modal in Core's log phrasing,
            // for a failure the design places inline (technical-debt.md §4.8 minor 5).
            shell.Strip.ShowFailure(result.Error!.Message);
        }
        else
        {
            shell.List.Select(result.Value.Id);
        }
    }

    // ================================================================================
    // ShellCommands.All, bound in MainWindow.xaml's Window.CommandBindings (all but
    // ClearSearch, which is bound narrowly on SearchBox and GroupsHost instead - see the
    // comment on Window.CommandBindings in the XAML for why).
    // ================================================================================

    private async void Refresh_Executed(object sender, ExecutedRoutedEventArgs e) =>
        await shell.List.RefreshAsync();

    private void Search_Executed(object sender, ExecutedRoutedEventArgs e) =>
        SearchBox.Focus();

    private void ClearSearch_Executed(object sender, ExecutedRoutedEventArgs e) =>
        shell.List.ClearSearch();

    private async void BackUpNow_Executed(object sender, ExecutedRoutedEventArgs e) =>
        await BackUpNowAsync();

    private void BackUpNow_CanExecute(object sender, CanExecuteRoutedEventArgs e) =>
        e.CanExecute = shell.CanBackUpNow;

    private void Rename_Executed(object sender, ExecutedRoutedEventArgs e) => BeginRename();

    private void Rename_CanExecute(object sender, CanExecuteRoutedEventArgs e) =>
        e.CanExecute = shell.CanRename;

    private void Delete_Executed(object sender, ExecutedRoutedEventArgs e) => DeleteSelected();

    private void Delete_CanExecute(object sender, CanExecuteRoutedEventArgs e) =>
        e.CanExecute = shell.CanDelete;

    // CanExecute, not a guard inside the handler: WPF does not grey the command's target out on
    // its own, so a check made only here would leave Enter looking live on a row that cannot be
    // restored. The async void is deliberate - a CommandBinding Executed handler has no Task to
    // return, and a restore error is surfaced by the outcome strip, not an unobserved exception.
    private async void Restore_Executed(object sender, ExecutedRoutedEventArgs e) =>
        await RestoreSelectedAsync();

    private void Restore_CanExecute(object sender, CanExecuteRoutedEventArgs e) =>
        e.CanExecute = shell.CanRestore;

    /// <summary>
    /// "What's in this backup" - Ctrl+I, the row's overflow menu, and a double-click.
    ///
    /// Reaches App rather than reading the file here for the same reason every other row action
    /// does: the window has no file system and no store, and this needs both (MainWindow.xaml.cs
    /// stays a renderer).
    /// </summary>
    private void Details_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        if (shell.List.Selected is not { } row) return;

        (Application.Current as App)?.OpenSnapshotDetails(this, row.Id);
    }

    /// <summary>
    /// A row, and that is all it asks. Damaged included - see the binding's own note in the XAML.
    /// </summary>
    private void Details_CanExecute(object sender, CanExecuteRoutedEventArgs e) =>
        e.CanExecute = shell.List.Selected is not null;

    /// <summary>
    /// A double-click on a row opens its details, which is what a double-click on a row means
    /// everywhere else in Windows.
    ///
    /// Guarded on the ORIGINAL SOURCE being inside a row: the ListBox fills the whole list area,
    /// so without this a double-click on the empty space below the last row would open the details
    /// of whatever happened to be selected.
    /// </summary>
    private void Rows_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject source) return;
        if (FindAncestor<ListBoxItem>(source) is null) return;

        ShellCommands.Details.Execute(null, this);
    }

    private static T? FindAncestor<T>(DependencyObject from) where T : DependencyObject
    {
        for (var node = from; node is not null; node = VisualTreeHelper.GetParent(node))
        {
            if (node is T match) return match;
        }

        return null;
    }

    /// <summary>
    /// Each date group is its own ListBox (Task 10b), so native Home/End only reach that GROUP's
    /// own first/last row - WPF has no concept of "the next Selector down" to fall through to.
    /// The map (README/7.4) asks for Home/End to reach the list's first/last row overall, so this
    /// closes that gap with the smallest fix that does not touch the ListBox structure Task 10
    /// fought to get right: on Home/End, move List.Selected directly to the first/last row of the
    /// first/last GROUP, then focus that row's own generated container once layout has caught up
    /// with the new selection (BeginInvoke at Input priority - the container does not exist yet on
    /// the same tick a virtualizing panel is asked to realise it).
    ///
    /// ↑/↓ are NOT handled here - they already move the selection natively within one group's own
    /// ListBox (Task 10b), and stopping at a group boundary is the one gap this task's brief
    /// explicitly says is fine to leave for Task 12's by-eye pass to confirm, rather than
    /// restructure the list to close.
    /// </summary>
    /// <summary>
    /// Puts keyboard focus on a row's container. One lookup, not a walk: the list is a single
    /// ListBox now, so the row either has a container in it or has not been realised yet.
    /// </summary>
    private void FocusRow(SnapshotRowViewModel row)
    {
        if (GroupsHost.ItemContainerGenerator.ContainerFromItem(row) is ListBoxItem container)
        {
            container.Focus();
        }
    }

    /// <summary>
    /// In-place rename's commit/cancel keys (README Interactions: "commit on Enter or blur, cancel
    /// on Escape"), owned here rather than in the row template because a CommandBinding inside a
    /// DataTemplate resolves against the WINDOW's bindings and the gesture is easier to own in one
    /// place. Window-level PreviewKeyDown runs BEFORE the list's own key handling and before the
    /// window's Restore (Enter) command binding, so when a row is editing:
    ///   Enter  commits and swallows the key - it must NOT also fire Restore on that same press.
    ///   Escape cancels and swallows the key - it must NOT fall through to ClearSearch either.
    /// When no row is editing this does nothing, so both keys keep their normal meaning (Enter
    /// restores the selected row; Escape clears search when the list or search field has focus).
    /// </summary>
    private void OnWindowPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (shell.List.Selected is not { IsEditing: true } row) return;

        switch (e.Key)
        {
            case Key.Enter:
                shell.List.CommitRename(row);
                e.Handled = true;
                break;
            case Key.Escape:
                row.CancelEdit();
                RestoreFocusToList();
                e.Handled = true;
                break;
        }
    }

    /// <summary>
    /// Move keyboard focus into the selected row's rename box once it has been made visible, and
    /// select all so the first keystroke replaces rather than appends. Deferred to the input
    /// dispatcher for the same reason FocusRow is: the TextBox does not exist in the visual tree
    /// until layout has realised the container after IsEditing flipped it Visible.
    /// </summary>
    private void FocusRenameBox(SnapshotRowViewModel row)
    {
        Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
        {
            foreach (var listBox in FindDescendants<ListBox>(GroupsHost))
            {
                if (listBox.ItemContainerGenerator.ContainerFromItem(row) is not ListBoxItem container) continue;

                var box = FindDescendants<TextBox>(container).FirstOrDefault();
                if (box is null) return;

                // "Commit on Enter or blur" (README Interactions): a click anywhere else, or a Tab
                // out of the box, commits. Escape cancels first and restores focus, so by the time
                // this fires the row is no longer editing and OnRenameBoxLostFocus does nothing.
                box.LostFocus += OnRenameBoxLostFocus;

                box.Focus();
                box.SelectAll();
                return;
            }
        }));
    }

    /// <summary>
    /// Return keyboard focus to the selected snapshot's row after a dialog closes, so a
    /// keyboard-only user lands back where they started rather than on a dead window. Defers to
    /// the input dispatcher for the same reason FocusRow does: the container may not be realised
    /// yet when the virtualizing panel is first asked to draw it. Internal so the settings dialog
    /// (opened from App, which does not own this window) can reuse the same seam after it closes.
    /// </summary>
    internal void RestoreFocusToList()
    {
        if (shell.List.Selected is not { } row) return;

        Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() => FocusRow(row)));
    }

    /// <summary>
    /// "Commit on Enter or blur" (README Interactions). The box commits when focus leaves it - a
    /// click anywhere else in the window, or a Tab out. If the draft is invalid the row stays in
    /// edit and shows its cue; if Escape already cancelled, IsEditing is false and this is a no-op,
    /// so the cancel path never double-fires a commit.
    /// </summary>
    private void OnRenameBoxLostFocus(object sender, RoutedEventArgs e)
    {
        if (shell.List.Selected is not { IsEditing: true } row) return;

        shell.List.CommitRename(row);
    }

    private static IEnumerable<T> FindDescendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);

            if (child is T match) yield return match;

            foreach (var descendant in FindDescendants<T>(child)) yield return descendant;
        }
    }

    private void OnSystemThemeChanged(object? sender, EventArgs e) => Dispatcher.Invoke(() =>
    {
        ApplyChrome();
        shell.IsHighContrast = systemTheme.IsHighContrast;
    });

    private void ApplyChrome()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero) return;

        var (backdrop, corners) = ChromeChoice.ForMainWindow(systemTheme.IsHighContrast);

        var tookBackdrop = chrome.Apply(
            handle, backdrop, corners, dark: systemTheme.Theme != AppTheme.Light);

        // WlChrome IS the Mica tint role - it only means anything with Mica behind it. Where the
        // backdrop did not land (Windows 10, high contrast, a remote session) the bar paints it
        // rather than showing whatever is behind the window. Apply returns this value for
        // exactly this caller; plan 3 Task 3 Step 1.
        CaptionBar.SetResourceReference(BackgroundProperty, tookBackdrop ? "WlTransparent" : "WlChrome");
    }

    private void Restore(ShellState state)
    {
        if (!ShellState.IsOnScreen(state, SystemScreens())) return;

        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = state.Left!.Value;
        Top = state.Top!.Value;
        Width = state.Width!.Value;
        Height = state.Height!.Value;

        if (state.IsMaximized) WindowState = WindowState.Maximized;
    }

    /// <summary>
    /// Every screen's working area. SystemParameters knows about the primary monitor only, and
    /// a window remembered on the second one is the case this whole check exists for.
    ///
    /// EnumDisplayMonitors rather than System.Windows.Forms.Screen: UseWindowsForms pulls the
    /// whole Forms namespace into implicit scope PROJECT-WIDE, and that collides with WPF's own
    /// Color, Brush, FontFamily, Size, Application, ContextMenu and MenuItem everywhere they are
    /// already used elsewhere in this project - eighteen ambiguous-reference build errors, not
    /// the warnings the brief expected. DllImport, not LibraryImport, per technical-debt §7.1.
    /// </summary>
    private static IReadOnlyList<Rect> SystemScreens()
    {
        var areas = new List<Rect>();

        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (hMonitor, _, _, _) =>
        {
            var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
            if (GetMonitorInfo(hMonitor, ref info))
            {
                areas.Add(new Rect(
                    info.WorkLeft, info.WorkTop,
                    info.WorkRight - info.WorkLeft, info.WorkBottom - info.WorkTop));
            }

            return true;
        }, IntPtr.Zero);

        return areas;
    }

    private delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdcMonitor, IntPtr lprcMonitor, IntPtr dwData);

    /// <summary>MONITORINFO, flattened - cbSize, then RECT rcMonitor, then RECT rcWork, then flags.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public int MonitorLeft, MonitorTop, MonitorRight, MonitorBottom;
        public int WorkLeft, WorkTop, WorkRight, WorkBottom;
        public uint Flags;
    }

    [DllImport("user32.dll")]
    private static extern bool EnumDisplayMonitors(
        IntPtr hdc, IntPtr lprcClip, MonitorEnumProc lpfnEnum, IntPtr dwData);

    [DllImport("user32.dll")]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MonitorInfo info);

    /// <summary>
    /// The RESTORE bounds, never the maximised ones - a window remembered as 3840 wide because
    /// it happened to be maximised opens absurd on the next machine.
    /// </summary>
    /// <remarks>
    /// Takes the CURRENT state and overwrites only the geometry, rather than constructing a whole
    /// ShellState from a geometry plus one argument per remembered setting. Every field this
    /// window knows nothing about - the close behaviour, the theme preference - then survives the
    /// save on the shutdown path by default. The constructor form silently dropped whichever
    /// field was added last, which is the kind of loss nobody notices until a preference stops
    /// sticking.
    /// </remarks>
    internal ShellState CurrentGeometry(ShellState current) => current with
    {
        Left = RestoreBounds.Left,
        Top = RestoreBounds.Top,
        Width = RestoreBounds.Width,
        Height = RestoreBounds.Height,
        IsMaximized = WindowState == WindowState.Maximized,
    };

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        base.OnClosing(e);

        // Not an App (e.g. a test harness running a bare Application) - close normally rather
        // than exploding on the cast. In production Application.Current is always the App, so
        // this branch never runs there.
        if (Application.Current is not App app) return;

        // Before the branch: geometry must survive a HIDE as well as an exit, and a hidden
        // window is the normal case for this app.
        app.SaveGeometry(this);

        if (app.IsShuttingDown || !app.ShellState.ClosingHidesToTray) return;

        e.Cancel = true;
        Hide();
    }

    protected override void OnClosed(EventArgs e)
    {
        systemTheme.Changed -= OnSystemThemeChanged;
        base.OnClosed(e);
    }
}
