---
title: "Scrolling the list selects a row"
status: published
created: 2026-08-21
updated: 2026-08-21
tags: [gotcha, wpf, xaml]
---

# Scrolling the list selects a row

**Provenance:** Observed, 2026-08-21, on the main window. Measured before and after through the
real `MainWindow` offscreen: with the attribute at its default, `MoveCurrentToLast()` selected the
last row; with it set to `False`, the same call left the selection untouched, and wheel scrolling
to the bottom never selects anything either way.

## Symptom

The backup list is longer than the window. You scroll down to the end — no click, no key press —
and a row at the bottom of what you can see becomes selected. The selection has followed the
scroll. Clicking nothing, it moved on its own.

## Cause

**A `ListBox` keeps its `SelectedItem` bound to the collection view's *current item*, and that
binding is on by default.**

```xml
<ListBox x:Name="GroupsHost"
         ItemsSource="{Binding List.View}"
         SelectedItem="{Binding List.Selected, Mode=TwoWay}"
         ...>
```

Nothing in this markup mentions `IsSynchronizedWithCurrentItem`, because its default is `True`.
While it is true, the `SelectedItem` binding is not a free TwoWay binding — it is *driven by* the
view's currency. Anything that advances the currency (`MoveCurrentToLast`, `MoveCurrentToNext`, a
refresh that repositions it) writes through the binding and selects the row the cursor landed on.

The list had been refactored from one `ListBox` per date group into a single grouped
`Selector` over a `ListCollectionView` (see the XAML comment above `GroupsHost`). The currency is
a property of that view, and with the sync flag at its default the view's cursor and the user's
selection are the same thing. They were never meant to be.

## The plausible explanation, and why it is wrong

**"The wheel scroll is selecting the row."** It is not. A wheel notch — and a scrollbar drag —
moves neither WPF's keyboard focus nor the view's currency; `WheelForwardingTests` and the first
test in this file's guard both measure that a full wheel scroll to the bottom selects nothing. The
wheel was never the cause, which is why fixing the wheel does not fix the symptom.

The second wrong turn is to suspect the selection *binding* itself and reach for `UpdateSourceTrigger`
or a one-way mode. The binding is fine; it is faithfully reporting what the currency did. Turning
the binding off would make the cursor invisible rather than stop it from driving anything.

## Fix

Decouple the two, on the element that owns both:

```xml
<ListBox x:Name="GroupsHost"
         ItemsSource="{Binding List.View}"
         SelectedItem="{Binding List.Selected, Mode=TwoWay}"
         IsSynchronizedWithCurrentItem="False"
         ...>
```

With the flag off, advancing the currency never touches `SelectedItem`, and a user's click or key
press still writes the selection through the binding exactly as before. The two are independent:
the cursor may sit anywhere without selecting anything.

## How to avoid it

**A grouped list that is one `Selector` over a view has two cursors, and only one of them is the
user's.** When you build or refactor such a list, ask which thing `SelectedItem` is bound *through*
— the view's currency, or directly — and pin the answer in the markup rather than leaving it to the
default.

The guard is in `MainWindowScrollSelectionTests`, four tests against the real window:

- **Wheel scrolling to the bottom does not select a row** — the claim, measured end to end.
- **Moving the view currency to the last row does not select it** — the actual defect; this fails
  if `IsSynchronizedWithCurrentItem` is ever removed or set back to `True`.
- **End still selects the last row** and **Home still selects the first row** — keyboard navigation
  is WPF's own on a single `Selector`, and the sync-off fix must not break it.

One measured limit, so the next person does not re-derive it: under a synthetic `RaiseEvent`, only
Home and End move WPF's logical focus far enough to select. Down and PageDown do not — they need
real keyboard input to move that focus — so the keyboard-nav regression is scoped to the two
extremes, which are the ones a scroll-to-the-end user would actually hit.

## References

- `src/WaveLinkBackup.App/Views/MainWindow.xaml` (the `GroupsHost` attribute) ·
  `tests/WaveLinkBackup.App.Tests/MainWindowScrollSelectionTests.cs`
- [[the-list-will-not-scroll-with-the-wheel]] — the other defect this list's nesting caused, and
  why the wheel is exonerated here
- [[three-backups-look-selected-at-once]] — the earlier selection defect on the per-group
  structure this was refactored away from
