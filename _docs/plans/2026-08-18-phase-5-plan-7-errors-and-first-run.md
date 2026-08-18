---
title: "Phase 5, plan 7: the twelve errors and the first-run / empty state"
status: planned
created: 2026-08-18
updated: 2026-08-18
tags: [plan, phase-5, wpf, errors, first-run]
---

# Plan 7 — The twelve errors and the first-run / empty state

**Phase:** 5 · WPF shell, part 7 of the phase.
**Design source:** `_docs/operations/design/screens/06-errors.md` (the twelve errors, four placements, the weight rule), `08-settings-persistence.md` §"The folder is gone" (error 12 full screen — one screen with H's missing folder), and `_docs/operations/design/README.md` Screen 4 (first-run / empty state).
**Depends on:** Plan 4 shipped (`136bed7`) — the list, status strip, and bottom bar exist. Plan 5 (restore) and Plan 6 (delete/trash) are independent; this plan only reads their outcomes where an error placement overlaps (e.g. a failed restore lands in the inline result strip).
**Goal:** Build the full error surface — all twelve errors in their four designed placements with the correct weight rule — plus the first-run/empty state (Screen 4), so every failure the app can hit has a designed, tested home and nothing fails silently.

---

## What this plan does and does not do

| In scope | Out of scope (other plans / phases) |
| --- | --- |
| All twelve errors, placed per `06-errors.md` | The restore flow itself (Plan 5) — only its failure lands in the inline strip here |
| Four placements: Status strip, Inline result strip, Dialog, Replaces-list | Delete/trash UI (Plan 6) |
| Weight rule: neutral unless the config is not whole | Settings dialog chrome (Plan 8) — error 12 reuses its missing-folder screen spec |
| First-run / empty state (Screen 4), Wave Link found variant | The "Wave Link not found" first-run variant (not yet designed — see Gaps) |
| Error 12 full screen (folder unavailable), neutral | Any new Core error types — surface what Core already reports |

---

## The twelve errors and their placements (from `06-errors.md`)

Placement key: **S** = Status strip · **I** = Inline result strip · **D** = Dialog · **R** = Replaces the list.

| # | Error | Placement | Weight |
| --- | --- | --- | --- |
| 1 | Wave Link not running / settings file missing | S | neutral |
| 2 | Multiple Wave Link installs, none chosen | D (chooser) | neutral |
| 3 | Backup folder unwritable | I | **amber** (config not whole) |
| 4 | Disk full while writing | D | **amber** |
| 5 | Backup write failed (generic) | I | **amber** |
| 6 | Corrupt / unreadable backup on restore | I | **amber** |
| 7 | Restore relaunch failed (Wave Link didn't come back) | I | **amber** |
| 8 | Pre-restore copy failed before a restore | D | **amber** |
| 9 | Backup folder vanished / not a valid backup folder | R (error 12 screen) | neutral |
| 10 | Automatic backup skipped — folder missing | S | neutral |
| 11 | Restore rejected by analysis (SUSPECT input drop) | I | **amber** |
| 12 | The backup folder can't be used (missing/moved/unwritable) | R (full screen) | neutral |

**Weight rule:** a state is amber only when the user's configuration is not whole. A missing *location* (errors 1, 9, 10, 12) is neutral — nothing is broken and nothing is lost; a location is simply missing. Errors that mean a write/restore did not produce a whole config are amber.

---

## Existing code this plan builds on

- **`src/WaveLinkBackup.Core`** — validation at write time and re-check on load already sets `IsSuspect` / `ValidationMessage`; the watcher/queuing logic already knows when the folder is missing (it must not fail silently or queue — 08 §"The folder is gone").
- **`src/WaveLinkBackup.App/ViewModels/RestoreOutcomeStrip.cs`** — the inline result strip already exists (Plan 4); errors 3,5,6,7,10,11 reuse its host.
- **`src/WaveLinkBackup.App/Views/MainWindow.xaml(.cs)`** — status strip, list area (what error 9/12 replace), bottom bar (disabled at 40% in the missing-folder state).
- **`src/WaveLinkBackup.App/ViewModels/ShellViewModel.cs`** — owns `IsFirstRun` (`Backups.Count == 0`), `WaveLinkRunning`, and the status-strip text.

---

## Task 1 — Error catalog (pure)

Model the twelve errors as data so placement and weight are decided in one testable place, not scattered across views.

- [x] **Step 1:** Add `src/WaveLinkBackup.App/ViewModels/ErrorCatalog.cs`: an `AppError` record with `Code` (the 12 ids), `Placement` (`StatusStrip` | `InlineStrip` | `Dialog` | `ReplacesList`), `Weight` (`Neutral` | `Amber`), `Title`, `Body`, and any `MonoLine`. Populate all twelve exactly per the table above.
- [x] **Step 2:** Add a pure mapper `AppError.FromCoreSignal(...)` that takes the signals Core already emits (validation result, write failure kind, restore verdict, folder probe result) and returns the matching `AppError` — or null when there is no error. Implemented as `AppErrorMapper.FromCoreSignal(CoreSignal)`.
- [x] **Step 3:** Unit tests (`ErrorCatalogTests.cs`) pinning: each of the twelve codes maps to the right placement and weight; the weight rule holds (location-missing → neutral, config-not-whole → amber); a healthy signal maps to null. Committed `4eba547`, then revised to match `06-errors.md` verbatim (the table above had drifted from the design source — see note below). 31 tests green.

> **Reconciliation note (this plan's table vs `06-errors.md`).** The placement/weight table
> above was drafted against an earlier error list and has since been superseded by the design
> source, which is authoritative on values and copy. The catalog (`ErrorCatalog.cs`) now follows
> `06-errors.md` exactly: only errors **1** (Wave Link not found — `--wl-warn` dot + text) and
> **4** (malformed settings file) are amber; every inline strip is neutral fill; error **9** is a
> Dialog (in Settings, after "Change folder…"), not Replaces-list; error **10** is the inline
> "This backup is damaged" strip, not a status-strip skip. The status-strip folder-missing fact
> ("BACKUP FOLDER UNAVAILABLE", neutral) already lives in `ShellViewModel` and is pinned by
> `ShellViewModelTests`; it is the 10-decisions rule, distinct from error 12's full screen.

## Task 2 — Status-strip errors (1, 10)

- [ ] **Step 1:** Extend the status strip in `MainWindow.xaml` to render an error variant: dot colour + text from `AppError`. Error 1 (Wave Link not running / settings file missing) → neutral dot, "WAVE LINK NOT RUNNING · …". Error 10 (automatic backup skipped — folder missing) → the strip says so explicitly; the automatic backup does nothing while the folder is missing and must not queue.
- [ ] **Step 2:** Unit test: with the folder-missing signal the strip text names the skip and no queued run is scheduled. Commit: `feat(app): status-strip errors 1 + 10`.

## Task 3 — Inline result-strip errors (3, 5, 6, 7, 10, 11)

These reuse the existing `RestoreOutcomeStrip` host but with amber weight where the config is not whole.

- [ ] **Step 1:** Generalise `RestoreOutcomeStrip` (or add a sibling `ResultStrip`) so it can render an `AppError` from the inline set, not just restore outcomes. Amber treatment for 3,5,6,7,11; neutral for 10 when it appears inline.
- [ ] **Step 2:** Wire each trigger: unwritable folder (3), generic write failure (5), corrupt-on-restore (6), relaunch-failed (7), rejected-by-analysis/SUSPECT input drop (11). Each shows the right copy and weight.
- [ ] **Step 3:** Unit tests pinning each code's strip text + weight, and that `Dismiss()`/auto-dismiss behaves as for outcomes. Commit: `feat(app): inline result-strip errors 3,5,6,7,10,11`.

## Task 4 — Dialog errors (2, 4, 8)

- [ ] **Step 1:** Error 2 (multiple installs, none chosen): a chooser dialog listing the found installations; choosing one persists it (the WHICH WAVE LINK section in Plan 8 reads this). Neutral.
- [ ] **Step 2:** Error 4 (disk full while writing) and error 8 (pre-restore copy failed before a restore): amber dialogs with the designed copy. Error 8 blocks the restore — no pre-restore copy, no restore.
- [ ] **Step 3:** Unit tests: each dialog's copy + weight; error 8 leaves the selection un-restored and the pre-restore step not retried silently. Commit: `feat(app): dialog errors 2,4,8`.

## Task 5 — Error 12 / error 9 full screen (replaces the list)

One screen serves both (08 §"The folder is gone"): the backup folder can't be used. Neutral — nothing broken, nothing lost, a location is missing.

- [ ] **Step 1:** Add `src/WaveLinkBackup.App/Views/FolderUnavailableView.xaml` (or a state in MainWindow): status strip ok-dot "WAVE LINK RUNNING · N INPUTS · BACKUP FOLDER UNAVAILABLE"; search field at 40% opacity, not interactive; centred in the 440px MeasureRule frame — h2 "The backup folder can't be used" (Rubik 500 26px/1.2), body (max 430px, centred), mono path, mono 75% "LAST SEEN … · N BACKUPS THEN", actions: primary "Choose a folder…", secondary "Look again", ghost "Use the default folder".
- [ ] **Step 2:** Bottom bar: mono 11px "NOTHING CAN BE LISTED, TAKEN OR RESTORED UNTIL A FOLDER IS SET"; all four action buttons at 40% opacity including Back up now. The column header goes with the list (nothing to head).
- [ ] **Step 3:** While in this state the automatic backup does nothing and the strip says so (no silent hourly failure, no queue). Unit test: entering/exiting the state toggles the view and disables all four actions; "Look again" re-probes. Commit: `feat(app): error 9/12 folder-unavailable full screen`.

## Task 6 — First-run / empty state (Screen 4)

- [ ] **Step 1:** Add the empty-state view, driven by `IsFirstRun` (`Backups.Count == 0`): caption bar and bottom bar as usual; Restore/Rename/Delete disabled at 40%; Back up now live. The list area is replaced by a centred column: the MeasureRule frame (440px hairlines above/below with end ticks + inner ticks at 12% and 38%), "No backups yet" (Rubik 500 30px), the body sentence (max 430px, centred), then Back up now (primary) + Choose where to keep them (secondary), the checked "Keep backing up on its own…" checkbox, and the found-line: ok dot + "WAVE LINK FOUND · N INPUTS · SETTINGS LAST SAVED …" with the settings path in mono at 80%.
- [ ] **Step 2:** Footer strip: `%LOCALAPPDATA%\WaveLinkBackup` left, "FREE AND OPEN SOURCE · NOTHING LEAVES THIS COMPUTER" right.
- [ ] **Step 3:** Unit test: `IsFirstRun` true → empty state shown and the three destructive/restore actions disabled; after the first backup is written the list returns. (The "Wave Link not found" variant is NOT built — it is an open gap, see below.) Commit: `feat(app): first-run / empty state`.

## Task 7 — Weight-rule integration test + full verification

- [ ] **Step 1:** Add a single integration-style test that walks representative Core signals and asserts the rendered weight (amber vs neutral) matches the rule for all twelve — this is the guard against a future edit silently re-weighting an error.
- [ ] **Step 2:** `dotnet build` — 0 warnings, 0 errors. Full suite green (764 + new tests). Commit: `test(app): weight-rule integration + full verification`.

---

## Open gap carried forward (not built in this plan)

The first-run **"Wave Link not found"** variant is specified only as "line 6 is the place to say so (amber dot, amber text, a `Choose the settings file…` action) — that variant is not yet designed." It stays a gap; log it in `_docs/technical-debt.md` when this plan lands.

## Definition of done for Plan 7

- All twelve errors render in their four placements with correct copy and weight.
- The weight rule (amber only when the config is not whole) holds and is integration-tested.
- Error 9/12 replace the list as one neutral full screen; the automatic backup stays silent-but-said while the folder is missing.
- First-run/empty state matches Screen 4 (found variant); the not-found variant is logged as a gap, not half-built.
- New tests green; full suite green; build clean.
