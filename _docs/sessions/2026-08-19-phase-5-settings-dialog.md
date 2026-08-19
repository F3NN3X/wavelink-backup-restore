---
title: "Session: Phase 5 plan 8 — the settings dialog"
status: published
created: 2026-08-19
updated: 2026-08-19
related_adrs: [ADR-004, ADR-005]
tags: [session, app, settings, theming, phase-5]
---

# Session: Phase 5 plan 8 — the settings dialog

**Date:** 2026-08-19

**939 tests green** (296 Core, 91 CLI, **552 App**), build clean with zero warnings. The count is
the cumulative result of plans 5–8 landing their surfaces in sequence; this plan contributes the
settings-dialog half of that jump. The real 680px settings modal replaces the placeholder
`MessageBox` that had stood in for *Settings…* since the tray shell shipped.

Executed: [README Screen 3](../operations/design/README.md) (the base settings layout) and
[screens/08-settings-persistence.md](../operations/design/screens/08-settings-persistence.md)
(WHERE THESE SETTINGS LIVE, WHICH WAVE LINK, the empty-trash row, unbuilt tiers, error-12 missing
folder). The trash row's *behaviour* came from plan 6; this dialog hosts it.

## What shipped

**No Save button — every control commits on change.** `SettingsViewModel` models that as bindable
properties each of which writes through to the settings file on change, atomically (temp file +
replace), **on change and never on exit**. The footer's "CHANGES APPLY AS YOU MAKE THEM" is a fact,
not a promise. A command-line flag overrides the file for one run and is never written back — the
view model carries that rule rather than leaving it to the caller to remember.

**The proportion bar is computed from enabled tiers, not hard-coded.** The 6px stacked bar
recomputes its widths whenever a tier is toggled, so "EACH BACKUP: ABOUT X MB" and "+ Y MB IF YOU
ADD THE PLUG-IN FILES" track what is actually included. The two locked rows (Your setup, A list of
your effects) cannot be moved at all — a programmatic set on them is rejected by the view model,
pinned by test rather than left to the UI to happen not to allow.

**Unbuilt tiers stay on screen, present but disabled.** PRESETS and PLUGINS render with the NOT
BUILT YET badge and the footnote explaining why they are not hidden ("hiding them would make the
backup look more complete than it is"). The keyboard/SR pass (Task 7) made the locked toggles
*present-but-disabled* rather than collapsed: a screen reader announces them as off, unavailable
switches instead of dropping them from the tree. The toggle's `WhatGoesInToggle` style DataTrigger
sets `IsEnabled=false` + 40% opacity when locked — the design's "off treatment at 40%, not
interactive" applied by the existing style rather than new view code.

**Focus returns to the list on dismiss.** Closing the dialog (Escape, either Close button, or the
window being dismissed) hands keyboard focus back to the main window's list via
`MainWindow.RestoreFocusToList` — made `internal` so the settings dialog, which is opened from
`App` rather than owned by the window, can reuse the same seam every other dialog already uses.

## What broke, and what it taught

**A `DataTrigger` on `{Binding Weight}` inside a `Border.Style` does not fire when the window is
shown off-screen without a full render pass.** The error-note weight (amber vs neutral) was driven
by such a trigger and silently did not apply in the test harness. The fix sets
`NoteBlock.Background`/`BorderBrush` explicitly in code-behind from `model.Weight` at construction,
so the state is right whether or not a render pass has happened.

**XAML comments cannot contain `--`.** Every brush name referenced in a comment had to be written
in PascalCase (`WlSunken`, not `--wl-sunken`) or the XAML would not compile. A trap that bites on
every themed surface, not just this one.

**The locked-tier toggle initially hid itself.** The first cut bound the toggle's `Visibility` to
`Locked` so the badge could sit in its place — which dropped the control from the screen-reader
tree entirely. The design says the toggle stays *on screen* (off treatment, not interactive), so
the fix keeps it present and disabled with the badge overlaid on top (transparent, non-hit-testable).

## Decisions

| Decision | Reasoning |
|---|---|
| **In-place commit, no Save button** | The design's footer is a fact only if every control writes through. A Save button would make "changes apply as you make them" a lie; the view model owning the write-on-change rule keeps it true |
| **Persist on change, never on exit; CLI flag overrides one run** | Writing on exit would persist a half-edited state and surprise the user. The override must not be written back or the file would drift from what was actually chosen |
| **Locked toggles present-but-disabled, not collapsed** | Collapsing them removes them from the screen-reader tree and makes the backup look more complete than it is — the exact thing the footnote says not to do |
| **Focus return reuses `RestoreFocusToList` (made internal)** | Inventing a second focus-return path for this dialog would be a third way to do the same thing. One seam, reused, is the shape this repo keeps |

## Still open

- **The not-found first-run variant** remains open in [technical-debt.md](../technical-debt.md)
  §4.10 — carried forward from plan 7, a distinct state that needs its own design pass in
  `06-errors.md` before code. Not touched by this plan.
- **High contrast for the settings dialog is unverified by eye.** The three-theme brush test passes
  (every key resolves in all three dictionaries), but whether the dialog reads well in a real
  high-contrast theme is part of plan 10's verification pass, not yet checked.

## References

- [phase-5-wpf.md](../dev-phases/phase-5-wpf.md) · [technical-debt.md](../technical-debt.md) §4.8 item 4, §4.9, §4.10
- [README Screen 3](../operations/design/README.md) · [screens/08-settings-persistence.md](../operations/design/screens/08-settings-persistence.md)
- [[ADR-004]] thin shells · [[ADR-005]] WPF
