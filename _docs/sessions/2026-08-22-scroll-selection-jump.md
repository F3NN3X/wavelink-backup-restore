---
title: "Session: The scroll-click jump was recycling, not currency"
status: published
created: 2026-08-22
updated: 2026-08-22
tags: [session, wpf, xaml, virtualization]
---

# Session: The scroll-click jump was recycling, not currency

**Date:** 2026-08-22

## Goal

Fix the reported defect: with the backup list longer than the window, scrolling to the end and
then *clicking* a row highlights a **different** row (one at the bottom of the visible view)
instead of the one clicked. The 2026-08-21 session had removed `IsSynchronizedWithCurrentItem`
from `GroupsHost` on this list, and the user reported the jump persisted — so that fix was not
the whole story.

## What happened

The real cause is one attribute away in the same markup: `VirtualizingPanel.VirtualizationMode`
on `GroupsHost` was left at its default **`Recycling`**. In recycling mode a scrolled-out
container is *reused* for a new data item, and WPF does not guarantee the container's content has
refreshed by the time it is under the cursor. So after a scroll, the row visually at the click
point can still be presenting a **stale** `SnapshotRowViewModel` — a different snapshot than the
one its position now represents. A click selects that stale item, which reads as the selection
"jumping."

The fix is `VirtualizationMode="Standard"` on `GroupsHost`. Standard mode creates and discards
containers instead of reusing them, so every realized container always matches the data item it
represents; there is no stale-content window for a click to land in. Virtualization itself stays
on (`IsVirtualizing=True`) — the list still virtualizes, it just stops recycling containers.

The 2026-08-21 fix is **independent and stays**: `IsSynchronizedWithCurrentItem="False"` was a
real latent defect (the view's currency driving `SelectedItem`), even though it was not the cause
of *this* symptom. Both attributes are now set on `GroupsHost`, for two different reasons, and the
gotcha names both so neither is re-derived from scratch.

## Decisions made

| Decision | Recorded in |
|---|---|
| Recycling virtualization is the cause of the click-after-scroll jump; `Standard` mode fixes it | [[scrolling-the-list-selects-a-row]] |
| The currency-sync fix (2026-08-21) is a separate defect and stays, even though it did not fix this symptom | [[scrolling-the-list-selects-a-row]] |
| A fifth regression test asserts that after scrolling, every realized container holds its own data item | `MainWindowScrollSelectionTests.cs` — `After_scrolling_every_realized_container_holds_its_own_data_item` |

## What did not work

- **The 2026-08-21 currency fix alone.** It was correct for the defect it targeted, but it
  exonerated nothing about recycling — so the reported symptom survived it. The lesson: a fix that
  is *right* can still be *incomplete* when there are two independent causes in the same markup.
  Measuring which cause produces the observed symptom (here: the click lands on a container whose
  data item is not the row at that position) is what separates them.

## Open questions

None. The symptom is fixed and pinned; both attributes are documented as independent fixes.

## Next

Commit the XAML change, the fifth test, this note, the corrected gotcha and the stats update
together (sessions 23 → 24, tests 1,554 → 1,555 — App 961 → 962).
