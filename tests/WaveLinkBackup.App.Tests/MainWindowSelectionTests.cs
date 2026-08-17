using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using WaveLinkBackup.App.Hosting;
using WaveLinkBackup.App.Theming;
using WaveLinkBackup.App.ViewModels;
using WaveLinkBackup.Core.Analysis;
using WaveLinkBackup.Core.Snapshots;
using WaveLinkBackup.Core.Tests.Fakes;

namespace WaveLinkBackup.App.Tests;

/// <summary>
/// Task 10b's fix replaced the hand-placed ListBoxItem-per-row with a real ListBox per date
/// group, each one two-way bound to the SAME ShellViewModel.List.Selected
/// (MainWindow.xaml: <c>SelectedItem="{Binding DataContext.List.Selected,
/// RelativeSource={RelativeSource AncestorType=Window}, Mode=TwoWay}"</c>). The fix brief asked
/// for this specifically to be VERIFIED, not assumed: does selecting a row in one group's ListBox
/// actually clear the container-level selection highlight in every OTHER group's ListBox, given
/// that there is no code-behind syncing them by hand any more?
///
/// This drives the REAL, unmodified WlRowTemplate style (RowStyles.xaml) - the exact
/// ItemContainerStyle MainWindow.xaml's row ListBox uses - through a minimal two-ListBox Window
/// built in code rather than through MainWindow itself. That is a deliberate scope narrowing, not
/// a shortcut: forcing MainWindow's own visual tree through a real Show() (needed for a
/// virtualizing panel to generate item containers at all - see MainWindowListStateTests' own
/// comment on why property triggers alone are not enough) surfaces a PRE-EXISTING resource-scope
/// issue in Row 2's column header (WlColumnHeaderRowTemplate, ControlStyles.xaml, referencing
/// WlColumnHeaderTrackedText, defined only in RowStyles.xaml) that has nothing to do with row
/// selection and is out of this fix's scope to touch. A window with two bare ListBoxes bound the
/// same way, styled by the same real WlRowTemplate, needs only Typography.xaml and RowStyles.xaml
/// merged and isolates exactly the WPF mechanism this test exists to pin: Selector.SelectedItem,
/// once written through a shared TwoWay-bound property, deselecting every OTHER Selector's current
/// container on its own - no code-behind syncing them by hand.
/// </summary>
public sealed class MainWindowSelectionTests
{
    private const string StorePath = @"C:\store";

    private static void EnsureRowResourcesLoaded()
    {
        var dictionaries = Application.Current.Resources.MergedDictionaries;
        dictionaries.Clear();
        ThemeManager.Apply(AppTheme.Dark);

        // RowStyles.xaml merges Typography.xaml itself (its own header comment explains why), so
        // that alone is everything WlRowTemplate needs - no ControlStyles.xaml, no MainWindow.xaml.
        dictionaries.Add(new ResourceDictionary
        {
            Source = new Uri(
                "pack://application:,,,/WaveLinkBackup;component/Views/RowStyles.xaml",
                UriKind.Absolute),
        });
    }

    /// <summary>Two snapshots two days apart, so Rebuild() (SnapshotListViewModel) puts them in
    /// two DIFFERENT DateGroups rather than one group with two rows.</summary>
    private static ShellViewModel BuildShellWithTwoGroups()
    {
        var fs = new FakeFileSystem();
        var clock = new FakeClock { UtcNow = new DateTimeOffset(2026, 8, 15, 23, 7, 0, TimeSpan.Zero) };

        fs.CreateDirectory(StorePath);
        var store = new SnapshotStore(fs, clock, StorePath);

        var bytes = Encoding.UTF8.GetBytes(
            """{"MixerConfiguration":{"InputSettings":{"a":{"InputName":"Mic"}}}}""");

        store.Write(bytes, SettingsAnalysis.Analyse(bytes).Value, SnapshotTrigger.Manual, "Group A backup");

        clock.UtcNow = clock.UtcNow.AddDays(-2);
        store.Write(bytes, SettingsAnalysis.Analyse(bytes).Value, SnapshotTrigger.Manual, "Group B backup");

        var list = new SnapshotListViewModel(store, new HealthProbe(store, fs, clock), fs, clock)
        {
            Marshal = action => action(),
        };

        return new ShellViewModel(list);
    }

