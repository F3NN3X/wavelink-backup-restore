using System.Windows.Controls;
using WaveLinkBackup.App.ViewModels;

namespace WaveLinkBackup.App.Views;

/// <summary>
/// Single-select across a list that is several Selectors.
///
/// The backup list is one <see cref="ListBox"/> per date group - Task 10b made it so, because a
/// real per-group Selector is what gives native mouse and keyboard row selection at all. The cost
/// is that WPF has no notion of a selection spanning them: each ListBox tracks its own, and three
/// date groups can hold three selected rows.
///
/// Binding every group's <c>SelectedItem</c> to one shared property does NOT solve it, and the
/// failure is worth stating because it looks like it should. A Selector handed an item its own
/// Items collection does not contain declines the write and keeps the container it already had.
/// Two-way, it is worse than useless: group A declines B's row and writes its OWN row back to the
/// shared property, B declines that and writes back, and the two ping-pong through the one
/// property until WPF's loop detection stops them - leaving both rows selected and the app
/// wedged for as long as it lasts.
///
/// So the rule is written out rather than bound: when a group gains a selection, that row becomes
/// the list's selection and every OTHER group is cleared. Clearing produces removals only, which
/// <see cref="Apply"/> ignores, so the cascade terminates on the first pass.
/// </summary>
public static class GroupSelection
{
    /// <summary>
    /// Handles one group's SelectionChanged. <paramref name="groups"/> is every group's ListBox,
    /// including the one that raised the event.
    /// </summary>
    /// <param name="added">
    /// The event's AddedItems. **Empty means a deselection, and this returns without touching
    /// anything** - which is exactly what makes clearing the other groups below safe: those clears
    /// raise SelectionChanged too, with removals only, and land here as no-ops. Without that
    /// guard, tidying group A would null out the selection the user just made in group B.
    /// </param>
    public static void Apply(
        SnapshotListViewModel list,
        IEnumerable<ListBox> groups,
        ListBox source,
        System.Collections.IList added)
    {
        ArgumentNullException.ThrowIfNull(list);
        ArgumentNullException.ThrowIfNull(groups);

        if (added is null || added.Count == 0) return;
        if (added[0] is not SnapshotRowViewModel row) return;

        // The view model first: it is what the bottom bar, the commands and every dialog read, and
        // its setter is what moves IsSelected off the outgoing row and onto this one.
        list.Selected = row;

        foreach (var group in groups)
        {
            if (ReferenceEquals(group, source)) continue;
            if (group.SelectedItem is null) continue;

            group.UnselectAll();
        }
    }
}
