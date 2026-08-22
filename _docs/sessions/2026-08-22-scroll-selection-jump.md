---
title: "Session: The scroll-click jump was two scroll owners, not recycling"
status: published
created: 2026-08-22
updated: 2026-08-22
tags: [session, wpf, xaml, virtualization]
---

# Session: The scroll-click jump was two scroll owners, not recycling

**Date:** 2026-08-22

## Goal

Fix the reported defect: with the backup list longer than the window, scrolling to the end and
then *clicking* a row highlights a **different** row (one at the bottom of the visible view)
instead of the one clicked. The 2026-08-21 session had shipped `IsSynchronizedWithCurrentItem="False"`
on this list, and the user reported the jump persisted — so that fix was not the whole story.

## What happened

Two independent causes, both on `GroupsHost`, and only the first was visible from the XAML alone.

**Cause 1 — two owners of the scroll.** The list lived inside an outer
`ScrollViewer x:Name="ListScrollViewer"` that did the real scrolling (the wheel was forwarded into
it), while the `ListBox`'s own inner `ScrollViewer` was disabled. A `VirtualizingStackPanel` tracks
only the offset of the `ScrollViewer` that *owns* it — here, the frozen inner one. When the outer
viewer moved the content, the panel never learned: its realized containers stayed top-anchored while
the pixels showed the last rows. A click hit-tested to a stale container still carrying an earlier
row's `DataContext`, and the `TwoWay` `SelectedItem` binding wrote *that* row into `List.Selected`.
That is the jump.

**Cause 2 — a grouped list reports a collapsed extent under content scrolling.** After removing the
outer viewer and making the `ListBox`'s own `ScrollViewer` live with `CanContentScroll="True"`, the
symptom did not clear, because the rows are **grouped by date**. WPF's item-scrolling path treats each
group as one scroll unit, so the inner `ScrollViewer` could not see through the group container to the
real content height — it reported `ExtentHeight=1`, `ScrollableHeight=0`. Measured offscreen: all 40
rows realized spanning ~2667px in a ~378px viewport, yet the scroll owner saw an extent of 1. This is a
known WPF breakage for grouped lists with content scrolling (dotnet/wpf issue 8687; the
MaterialDesignInXAML grouped-ListView report).

**The fix** is two changes: (1) remove the outer `ListScrollViewer` and its wheel-forwarding shim so
the `ListBox`'s own `ScrollViewer` is the single scroll owner, and (2) set
`ScrollViewer.CanContentScroll="False"` (pixel scrolling), which measures the real pixel height and is
immune to the grouping extent collapse. The `VirtualizingStackPanel` still virtualizes — it gets its
viewport through `IViewportProvider` even in pixel mode.

## Decisions made

| Decision | Recorded in |
|---|---|
| Two scroll owners (outer viewer + disabled inner) is the cause of the click-after-scroll jump; one owner fixes it | [[scrolling-the-list-selects-a-row]] |
| A grouped list needs `CanContentScroll="False"` — content scrolling collapses its extent to ~1px | [[scrolling-the-list-selects-a-row]] |
| `VirtualizationMode="Standard"` is the correct mode for this list but was **not** the cause of the jump | [[scrolling-the-list-selects-a-row]] |
| The currency-sync fix (2026-08-21) is a separate defect and stays, even though it did not fix this symptom | [[scrolling-the-list-selects-a-row]] |

## What did not work

- **The 2026-08-21 currency fix alone.** Correct for its own defect; exonerated nothing about the
  scroll ownership. The reported symptom survived it.
- **`VirtualizationMode="Standard"` (the recycling hypothesis).** Shipped and the user confirmed the
  jump was *still present*. Recycling reuses containers, but a reused container still reports its
  current item's data — it does not explain a click selecting a row at the bottom of the view. The
  real defect was which `ScrollViewer` the panel believed owned the scroll. This is the session's main
  lesson: **a plausible mechanism that matches the symptom can still be wrong; measure the layout
  tree of the live window before committing to a cause.** The offscreen diagnostic (dumping the visual
  tree, measuring each realized container's bounds and its `DataContext`, and reading the inner
  `ScrollViewer`'s extent) is what separated "recycling" from "two scroll owners" — the former looked
  right in the XAML, the latter was only visible in the measured layout.

## Open questions

None. The symptom is fixed and pinned; both causes are documented as independent fixes.

## Next

Commit the XAML change (single scroll owner + pixel scrolling), the deleted wheel-forwarding shim,
the updated tests, this note, the corrected gotcha and the stats update together.
