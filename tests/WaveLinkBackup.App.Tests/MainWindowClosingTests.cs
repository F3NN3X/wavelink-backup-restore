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
/// OnClosing used to hard-cast Application.Current to App. Wpf.cs's harness runs a bare
/// System.Windows.Application for the whole test assembly (Application.Current is per-process,
/// so a second real App cannot be constructed here) - exactly the condition production never
/// hits but this suite always does the moment a test reaches OnClosing. That made the previous
/// cast throw InvalidCastException, and because Application.Current is shared assembly-wide,
/// whether any given test run witnessed it depended on test order - an order-dependent flake.
///
/// This file exists to pin the fixed behaviour down: on this harness, Application.Current is
/// never an App, so closing a MainWindow here must complete normally (not hide, not throw)
/// every time, regardless of what ran before it.
/// </summary>
public sealed class MainWindowClosingTests
{
    private static readonly ShellState OnScreen =
        new(50, 50, 400, 300, IsMaximized: false, ClosingHidesToTray: true, Theme: ThemePreference.Auto);

    private static ShellViewModel Shell() => ShellViewModelHarness.Build(
        waveLinkRunning: true, waveLinkFound: true, folderMissing: false, autoBackupEnabled: true,
        freeBytes: 100, storePath: @"C:\store",
        savedAt: new DateTimeOffset(2026, 8, 15, 23, 7, 0, TimeSpan.Zero));

    // No test here drives a restore, so the service is a throw-if-called stub and inspectLive a
    // closure that never runs (a failure result would be surfaced, but nothing reaches it).
    private static MainWindow Build() =>
        new(new FakeWindowChrome(), new FakeSystemTheme(), OnScreen, Shell(),
            new FakeRestoreService(), () => Result<SettingsInspection>.Fail(new WaveLinkNotInstalled()));

    // Mirrors MainWindowGeometryTests.EnsureCaptionResourcesLoaded - MainWindow.xaml's
    // InitializeComponent needs these merged before construction or it throws resolving a
    // StaticResource that App.xaml.cs would normally have merged for us.
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

    // The regression test: on Wpf.Run's harness, Application.Current is a plain Application, not
    // an App, which is precisely the condition the old hard cast blew up on. Close() on a window
    // that was never Show()n still runs OnClosing (confirmed here: IsClosed flips to true and no
    // exception surfaces), so this drives the real bug without violating the suite's
    // never-Show()/ShowDialog() rule.
    //
    // What is NOT covered here, and why: the hide branch of OnClosing (Application.Current IS an
    // App, ClosingHidesToTray on, not shutting down -> e.Cancel = true; Hide()). That path needs a
    // real App installed as Application.Current, and WPF allows exactly one Application per
    // AppDomain - Wpf.cs's shared bare Application occupies the slot, and `new App()` throws
    // InvalidOperationException (confirmed empirically with a throwaway probe before writing this
    // note; Application.Current is not settable). So the hide-vs-exit behaviour is a "look at it"
    // item for the tray shell, same class of exclusion as the DWM interop and unshown-window
    // geometry already documented in MainWindowGeometryTests. The exit branch (IsShuttingDown or
    // ClosingHidesToTray off -> close normally) is exercised by the test below, because on this
    // harness Application.Current is never an App and OnClosing returns before reaching it - which
    // is also why that branch's own logic is not independently pinned here.
    [Fact]
    public void Closing_a_window_when_Application_Current_is_not_an_App_closes_normally()
    {
        var (threw, isClosed) = Wpf.Run(() =>
        {
            EnsureCaptionResourcesLoaded();
            var window = Build();

            var closed = false;
            window.Closed += (_, _) => closed = true;

            Exception? caught = null;
            try
            {
                window.Close();
            }
            catch (Exception ex)
            {
                caught = ex;
            }

            return (caught, closed);
        });

        Assert.Null(threw);
        Assert.True(isClosed);
    }
}
