---
title: "Phase 5, plan 7: the twelve errors and the first-run / empty state"
status: in-progress
created: 2026-08-18
updated: 2026-08-19
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

- [x] **Step 1:** Status-strip error variants. Error 1 (Wave Link not found) already renders as amber dot + "WAVE LINK NOT FOUND ON THIS COMPUTER" via `ShellViewModel.StatusStrip`/`StatusTone` (pinned by `ShellViewModelTests.Wave_link_not_found_is_amber_and_says_so`). Added the design's first-run variant (`06-errors.md` lines 24–27): `FirstRunError1Label` + `FirstRunLookedInLabel` on `ShellViewModel`, shown only when the store is empty AND Wave Link is missing. Error 10 (folder missing) already renders as neutral dot + "BACKUP FOLDER UNAVAILABLE" replacing the auto-backup segment (pinned by `A_missing_folder_replaces_the_last_segment_and_is_neutral`).
- [x] **Step 2:** Unit tests: `A_missing_folder_leaves_nothing_to_queue_a_backup_into` pins that with the folder gone all four actions are dark (the no-queue guarantee at the shell — Core's `AutoBackupCoordinator.Tick` clears the pending write on failure, covered by `WatcherFailureTests`). `The_first_run_variant_shows_only_when_the_store_is_empty_and_wave_link_is_missing`, `..._absent_when_wave_link_is_found`, `..._absent_when_the_store_has_backups` pin the first-run variant. Committed `a1c2e90`. 885 tests green (296 Core + 91 Cli + 498 App).

## Task 3 — Inline result-strip errors (3, 5, 6, 7, 10, 11)

These reuse the existing `RestoreOutcomeStrip` host. All inline strips are **neutral fill**
(they are refusals: nothing was written, nothing changed) — the weight rule says only error 4's
malformed-settings dialog is amber.

- [x] **Step 1:** Generalise `RestoreOutcomeStrip` so it can render an `AppError` from the inline set, not just restore outcomes. Added a `RestoreStripKind.InlineError` member, `ShowError(AppError, string? monoMeta, string? actionLabel)` (throws `ArgumentException` for a non-inline placement), and `ErrorNumber`/`MonoMeta`/`IsInlineError` properties; `Dismiss()`/`AcknowledgeReject()` clear the error state. All neutral fill per the weight rule.
- [x] **Step 2:** Wire each trigger in `MainWindow.xaml.cs`. A new `TryShowInlineError(CoreError?)` helper is the one place a typed CoreError becomes the strip it renders as — it maps through `AppErrorMapper.FromCoreSignal` and forwards only when the catalog says inline. Wired at: restore live-inspect failure (3), restore plan failure (7, 11), failed-restore outcome (7, 10 via the new `RestoreResultView.CoreError`), and Back-up-now failure (3, 5). Errors 4/8 are dialogs (Task 4) and keep the message box until then; error 6 (write-failed) has no reachable trigger in the current restore path.
- [x] **Step 3:** Unit tests pinning each code's strip text + weight (`RestoreOutcomeStripTests.cs` inline-error section: per-code copy/number/neutral/dismiss/action for 3,5,6,7,10,11, dismiss-clears, PropertyChanged-raises, non-inline-placement-throws). Build 0/0; 894 tests green (296 Core + 91 Cli + 507 App).

## Task 4 — Dialog errors (2, 4, 8)

The three decision dialogs share one borderless modal (`Views/ErrorDialog.xaml`, the same shape as
`DeleteDialog`) driven by a pure projection (`ViewModels/ErrorDialogModel.cs`). The model's
`Build(CoreError)` renders only errors 2/4/8 and throws on anything else, so a non-dialog error
reaching the dialog path is a caller bug, not a state to render.

- [x] **Step 1:** Error 2 (multiple installs, none chosen): a chooser dialog listing the found installations; choosing one persists it (the WHICH WAVE LINK section in Plan 8 reads this). Neutral. Wired at App level: `RefreshShellFacts` detects `MultiplePackagesFound` with no chosen path (once per process via `error2Prompted`) and shows the dialog; a confirmed pick is written to `settings.ChosenWaveLinkPath` through `settingsRepository.Save`.
- [x] **Step 2:** Error 4 (malformed settings) and error 8 (newer-version backup): dialogs with the designed copy. Error 4 is the only amber of the three — its note block takes `WlWarnSoft`/`WlWarn`, set in the code-behind from the model's weight (a DataTrigger swapping brushes here never fires for a window shown without a full render pass, and would make the variant untestable). Error 8 is neutral and blocks the restore — no pre-restore copy, no restore. Wired via `MainWindow.TryShowDialogError`, which forwards a plan-failure only when the catalog says Dialog.
- [x] **Step 3:** Unit tests: `ErrorDialogModelTests` pins each variant's copy/weight/options/buttons/card-width and that every non-dialog error throws; `ErrorDialogViewTests` forces each variant through a real layout pass off-screen in both themes and asserts the chooser/note/footer visibility per variant, the amber note fill for error 4, and the bound card width (620 vs 560). Build 0/0; 904 tests green (296 Core + 91 Cli + 517 App). Commit: `feat(app): dialog errors 2,4,8`.

## Task 5 — Error 12 / error 9 full screen (replaces the list)

One screen serves both (08 §"The folder is gone"): the backup folder can't be used. Neutral — nothing broken, nothing lost, a location is missing.

- [x] **Step 1:** Built as a state in `MainWindow.xaml` (`FolderMissingStandIn`), not a separate view — the list area is replaced in place. Status strip ok-dot "WAVE LINK RUNNING · N INPUTS · BACKUP FOLDER UNAVAILABLE" (existing, pinned by `ShellViewModelTests`); search field at 40% opacity and disabled via a DataTrigger on `List.State == FolderMissing`; centred column — h2 "The backup folder can't be used" (`WlDisplayFont` Medium 26px), body (max 430px, centred), mono path, mono 75% "LAST SEEN … · N BACKUPS THEN", actions: primary "Choose a folder…", secondary "Look again", ghost "Use the default folder".
- [x] **Step 2:** Bottom bar: `FolderMissingBottomLine` mono 11px "NOTHING CAN BE LISTED, TAKEN OR RESTORED UNTIL A FOLDER IS SET" (visible only in this state); all four action buttons at 40% via the existing `CanX` properties folding in `!facts.FolderMissing` — including Back up now. The column header goes with the list (nothing to head).
- [x] **Step 3:** While in this state the automatic backup does nothing and the strip says so (no silent hourly failure, no queue) — verified against `AutoBackupCoordinator.Tick` (clears the pending write on failure, reports once). Unit tests: `FolderMissing_enter_dims_all_four_actions_and_the_search_field`, `FolderMissing_exit_restores_all_four_actions_and_the_search_field`, `FolderMissing_actions_are_visible_only_with_the_stand_in` in `MainWindowListStateTests.cs`. Store re-pointing via `App.SetStorePath` / `UseDefaultStore` / `RecheckStore` + `BackupHost.SetStore`. Build 0/0; 907 tests green (296 Core + 91 Cli + 520 App).

## Task 6 — First-run / empty state (Screen 4)

- [x] **Step 1:** Add the empty-state view, driven by `IsFirstRun` (`Backups.Count == 0`): caption bar and bottom bar as usual; Restore/Rename/Delete disabled at 40%; Back up now live. The list area is replaced by a centred column: the MeasureRule frame (440px hairlines above/below with end ticks + inner ticks at 12% and 38%), "No backups yet" (Rubik 500 30px), the body sentence (max 430px, centred), then Back up now (primary) + Choose where to keep them (secondary), the checked "Keep backing up on its own…" checkbox, and the found-line: ok dot + "WAVE LINK FOUND · N INPUTS · SETTINGS LAST SAVED …" with the settings path in mono at 80%.
- [x] **Step 2:** Footer strip: `%LOCALAPPDATA%\WaveLinkBackup` left, "FREE AND OPEN SOURCE · NOTHING LEAVES THIS COMPUTER" right.
- [x] **Step 3:** Unit test: `IsFirstRun` true → empty state shown and the three destructive/restore actions disabled; after the first backup is written the list returns. (The "Wave Link not found" variant is NOT built — it is an open gap, see below.) Commit: `feat(app): first-run / empty state`.

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
