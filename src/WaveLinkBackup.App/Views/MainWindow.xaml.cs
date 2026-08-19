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
using WaveLinkBackup.Core.Results;

namespace WaveLinkBackup.App.Views;

public partial class MainWindow : Window
{
    private readonly IWindowChrome chrome;
    private readonly ISystemTheme systemTheme;
    private readonly ShellViewModel shell;
    private readonly IRestoreService restoreService;
    private readonly Func<Result<SettingsInspection>> inspectLive;
    private CancellationTokenSource? restoreCts;

    public MainWindow(
        IWindowChrome chrome,
        ISystemTheme systemTheme,
        ShellState state,
        ShellViewModel shell,
        IRestoreService restoreService,
        Func<Result<SettingsInspection>> inspectLive)
    {
        this.chrome = chrome;
        this.systemTheme = systemTheme;
        this.shell = shell;
        this.restoreService = restoreService;
        this.inspectLive = inspectLive;

        InitializeComponent();

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

        SettingsButton.Click += (_, _) => App.OpenSettings();
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

        var model = RestoreDialogModel.Build(planResult.Value, row.TakenAt);
        var dialog = new RestoreDialog(model) { Owner = this };

        // Focus returns to the list when the dialog closes (confirm or cancel), so a keyboard-only
        // user is never stranded on a dead window. The list re-focuses its previously selected row.
        if (dialog.ShowDialog() != true)
        {
            RestoreFocusToList();
            return; // Cancel or Escape - nothing was touched.
        }

        await RunRestoreAsync(row.Id, row.Name, live);
    }

    /// <summary>
    /// The confirmed half of a restore: drives the four-stage strip from the service's stage
    /// reports and hands the finished result to the existing outcome strip. Kept separate from
    /// RestoreSelectedAsync so the confirmation dialog is not part of this method's lifecycle.
    /// </summary>
    private async Task RunRestoreAsync(string snapshotId, string snapshotName, SettingsInspection live)
    {
        // Begin BEFORE the first stage report: it swaps in a fresh four-stage model (stage 0 current)
        // and marks the window restoring, which is what makes the strip show instead of the outcome.
        shell.BeginRestore(snapshotName);

        var progress = new Progress<RestoreStage>(stage => shell.RestoreProgress.Advance(stage));
        restoreCts = new CancellationTokenSource();

        RestoreResultView view;
        try
        {
            view = await restoreService.RestoreAsync(snapshotId, live, progress, restoreCts.Token);
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

            shell.Strip.ShowFailure(view.FailureMessage ?? "The restore failed.");
        }
        else
        {
            shell.Strip.ShowResult(view.Result);
        }
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
        RestoreFocusToList();
        return true;
    }

    private async Task BackUpNowAsync()
    {
        // Not an App (e.g. a test harness running a bare Application) - do nothing rather than
        // exploding on the cast. In production Application.Current is always the App, so this
        // branch never runs there.
        if (Application.Current is not App app) return;

        var result = app.BackUpNow();

        await shell.List.RefreshAsync();

        if (!result.IsSuccess)
        {
            // 06-errors.md: a failed "Back up now" is the consequence of the press, so its
            // live-settings errors (3 unreadable, 5 still running) render as inline strips - not
            // the old message box. AppErrorMapper decides placement; only inline forwards here.
            if (TryShowInlineError(result.Error)) return;

            MessageBox.Show(result.Error!.Message, "Wave Link Backup",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        else
        {
            shell.List.Select(result.Value.Id);
        }
    }

    // ================================================================================
    // ShellCommands.All, bound in MainWindow.xaml's Window.CommandBindings (all but
    // ClearSearch, which is bound narrowly on SearchBox and ListScrollViewer instead - see the
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
    private void ListScrollViewer_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Home && e.Key != Key.End) return;
        if (shell.List.Groups.Count == 0) return;

        var group = e.Key == Key.Home ? shell.List.Groups[0] : shell.List.Groups[^1];
        if (group.Rows.Count == 0) return;

        var row = e.Key == Key.Home ? group.Rows[0] : group.Rows[^1];

        shell.List.Select(row.Id);
        e.Handled = true;

        Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() => FocusRow(row)));
    }

    private void FocusRow(SnapshotRowViewModel row)
    {
        foreach (var listBox in FindDescendants<ListBox>(GroupsHost))
        {
            if (listBox.ItemContainerGenerator.ContainerFromItem(row) is ListBoxItem container)
            {
                container.Focus();
                return;
            }
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
    /// yet when the virtualizing panel is first asked to draw it.
    /// </summary>
    private void RestoreFocusToList()
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
    internal ShellState CurrentGeometry(bool closingHidesToTray) => new(
        Left: RestoreBounds.Left,
        Top: RestoreBounds.Top,
        Width: RestoreBounds.Width,
        Height: RestoreBounds.Height,
        IsMaximized: WindowState == WindowState.Maximized,
        ClosingHidesToTray: closingHidesToTray);

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
