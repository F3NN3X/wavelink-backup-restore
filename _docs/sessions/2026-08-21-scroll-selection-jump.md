---
title: "Session: Scrolling the list selected a row"
status: published
created: 2026-08-21
updated: 2026-08-21
tags: [session, wpf, xaml]
---

# Session: Scrolling the list selected a row

**Date:** 2026-08-21

## Goal

Fix the reported defect: with the backup list longer than the window, scrolling down to the end
auto-selects the last visible row, with no click involved.

## What happened

The fix is one attribute on `GroupsHost` — `IsSynchronizedWithCurrentItem="False"` — and the work
was in proving where the selection actually came from before touching it.

The list had been refactored from one `ListBox` per date group into a single grouped `Selector`
over a `ListCollectionView`, with `SelectedItem` bound TwoWay to `List.Selected`. The binding
looked correct, which is what made this one hard: nothing in the markup mentioned currency
synchronisation, because its default — `True` — is exactly the defect. While it is on, the
`SelectedItem` binding is driven by the view's *current item*, so anything that advances the
currency (`MoveCurrentToLast`, a refresh) writes through the binding and selects the row the
cursor landed on.

The first suspect was the wheel, because the user was scrolling with the wheel when they saw it.
It did not survive measurement: a full wheel scroll to the bottom — raised on a realized row so it
tunnels through `WheelForwarding` exactly as in `WheelForwardingTests` — moves neither WPF's
keyboard focus nor the view's currency, and selects nothing. The wheel was the gesture the user
*used*; the currency was the mechanism that *fired*. Written up as
[[scrolling-the-list-selects-a-row]], which carries the exonerated-wheel reasoning so it is not
re-tried.

Four regression tests drive the real `MainWindow` offscreen with forty snapshots across ten days:
the wheel claim, the currency defect itself (fails if the attribute is removed), and End/Home still
selecting their extremes — keyboard navigation is WPF's own on a single `Selector`, and the fix
must not break it.

## Decisions made

| Decision | Recorded in |
|---|---|
| Currency and selection are independent; the sync flag stays off on `GroupsHost` | [[scrolling-the-list-selects-a-row]] |
| Keyboard-nav regression is scoped to Home/End, the two extremes measurable under a synthetic `RaiseEvent` | `MainWindowScrollSelectionTests.cs` — the note on `PressKey` |

## What did not work

- **Suspecting the wheel.** It is the most plausible cause given how the user hit it, and it is
  wrong. Measured clean before the fix was even applied; the test that pins it passes with or
  without the attribute.
- **Suspecting the `SelectedItem` binding** (`UpdateSourceTrigger`, one-way mode). The binding is
  faithful — it reports what the currency did. Disabling it would hide the cursor, not stop it
  driving anything.
- **Measuring Down/PageDown offscreen.** Under a synthetic `RaiseEvent` they do not move WPF's
  logical focus, so they cannot be asserted here. Not a failure of the fix — a limit of the
  harness, recorded on `PressKey` so it is not rediscovered.

## Open questions

None. The defect is fixed and pinned; the wheel was exonerated with evidence rather than
assumed.

## Next

Commit the attribute, the four tests, this note and the gotcha together, and update
`documentation-stats.md` in the same commit (gotchas 26 → 27, sessions 22 → 23, tests 1,549 →
1,554).
