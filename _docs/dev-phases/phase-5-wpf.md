---
title: "Phase 5 — WPF shell"
status: published
created: 2026-08-16
updated: 2026-08-16
related_adrs: [ADR-004, ADR-005]
tags: [dev-phase]
---

# Phase 5 — WPF shell

**Status:** Not started — **next**.
**Entry criteria:** phase 4 complete. ✅ 2026-08-16.
**Exit criteria:** the four designed screens match the handoff in **both themes**, the six
undesigned surfaces are designed and built, and no Core logic has leaked into the shell.

## Why this phase exists

**The GUI is the product.** The whole value proposition is *configured once, then ignored
until the day it saves your rig*, and a CLI cannot deliver that — a person who has to remember
to run `wlbackup watch` has the same problem upstream's users have.

Everything Core does is reachable from a command line today. This phase is about it being
reachable by someone who does not want a command line.

## The biggest phase, for a reason worth stating

The other phases had a specification. This one has **four finished screens plus six holes**,
and the holes are not small ([technical-debt.md](../technical-debt.md) §4):

1. Delete confirmation dialog
2. Backup-in-progress and restore-in-progress states
3. Error states — Wave Link not installed, settings file missing, backup folder unwritable,
   disk full, corrupt backup on restore
4. Search results and no-results state
5. Keyboard map, screen-reader labels, Windows high-contrast mode
6. Tray behaviour, autostart, update mechanics

**Budget for designing these, not just building them.** Improvised UI is how a coherent design
erodes, and the handoff is high-fidelity enough that improvisation will show.

Item 3 deserves particular attention: the CLI already renders every one of those errors,
because `CoreError` is a closed hierarchy and the CLI maps all of it. **The information exists;
only the visual treatment is missing.**

## Scope

### In

- The four designed screens: main list, restore confirmation, settings, first run.
- Brush resources per theme, **live OS theme following**, OS accent bound to `--wl-accent`
  with `--wl-danger` fixed.
- Custom 34px caption bar with Mica, the five-slot health strip, tier badges, row expansion.
- The six gaps above.
- Settings **persistence** — the first place a user changes a setting without a command line.
- Tray + autostart, or an explicit decision not to.

### Out

- Tier 2–4 capture → **phase 6**. The Settings dialog shows those toggles as designed; they
  can be disabled with a "coming soon" affordance or omitted, but not silently non-functional.
- Update mechanics → **phase 7**.

## Work

### 1 · Tokens as resources, once

Every `--wl-*` value in the handoff becomes a **brush resource key**, declared once per theme
and referenced with `DynamicResource`. Never a literal at a call site. That is what makes live
theme switching a resource swap rather than a window rebuild.

### 2 · The list is the app

Name, date, trigger pill, five-slot health strip, tier badges, suspect marker, row expansion.
`SnapshotManifest` already carries **every field this needs** — nothing has to open a snapshot
to render a row, which was the point of putting them there in phase 2.

### 3 · Restore confirmation

`RestorePlan` is already built and already pure. The dialog **renders** it; it computes
nothing. If a view model starts calculating what changes, that logic belongs in Core.

### 4 · What has no Core support yet

Honest list, because these are the things that will feel like "just UI work" and are not:

| Need | Status |
|---|---|
| Search / filter over snapshots | Not in Core. Trivial, but it is a Core addition with tests. |
| Settings persistence | `BackupSettings` exists as a value; **nothing reads or writes it to disk**. |
| Disk-free reporting | The design's bottom bar shows it. Not in Core. |
| A hosted watcher | The CLI's `watch` owns its own loop. The GUI needs the same thing running under a window. |

### 5 · MVVM, thinly

View models translate Core records to bindable shapes and nothing more. The temptation in a
WPF app is to let the view model become the application; [[ADR-004]] says it does not.

## Testing

The suite so far tests everything except pixels, and that should continue.

- **View models are testable** — they take Core types and expose bindable properties. Test
  them like anything else.
- **Do not test XAML.** Layout correctness is what the handoff and a human eye are for.
- **Theme switching is testable** — assert the resource dictionary swaps and that no brush
  is a literal.
- **The accent rule is testable**: `--wl-danger` must not follow the OS accent. Two different
  reds in one window is a bug the design calls out explicitly.

## Risks

| Risk | Early signal | Response |
|---|---|---|
| The six gaps get improvised under time pressure | A dialog appearing without a design note | Design them first; they are listed here so they can be scheduled |
| Logic migrating into view models | A view model computing what a restore changes | `RestorePlan` already exists; use it |
| Colour literals in XAML | Any `#RRGGBB` outside a theme dictionary | A test, or a source scan like the Core guards |
| Tier toggles that do nothing | Settings showing presets/plugins as working | Disable with an explanation until phase 6 |
| "It looks close enough" | — | The handoff is high-fidelity on purpose; check both themes |

## References

- [design-handoff.md](../operations/design/design-handoff.md) — the authority on values and layout
- [[ADR-004]] · [[ADR-005]]
- [technical-debt.md](../technical-debt.md) §4 — the six gaps, listed since day one
