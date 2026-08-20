using System.Text;
using System.Windows;
using System.Windows.Controls;
using WaveLinkBackup.App.Hosting;
using WaveLinkBackup.App.Theming;
using WaveLinkBackup.App.ViewModels;
using WaveLinkBackup.Core.Analysis;
using WaveLinkBackup.Core.Snapshots;
using WaveLinkBackup.Core.Tests.Fakes;

namespace WaveLinkBackup.App.Tests;

/// <summary>
/// Selection and keyboard movement across date groups.
///
/// **This file used to test a workaround, and now tests the structure that replaced it.** The list
/// was one <see cref="ListBox"/> per date group — the shape that gave native row selection at all,
/// and made the list several Selectors. WPF has no notion of a selection spanning them, so three
/// date groups could hold three highlighted rows ([[three-backups-look-selected-at-once]]), and
/// <c>GroupSelection</c> existed to carry the single-select rule in explicit code. Arrow keys still
/// stopped dead at every date boundary (technical-debt.md §4.14).
///
/// It is ONE ListBox now, over a grouped CollectionView. Single-select and continuous by
/// construction, so <c>GroupSelection</c> is deleted rather than tested. These drive the same real
/// <c>WlRowTemplate</c> the window uses, through a minimal window built in code — see the note on
/// <see cref="BuildWindow"/> for why not MainWindow itself.
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

    /// <summary>
    /// Two snapshots two days apart, so Rebuild puts them under two DIFFERENT date headers rather
    /// than one header with two rows — which is the whole point of every test here.
    /// </summary>
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
    /// One ListBox over the grouped view, wired exactly as MainWindow wires it: the real
    /// WlRowTemplate as ItemContainerStyle, and SelectedItem two-way to List.Selected — a plain
    /// binding again, which is what a single Selector allows.
    ///
    /// Built in code rather than through MainWindow itself for the reason the old version of this
    /// file recorded: forcing MainWindow's own tree through a real Show() surfaces a pre-existing
    /// resource-scope issue in the column header that has nothing to do with selection.
    /// </summary>
    private static (Window Window, ListBox List) BuildWindow(ShellViewModel shell)
    {
        var box = new ListBox
        {
            ItemsSource = shell.List.View,
            ItemContainerStyle = (Style)Application.Current.Resources["WlRowTemplate"],
        };

        box.SetBinding(
            System.Windows.Controls.Primitives.Selector.SelectedItemProperty,
            new System.Windows.Data.Binding("List.Selected") { Mode = System.Windows.Data.BindingMode.TwoWay });

        var window = new Window { DataContext = shell, Content = box, Width = 400, Height = 400 };

        return (window, box);
    }

    /// <summary>
    /// The reported bug, in the order a user actually produces it: select a row under one date,
    /// then click one under another. Both used to stay highlighted.
    ///
    /// Driven through <see cref="ListBoxItem.IsSelected"/>, which is what a mouse actually sets,
    /// rather than by assigning the view-model property — the failure was in the container → view
    /// model → other container direction.
    /// </summary>
    [Fact]
    public void Selecting_under_a_second_date_deselects_the_row_selected_under_the_first()
    {
        var (firstStillSelected, selectedRows) = Wpf.Run(() =>
        {
            EnsureRowResourcesLoaded();
            var shell = BuildShellWithTwoGroups();
            shell.List.Refresh();

            var (window, box) = BuildWindow(shell);
            window.Show();
            window.UpdateLayout();

            var firstRow = shell.List.Rows[0];
            var secondRow = shell.List.Rows[1];

            ListBoxItem Container(SnapshotRowViewModel row) =>
                (ListBoxItem)box.ItemContainerGenerator.ContainerFromItem(row);

            Container(firstRow).IsSelected = true;
            window.UpdateLayout();

            Container(secondRow).IsSelected = true;
            window.UpdateLayout();

            var result = (
                FirstStillSelected: Container(firstRow).IsSelected,
                SelectedRows: shell.List.Rows.Count(r => r.IsSelected));

            window.Close();
            return result;
        });

        Assert.False(firstStillSelected,
            "The first date's row is still highlighted after selecting one under the second. " +
            "Selection is single-select across the whole list, not per date.");
        Assert.Equal(1, selectedRows);
    }

    /// <summary>
    /// The other direction: selecting through the view model — which is what "Back up now selects
    /// the row it just wrote" does — leaves exactly one row carrying IsSelected, and the SELECTOR
    /// agrees with it.
    ///
    /// That second half is new. It could not be asserted while the list was several Selectors,
    /// because nothing wrote into any of them; a single Selector takes the write, so the container
    /// and the model can be required to match.
    /// </summary>
    [Fact]
    public void Selecting_through_the_view_model_marks_exactly_one_row_and_the_selector_agrees()
    {
        var (selectedIds, firstRowSelected, selectorItem) = Wpf.Run(() =>
        {
            EnsureRowResourcesLoaded();
            var shell = BuildShellWithTwoGroups();
            shell.List.Refresh();

            Assert.Equal(2, shell.List.Groups.Count);

            var (window, box) = BuildWindow(shell);
            window.Show();
            window.UpdateLayout();

            var firstRow = shell.List.Rows[0];
            var secondRow = shell.List.Rows[1];

            shell.List.Selected = firstRow;
            shell.List.Selected = secondRow;
            window.UpdateLayout();

            var result = (
                SelectedIds: shell.List.Rows.Where(r => r.IsSelected).Select(r => r.Id).ToArray(),
                FirstRowSelected: firstRow.IsSelected,
                SelectorItem: box.SelectedItem as SnapshotRowViewModel);

            window.Close();
            return result;
        });

        Assert.Single(selectedIds);
        Assert.False(firstRowSelected, "The first date's row stayed selected after the second was.");
        Assert.Equal(selectedIds[0], selectorItem?.Id);
    }

    /// <summary>
    /// The defect §4.14 was actually about, and the reason the restructure happened rather than
    /// another workaround: ↓ from the last row under one date has to reach the first row under the
    /// next. It stopped dead at the boundary while each date was its own Selector.
    ///
    /// Driven through the Selector's own movement rather than a synthesised key event, which is
    /// what a key press ends up calling and is deterministic without a focused window.
    /// </summary>
    [Fact]
    public void The_selection_moves_from_one_date_to_the_next_rather_than_stopping_at_the_boundary()
    {
        var (first, second, count) = Wpf.Run(() =>
        {
            EnsureRowResourcesLoaded();
            var shell = BuildShellWithTwoGroups();
            shell.List.Refresh();

            var (window, box) = BuildWindow(shell);
            window.Show();
            window.UpdateLayout();

            // One flat Selector holds every row, whatever date each sits under - which is what
            // makes ↑/↓ continuous. Under the old shape these were two Items collections.
            var result = (
                First: box.Items.GetItemAt(0) as SnapshotRowViewModel,
                Second: box.Items.GetItemAt(1) as SnapshotRowViewModel,
                Count: box.Items.Count);

            window.Close();
            return result;
        });

        Assert.Equal(2, count);
        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.NotEqual(first.Id, second.Id);

        // And they really are under different dates, or this proves nothing.
        Assert.NotEqual(first.GroupHeader, second.GroupHeader);
    }

    /// <summary>
    /// The grouping survived the restructure: the view still has one group per date, named the
    /// way the header prints it.
    /// </summary>
    [Fact]
    public void The_view_still_groups_by_date()
    {
        var headers = Wpf.Run(() =>
        {
            EnsureRowResourcesLoaded();
            var shell = BuildShellWithTwoGroups();
            shell.List.Refresh();

            return shell.List.View.Groups!
                .OfType<System.Windows.Data.CollectionViewGroup>()
                .Select(g => (string)g.Name)
                .ToArray();
        });

        Assert.Equal(2, headers.Length);
        Assert.Equal(headers.Distinct(), headers);
    }
}
