---
title: "Dialogs are see-through in high contrast"
status: published
created: 2026-08-25
updated: 2026-08-25
related_adrs: [ADR-013]
tags: [gotcha, wpf, theming, accessibility, high-contrast]
---

# Dialogs are see-through in high contrast

**Provenance:** **Observed**, 2026-08-25, reported from a real high-contrast scheme. Present in
every dialog since the theme dictionaries were written; invisible in light and dark, and invisible
to the entire test suite.

## Symptom

Switch Windows to a real high-contrast scheme. Open any dialog. Delete. Restore, settings, the
details view, and the dialog is a hole. Its border draws, its text draws, its buttons draw. Behind
all of that you can see the main window, and behind that the desktop.

Light and dark are fine. Nothing is logged, nothing throws, and the dialog works perfectly: it
takes focus, it responds, it closes. It is only unreadable.

## Cause

Every dialog in this app is a layered window, `WindowStyle="None"`, `AllowsTransparency="True"`,
`Background="Transparent"`, with two things painted inside it:

```xml
<Border Background="{DynamicResource WlScrim}" />   <!-- edge to edge, dims the app -->
<Border Background="{DynamicResource WlCard}" ... > <!-- the card, centred -->
```

`HighContrast.xaml` sets **both** to `Transparent`, and each is right on its own:

- `WlScrim` is transparent because a dialog in high contrast is separated by a border, not by
  dimming, and dimming is opacity.
- `WlCard` is transparent because of the dictionary's governing rule, *"every fill goes
  transparent"*.

Together they leave the window without a single opaque pixel. `AllowsTransparency="True"` means
that is taken literally, a layered window's transparent regions really are transparent.

The rule is not wrong; its scope is. *"Every fill goes transparent"* is about surfaces drawn **on**
something. A card inside the main window sits on `WlBg`, which resolves to `WindowColor`, so
dropping its fill reveals the window colour and the card stays legible, separated by its border
exactly as intended. A dialog card is not drawn on anything. **It is the window.**

## The plausible explanation, and why it is wrong

**"The frosted backdrop stopped working."** This is where the search starts, because
`AcrylicDialogBackdrop` is the component that makes dialogs look right, it is full of interop, and
it is disabled in high contrast anyway. It is a dead end: the backdrop is advisory, it never throws,
and the dialog's own fill is what carries the surface with or without it. Disabling acrylic on a
light-theme dialog leaves it perfectly opaque, which is the experiment that rules this out in about
a minute.

**"High contrast is meant to look flat."** Also true, and also not this. Flat means no gradients, no
tints, no shadows carrying meaning. It does not mean no surface. A real Windows dialog in high
contrast is `WindowColor` with a `WindowText` border, opaque, and bordered.

## Fix

`WlCard` is opaque in high contrast, and it is the only fill in that dictionary that is:

```xml
<SolidColorBrush x:Key="WlCard" Color="{DynamicResource {x:Static SystemColors.WindowColorKey}}" />
```

`WindowColorKey`, not a literal, the palette in high contrast is Windows', not ours, and a
hardcoded white looks correct on the usual black-on-white scheme and wrong on every other one.
`ColorKey` rather than `Color` for the reason the dictionary already documents: the latter is the
Color *value*, and a value used as a `DynamicResource` key never resolves, so the brush renders
black.

Inside the main window this changes nothing visible, `WlBg` is `WindowColor` too, so an opaque
`WlCard` and a transparent one look identical there. On a layered window it is the difference
between a dialog and a hole.

## How to avoid it

`LayeredWindowSurfaceTests` holds two rules:

1. **`WlCard` is opaque in every theme**, not just high contrast. Whatever a future theme does
   with its fills, a dialog card is load-bearing in all of them.
2. **Every view with `AllowsTransparency="True"` references `WlCard`**, discovered by scanning
   `Views/*.xaml` rather than from a list, so a tenth dialog added next year is covered without
   anyone remembering the file exists.

Both were verified to fail against the bug before it was fixed.

**The scan strips XML comments first**, and that is not incidental: `MainWindow.xaml` mentions
`AllowsTransparency="True"` twice in comments explaining why it stays *false* there, and the first
version of the guard read that prose as markup and flagged the main window. Same lesson as
`SourceGuardTests` and `ToolScriptGuardTests`, **a source scan that does not strip comments is
scanning the documentation too.**

**The wider lesson, and it cuts both ways.** Nothing in the suite could assert this, and nothing
did: the dialog laid out correctly, bound correctly and closed correctly. It was found by a person
looking at a screen in a scheme they had switched to on purpose, which is the argument for
[the by-eye checklist](../../operations/design/screen-1-by-eye-checklist.md).

**But the checklist had already looked, and missed it.** `WlCard` has been transparent in high
contrast since the theme dictionaries were first written (commit `eb726dc`), and item 2 of the
2026-08-22 sitting ticked *"the details dialog ... in a real high-contrast scheme"* with the note
*"reads as the settings dialog's shape, not a new idea; nothing clips."* Both halves of that note
are true of a see-through dialog. The item asked whether the dialog matched a shape and whether
anything clipped, and it got an honest answer to exactly those questions while a dialog with no
background went unrecorded.

So the guard here is not "look harder". It is that **a by-eye item is only as good as the question
it asks**, and questions about *conformance*, does this match the design, does anything clip,
do not catch a surface that is missing. Ask about the surface: *is there something behind the
text?* A checklist item phrased as a comparison assumes the thing being compared exists.

## References

- [[ADR-013]]: the theme preference and the seam this resolves through
- `src/WaveLinkBackup.App/Theming/HighContrast.xaml`, the dictionary, and the one documented
  exception to its own rule
- [the by-eye checklist](../../operations/design/screen-1-by-eye-checklist.md), item 5
