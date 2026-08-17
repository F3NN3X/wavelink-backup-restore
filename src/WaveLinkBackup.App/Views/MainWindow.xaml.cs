using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using WaveLinkBackup.App.Hosting;
using WaveLinkBackup.App.Theming;
using WaveLinkBackup.App.ViewModels;
using WaveLinkBackup.App.Windows;

namespace WaveLinkBackup.App.Views;

public partial class MainWindow : Window
{
    private readonly IWindowChrome chrome;
    private readonly ISystemTheme systemTheme;
    private readonly ShellViewModel shell;

    public MainWindow(IWindowChrome chrome, ISystemTheme systemTheme, ShellState state, ShellViewModel shell)
    {
        this.chrome = chrome;
        this.systemTheme = systemTheme;
        this.shell = shell;

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

        WireBottomBar();
        WireSearch();

        // The HWND does not exist before SourceInitialized, and DwmSetWindowAttribute needs
        // one. Re-applied on every theme change because the dark-frame attribute is a colour
        // decision and high contrast withdraws the backdrop entirely.
        SourceInitialized += (_, _) => ApplyChrome();
        systemTheme.Changed += OnSystemThemeChanged;
    }

    /// <summary>
    /// A row's click. WlRowTemplate's ListBoxItem is hand-placed rather than ListBox-generated
    /// (see MainWindow.xaml's own comment on the row DataTemplate), so nothing gives it the
    /// usual Selector click-to-select behaviour - this is that behaviour, routed through
    /// List.Selected so every dependent (ShellViewModel's Can* properties, the bottom bar, the
    /// expansion) updates the same way a real Selector's SelectedItem binding would have driven
    /// it.
    /// </summary>
    private void Row_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is SnapshotRowViewModel row) shell.List.Selected = row;
    }

    /// <summary>
    /// Rename, Delete and Restore render live with correct enablement and open a placeholder
    /// naming the session that builds them - the same answer plan 3 gave Settings
    /// (App.OpenSettings). Back up now is real: it calls App.BackUpNow, refreshes the list, and
    /// selects the row the capture just wrote.
    /// </summary>
    private void WireBottomBar()
    {
        RenameButton.Click += (_, _) => MessageBox.Show(
            "Renaming a backup arrives in the next plan.", "Wave Link Backup",
            MessageBoxButton.OK, MessageBoxImage.Information);

        DeleteButton.Click += (_, _) => MessageBox.Show(
            "Deleting a backup arrives in the next plan.", "Wave Link Backup",
            MessageBoxButton.OK, MessageBoxImage.Information);

        RestoreButton.Click += (_, _) => MessageBox.Show(
            "Restoring a backup arrives in the next plan.", "Wave Link Backup",
            MessageBoxButton.OK, MessageBoxImage.Information);

        BackUpNowButton.Click += async (_, _) =>
        {
            var app = (App)Application.Current;
            var result = app.BackUpNow();

            await shell.List.RefreshAsync();

            if (result.IsSuccess) shell.List.Select(result.Value.Id);
        };

        SettingsButton.Click += (_, _) => App.OpenSettings();
    }

    private void WireSearch()
    {
        ClearSearchButton.Click += (_, _) => shell.List.ClearSearch();
        ClearSearchLinkButton.Click += (_, _) => shell.List.ClearSearch();
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

        var app = (App)Application.Current;

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
