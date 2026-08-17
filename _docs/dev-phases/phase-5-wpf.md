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

## The gaps are designed — 2026-08-17

The six holes this document was written around are **specified**. Handoff part 2 lives in
[operations/design/screens/](../operations/design/README.md): eleven files, one state group
each, a PNG beside each, and `10-decisions.md` closing every open question including the two I
flagged as needing a deliberate answer (SUSPECT vs DAMAGED; whether a rejected restore gets a
louder treatment — it does, and it is the one strip that cannot be dismissed).

**Read `screens/01-tokens-and-mapping.md` before writing any markup.** It is the only file the
others assume you have read, and it carries the WPF brush keys and the F3NN3X token provenance.

**Only Windows high-contrast mode and item 6 (tray/autostart/update) remain undesigned.**

### But four decisions in it contradict shipped code

This is now the interesting part of phase 5, and none of it is XAML.
[technical-debt.md](../technical-debt.md) §7 has the detail:

| Decision | Conflict |
|---|---|
| Delete goes to the **Recycle Bin** | `SnapshotStore.Delete` is permanent. `SHFileOperation` is Win32 interop, and `Core` targets `net10.0` on purpose. **Needs an architectural decision.** |
| **Damaged backups don't count toward the keep-count** | Retention can't see damage — it's detected by `SnapshotGuard` at restore time, not stored in the manifest. |
| **Automatic backup must not queue** when the folder is missing | It currently retries every 15s, silently, forever — both halves of what the decision forbids. |
| Keyboard map, focus behaviour | Specified, unimplemented. No conflict, just work. |

**Settle the Recycle Bin question first.** It reaches into a phase-1 decision the
`GuardNoDesktopFramework` build guard actively enforces, and discovering that mid-phase is how
a UI phase turns into an architecture phase.

## Scope

### In

- The four screens from `README.md`, **plus the eleven state groups in `screens/`**.
- Brush resources per theme, **live OS theme following**, OS accent bound to `--wl-accent`
  with `--wl-danger` fixed.
- Custom 34px caption bar with Mica, the five-slot health strip, tier badges, row expansion.
- The **new inline result strip** between the status strip and the column header — the single
  home for restore outcomes, in-progress states and six of the twelve errors.
- The four Core changes in [technical-debt.md](../technical-debt.md) §7.
- Settings **persistence** at `%LOCALAPPDATA%\WaveLinkBackup\settings.json`, with command-line
  flags winning for one run and not written back.
- Tray + autostart, or an explicit decision not to.

### Edits to things already specified — do these, don't re-derive them

`CHANGES-SINCE-V1.md` §3 is the authoritative list. The ones that will bite:

- **SUSPECT badge is amber now, not red.** Everywhere, including the component sheet.
- The five-slot INPUTS strip is **replaced by a single dotted cell** on damaged rows — the one
  place the fixed-slot pattern is deliberately broken.
- Restore dialog focus starts on **Cancel**, not the destructive button.
- Deleting your only backup is **neutral, not amber** — nothing is un-whole at that moment,
  and the red Delete button already carries the weight.
- An amber tint **must composite over an opaque `--wl-bg` base**; a bare 18% tint on a darker
  surface goes unreadable.

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

- [README.md](../operations/design/README.md) — the authority on values and layout
- [[ADR-004]] · [[ADR-005]]
- [technical-debt.md](../technical-debt.md) §4 — the six gaps, listed since day one
