---
title: "The list will not scroll with the wheel"
status: published
created: 2026-08-21
updated: 2026-08-21
tags: [gotcha, wpf, xaml]
---

# The list will not scroll with the wheel

**Provenance:** Observed, 2026-08-21, on the main window. Measured before and after through the
list's UI Automation `ScrollPattern`: six wheel notches left it at 0%, with 31% of the list below
the fold.

## Symptom

The backup list does not move when you turn the wheel over it. Everything else about scrolling
works, the scroll bar drags, Page Up/Down move, the arrow keys move the selection and bring rows
into view, so the list is plainly scrollable. The wheel does nothing at all.

## Cause

**A `ScrollViewer` marks every wheel event handled, whether or not it scrolled anything.**

```csharp
protected override void OnMouseWheel(MouseWheelEventArgs e)
{
    ...
    e.Handled = true;      // unconditional
}
```

The list is a `ListBox` whose own scrolling is turned off, inside the `ScrollViewer` that really
scrolls:

```xml
<ScrollViewer x:Name="ListScrollViewer" VerticalScrollBarVisibility="Auto">
    <ListBox x:Name="GroupsHost"
             ScrollViewer.VerticalScrollBarVisibility="Disabled">
```

That arrangement is deliberate. It is what gives the column header and the rows one scroll
position, so the header cannot drift away from the columns it heads. But the ListBox still *has* a
ScrollViewer in its template, and a notch over a row bubbles up through it on the way out. It
handles the event, scrolls nothing, and `ListScrollViewer` never hears about it.

## The plausible explanation, and why it is wrong

*"`ScrollViewer.VerticalScrollBarVisibility="Disabled"` should mean it does not take part."* It
means it will not *scroll*. It goes on handling, because `HandlesMouseWheelScrolling` is what
decides that and it is true, and it is internal, so it cannot be turned off from XAML either.

The second wrong turn is to reach for `ScrollToVerticalOffset` with a number of pixels per notch.
That works and then quietly disagrees with the rest of Windows: the distance is
`SystemParameters.WheelScrollLines`, times the line height, times whatever the user set in their
mouse control panel. Re-raising the event lets the ScrollViewer that owns the scrolling work it out.

## Fix

Intercept while the event is still TUNNELLING, `PreviewMouseWheel` runs on the way down, before
the inner ScrollViewer's turn to bubble, and re-raise it on the ScrollViewer that scrolls:

```csharp
private void Rows_PreviewMouseWheel(object sender, MouseWheelEventArgs e) =>
    WheelForwarding.Redirect(ListScrollViewer, sender, e);
```

## How to avoid it

**Any time a scrolling control sits inside another one, ask which of them the wheel reaches.** The
answer is always the inner one, and it always says it handled it.

`WheelForwardingTests` pins both halves, the swallow and the fix, and getting that test to fail
correctly was most of the work:

- **Raising `MouseWheelEvent` on the ListBox proves nothing.** A routed event raised on the ListBox
  travels from the ListBox *upwards*, and the ScrollViewer that swallows the wheel is *inside* its
  template. The first version of the test did this and "passed" by scrolling, the opposite of the
  shipped behaviour. Raise it on the **row's container** instead, which is where a real notch
  starts.
- **`RaiseEvent` raises the one event you hand it.** Real input raises the preview *and* the
  bubbling event; a test that raises only `MouseWheelEvent` never runs a `PreviewMouseWheel`
  handler and reports the fix as broken. The test models both halves.

**And note what the same arrangement costs quietly:** the ListBox is measured with unbounded height
inside the outer ScrollViewer, so `VirtualizingPanel.IsVirtualizing="True"` on it does nothing.
Measured: **500 of 500 rows realised**. See `technical-debt.md` §8.4.

## References

- `src/WaveLinkBackup.App/Views/WheelForwarding.cs` · `tests/…/WheelForwardingTests.cs`
- `_docs/technical-debt.md` §8.4, the virtualization half, and the structural fix for both
- [[three-backups-look-selected-at-once]]: the other time this list's structure caused a defect
  nothing tested for
