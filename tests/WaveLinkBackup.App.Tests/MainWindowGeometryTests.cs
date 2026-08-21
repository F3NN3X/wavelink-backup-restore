using System.Windows;
using WaveLinkBackup.App.Hosting;
using WaveLinkBackup.App.Tests.Fakes;
using WaveLinkBackup.App.Theming;
using WaveLinkBackup.App.ViewModels;
using WaveLinkBackup.App.Views;
using WaveLinkBackup.Core.Io;
using WaveLinkBackup.Core.Results;

namespace WaveLinkBackup.App.Tests;

/// <summary>
/// MainWindow's half of the ShellState contract: turning a remembered rectangle into an actual
/// window position (<c>Restore</c>) and reading the window's current bounds back into one
/// (<c>CurrentGeometry</c>). <see cref="ShellState.IsOnScreen"/> itself is ShellStateTests' job -
/// this covers only the wiring Task 4 built around it, which shipped with no coverage of its own.
///
/// Every test runs on Wpf.Run's single STA thread and loads the resources MainWindow.xaml's
/// StaticResources need - WlCaptionButton, WlCaptionCloseButton, WlShieldCheckGeometry, all
/// declared in Views/ControlStyles.xaml - before constructing a window. Normally App.xaml.cs's
/// own InitializeComponent merges those; nothing here runs App, so each test does it manually or
/// InitializeComponent throws resolving a StaticResource that was never merged.
///
/// No test calls Show()/ShowDialog(). A window that has never been shown never gets an HWND, and
/// (confirmed empirically before writing this file, with a throwaway probe test since deleted)
/// RestoreBounds is Rect.Empty regardless of WindowState, because nothing has ever driven it
/// through a real maximize/restore transition. CurrentGeometry's Left/Top/Width/Height come from
/// RestoreBounds, so those four fields are deliberately not asserted anywhere below - that half of
/// the contract needs a shown window, which the brief's fix-scope rules out for this suite. It
/// stays a "look at it" item, same as the DWM interop design section E already excludes.
/// IsMaximized and ClosingHidesToTray never touch RestoreBounds and are fully covered.
/// </summary>
public sealed class MainWindowGeometryTests
{
    // Real desktop coordinates, not fakes: MainWindow's SystemScreens() talks to
    // EnumDisplayMonitors directly (Task 4 moved it off System.Windows.Forms.Screen for build
    // reasons - see task-4-report.md, deviation 4) and is not an injectable seam, so there is no
    // DI point for a fake monitor layout. Windows always anchors the primary monitor's origin at
    // (0,0) in virtual-screen coordinates, so a small rectangle there overlaps the primary
    // monitor on any real or CI machine; a rectangle five million units out does not overlap
    // anything real ever built.
    private static readonly ShellState OnScreen =
        new(50, 50, 400, 300, IsMaximized: false, ClosingHidesToTray: true, Theme: ThemePreference.Auto);

    private static readonly ShellState OnScreenMaximized =
        new(50, 50, 400, 300, IsMaximized: true, ClosingHidesToTray: true, Theme: ThemePreference.Auto);

    private static readonly ShellState OffScreen =
        new(5_000_000, 5_000_000, 400, 300, IsMaximized: false, ClosingHidesToTray: true, Theme: ThemePreference.Auto);

    /// <summary>
    /// A minimal, otherwise-default ShellViewModel - Task 10b added a fourth constructor
    /// parameter to MainWindow, and every test in this file needs SOME view model to hand it,
    /// even though none of them assert anything about the shell itself.
    /// </summary>
    private static ShellViewModel Shell() => ShellViewModelHarness.Build(
        waveLinkRunning: true, waveLinkFound: true, folderMissing: false, autoBackupEnabled: true,
        freeBytes: 100, storePath: @"C:\store",
        savedAt: new DateTimeOffset(2026, 8, 15, 23, 7, 0, TimeSpan.Zero));

    // No test here drives a restore, so the service is a throw-if-called stub and inspectLive a
    // closure that never runs (a failure result would be surfaced, but nothing reaches it).
    private static MainWindow Build(ShellState state) =>
        new(new FakeWindowChrome(), new FakeSystemTheme(), state, Shell(),
            new FakeRestoreService(), () => Result<SettingsInspection>.Fail(new WaveLinkNotInstalled()));

