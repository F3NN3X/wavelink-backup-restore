---
title: "Scrolling the list selects a row"
status: published
created: 2026-08-21
updated: 2026-08-22
tags: [gotcha, wpf, xaml]
---

# Scrolling the list selects a row

**Provenance:** Observed, 2026-08-21 and corrected 2026-08-22, on the main window. The first pass
(2026-08-21) measured a real but *different* mechanism — currency sync — through the real
`MainWindow` offscreen, and shipped `IsSynchronizedWithCurrentItem="False"`. That did not fix the
reported symptom. The reported symptom (below) is container **recycling**, found 2026-08-22 by
re-reading the XAML against the user's exact gesture: scroll to the end, then *click*, and the
highlighted row is one at the bottom of the view instead of the one clicked.

## Symptom

The backup list is longer than the window. You scroll down to the end, then **click a row with the
mouse** — and the row that ends up highlighted is one sitting at the bottom of what you can see,
not the row under your cursor. The selection has "jumped" to wherever the scroll landed.

## Cause

**The list virtualizes in `Recycling` mode, and a recycled container can hold stale data under the
cursor.**

```xml
<ListBox x:Name="GroupsHost"
         ItemsSource="{Binding List.View}"
         SelectedItem="{Binding List.Selected, Mode=TwoWay}"
         VirtualizingPanel.VirtualizationMode="Recycling"   <!-- the cause -->
         ...>
```

`VirtualizingStackPanel` in `Recycling` mode does not create and discard a `ListBoxItem` per row;
it **reuses** a scrolled-out container for a new data item. The reuse can land before the
container's content has refreshed to the new item, so for a moment — and, under a fast scroll-to-
the-end followed immediately by a click, at exactly the moment of the click — the container under
the pointer is still carrying the *previous* row's data. WPF selects whatever data item the
container under the click reports, which is the stale one. The highlight lands on a different row
than the one you clicked.

This is a known WPF hazard with `Recycling` + grouped items, not something this codebase invented:
the container/data pairing that a click relies on is only guaranteed by *standard* virtualization,
where every realized container is created fresh for its item.

## The plausible explanations, and why they are wrong

**"The wheel scroll is selecting the row."** It is not. A wheel notch moves neither WPF's keyboard
focus nor the view's currency; `WheelForwardingTests` and the first guard test both measure that a
full wheel scroll to the bottom selects nothing. The wheel was never the cause.

**"The selection binding is driving it off the currency."** That *is* a real defect in this list —
and it was fixed, on 2026-08-21, with `IsSynchronizedWithCurrentItem="False"`. But it is a
*different* symptom: that one made a scroll/refresh move the selection with no click at all. The
reported jump needs a **click**, and no amount of currency decoupling changes what data item a
recycled container reports under the pointer. This is why the first fix, though correct for its
own mechanism, did not resolve the reported bug.

**"Reach for `UpdateSourceTrigger` or a one-way binding."** The binding is fine in both cases; it
faithfully reports what the control tells it. The problem is upstream of the binding — which data
item the container under the cursor actually holds.

## Fix

Stop recycling, on the element that owns the panel:

```xml
<ListBox x:Name="GroupsHost"
         ItemsSource="{Binding List.View}"
         SelectedItem="{Binding List.Selected, Mode=TwoWay}"
         IsSynchronizedWithCurrentItem="False"
         VirtualizingPanel.VirtualizationMode="Standard"
         ...>
```

`Standard` mode creates and discards containers, so a realized container always matches its data
item — there is no stale container to click into. Virtualization stays on (`IsVirtualizing=True`),
so memory use is unchanged; for a list of dozens of rows the create/discard cost is not
measurable. `IsSynchronizedWithCurrentItem="False"` stays: it closes the separate currency-sync
defect and the two fixes are independent.

## How to avoid it

**When a virtualized list lets you click into the wrong row after scrolling, suspect container
recycling before you suspect the selection binding.** The binding reports faithfully; the question
is what data the container under the cursor holds. `Recycling` reuses containers and can hand you
a stale one; `Standard` cannot. Prefer `Standard` unless a list is large enough that recycling's
performance win is proven necessary — and even then, keep selection state on the *data* item (as
this app already does via `SnapshotRowViewModel.IsSelected`) so a recycled container re-reads the
right state when it is refreshed.

The guard is in `MainWindowScrollSelectionTests`, five tests against the real window:

- **Wheel scrolling to the bottom does not select a row** — the claim, measured end to end.
- **After scrolling, every realized container holds its own data item** — the reported defect;
  fails if `VirtualizationMode` is ever set back to `Recycling`.
- **Moving the view currency to the last row does not select it** — the separate currency-sync
  defect; fails if `IsSynchronizedWithCurrentItem` is removed or set back to `True`.
- **End still selects the last row** and **Home still selects the first row** — keyboard navigation
  is WPF's own on a single `Selector`, and neither fix may break it.

One measured limit, so the next person does not re-derive it: under a synthetic `RaiseEvent`, only
Home and End move WPF's logical focus far enough to select. Down and PageDown do not — they need
real keyboard input to move that focus — so the keyboard-nav regression is scoped to the two
extremes, which are the ones a scroll-to-the-end user would actually hit.

## References

- `src/WaveLinkBackup.App/Views/MainWindow.xaml` (the `GroupsHost` attributes) ·
  `tests/WaveLinkBackup.App.Tests/MainWindowScrollSelectionTests.cs`
- [[the-list-will-not-scroll-with-the-wheel]] — the other defect this list's nesting caused, and
  why the wheel is exonerated here
- [[three-backups-look-selected-at-once]] — the earlier selection defect on the per-group
  structure this was refactored away from
