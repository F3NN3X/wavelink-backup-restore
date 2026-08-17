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

**As of v4 of the package, nothing is undesigned.** High contrast (`11`) and
tray/autostart/updates (`12`) closed the last two.

### The tray design changes what this app *is*

`12-tray-autostart-update.md` opens with the sentence the rest of the phase should be built
around: *"Configured once, then ignored — so it lives in the tray and the window is the
exception."*

That is not a feature added to a window app. **It is a tray app that has a window**, and it
lands scope this document did not previously carry:

- Tray icon with **four states**, of which `NEEDS YOU` is the one Core cannot currently
  produce — it needs the error `TickResult` is about to start carrying (§7.3).
- A context menu that is the primary interface: back up, open, pause for an hour, quit.
  **"Quit — stops backing up"** says so in the item, because quitting is not closing.
- **Exactly two notifications**, and a rule worth quoting: *a successful backup never
  notifies. A safety net that congratulates itself weekly gets muted, and then it is not a
  safety net.*
- Autostart through `HKCU\...\Run` with `--tray`, per-user, never a scheduled task — and
  **Task Manager wins**: if it has disabled the entry, the toggle reads off and cannot be
  switched on from here.
- An **update section in Settings**. The *UI* is phase 5; the *mechanism* — downloading,
  installing, restarting — stays phase 7. Error 8 ("made by a newer version") deep-links into
  this section, so the section must exist even while the mechanism does not.

### But four decisions in it contradict shipped code

This is now the interesting part of phase 5, and none of it is XAML.
[technical-debt.md](../technical-debt.md) §7 has the detail:

**All four are now decided** (2026-08-17) — the approaches are in
[technical-debt.md](../technical-debt.md) §7. Summarised, because they are Core work that has
to happen before or alongside the XAML:

| Decision | Approach |
|---|---|
| Delete | **Two-stage.** Move to `<store>/.trash/<id>/` — a plain directory move, no interop. **Empty trash** in Settings hands it to the Recycle Bin behind an `IRecycleBin` seam. Keeps `Core` on `net10.0`, and works on network stores where the Recycle Bin simply does not exist. **Amends design decision 3** — the dialog must name `.trash`, not the Recycle Bin. |
| Damaged vs keep-count | **Verify lazily, only the condemned.** `SelectForPruning` returns candidates; the pruner verifies just those and refuses to delete any that fail. Hashes one or two, not thirty. No manifest field to keep in sync. |
| Watcher queuing | **Clear `lastWriteAt` on failure and carry the `CoreError` in `TickResult`.** The error is what feeds the tray's `NEEDS YOU` state — without it the tray has a state it cannot enter. |
| Keyboard | **Windows conventions generally**, not only the four keys the design names — accelerators, `Space`, `Home`/`End`, `Shift+F10`, `Ctrl+F`, `Delete`. Screen-reader labels are part of this, not a follow-up: the five-slot health strip reads as five unlabelled cells without an `AutomationProperties` name. |

**Do the Core work first.** Three of the four are `Core` changes with tests, and the tray's
`NEEDS YOU` state is blocked on one of them.

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