    /// <summary>
    /// What App.xaml.cs's own InitializeComponent normally merges into Application.Resources.
    /// Wpf.cs's harness constructs a bare System.Windows.Application rather than this app's App,
    /// so nothing does this automatically here. Typography and RowStyles joined ControlStyles in
    /// Task 10b: MainWindow.xaml now references TrackedText styles from both (WlColumnHeaderTrackedText,
    /// WlStatusStripTrackedText) and the row template itself (WlRowTemplate), all StaticResource,
    /// so InitializeComponent throws resolving them unless every one of these is merged first.
    /// </summary>
    private static void EnsureCaptionResourcesLoaded()
    {
        var dictionaries = Application.Current.Resources.MergedDictionaries;
        dictionaries.Clear();
        ThemeManager.Apply(AppTheme.Dark);
        dictionaries.Add(new ResourceDictionary
        {
            Source = new Uri(
                "pack://application:,,,/WaveLinkBackup;component/Views/Typography.xaml",
                UriKind.Absolute),
        });
        dictionaries.Add(new ResourceDictionary
        {
            Source = new Uri(
                "pack://application:,,,/WaveLinkBackup;component/Views/RowStyles.xaml",
                UriKind.Absolute),
        });
        dictionaries.Add(new ResourceDictionary
        {
            Source = new Uri(
                "pack://application:,,,/WaveLinkBackup;component/Views/ControlStyles.xaml",
                UriKind.Absolute),
        });
    }

    [Fact]
    public void A_remembered_geometry_on_an_existing_screen_is_applied()
    {
        var (left, top, width, height, startup) = Wpf.Run(() =>
        {
            EnsureCaptionResourcesLoaded();
            var window = Build(OnScreen);
            return (window.Left, window.Top, window.Width, window.Height, window.WindowStartupLocation);
        });

        Assert.Equal(50, left);
        Assert.Equal(50, top);
        Assert.Equal(400, width);
        Assert.Equal(300, height);
        Assert.Equal(WindowStartupLocation.Manual, startup);
    }

    // The trap this exists for: a window remembered on a monitor that has since been unplugged
    // opens where nobody can see it, and a tray app whose window "won't open" reads exactly like
    // one that has crashed.
    [Fact]
    public void A_remembered_geometry_off_every_screen_is_rejected_and_the_default_is_kept()
    {
        var (width, height, startup) = Wpf.Run(() =>
        {
            EnsureCaptionResourcesLoaded();
            var window = Build(OffScreen);
            return (window.Width, window.Height, window.WindowStartupLocation);
        });

        // MainWindow.xaml's own designed size (README: Window geometry) - Restore() never
        // touched it because the remembered rectangle is nowhere near a real screen.
        Assert.Equal(1180, width);
        Assert.Equal(760, height);
        Assert.Equal(WindowStartupLocation.CenterScreen, startup);
    }

    [Fact]
    public void No_remembered_geometry_leaves_the_window_at_its_default_size_and_position()
    {
        var (width, height, startup) = Wpf.Run(() =>
        {
            EnsureCaptionResourcesLoaded();
            var window = Build(ShellState.Default);
            return (window.Width, window.Height, window.WindowStartupLocation);
        });

        Assert.Equal(1180, width);
        Assert.Equal(760, height);
        Assert.Equal(WindowStartupLocation.CenterScreen, startup);
    }

    [Fact]
    public void A_maximized_remembered_state_restores_as_maximized()
    {
        var state = Wpf.Run(() =>
        {
            EnsureCaptionResourcesLoaded();
            return Build(OnScreenMaximized).WindowState;
        });

        Assert.Equal(WindowState.Maximized, state);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CurrentGeometry_carries_closingHidesToTray_through_as_passed(bool closingHidesToTray)
    {
        var carried = Wpf.Run(() =>
        {
            EnsureCaptionResourcesLoaded();
            var current = OnScreen with { ClosingHidesToTray = closingHidesToTray };

            return Build(OnScreen).CurrentGeometry(current).ClosingHidesToTray;
        });

        Assert.Equal(closingHidesToTray, carried);
    }

    /// <summary>
    /// Everything that is NOT geometry has to survive the save on the shutdown path. The theme
    /// preference is the field that made this concrete: CurrentGeometry used to build a whole
    /// ShellState from a rectangle plus one argument per remembered setting, so a new setting was
    /// dropped every time the window closed - visible to the user as a theme that reverts to Auto
    /// on the next launch.
    /// </summary>
    [Fact]
    public void CurrentGeometry_carries_every_non_geometry_field_through()
    {
        var carried = Wpf.Run(() =>
        {
            EnsureCaptionResourcesLoaded();
            var current = OnScreen with { Theme = ThemePreference.Light, ClosingHidesToTray = false };

            return Build(OnScreen).CurrentGeometry(current);
        });

        Assert.Equal(ThemePreference.Light, carried.Theme);
        Assert.False(carried.ClosingHidesToTray);
    }

    [Fact]
    public void CurrentGeometry_reports_maximized_when_the_window_is_maximized()
    {
        var isMaximized = Wpf.Run(() =>
        {
            EnsureCaptionResourcesLoaded();
            return Build(OnScreenMaximized).CurrentGeometry(OnScreenMaximized).IsMaximized;
        });

        Assert.True(isMaximized);
    }

    [Fact]
    public void CurrentGeometry_reports_not_maximized_when_the_window_is_normal()
    {
        var isMaximized = Wpf.Run(() =>
        {
            EnsureCaptionResourcesLoaded();
            return Build(OnScreen).CurrentGeometry(OnScreen).IsMaximized;
        });

        Assert.False(isMaximized);
    }
}
