using System.Text;
using System.Windows;
using WaveLinkBackup.App.Hosting;
using WaveLinkBackup.App.Tests.Fakes;
using WaveLinkBackup.App.Theming;
using WaveLinkBackup.App.ViewModels;
using WaveLinkBackup.App.Views;
using WaveLinkBackup.Core.Analysis;
using WaveLinkBackup.Core.Snapshots;
using WaveLinkBackup.Core.Tests.Fakes;

namespace WaveLinkBackup.App.Tests;

/// <summary>
/// Row 3's four ListState-driven regions - Loaded, NoResults, Empty, FolderMissing - are one Grid
/// with four named siblings, each switched by a Style.Trigger on List.State (MainWindow.xaml).
/// This is the rendered half of that claim: MainWindowTemplateTests proves each state is WIRED to
/// SOME trigger; this proves the trigger actually lands on the right element and nothing else.
///
/// Fix 1 moved the Loaded/Collapsed trigger off ListScrollViewer itself and onto the
/// ListLoadedRegion Grid that now wraps it alongside the new search-footer strip (so the two show
/// and hide together) - this reads ListLoadedRegion.Visibility rather than
/// ListScrollViewer.Visibility for exactly that reason: the ScrollViewer's own Visibility property
/// no longer carries a trigger of its own and always reports Visible.
///
/// No test here calls Show()/RefreshAsync/Loaded - constructing the window and calling the
/// view model's synchronous Refresh() is enough to populate List.State, and staying off the async
/// health-probe path avoids the deadlock risk of blocking Wpf.Run's own dispatcher thread on a
/// Task that marshals back onto that same thread (see MainWindow.xaml.cs: Marshal is wired to
/// Dispatcher.Invoke unconditionally in the constructor).
/// </summary>
public sealed class MainWindowListStateTests
{
    private const string StorePath = @"C:\store";

    private static void EnsureResourcesLoaded()
    {
        var dictionaries = Application.Current.Resources.MergedDictionaries;
        dictionaries.Clear();
        ThemeManager.Apply(AppTheme.Dark);

        foreach (var file in new[] { "Typography.xaml", "RowStyles.xaml", "ControlStyles.xaml" })
        {
            dictionaries.Add(new ResourceDictionary
            {
                Source = new Uri(
                    $"pack://application:,,,/WaveLinkBackup;component/Views/{file}", UriKind.Absolute),
            });
        }
    }

    private static ShellViewModel BuildShell(bool withDirectory, bool withSnapshot)
    {
        var fs = new FakeFileSystem();
        var clock = new FakeClock { UtcNow = new DateTimeOffset(2026, 8, 15, 23, 7, 0, TimeSpan.Zero) };

        if (withDirectory) fs.CreateDirectory(StorePath);

        var store = new SnapshotStore(fs, clock, StorePath);

        if (withSnapshot)
        {
            var bytes = Encoding.UTF8.GetBytes(
                """{"MixerConfiguration":{"InputSettings":{"a":{"InputName":"Mic"}}}}""");
            store.Write(bytes, SettingsAnalysis.Analyse(bytes).Value, SnapshotTrigger.Manual, "Backup one");
        }

        var list = new SnapshotListViewModel(store, new HealthProbe(store, fs, clock), fs, clock)
        {
            // Inline, exactly like every other view-model-level test - RefreshAsync/the probe are
            // never exercised here, only the synchronous Refresh() that sets List.State.
            Marshal = action => action(),
        };

        return new ShellViewModel(list);
    }

    private static MainWindow Build(ShellViewModel shell) =>
        new(new FakeWindowChrome(), new FakeSystemTheme(), ShellState.Default, shell);

    private sealed record Visibilities(
        Visibility List, Visibility Header, Visibility NoResults, Visibility Empty, Visibility FolderMissing);

