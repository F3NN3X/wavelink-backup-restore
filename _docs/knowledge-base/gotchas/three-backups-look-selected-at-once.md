---
title: "Three backups look selected at once"
status: published
created: 2026-08-19
updated: 2026-08-19
tags: [gotcha, wpf, selection]
---

# Three backups look selected at once

**Provenance:** **Experienced.** Reported by the user against the shipped 0.5.0 backup list:
"select a backup from today, click one from yesterday, and so on. I can have three selected at
the same time." Reproduced first try. Now pinned by
`MainWindowSelectionTests.Selecting_in_a_second_group_deselects_the_row_already_selected_in_the_first`.

## Symptom

Click a row under TODAY, then a row under TUE 11 AUG, then one under TUE 4 AUG. All three stay
highlighted, full selected fill, accent left edge, expansion open, while the bottom bar
describes only the last one clicked.

The list is documented and built as single-select. Nothing errors.

## Cause

**The list is not one Selector. It is one `ListBox` per date group.** A deliberate choice, and a
good one: a real per-group `Selector` is what gives native mouse and keyboard row selection at all,
instead of a hand-placed `ListBoxItem` per row. The cost is that WPF has no notion of a selection
spanning several Selectors. Each `ListBox` tracks its own, and three groups can hold three.

The build's answer was to bind every group's `SelectedItem` two-way to one shared
`List.Selected`, on the assumption that writing group B's row into group A's `SelectedItem` would
clear group A.

**It does not.** A `Selector` handed an item its own `Items` collection does not contain declines
the write and keeps the container it already had selected.

And two-way, it is worse than merely ineffective: group A declines B's row and writes its **own**
current row back to the shared property; B then declines that and writes back; the two ping-pong
through the one property until WPF's binding loop detection stops them. Reproducing this in a test
did not fail, it **hung**.

## Why the existing test passed

`MainWindowSelectionTests` had a test for exactly this, named for exactly this, and it was green.
It asserted from a state where **nothing was selected yet**:

```csharp
shell.List.Selected = secondRow;              // nothing was selected before this
Assert.True(first.SelectedItem is null);      // ...so this was already true
```

The clearing path it claimed to prove was never exercised. A test that sets up the state its
assertion needs, rather than the state a user produces, can pin nothing at all.

The replacement drives the containers the way a mouse does, `ListBoxItem.IsSelected`, with a
prior selection in another group, which is the whole bug.

## What does not fix it

| Attempt | Why it fails |
|---|---|
| `Mode=OneWayToSource` on the shared binding | Stops the ping-pong, and the click still reaches the view model. But nothing clears the other groups' Selectors, so the stale highlight remains. |
| Bind each container's `IsSelected` two-way to the row's own `IsSelected` from the `ItemContainerStyle` | Looks like the clean MVVM answer. It fights `Selector`'s own container management, the Selector sets `IsSelected` locally, which outranks the style-level binding, and hangs on a real layout pass. |
| One flat `ListBox` with `GroupStyle` | Genuinely correct, and a much larger change: the view model pre-groups into `DateGroup`s, so it would mean re-deriving the grouping through a `CollectionView` and rebuilding the row template's host. Worth doing when arrow-key movement across groups is wanted too, it would fix that known limitation in the same stroke. Not worth it as a bug fix. |

## The fix

Write the rule out instead of binding it. Two halves:

**1. An explicit handler.** `GroupSelection.Apply`, attached once to `GroupsHost` (the
`ItemsControl` above every group's `ListBox`) rather than to each `ListBox`, so it survives the
virtualizing panel creating and recycling them:

```csharp
if (added.Count == 0) return;          // a deselection: ignore
list.Selected = row;                   // the view model is the authority
foreach (var group in groups)          // clear every OTHER group
    if (!ReferenceEquals(group, source)) group.UnselectAll();
```

**That first line is what makes the rest safe.** `UnselectAll()` raises `SelectionChanged` too,
with removals only, so the cascade lands back here and returns immediately. Without the guard,
tidying group A would null out the selection the user just made in group B.

**2. The row's own `IsSelected` decides what looks selected.**
`SnapshotListViewModel.Selected`'s setter already cleared the outgoing row's flag and set the
incoming one's, so exactly one row carries it across the whole list. The row template's triggers
now key off that rather than off the container's `IsSelected`, which means a stale container in
another group has nothing left to paint with.

There is no `SelectedItem` binding on the group ListBoxes any more.
`MainWindowTemplateTests.No_group_binds_its_SelectedItem_to_the_shared_selection` pins its
absence, because re-adding one reintroduces both faults.

## See also

- `src/WaveLinkBackup.App/Views/GroupSelection.cs`
- `src/WaveLinkBackup.App/Views/RowStyles.xaml`, `WlRowTemplate`
- The 0.5.1 design audit, 2026-08-19
