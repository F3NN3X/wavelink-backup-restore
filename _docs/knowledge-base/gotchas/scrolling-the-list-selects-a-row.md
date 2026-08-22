---
title: "Scrolling the list selects a row"
status: published
created: 2026-08-21
updated: 2026-08-22
tags: [gotcha, wpf, xaml]
---

# Scrolling the list selects a row

**Provenance:** Observed on the main window, corrected twice. The first pass (2026-08-21) measured
a real but *different* mechanism — currency sync — and shipped `IsSynchronizedWithCurrentItem="False"`.
The second pass (2026-08-22) blamed container **recycling** and shipped `VirtualizationMode="Standard"`;
the user confirmed the symptom was still present, so that was wrong too. The real cause was found the
same day by measuring the layout tree of the live window offscreen: a **two-scroll-owner mismatch**,
with a second, independent breakage hiding behind it — a grouped list whose inner `ScrollViewer`
reports an extent of ~1px under content scrolling. Both are fixed; this page names them so neither is
re-derived from scratch.

## Symptom

The backup list is longer than the window. You scroll down to the end, then **click a row with the
mouse** — and the row that ends up highlighted is one sitting at the bottom of what you can see,
not the row under your cursor. The selection has "jumped" to wherever the scroll landed.

## Cause

There were two, and only the first was visible from the XAML alone.

### 1. Two owners of the scroll — a `VirtualizingStackPanel` that never learned the content moved

The list lived inside an **outer** `ScrollViewer x:Name="ListScrollViewer"` that did the real
scrolling (the wheel was forwarded into it), while the `ListBox`'s **own** inner `ScrollViewer`
was disabled. A `VirtualizingStackPanel` tracks only the scroll offset of the `ScrollViewer` that
*owns* it — here, the frozen inner one. When the outer viewer moved the content, the panel never
found out: its realized containers stayed anchored to the top of the list while the pixels showed
the last rows. A click then hit-tested to a stale container still carrying an earlier row's
`DataContext`, and the `TwoWay` `SelectedItem` binding faithfully wrote *that* row into
`List.Selected`. The highlight landed on the bottom-most visible row, not the one clicked.

This is why "just disable virtualization" or "fix the recycling mode" could never reach it: the
panel's container-to-item mapping was wrong because of *which* `ScrollViewer` it thought it was
scrolling under, not because of how it recycled.

### 2. A grouped list reports a collapsed extent under content scrolling

Once the outer viewer was removed and the `ListBox`'s own `ScrollViewer` made live with
`CanContentScroll="True"` (item scrolling), the symptom did not clear — because the rows are
**grouped by date**, and WPF's item-scrolling path treats each group as a single scroll unit. The
inner `ScrollViewer` could not see through the group container to the real content height: it
reported `ExtentHeight=1`, `ViewportHeight=1`, `ScrollableHeight=0`, so there was *nothing to
scroll* and the list stayed pinned at the top. This is a known WPF breakage for grouped lists with
content scrolling (dotnet/wpf issue 8687; the MaterialDesignInXAML grouped-ListView report).

Measured, not assumed: with all 40 rows realized spanning ~2667px of content in a ~378px viewport,
the inner `ScrollViewer` still reported an extent of 1. The content was genuinely there; the
scroll owner simply could not measure it.

## The plausible explanations, and why they are wrong

**"The list virtualizes in Recycling mode and a recycled container holds stale data."** Measured
wrong. Setting `VirtualizationMode="Standard"` shipped on 2026-08-22 and the user confirmed the
jump was *still present*. Recycling reuses containers, but a reused container still reports its
current item's data — it does not explain a click selecting a row at the bottom of the view. The
real defect is upstream: which `ScrollViewer` the panel believes owns the scroll.

**"The wheel scroll is selecting the row."** It is not. A wheel notch moves neither WPF's keyboard
focus nor the view's currency; the guard tests measure that a full wheel scroll to the bottom
selects nothing. The wheel was never the cause.

**"The selection binding is driving it off the currency."** That *was* a real, separate defect in
this list — fixed on 2026-08-21 with `IsSynchronizedWithCurrentItem="False"` — but it produced a
*different* symptom (a scroll/refresh moving the selection with no click at all). The reported jump
needs a **click**, and no amount of currency decoupling changes which data item a stale container
reports under the pointer.

**"Reach for `UpdateSourceTrigger` or a one-way binding."** The binding is fine in every case; it
faithfully reports what the control tells it. Both real causes are upstream of the binding — which
container the click hits, and whether the scroll owner can measure the content at all.

## Fix

Two changes, each closing one cause:

```xml
<!-- 1. ONE scroll owner: the ListBox's own ScrollViewer is live; there is no outer viewer. -->
<ListBox x:Name="GroupsHost"
         ItemsSource="{Binding List.View}"
         SelectedItem="{Binding List.Selected, Mode=TwoWay}"
         IsSynchronizedWithCurrentItem="False"
         VirtualizingPanel.VirtualizationMode="Standard"
         VirtualizingPanel.ScrollUnit="Pixel"
         ScrollViewer.CanContentScroll="False"
         ScrollViewer.VerticalScrollBarVisibility="Auto"
         ...>
```

- **Remove the outer `ListScrollViewer` and its wheel-forwarding shim.** The `ListBox`'s own
  `ScrollViewer` now owns the scroll, so the `VirtualizingStackPanel` tracks the same offset that
  moves the pixels. A realized container always matches its data item, and a click selects the row
  under the cursor. (`Standard` mode stays — it is the correct virtualization mode for this list and
  costs nothing at this size.)
- **`CanContentScroll="False"` (pixel scrolling), not `True`.** With the rows grouped by date,
  item-scrolling collapses the inner extent to ~1px and nothing scrolls. Pixel scrolling measures
  the content's actual pixel height directly, so it scrolls correctly with or without grouping —
  and the `VirtualizingStackPanel` still virtualizes, because it receives its viewport through
  `IViewportProvider` even in pixel mode.

## How to avoid it

**When a virtualized list selects the wrong row after scrolling, suspect the scroll *ownership*
before you suspect recycling or the binding.** A `VirtualizingStackPanel` only stays in sync with
the `ScrollViewer` that owns it. If an outer viewer does the real scrolling while the panel's own
viewer is disabled (or vice versa), the panel's container-to-item mapping drifts from what is
painted, and a click lands on a stale container. One owner: let the `ListBox`'s own `ScrollViewer`
scroll, or wrap it — never both at once.

**When that list is also grouped, set `CanContentScroll="False"`.** Grouping breaks WPF's
item-scrolling extent calculation (the group becomes one scroll unit and the inner viewer sees an
extent of ~1px). Pixel scrolling measures real pixels and is immune; virtualization is unaffected.

The guard is in `MainWindowScrollSelectionTests`, five tests against the real window:

- **Wheel scrolling to the bottom does not select a row** — the claim, measured end to end.
- **After scrolling, every realized container holds its own data item** — the reported defect;
  fails if the two-owner mismatch is reintroduced (the panel's containers no longer match their
  painted rows).
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