    /// <summary>
    /// One ListBox per group, wired EXACTLY the way MainWindow.xaml wires each one: the real
    /// WlRowTemplate as ItemContainerStyle, SelectedItem two-way bound to the shared
    /// List.Selected via a RelativeSource walk up to the Window's own DataContext - the identical
    /// binding path MainWindow.xaml's row ListBox uses (<c>DataContext.List.Selected,
    /// RelativeSource={RelativeSource AncestorType=Window}, Mode=TwoWay</c>).
    /// </summary>
    private static (Window Window, ListBox First, ListBox Second) BuildTwoGroupWindow(ShellViewModel shell)
    {
        var rowTemplate = (Style)Application.Current.Resources["WlRowTemplate"];

        ListBox MakeListBox(IReadOnlyList<SnapshotRowViewModel> rows)
        {
            var box = new ListBox { ItemsSource = rows, ItemContainerStyle = rowTemplate };
            BindingOperations.SetBinding(box, Selector.SelectedItemProperty, new Binding("DataContext.List.Selected")
            {
                RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(Window), 1),
                Mode = BindingMode.TwoWay,
            });
            return box;
        }

        var first = MakeListBox(shell.List.Groups[0].Rows);
        var second = MakeListBox(shell.List.Groups[1].Rows);

        var panel = new StackPanel();
        panel.Children.Add(first);
        panel.Children.Add(second);

        var window = new Window { DataContext = shell, Content = panel, Width = 400, Height = 400 };

        return (window, first, second);
    }

    [Fact]
    public void Selecting_a_row_in_one_group_clears_the_others_container()
    {
        var (firstGroupCleared, secondGroupSelected) = Wpf.Run(() =>
        {
            EnsureRowResourcesLoaded();
            var shell = BuildShellWithTwoGroups();
            shell.List.Refresh();

            Assert.Equal(2, shell.List.Groups.Count);

            var (window, first, second) = BuildTwoGroupWindow(shell);

            // Show(), not Measure/Arrange: a virtualizing/non-virtualizing panel alike only
            // generates item containers on an actual layout pass tied to a live PresentationSource
            // - confirmed empirically while writing this test, Measure/Arrange alone on an unshown
            // Window left the whole content subtree at zero visual children. This minimal window
            // (no async Loaded handlers, no HealthProbe, no RefreshAsync) carries none of
            // MainWindow's own reasons for avoiding Show() (see MainWindowListStateTests' comment).
            window.Show();

            var firstRow = shell.List.Groups[0].Rows[0];
            var secondRow = shell.List.Groups[1].Rows[0];

            // Select the SECOND group's row through the exact shared property the SelectedItem
            // bindings target - not through either ListBox directly - so this proves the BINDING
            // is what drives both containers, not test code reaching in and doing it by hand.
            shell.List.Selected = secondRow;
            window.UpdateLayout();

            var firstContainer = first.ItemContainerGenerator.ContainerFromItem(firstRow) as ListBoxItem;
            var secondContainer = second.ItemContainerGenerator.ContainerFromItem(secondRow) as ListBoxItem;

            var result = (
                FirstCleared: first.SelectedItem is null && firstContainer is { IsSelected: false },
                SecondSelected: ReferenceEquals(second.SelectedItem, secondRow)
                    && secondContainer is { IsSelected: true });

            window.Close();
            return result;
        });

        Assert.True(firstGroupCleared,
            "Selecting a row in the second group's ListBox must clear the first group's own " +
            "ListBox - both SelectedItem and its row container's IsSelected.");
        Assert.True(secondGroupSelected,
            "The second group's own ListBox must show the newly selected row as selected.");
    }
}
