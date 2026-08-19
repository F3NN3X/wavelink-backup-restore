using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using WaveLinkBackup.App.Hosting;
using WaveLinkBackup.App.Theming;
using WaveLinkBackup.App.ViewModels;
using WaveLinkBackup.App.Views;
using WaveLinkBackup.Core.Analysis;
using WaveLinkBackup.Core.Snapshots;
using WaveLinkBackup.Core.Tests.Fakes;

namespace WaveLinkBackup.App.Tests;

/// <summary>
/// Task 10b's fix replaced the hand-placed ListBoxItem-per-row with a real ListBox per date group,
/// and asked for one thing to be VERIFIED rather than assumed: does selecting a row in one group
/// clear the selection in every OTHER group?
///
/// The answer was no, and the tests below now say so in both directions. The mechanism it was built
/// on - every group's SelectedItem two-way bound to one shared List.Selected - does not work at
/// all: a Selector handed an item its own Items collection does not contain declines the write and
/// keeps its existing container, and two of them wired that way write each other's rows back and
/// forth through the shared property until WPF's loop detection intervenes. Selection is explicit
/// now (GroupSelection), and these drive that helper directly.
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
    /// One ListBox per group, wired EXACTLY the way MainWindow wires each one: the real
    /// WlRowTemplate as ItemContainerStyle, and GroupSelection.Apply on SelectionChanged. No
    /// SelectedItem binding, because MainWindow has none either.
    /// </summary>
    private static (Window Window, ListBox First, ListBox Second) BuildTwoGroupWindow(ShellViewModel shell)
    {
        var rowTemplate = (Style)Application.Current.Resources["WlRowTemplate"];
        var groups = new List<ListBox>();

        ListBox MakeListBox(IReadOnlyList<SnapshotRowViewModel> rows)
        {
            var box = new ListBox { ItemsSource = rows, ItemContainerStyle = rowTemplate };

            // The same call MainWindow.GroupSelectionChanged makes, against the same helper - so
            // this exercises the real mechanism rather than a re-implementation of it. There is no
            // SelectedItem binding here for the same reason there is none in MainWindow.xaml: see
            // GroupSelection's own summary.
            box.SelectionChanged += (sender, e) =>
                GroupSelection.Apply(shell.List, groups, (ListBox)sender, e.AddedItems);

            groups.Add(box);
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

    /// <summary>
    /// The reported bug, in the order a user actually produces it: select a row in one group, then
    /// click a row in another, and both stay highlighted - three groups, three highlighted rows.
    ///
    /// <see cref="Selecting_a_row_in_one_group_clears_the_others_container"/> below was supposed to
    /// cover this and does not: it asserts from a state where NOTHING is selected yet, so the first
    /// group's "cleared" assertion was already true before the act. The clearing path it claims to
    /// prove was never exercised.
    ///
    /// This drives the containers the way a click does - ListBoxItem.IsSelected, which is what a
    /// mouse actually sets - rather than assigning the shared view-model property, because the
    /// failure is specifically in the container -> view model -> other container direction.
    /// </summary>
    [Fact]
    public void Selecting_in_a_second_group_deselects_the_row_already_selected_in_the_first()
    {
        var (firstStillSelected, selectedRows) = Wpf.Run(() =>
        {
            EnsureRowResourcesLoaded();
            var shell = BuildShellWithTwoGroups();
            shell.List.Refresh();

            var (window, first, second) = BuildTwoGroupWindow(shell);
            window.Show();
            window.UpdateLayout();

            var firstRow = shell.List.Groups[0].Rows[0];
            var secondRow = shell.List.Groups[1].Rows[0];

            ListBoxItem Container(ListBox box, SnapshotRowViewModel row) =>
                (ListBoxItem)box.ItemContainerGenerator.ContainerFromItem(row);

            // Click group A's row, then group B's - as a mouse would, on the container itself.
            Container(first, firstRow).IsSelected = true;
            window.UpdateLayout();

            Container(second, secondRow).IsSelected = true;
            window.UpdateLayout();

            var result = (
                FirstStillSelected: Container(first, firstRow).IsSelected,
                SelectedRows: shell.List.Groups.SelectMany(g => g.Rows).Count(r => r.IsSelected));

            window.Close();
            return result;
        });

        Assert.False(firstStillSelected,
            "The first group's row is still highlighted after selecting a row in the second. " +
            "Selection is single-select across the whole list, not per date group.");
        Assert.Equal(1, selectedRows);
    }

    /// <summary>
    /// The other direction: selecting through the shared view-model property - which is what
    /// "Back up now selects the row it just wrote" and Home/End both do - must leave exactly one
    /// row carrying IsSelected, across every group.
    ///
    /// This used to assert that the second group's ListBox showed the row as its SelectedItem. It
    /// deliberately no longer does: nothing writes into a group's Selector any more, because the
    /// binding that did could not be made to work (see the class summary). What the app renders is
    /// the ROW's IsSelected, so that is what is asserted.
    /// </summary>
    [Fact]
    public void Selecting_through_the_view_model_marks_exactly_one_row_across_the_groups()
    {
        var (selectedIds, firstRowSelected) = Wpf.Run(() =>
        {
            EnsureRowResourcesLoaded();
            var shell = BuildShellWithTwoGroups();
            shell.List.Refresh();

            Assert.Equal(2, shell.List.Groups.Count);

            var (window, _, _) = BuildTwoGroupWindow(shell);
            window.Show();

            var firstRow = shell.List.Groups[0].Rows[0];
            var secondRow = shell.List.Groups[1].Rows[0];

            shell.List.Selected = firstRow;
            shell.List.Selected = secondRow;
            window.UpdateLayout();

            var result = (
                SelectedIds: shell.List.Groups
                    .SelectMany(g => g.Rows).Where(r => r.IsSelected).Select(r => r.Id).ToArray(),
                FirstRowSelected: firstRow.IsSelected);

            window.Close();
            return result;
        });

        Assert.Single(selectedIds);
        Assert.False(firstRowSelected, "The first group's row stayed selected after the second was.");
    }

}
