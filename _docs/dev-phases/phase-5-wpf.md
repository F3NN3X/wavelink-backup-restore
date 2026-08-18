---
title: "Phase 5 — WPF shell"
status: published
created: 2026-08-16
updated: 2026-08-16
related_adrs: [ADR-004, ADR-005]
tags: [dev-phase]
---

# Phase 5 — WPF shell

**Status:** In progress — backup list (part 4) and restore-outcome strip shipped (`136bed7`);
execution plans for every remaining surface are written (2026-08-18, see below). **next.**
**Entry criteria:** phase 4 complete. ✅ 2026-08-16.
**Exit criteria:** the four designed screens match the handoff in **both themes**, the six
undesigned surfaces are designed and built, and no Core logic has leaked into the shell.

### Execution plans for the remaining surfaces — 2026-08-18

The backup list (part 4) is built and committed. The rest of the phase is now broken into four
dated execution plans under [`plans/`](../plans/), each following part 4's task format (pure
model → tests → view → wiring → keyboard/SR → guards + full verification):

| Plan | Surface | Design source |
|---|---|---|
| [plan-5](../plans/2026-08-18-phase-5-plan-5-the-restore-flow.md) | Real restore flow: confirmation dialog, four-stage in-progress strip, wire `RestoreOrchestrator`, feed the outcome strip | `screens/04-in-progress.md`, `09` |
| [plan-6](../plans/2026-08-18-phase-5-plan-6-delete-rename-trash.md) | In-place rename, three-variant two-stage delete, empty-trash row + per-volume detection | `screens/05-delete-dialogs.md`, `08` |
| [plan-7](../plans/2026-08-18-phase-5-plan-7-errors-and-first-run.md) | The twelve errors in their four placements (weight rule), error 9/12 full screen, first-run/empty state | `screens/06-errors.md`, `08`, README Screen 4 |
| [plan-8](../plans/2026-08-18-phase-5-plan-8-settings-dialog.md) | Settings dialog: in-place commit (no Save button), atomic persistence, WHICH WAVE LINK + WHERE THESE SETTINGS LIVE, unbuilt tiers | README Screen 3, `screens/08` |

Still outstanding within the phase after those four: the **tray shell** (icon states,
context menu, hide-on-close, single-instance, autostart) and **high contrast** — both named in
the Scope above. They are not yet broken into a dated plan; that is the next planning step once
plans 5–8 have landed.

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

### It is a tray app with a window, and that costs less than it sounds

`12-tray-autostart-update.md` opens with the sentence the phase should be built around:
*"Configured once, then ignored — so it lives in the tray and the window is the exception."*

Not aesthetics. **If closing the window stops backups, the app fails its own promise** and
becomes upstream's tool with extra steps.

| | App with a tray icon | Tray app with a window |
|---|---|---|
| What "the app" is | The window | The process |
| Closing the window | Quits | Hides it; work continues |
| `ShutdownMode` | `OnLastWindowClose` | `OnExplicitShutdown` |
| Window at startup | Always | Optional — `--tray` starts windowless |
| Who owns the watcher | The window | `App`, outliving any window |
| Single instance | Nice to have | **Mandatory** — two watchers race on one file |

**Core is already shaped for this.** `AutoBackupCoordinator` owns no timer, holds two
timestamps, and waits for a host to call `Tick()` — the CLI's `watch` verb is one such host
today. Moving that host into `App` is a small change, and `ShutdownMode` is one line.

**The real cost is three Windows integrations WPF does not provide**, which is why the
notification and update halves are deferred below:

| Need | Cost |
|---|---|
| Tray icon | No `NotifyIcon` in WPF. `UseWindowsForms=true` in the App project, a package, or `Shell_NotifyIcon` interop — **and it must survive Explorer restarting**, which naive implementations miss |
| Toast notifications | A separate API again, and the modern one wants packaging → **phase 7** |
| Autostart | `HKCU\...\Run`, easy — but **Task Manager can veto it** and the toggle must read that back |

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
- **The tray shell**: icon with its four states, context menu, hide-on-close,
  `OnExplicitShutdown`, single-instance, `--tray` windowless start, and the autostart toggle
  with its Task Manager read-back.
- **High contrast** (`11`) — in scope, not deferred. It is a theme, and this is the phase that
  builds the theming.

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

- Tier 2–4 capture → **phase 6**. The Settings dialog shows those toggles as designed, with
  the **NOT BUILT YET** badge from `08` — visible on purpose, never silently non-functional.
- **The two toast notifications → phase 7.** Both are *"something has been wrong for a while"*
  cases — the nine-day silence and a rejected restore. Real, but not day-one, and they need a
  notification API WPF does not provide. Nothing else in the design depends on them existing;
  the tray's `NEEDS YOU` icon state and tooltip already carry the same information passively.
- **The update section beyond a static row → phase 7.** Build the `UPDATES` section showing
  *Up to date* with the running version, because error 8 deep-links into it and the section
  must exist. **Do not build** check-for-updates, download, install or restart.
- Update mechanics generally → **phase 7**.

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
