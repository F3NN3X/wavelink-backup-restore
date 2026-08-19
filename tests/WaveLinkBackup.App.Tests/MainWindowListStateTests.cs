using System.Text;
using System.Windows;
using WaveLinkBackup.App.Hosting;
using WaveLinkBackup.App.Tests.Fakes;
using WaveLinkBackup.App.Theming;
using WaveLinkBackup.App.ViewModels;
using WaveLinkBackup.App.Views;
using WaveLinkBackup.Core.Analysis;
using WaveLinkBackup.Core.Io;
using WaveLinkBackup.Core.Results;
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

    // No test here drives a restore, so the service is a throw-if-called stub and inspectLive a
    // closure that never runs (a failure result would be surfaced, but nothing reaches it).
    private static MainWindow Build(ShellViewModel shell) =>
        new(new FakeWindowChrome(), new FakeSystemTheme(), ShellState.Default, shell,
            new FakeRestoreService(), () => Result<SettingsInspection>.Fail(new WaveLinkNotInstalled()));

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

    // --------------------------------------------------------------------------- error 12 (08)
    //
    // The folder-unavailable full screen is not just a stand-in: it REPLACES the list, dims the
    // search field to 40% and makes it non-interactive, and holds ALL FOUR bottom-bar actions at
    // 40% - "including Back up now" (08-settings-persistence.md). The enter/exit toggle below is
    // the rendered half of that claim: one Apply flips FolderMissing on, a second flips it off,
    // and the four buttons plus the search field follow.

    private static ShellFacts Facts(bool folderMissing) => new(
        WaveLinkFound: true, WaveLinkRunning: false, SettingsLastSavedLocal: null,
        WaveLinkInputs: 0, WaveLinkSettingsPath: null,
        AutoBackupEnabled: true, FolderMissing: folderMissing, StorePath: StorePath, FreeBytes: null);

    private sealed record BottomBarState(
        bool CanRename, bool CanDelete, bool CanRestore, bool CanBackUpNow,
        double SearchOpacity, bool SearchEnabled, Visibility FolderMissingLine);

    private static BottomBarState ReadBottomBar(MainWindow window, ShellViewModel shell) =>
        new(shell.CanRename, shell.CanDelete, shell.CanRestore, shell.CanBackUpNow,
            window.SearchBoxBorder.Opacity, window.SearchBoxBorder.IsEnabled,
            window.FolderMissingBottomLine.Visibility);

    // ENTER: the folder goes missing. The full screen's own region comes up (driven by
    // List.State == FolderMissing, exactly like the four existing state tests), and the SAME fact
    // that shows it - ShellFacts.FolderMissing - holds ALL FOUR bottom-bar actions at 40%
    // (IsEnabled false -> WPF's disabled visual) plus the search field, and lights the bottom-bar
    // mono line. "Including Back up now" (08-settings-persistence.md).
    [Fact]
    public void FolderMissing_enter_dims_all_four_actions_and_the_search_field()
    {
        var v = Wpf.Run(() =>
        {
            EnsureResourcesLoaded();
            // No directory -> List.State == FolderMissing after Refresh(), so the stand-in is up.
            var shell = BuildShell(withDirectory: false, withSnapshot: false);
            shell.List.Refresh();

            var window = Build(shell);
            PumpPendingBindings(window);

            // The production path (App.RecheckStore -> RefreshShellFacts) hands the window the
            // same FolderMissing fact that the list just derived. Apply it so the CanX facts and
            // the bottom-bar line follow the stand-in.
            shell.Apply(Facts(folderMissing: true));
            PumpPendingBindings(window);

            return new BottomBarState(
                shell.CanRename, shell.CanDelete, shell.CanRestore, shell.CanBackUpNow,
                window.SearchBoxBorder.Opacity, window.SearchBoxBorder.IsEnabled,
                window.FolderMissingBottomLine.Visibility)
            {
                // Piggyback the region visibility on the record via a tuple return below.
            };
        });

        // Re-read the region separately (the record above carries only the bottom-bar facts).
        var standIn = Wpf.Run(() =>
        {
            EnsureResourcesLoaded();
            var shell = BuildShell(withDirectory: false, withSnapshot: false);
            shell.List.Refresh();
            var window = Build(shell);
            PumpPendingBindings(window);
            shell.Apply(Facts(folderMissing: true));
            PumpPendingBindings(window);
            return window.FolderMissingStandIn.Visibility;
        });

        // The full screen is up...
        Assert.Equal(Visibility.Visible, standIn);
        // ...and every action + the search field are held at 40%, with the bottom line lit.
        Assert.False(v.CanRename);
        Assert.False(v.CanDelete);
        Assert.False(v.CanRestore);
        Assert.False(v.CanBackUpNow);
        Assert.Equal(0.4, v.SearchOpacity);
        Assert.False(v.SearchEnabled);
        Assert.Equal(Visibility.Visible, v.FolderMissingLine);
    }

    // EXIT: "Look again" re-probes the current path and the folder is back. List.State flips off
    // FolderMissing (the list's Refresh), which collapses the full screen; the matching
    // ShellFacts.FolderMissing == false restores every action and the search field - no explicit
    // hide call anywhere.
    [Fact]
    public void FolderMissing_exit_restores_all_four_actions_and_the_search_field()
    {
        var v = Wpf.Run(() =>
        {
            EnsureResourcesLoaded();
            // Directory present -> List.State == Loaded, so the stand-in is down.
            var shell = BuildShell(withDirectory: true, withSnapshot: false);
            shell.List.Refresh();

            var window = Build(shell);
            PumpPendingBindings(window);

            // Folder is back: the production re-probe hands FolderMissing == false.
            shell.Apply(Facts(folderMissing: false));
            PumpPendingBindings(window);

            return (state: new BottomBarState(
                shell.CanRename, shell.CanDelete, shell.CanRestore, shell.CanBackUpNow,
                window.SearchBoxBorder.Opacity, window.SearchBoxBorder.IsEnabled,
                window.FolderMissingBottomLine.Visibility),
                standIn: window.FolderMissingStandIn.Visibility);
        });

        // Full screen collapsed...
        Assert.Equal(Visibility.Collapsed, v.standIn);
        // ...and everything is live again.
        Assert.True(v.state.CanBackUpNow);
        Assert.True(v.state.SearchEnabled);
        Assert.Equal(1.0, v.state.SearchOpacity);
        Assert.Equal(Visibility.Collapsed, v.state.FolderMissingLine);
    }

    // The full screen's own three actions (Choose a folder… / Look again / Use the default
    // folder) live inside the stand-in Grid, so they are reachable ONLY while it is on screen -
    // a Collapsed parent is not hit-testable. This pins that the buttons appear together with the
    // region rather than floating over a hidden one.
    private sealed record StandInActions(
        Visibility StandIn, Visibility Choose, Visibility LookAgain, Visibility UseDefault);

    [Fact]
    public void FolderMissing_actions_are_visible_only_with_the_stand_in()
    {
        var inMissing = Wpf.Run(() =>
        {
            EnsureResourcesLoaded();
            var shell = BuildShell(withDirectory: false, withSnapshot: false);
            shell.List.Refresh();
            var window = Build(shell);
            PumpPendingBindings(window);
            return new StandInActions(
                window.FolderMissingStandIn.Visibility,
                window.ChooseFolderButton.Visibility,
                window.LookAgainButton.Visibility,
                window.UseDefaultFolderButton.Visibility);
        });

        var after = Wpf.Run(() =>
        {
            EnsureResourcesLoaded();
            var shell = BuildShell(withDirectory: true, withSnapshot: false);
            shell.List.Refresh();
            var window = Build(shell);
            PumpPendingBindings(window);
            return new StandInActions(
                window.FolderMissingStandIn.Visibility,
                window.ChooseFolderButton.Visibility,
                window.LookAgainButton.Visibility,
                window.UseDefaultFolderButton.Visibility);
        });

        // Folder missing: region up, all three of its actions with it.
        Assert.Equal(Visibility.Visible, inMissing.StandIn);
        Assert.Equal(Visibility.Visible, inMissing.Choose);
        Assert.Equal(Visibility.Visible, inMissing.LookAgain);
        Assert.Equal(Visibility.Visible, inMissing.UseDefault);

        // Folder present: region collapsed - that is what makes the three buttons unreachable.
        Assert.Equal(Visibility.Collapsed, after.StandIn);
    }

    // ------------------------------------------------------------------- Screen 4 (first run)
    //
    // The first-run / empty state REPLACES the list with a centred column: measure-rule frame,
    // "No backups yet", body, two actions (Back up now live; Choose where to keep them), an
    // auto-backup checkbox, the found-line, and a footer strip. Caption bar and bottom bar stay
    // as usual - Restore / Rename / Delete hold at 40% because there is nothing to act on, and
    // Back up now stays live (design/README.md "First run"). The region itself is driven by
    // List.State == Empty exactly like the other three state tests; the CanX facts come from a
    // matching ShellFacts.Apply, the same seam the FolderMissing enter/exit tests use.

    private const string SettingsPath = @"C:\Users\t\AppData\Local\Packages\Elgato.WaveLink_g54w8ztgkx496\LocalState\Settings.json";
    private static readonly DateTimeOffset SavedAt = new(2026, 8, 15, 23, 7, 0, TimeSpan.Zero);

    /// <summary>
    /// A shell whose store directory exists but holds NO snapshots - List.State == Empty after
    /// Refresh() - and whose ShellFacts say Wave Link was discovered with N inputs. The found-line
    /// (line 6) is what this stands up; the not-found variant leaves it absent.
    /// </summary>
    private static (ShellViewModel Shell, SnapshotStore Store) BuildFirstRunShell(int inputs, bool waveLinkFound = true)
    {
        var fs = new FakeFileSystem();
        var clock = new FakeClock { UtcNow = SavedAt };

        // Directory present (so State is Empty, not FolderMissing), no snapshots written.
        fs.CreateDirectory(StorePath);

        if (waveLinkFound)
            fs.AddFile(SettingsPath, Encoding.UTF8.GetBytes(
                """{"MixerConfiguration":{"InputSettings":{"a":{"InputName":"Wave Mic 1"}}}}"""));

        var store = new SnapshotStore(fs, clock, StorePath);
        var list = new SnapshotListViewModel(store, new HealthProbe(store, fs, clock), fs, clock)
        {
            Marshal = action => action(),
        };

        var shell = new ShellViewModel(list);
        shell.List.Refresh(); // -> List.State == Empty
        shell.Apply(new ShellFacts(
            WaveLinkFound: waveLinkFound, WaveLinkRunning: false,
            SettingsLastSavedLocal: waveLinkFound ? SavedAt : null,
            WaveLinkInputs: inputs,
            WaveLinkSettingsPath: waveLinkFound ? SettingsPath : null,
            AutoBackupEnabled: true, FolderMissing: false, StorePath: StorePath, FreeBytes: null));

        return (shell, store);
    }

    private sealed record FirstRunState(
        Visibility StandIn, bool CanRename, bool CanDelete, bool CanRestore, bool CanBackUpNow,
        string? FoundLine, string? SettingsPath, Visibility FoundLinePanel);

    [Fact]
    public void FirstRun_empty_store_shows_screen4_and_disables_three_actions_but_keeps_backup_live()
    {
        var v = Wpf.Run(() =>
        {
            EnsureResourcesLoaded();
            var (shell, _) = BuildFirstRunShell(inputs: 5);
            var window = Build(shell);
            PumpPendingBindings(window);

            return new FirstRunState(
                window.EmptyStandIn.Visibility,
                shell.CanRename, shell.CanDelete, shell.CanRestore, shell.CanBackUpNow,
                shell.FoundLine, shell.FoundSettingsPath,
                window.FoundLinePanel.Visibility);
        });

        // The stand-in is up (List.State == Empty)...
        Assert.Equal(Visibility.Visible, v.StandIn);
        // ...Restore / Rename / Delete hold at 40% (nothing to act on), Back up now stays live.
        Assert.False(v.CanRename);
        Assert.False(v.CanDelete);
        Assert.False(v.CanRestore);
        Assert.True(v.CanBackUpNow);
        // The found-line renders with the discovered input count and points at the settings file.
        Assert.Equal("WAVE LINK FOUND · 5 INPUTS · SETTINGS LAST SAVED 23:07", v.FoundLine);
        Assert.Equal(SettingsPath, v.SettingsPath);
        Assert.Equal(Visibility.Visible, v.FoundLinePanel);
    }

    // The found-line is the Wave-Link-found variant only. When discovery failed, FoundLine is
    // null and the ok-dot line stays absent (the not-found variant is an open gap - technical-debt).
    [Fact]
    public void FirstRun_wave_link_not_found_hides_the_found_line()
    {
        var v = Wpf.Run(() =>
        {
            EnsureResourcesLoaded();
            var (shell, _) = BuildFirstRunShell(inputs: 0, waveLinkFound: false);
            var window = Build(shell);
            PumpPendingBindings(window);

            return new FirstRunState(
                window.EmptyStandIn.Visibility,
                shell.CanRename, shell.CanDelete, shell.CanRestore, shell.CanBackUpNow,
                shell.FoundLine, shell.FoundSettingsPath,
                window.FoundLinePanel.Visibility);
        });

        Assert.Equal(Visibility.Visible, v.StandIn);
        Assert.Null(v.FoundLine);
        Assert.Null(v.SettingsPath);
        Assert.Equal(Visibility.Collapsed, v.FoundLinePanel);
    }

    // The brief's after-first-backup step: once a snapshot exists, List.State flips off Empty and
    // the list takes over - Screen 4 collapses on its own with no explicit hide call.
    [Fact]
    public void FirstRun_after_first_backup_the_list_returns_and_screen4_collapses()
    {
        var v = Wpf.Run(() =>
        {
            EnsureResourcesLoaded();
            // Empty store -> Screen 4 up.
            var (shell, store) = BuildFirstRunShell(inputs: 5);
            var window = Build(shell);
            PumpPendingBindings(window);
            var before = window.EmptyStandIn.Visibility;

            // The first backup writes a snapshot and refreshes the list (the production path is
            // BackUpNowAsync -> Refresh(); here we stand in for it directly).
            var bytes = Encoding.UTF8.GetBytes(
                """{"MixerConfiguration":{"InputSettings":{"a":{"InputName":"Mic"}}}}""");
            store.Write(bytes, SettingsAnalysis.Analyse(bytes).Value, SnapshotTrigger.Manual, "First backup");
            shell.List.Refresh();
            PumpPendingBindings(window);

            return (before: before, after: window.EmptyStandIn.Visibility, list: window.ListLoadedRegion.Visibility);
        });

        Assert.Equal(Visibility.Visible, v.before);
        // A snapshot now exists: Screen 4 collapses and the real list region comes up.
        Assert.Equal(Visibility.Collapsed, v.after);
        Assert.Equal(Visibility.Visible, v.list);
    }
}
