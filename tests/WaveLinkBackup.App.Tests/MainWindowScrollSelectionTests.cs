using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
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
/// The reported defect: with a list longer than the window, scrolling down to the end auto-selects
/// the last visible row - the selection jumps to wherever the scroll landed, with no click involved.
///
/// WPF's ListBox selects on keyboard navigation (PageDown / End / arrow) because focus moves onto a
/// row and OnGotKeyboardFocus -> MakeKeyboardSelection follows it; pure wheel/scrollbar scrolling
/// does NOT move that focus and therefore cannot select. This test drives the REAL MainWindow and
/// measures, gesture by gesture, which input actually moves the selection - so the fix targets the
/// mechanism the user is hitting rather than a guess.
/// </summary>
public sealed class MainWindowScrollSelectionTests
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

    /// <summary>
    /// 40 snapshots spread across 10 days (four per day), so the list is far longer than any test
    /// window and every scroll to the bottom realizes containers that were never on screen.
    /// </summary>
    private static ShellViewModel BuildShellWithManySnapshots()
    {
        var fs = new FakeFileSystem();
        var clock = new FakeClock { UtcNow = new DateTimeOffset(2026, 8, 15, 23, 7, 0, TimeSpan.Zero) };

        fs.CreateDirectory(StorePath);
        var store = new SnapshotStore(fs, clock, StorePath);

        for (var day = 0; day < 10; day++)
        {
            for (var n = 0; n < 4; n++)
            {
                // Distinct content per snapshot so dedup keeps all forty.
                var payload = System.Text.Encoding.UTF8.GetBytes(
                    "{\"MixerConfiguration\":{\"InputSettings\":{\"a\":{\"InputName\":\"" +
                    $"Mic {day}-{n}" +
                    "\"}}}}");
                store.Write(payload, SettingsAnalysis.Analyse(payload).Value, SnapshotTrigger.Manual,
                    $"Backup {day}-{n}");
                clock.UtcNow = clock.UtcNow.AddMinutes(5);
            }
        }

        var list = new SnapshotListViewModel(store, new HealthProbe(store, fs, clock), fs, clock)
        {
            Marshal = action => action(),
        };

        return new ShellViewModel(list);
    }

    private static MainWindow Build(ShellViewModel shell) =>
        new(new FakeWindowChrome(), new FakeSystemTheme(), ShellState.Default, shell,
            new FakeRestoreService(), () => Result<SettingsInspection>.Fail(new WaveLinkNotInstalled()));

    /// <summary>
    /// THE CLAIM: scrolling the list to its bottom with nothing selected must not select a row.
    /// Pure wheel/scrollbar scrolling is the gesture that cannot move WPF's keyboard focus, so it
    /// must leave the selection untouched. This PASSES today and guards against a regression where a
    /// scroll starts driving the selection.
    /// </summary>
    [Fact]
    public void Wheel_scrolling_to_the_bottom_does_not_select_a_row()
    {
        var result = Wpf.Run(() =>
        {
            EnsureResourcesLoaded();
            var shell = BuildShellWithManySnapshots();
            shell.List.Refresh();

            Assert.True(shell.List.Rows.Count >= 30,
                $"Expected a long list to scroll; got {shell.List.Rows.Count} rows.");

            var window = Build(shell);
            // Offscreen so the outer ScrollViewer has a real extent to scroll.
            window.Left = -3000;
            window.Top = -3000;
            window.Width = 480;
            window.Height = 360;
            window.ShowInTaskbar = false;

            try
            {
                window.Show();
                window.UpdateLayout();

                var scrollViewer = window.ListScrollViewer;
                Assert.True(scrollViewer.ScrollableHeight > 0,
                    "Nothing to scroll - the fixture is wrong.");

                // Nothing is selected before we touch the list.
                Assert.Null(shell.List.Selected);

                // A real user has the list FOCUSED before they scroll - click it, or tab to it.
                window.GroupsHost.Focus();
                Keyboard.Focus(window.GroupsHost);
                window.UpdateLayout();

                // Scroll the way a user actually does: wheel notches over a row. The notch must be
                // raised on a REALIZED ROW (not the ListBox) so it tunnels through the window's own
                // PreviewMouseWheel -> WheelForwarding.Redirect -> outer viewer, exactly as in
                // WheelForwardingTests. Raising it on the ListBox reproduces nothing.
                var firstRow = (UIElement)window.GroupsHost.ItemContainerGenerator.ContainerFromIndex(0);
                Assert.NotNull(firstRow);

                for (var i = 0; i < 200 && scrollViewer.VerticalOffset < scrollViewer.ScrollableHeight; i++)
                {
                    WheelNotch(firstRow);
                    window.UpdateLayout();
                }

                return (
                    ScrollableHeight: scrollViewer.ScrollableHeight,
                    VerticalOffset: scrollViewer.VerticalOffset,
                    SelectedId: shell.List.Selected?.Id,
                    SelectedCount: shell.List.Rows.Count(r => r.IsSelected));
            }
            finally
            {
                window.Close();
            }
        });

        Assert.True(result.ScrollableHeight > 0, "Nothing to scroll - the fixture is wrong.");
        // We really did reach the bottom (offset at or near extent), so this exercised the path.
        Assert.True(result.VerticalOffset >= result.ScrollableHeight * 0.9,
            $"Did not scroll to the bottom: offset {result.VerticalOffset} of {result.ScrollableHeight}.");

        // A wheel scroll must never move the selection.
        Assert.True(result.SelectedId is null,
            $"Wheel scrolling to the bottom auto-selected row '{result.SelectedId}'. " +
            "A scroll must not move the selection.");
        Assert.Equal(0, result.SelectedCount);
    }

    /// <summary>
    /// REGRESSION: moving the view's currency to the last row must NOT select that row. This is the
    /// spurious path behind the reported defect - a scroll/refresh that advances the collection-view
    /// currency used to drag the selection along with it (IsSynchronizedWithCurrentItem was left at
    /// its default True). GroupsHost now sets IsSynchronizedWithCurrentItem="False", so the currency
    /// and the selection are independent: advancing one never moves the other.
    /// </summary>
    [Fact]
    public void Moving_the_view_currency_to_the_last_row_does_not_select_it()
    {
        var result = Wpf.Run(() =>
        {
            EnsureResourcesLoaded();
            var shell = BuildShellWithManySnapshots();
            shell.List.Refresh();

            var window = Build(shell);
            window.Left = -3000;
            window.Top = -3000;
            window.Width = 480;
            window.Height = 360;
            window.ShowInTaskbar = false;

            try
            {
                window.Show();
                window.UpdateLayout();

                // Sanity: the fix is actually in place on the real tree.
                Assert.False(window.GroupsHost.IsSynchronizedWithCurrentItem,
                    "GroupsHost must set IsSynchronizedWithCurrentItem=False - the currency and the " +
                    "selection are only independent when it is off.");

                Assert.Null(shell.List.Selected);

                // Advance the collection-view currency to the last item - exactly what a scroll or a
                // refresh does. With sync on, this used to select the row; with it off, it must not.
                shell.List.View.MoveCurrentToLast();
                window.UpdateLayout();

                return (
                    SelectedId: shell.List.Selected?.Id,
                    SelectedCount: shell.List.Rows.Count(r => r.IsSelected));
            }
            finally
            {
                window.Close();
            }
        });

        Assert.True(result.SelectedId is null,
            $"Moving the view currency to the last row auto-selected '{result.SelectedId}'. " +
            "Currency movement must not move the selection.");
        Assert.Equal(0, result.SelectedCount);
    }

    /// <summary>
    /// REGRESSION: keyboard navigation must STILL select rows after the sync-off fix. The list was
    /// deliberately built as a single Selector so that ↑/↓/Home/End are WPF's own (see the XAML
    /// comment above GroupsHost); IsSynchronizedWithCurrentItem=False must not break that. End is the
    /// strongest case - it navigates to the last row and selects it, which is the intended behaviour
    /// a user pressing End expects.
    /// </summary>
    [Fact]
    public void Keyboard_End_still_selects_the_last_row()
    {
        var result = Wpf.Run(() =>
        {
            EnsureResourcesLoaded();
            var shell = BuildShellWithManySnapshots();
            shell.List.Refresh();

            var window = Build(shell);
            window.Left = -3000;
            window.Top = -3000;
            window.Width = 480;
            window.Height = 360;
            window.ShowInTaskbar = false;

            try
            {
                window.Show();
                window.UpdateLayout();

                var lastId = shell.List.Rows[^1].Id;

                window.GroupsHost.Focus();
                Keyboard.Focus(window.GroupsHost);
                window.UpdateLayout();

                PressKey(window.GroupsHost, Key.End);
                window.UpdateLayout();

                return (SelectedId: shell.List.Selected?.Id, LastId: lastId);
            }
            finally
            {
                window.Close();
            }
        });

        Assert.NotNull(result.LastId);
        Assert.True(string.Equals(result.LastId, result.SelectedId, StringComparison.Ordinal),
            $"Pressing End must select the last row ('{result.LastId}'); got " +
            $"'{result.SelectedId}'. Keyboard navigation is intended and must not regress from the " +
            "sync-off fix.");
    }

    /// <summary>
    /// REGRESSION: Home must STILL select the first row after the sync-off fix - the other end of the
    /// keyboard-navigation guarantee End covers. Together they prove WPF's own navigation survives at
    /// both extremes of the list. (Down/PageDown are not asserted here: under a synthetic RaiseEvent
    /// they do not move WPF's logical focus, so they cannot be measured offscreen - see the note on
    /// PressKey.)
    /// </summary>
    [Fact]
    public void Keyboard_Home_still_selects_the_first_row()
    {
        var result = Wpf.Run(() =>
        {
            EnsureResourcesLoaded();
            var shell = BuildShellWithManySnapshots();
            shell.List.Refresh();

            var window = Build(shell);
            window.Left = -3000;
            window.Top = -3000;
            window.Width = 480;
            window.Height = 360;
            window.ShowInTaskbar = false;

            try
            {
                window.Show();
                window.UpdateLayout();

                var firstId = shell.List.Rows[0].Id;

                window.GroupsHost.Focus();
                Keyboard.Focus(window.GroupsHost);
                window.UpdateLayout();

                PressKey(window.GroupsHost, Key.Home);
                window.UpdateLayout();

                return (SelectedId: shell.List.Selected?.Id, FirstId: firstId);
            }
            finally
            {
                window.Close();
            }
        });

        Assert.NotNull(result.FirstId);
        Assert.True(string.Equals(result.FirstId, result.SelectedId, StringComparison.Ordinal),
            $"Pressing Home must select the first row ('{result.FirstId}'); got " +
            $"'{result.SelectedId}'. Keyboard navigation is intended and must not regress from the " +
            "sync-off fix.");
    }

    /// <summary>
    /// One key press, delivered the way the input system does it: the TUNNELLING preview first,
    /// then - only if nothing answered it - the bubbling event. WPF's ListBox.OnKeyDown has no
    /// "real keyboard" gate, so a raised event drives its native navigation and selection.
    ///
    /// Measured limit: Home and End fire (they navigate to a list extreme and select), but Down and
    /// PageDown do NOT under a synthetic RaiseEvent - they need WPF's logical focus to actually move,
    /// which only real keyboard input does. So the keyboard-nav regression here is scoped to Home/End,
    /// the two extremes that are measurable offscreen.
    /// </summary>
    private static void PressKey(UIElement from, Key key)
    {
        var device = Keyboard.PrimaryDevice;
        // The window is shown, so it has a live PresentationSource - the constructor null-checks it.
        var source = PresentationSource.FromVisual(from) ?? new HwndSource(0, 0, 0, 0, 0, "", IntPtr.Zero);

        var preview = new KeyEventArgs(device, source, Environment.TickCount, key)
        {
            RoutedEvent = UIElement.PreviewKeyDownEvent,
        };
        from.RaiseEvent(preview);

        if (preview.Handled) return;

        from.RaiseEvent(new KeyEventArgs(device, source, Environment.TickCount, key)
        {
            RoutedEvent = Keyboard.KeyDownEvent,
        });
    }

    /// <summary>
    /// One wheel notch, delivered the way the input system does it: the TUNNELLING preview first,
    /// then - only if nothing answered it - the bubbling event. Mirrors WheelForwardingTests.Wheel.
    /// </summary>
    private static void WheelNotch(UIElement from)
    {
        var preview = new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, -120)
        {
            RoutedEvent = UIElement.PreviewMouseWheelEvent,
        };
        from.RaiseEvent(preview);

        if (preview.Handled) return;

        from.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, -120)
        {
            RoutedEvent = UIElement.MouseWheelEvent,
        });
    }
}
