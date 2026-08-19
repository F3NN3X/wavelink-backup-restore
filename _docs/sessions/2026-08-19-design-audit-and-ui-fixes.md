---
title: "Session: the design audit, and what it turned up"
status: published
created: 2026-08-19
updated: 2026-08-19
tags: [session, design, wpf, audit]
---

# Session: the design audit, and what it turned up

**Date:** 2026-08-19

## Goal

An independent audit of the shipped shell against
[the design handoff](../operations/design/README.md), on a report that the app "does not look like
the reference" — then fix what it found. It grew three more times as the user reported specific
faults: dialogs opening on a black background, binding expressions rendering as text, and three
backups looking selected at once.

## What happened

**Four defects that made features unusable**, none of which any test could see:

1. **The restore dialog threw on construction.** It applied a `TargetType="TextBlock"` style to a
   `TrackedText`. The app's one irreversible action had been unreachable since it shipped.
2. **The settings dialog printed `{Binding WhatGoesIn.NoteOneLead}`** and two more expressions as
   literal text — a markup extension in property-element syntax.
3. **Every dialog opened as a black rectangle** — `Background="Transparent"` with
   `AllowsTransparency` false, which is not transparency at all.
4. **Selection was per date group**, so clicking through three groups left three highlighted rows.

All four are written up as gotchas ([[a-dialog-opens-as-a-black-rectangle]],
[[the-window-never-opens-and-nothing-says-why]], [[a-binding-expression-appears-on-screen]],
[[three-backups-look-selected-at-once]]) rather than re-argued here.

**Two systematic drifts**, each with a single cause:

- **The dark theme carried the light theme's numbers** for `WlLine`, `WlLine2`, `WlHover` and
  `WlScrim`, plus wrong alphas on `WlOkSoft`/`WlWarnSoft`. The scrim was the visible one — 22%
  where the design specifies 55%.
- **Three paddings were transcribed from CSS shorthand into WPF's `Left,Top,Right,Bottom`
  order.** The column header's put 11px on the *left* instead of the top. That was the
  header-vs-row misalignment
  [the by-eye checklist](../operations/design/screen-1-by-eye-checklist.md) (Part D) had been
  asking design to sign off as a judgement call. It was a transcription bug; the rows were right.

**Then the two deferred items**, both now closed in [technical-debt.md](../technical-debt.md):

- **§4.12 Motion.** README's 140ms/220ms and `cubic-bezier(.2,0,0,1)` are built.
  `CubicBezierEase` implements the curve exactly rather than substituting the nearest named WPF
  easing, with its solver unit-tested against points computed by hand and against the identity
  bezier.
- **§4.13** The missing-plug-in warning reaches the view as a lead clause and a consequence.

Plus a styled scrollbar (Windows' 17px grey trough was the loudest thing in the settings dialog)
and letter-spacing restored to the micro-caps that had lost it.

## Decisions made

| Decision | Recorded in |
|---|---|
| A hover is a fading LAYER, not a `Background` swap — a frozen theme brush cannot be animated | `Motion.xaml` · [technical-debt.md](../technical-debt.md) §4.12 |
| The row's selection FILL stays instant | [technical-debt.md](../technical-debt.md) §4.12 |
| Selection is explicit code, not a binding | [[three-backups-look-selected-at-once]] · `GroupSelection` |
| Dialogs are owner-sized layered overlays | [[a-dialog-opens-as-a-black-rectangle]] · `DialogOverlay` |
| The scrollbar style is implicit, not keyed | `ControlStyles.xaml` — a keyed one gets applied one call site at a time, and the forgotten one is the one the user sees |

## What did not work

**Three wrong diagnoses, each of which cost real time. All three are in the gotchas; the pattern
across them is the point:**

- **"The settings dialog hangs" → assumed a layout loop.** Wrote a plausible, detailed comment
  explaining scrollbar-width feedback re-wrapping text. Wrong. It was a `Run.Text` TwoWay binding
  throwing on a read-only property, on the WPF thread, during construction — which presents
  identically to a hung measure pass. The comment was corrected in place rather than left to
  mislead the next reader.
- **Binding each container's `IsSelected` two-way from the `ItemContainerStyle`.** The clean MVVM
  answer, and it fights `Selector`'s own container management and hangs.
- **`git checkout --` on a file to revert one change** silently discarded three unrelated fixes
  made earlier in the same session, in the same uncommitted file. Caught by re-grepping for them;
  it would not have been caught by the suite, because those three fixes had no tests.

**One deliberate non-fix.** Animating the row's selection fill means moving `RowSurface`'s
background onto a layer, and that background is painted by a trigger graph whose order is
load-bearing and pinned by `RowTemplateTests`. Hover and the expansion animate without touching
it; the fill cannot. Left instant, and said so, rather than destabilising a tested invariant to
claim a checkbox.

## Open questions

- **The acrylic blur is unverifiable here.** `SetWindowCompositionAttribute` is undocumented and
  needs eyes on a real desktop. It is structured to fail safely — the scrim guarantees a dimmed
  owner regardless — but whether the frost renders is unconfirmed.
- **The theme swap still does not fade** (README gives it 220ms). A resource swap has no
  intermediate state to interpolate; it needs a snapshot layer cross-faded over the window. Noted
  in §4.12 rather than carried as its own item.
- **Arrow keys still do not cross date groups.** One flat `ListBox` with `GroupStyle` would fix
  that and the selection bug in one stroke; it was too large a change to make as a bug fix.

## A fifth defect, found by writing the session note

Drafting the "Next" section, this file claimed the settings dialog's proportion-bar segments
carried no width binding and the bar therefore rendered empty — noted as a thing to look at later.
Checking the claim before leaving it in a document turned it into a test, and the test failed:
three segments, all zero-width. **The bar had never rendered**, for the whole of phase 5.

The fractions were right the entire time — `WhatGoesInModel` computes them and
`SettingsViewModelTests` pins the arithmetic. Nothing bound a **width** to them.

Fixed with a `FractionToWidthConverter` MultiBinding. Two details worth keeping:

- **`RelativeSource`, not `ElementName`.** An `ItemTemplate` is its own namescope, so a name
  declared on the track outside it does not resolve from within.
- **A present tier gets a 2px floor.** The effects list is 4 KB against a 10 MB backup — a quarter
  of a pixel, which rounds to nothing, and a segment that disappears reads as a tier that is not
  included. Zero stays categorically different from small.

## Next

Phase 6 §2 — the tier 2 manifest.