    /// <summary>
    /// Style-driven DataTrigger effects (Visibility here) are applied through WPF's own property
    /// invalidation queue, not synchronously the instant DataContext is set - in a real, running
    /// app that queue drains on its own because the dispatcher is continuously pumping render and
    /// layout passes. A single blocking Wpf.Run call never does that, so reading .Visibility
    /// immediately after construction can observe the PRE-trigger value. One
    /// Dispatcher.Invoke at ContextIdle forces everything queued ahead of it - including the
    /// binding-driven Setters - to run first.
    /// </summary>
    private static void PumpPendingBindings(MainWindow window) =>
        window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);

    [Fact]
    public void Loaded_shows_the_list_and_the_column_header()
    {
        var v = Wpf.Run(() =>
        {
            EnsureResourcesLoaded();
            var shell = BuildShell(withDirectory: true, withSnapshot: true);
            shell.List.Refresh();

            var window = Build(shell);
            PumpPendingBindings(window);
            return new Visibilities(
                window.ListLoadedRegion.Visibility, window.ColumnHeaderBorder.Visibility,
                window.NoResultsPanel.Visibility, window.EmptyStandIn.Visibility,
                window.FolderMissingStandIn.Visibility);
        });

        Assert.Equal(Visibility.Visible, v.List);
        Assert.Equal(Visibility.Visible, v.Header);
        Assert.Equal(Visibility.Collapsed, v.NoResults);
        Assert.Equal(Visibility.Collapsed, v.Empty);
        Assert.Equal(Visibility.Collapsed, v.FolderMissing);
    }

    // 07: the column header stays on screen during a search, precisely so an empty RESULT never
    // looks like an empty APP.
    [Fact]
    public void NoResults_shows_its_own_panel_but_keeps_the_column_header()
    {
        var v = Wpf.Run(() =>
        {
            EnsureResourcesLoaded();
            var shell = BuildShell(withDirectory: true, withSnapshot: true);
            shell.List.Refresh();
            shell.List.Query = "no such backup exists";

            var window = Build(shell);
            PumpPendingBindings(window);
            return new Visibilities(
                window.ListLoadedRegion.Visibility, window.ColumnHeaderBorder.Visibility,
                window.NoResultsPanel.Visibility, window.EmptyStandIn.Visibility,
                window.FolderMissingStandIn.Visibility);
        });

        Assert.Equal(Visibility.Collapsed, v.List);
        Assert.Equal(Visibility.Visible, v.Header);
        Assert.Equal(Visibility.Visible, v.NoResults);
        Assert.Equal(Visibility.Collapsed, v.Empty);
        Assert.Equal(Visibility.Collapsed, v.FolderMissing);
    }

    // The brief's Step 4: the header goes with the list for Empty/FolderMissing, and the
    // stand-in renders its own copy instead (ColumnHeaderBorder itself is collapsed here).
    [Fact]
    public void Empty_shows_its_stand_in_and_hides_the_real_column_header()
    {
        var v = Wpf.Run(() =>
        {
            EnsureResourcesLoaded();
            var shell = BuildShell(withDirectory: true, withSnapshot: false);
            shell.List.Refresh();

            var window = Build(shell);
            PumpPendingBindings(window);
            return new Visibilities(
                window.ListLoadedRegion.Visibility, window.ColumnHeaderBorder.Visibility,
                window.NoResultsPanel.Visibility, window.EmptyStandIn.Visibility,
                window.FolderMissingStandIn.Visibility);
        });

        Assert.Equal(Visibility.Collapsed, v.List);
        Assert.Equal(Visibility.Collapsed, v.Header);
        Assert.Equal(Visibility.Collapsed, v.NoResults);
        Assert.Equal(Visibility.Visible, v.Empty);
        Assert.Equal(Visibility.Collapsed, v.FolderMissing);
    }

    [Fact]
    public void FolderMissing_shows_its_stand_in_and_hides_the_real_column_header()
    {
        var v = Wpf.Run(() =>
        {
            EnsureResourcesLoaded();
            var shell = BuildShell(withDirectory: false, withSnapshot: false);
            shell.List.Refresh();

            var window = Build(shell);
            PumpPendingBindings(window);
            return new Visibilities(
                window.ListLoadedRegion.Visibility, window.ColumnHeaderBorder.Visibility,
                window.NoResultsPanel.Visibility, window.EmptyStandIn.Visibility,
                window.FolderMissingStandIn.Visibility);
        });

        Assert.Equal(Visibility.Collapsed, v.List);
        Assert.Equal(Visibility.Collapsed, v.Header);
        Assert.Equal(Visibility.Collapsed, v.NoResults);
        Assert.Equal(Visibility.Collapsed, v.Empty);
        Assert.Equal(Visibility.Visible, v.FolderMissing);
    }
}
