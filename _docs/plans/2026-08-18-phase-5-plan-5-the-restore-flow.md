---
title: "Phase 5, plan 5: the restore flow"
status: completed
created: 2026-08-18
updated: 2026-08-19
tags: [plan, phase-5, wpf, restore]
---

# Plan 5 — The restore flow (confirmation → in-progress → outcome)

> **Status: complete.** All tasks landed and verified; the restore button runs the real flow
> (confirmation → four named stages → outcome strip), `ShowRestorePlaceholder()` is gone, and no
> Wave Link process API is called outside `IRestoreService` → `RestoreOrchestrator`. Checkboxes
> below are ticked to record that; the session note for this work is in
> [documentation-stats.md](../documentation-stats.md) → Recent additions (plan 8's entry, which
> closed §4.9 by wiring the strip).

**Phase:** 5 · WPF shell, part 5 of the phase.
**Design source:** `_docs/operations/design/screens/04-in-progress.md` (restore stages), `09-restore-dialog-additions.md` (version-mismatch note), `10-decisions.md` §3–§4 (SUSPECT restore, version mismatch). The four finished screens' base spec lives in `_docs/operations/design/README.md` Screen 2.
**Depends on:** Plan 4 shipped (`136bed7`) — the list, selection, bottom-bar actions, and the `RestoreOutcomeStrip` are all live. The Restore button currently calls `ShowRestorePlaceholder()`.
**Goal:** Replace the placeholder with the real restore flow: a confirmation dialog (Screen 2) → a four-stage in-progress strip (`04-in-progress.md`) → wire `RestoreOrchestrator.Plan()` from Core → feed the result into the existing `RestoreOutcomeStrip`.

---

## What this plan does and does not do

| In scope | Out of scope (other plans / phases) |
| --- | --- |
| Restore confirmation dialog, 620px, Now-vs-after table | Delete dialogs, rename, empty trash (Plan 6) |
| Version-mismatch mono note under the body (`09`) | The twelve errors + first-run/empty state (Plan 7) |
| Four-stage in-progress strip, no spinner (`04-in-progress.md`) | Settings dialog (Plan 8) |
| Wire `RestoreOrchestrator.Plan()` from the Restore button | Wave Link process control internals (already in Core) |
| Feed result into existing `RestoreOutcomeStrip` | Any new Core restore logic — reuse what exists |

---

## Existing code this plan builds on

- **`src/WaveLinkBackup.Core/Restore/RestoreOrchestrator.cs`** — `Plan()` at line 102. Ctor `(IFileSystem, IWaveLinkProcess, SnapshotStore, SettingsWriter, SettingsReader)`. Returns `RestoreOutcome(PreRestoreSnapshot, Relaunched, RestoreVerdict?)`; `Confirmed => Verdict?.Succeeded == true`.
- **`src/WaveLinkBackup.Core/Restore/Analysis/LogAnalysis.cs:13`** — `RestorePlan` + `RestoreVerdict` (the verdict the orchestrator produces after re-launch).
- **`src/WaveLinkBackup.App/ViewModels/RestoreOutcomeStrip.cs`** — outcomes `None`, `SucceededConfirmed`, `SucceededUnconfirmed`, `Rejected`, `Failed`; null-verdict → `SucceededUnconfirmed`; `Dismiss()` refuses on `Rejected`.
- **`src/WaveLinkBackup.App/Views/MainWindow.xaml(.cs)`** — strip markup + auto-dismiss timer; `ShowRestorePlaceholder()` at line 149 is what this plan replaces.
- **`src/WaveLinkBackup.App/ViewModels/ShellViewModel.cs`** — exposes `Strip`; owns the list, selection, and bottom-bar commands.

---

## Task 1 — Restore confirmation dialog view-model (pure)

The dialog's content is a pure projection: given a selected snapshot and the current live settings, produce the Now-vs-after rows, flag which values change, and decide whether the version-mismatch note shows. No I/O here.

- [x] **Step 1:** Add `src/WaveLinkBackup.App/ViewModels/RestoreDialogModel.cs` (a plain record/class, no WPF dependency) with:
  - `Title`, `Body` strings.
  - A list of table rows: each row has `Label`, `NowValue`, `AfterValue`, `Changed` (bool). Rows in fixed order: Inputs, Channel names, Effects, Saved presets, Mixes.
  - `VersionMismatchNote?: string` — the mono note text, present only when the snapshot's Wave Link version differs from the running one (`09-restore-dialog-additions.md`: placed under the body, above the table).
  - `MissingPluginWarning?: string` — present when an effect in the snapshot has no matching installed plug-in.
  - `Reassurance` string (always present: "Your current settings are saved as 'Before restore' first…").
- [x] **Step 2:** Add a pure builder `RestoreDialogModel.Build(Snapshot selected, LiveSettings current, VersionInfo running)` that computes each row's `Changed` flag by comparing values. A changed value is the only one that gets the accent dot in the view.
- [x] **Step 3:** Write unit tests (`tests/WaveLinkBackup.App.Tests/RestoreDialogModelTests.cs`) covering: a no-change restore (no rows flagged), a change in Effects and Presets (those two rows flagged, others not), version mismatch present → note set, version match → note null, missing plug-in → warning set.
- [x] **Step 4:** `dotnet test tests/WaveLinkBackup.App.Tests --filter RestoreDialogModelTests` — green. Commit: `feat(app): restore confirmation dialog model (pure projection)`.

## Task 2 — Restore in-progress strip view-model (pure state machine)

The in-progress UI is a four-stage named progression, no spinner (`04-in-progress.md`). Model it as an explicit state so the view just renders it.

- [x] **Step 1:** Add `src/WaveLinkBackup.App/ViewModels/RestoreProgressModel.cs` with a fixed stage list: `ClosingWaveLink`, `WritingSettings`, `StartingWaveLink`, `Checking`. Each stage has a `Status`: `Pending`, `Current`, `Done`. Expose `Stages` (ordered) and the reassurance line text.
- [x] **Step 2:** Add `Advance(Stage)` that marks the given stage `Current`, all earlier stages `Done`, later ones `Pending`. Guard: advancing past `Checking` is a no-op; calling with an out-of-order stage throws (the orchestrator drives it in order).
- [x] **Step 3:** Unit tests (`RestoreProgressModelTests.cs`): initial state has stage 0 current, rest pending; each `Advance` moves the frontier correctly; advancing out of order throws.
- [x] **Step 4:** `dotnet test ... --filter RestoreProgressModelTests` — green. Commit: `feat(app): restore in-progress four-stage model`.

## Task 3 — Wire the orchestrator behind a shell-facing service

The shell must not touch Core types directly for process control. Wrap the orchestrator so the view-model gets stage callbacks and a final outcome, and the app never calls Wave Link process APIs itself.

- [x] **Step 1:** Add `src/WaveLinkBackup.App/Services/RestoreService.cs` (interface `IRestoreService`) exposing `Task<RestoreResult> RestoreAsync(SnapshotId id, IProgress<RestoreStage> progress, CancellationToken ct)`. `RestoreResult` is an app-level enum: `Confirmed`, `Unconfirmed`, `Rejected`, `Failed` — mapped from `RestoreOutcome.Verdict`.
- [x] **Step 2:** Implement it by constructing/using the existing `RestoreOrchestrator` (already registered or resolvable from DI) and translating its stage transitions into `IProgress<RestoreStage>` reports. Map `Verdict.Succeeded == true` → `Confirmed`; null verdict → `Unconfirmed`; a rejected analysis → `Rejected`; exception → `Failed`.
- [x] **Step 3:** Register the service in the app's DI/composition root alongside the other services.
- [x] **Step 4:** Unit test the mapping (`RestoreServiceTests.cs`) with a stubbed orchestrator: each verdict shape maps to the right `RestoreResult`; a thrown exception maps to `Failed`. Commit: `feat(app): restore service wrapping RestoreOrchestrator`.

## Task 4 — The confirmation dialog view (XAML)

Build Screen 2 as a modal, 620px, `--wl-card`, hairline-24% border, 8px radius, over the window scrim. This is visual work — delegate to the `visual-engineering` category with the `frontend-design` skill if doing it in-session; otherwise author directly against the spec.

- [x] **Step 1:** Add `src/WaveLinkBackup.App/Views/RestoreDialog.xaml(.cs)` (or a content dialog) rendering: title, body, the version-mismatch mono note (only when present), the Now-vs-after table (changed values in `--wl-strong` with a 5px accent dot; unchanged in muted), the missing-plugin warning block (only when present), the reassurance line, and the footer (Cancel secondary + "Restore this backup" `--wl-danger` fill).
- [x] **Step 2:** Escape and Cancel are equivalent; focus starts on Cancel. No typed confirmation.
- [x] **Step 3:** Verify it renders in both dark and light themes with a quick manual run or a visual-QA pass (the `visual-qa` skill). Commit: `feat(app): restore confirmation dialog view`.

## Task 5 — The in-progress strip view (XAML)

Replace the placeholder area with the four-stage progression while a restore runs. No spinner; each stage shows done/current/pending treatment, 220ms `cubic-bezier(.2,0,0,1)` transitions, plus the reassurance line.

- [x] **Step 1:** Add the in-progress strip markup to `MainWindow.xaml` (or a dedicated user control), bound to `RestoreProgressModel`. Stages render as named rows with the three status treatments.
- [x] **Step 2:** While running, disable the list actions and Back up now (40% opacity) so the window can't be driven mid-restore.
- [x] **Step 3:** Commit: `feat(app): restore in-progress strip view`.

## Task 6 — Wire the Restore command end-to-end

Replace `ShowRestorePlaceholder()` with the real flow.

- [x] **Step 1:** In `ShellViewModel`, change the Restore command to: build `RestoreDialogModel` from the selection, show the confirmation dialog; on confirm, call `IRestoreService.RestoreAsync`, driving `RestoreProgressModel` via the progress reports and showing the in-progress strip.
- [x] **Step 2:** On completion, map `RestoreResult` → `RestoreOutcomeStrip` outcome (`Confirmed`→`SucceededConfirmed`, `Unconfirmed`→`SucceededUnconfirmed`, `Rejected`→`Rejected`, `Failed`→`Failed`) and call `Strip.Show(...)`. Re-enable the list actions.
- [x] **Step 3:** Remove `ShowRestorePlaceholder()` from `MainWindow.xaml.cs`.
- [x] **Step 4:** Manual smoke: with a real (or fixture) backup, confirm → watch the four stages advance → see the outcome strip appear with the right treatment. Commit: `feat(app): wire restore flow end-to-end, drop placeholder`.

## Task 7 — Keyboard, focus, screen-reader parity for the new surfaces

- [x] **Step 1:** The dialog is reachable by Enter on a selected row (the list already opens Restore on Enter per README Interactions). Confirm Escape cancels and focus returns to the list on close.
- [x] **Step 2:** Give each in-progress stage an `AutomationProperties.Name` so screen readers announce the current stage; the reassurance line is read once.
- [x] **Step 3:** Verify with a keyboard-only pass (Tab/Enter/Escape) and, if available, a Narrator spot-check. Commit: `feat(app): restore flow keyboard + SR parity`.

## Task 8 — Guards, running-state, and full verification

- [x] **Step 1:** Guard the Restore command when no row is selected (it should already be disabled at 40% opacity; assert it).
- [x] **Step 2:** If a restore is already in progress, the command is disabled (no double-launch).
- [x] **Step 3:** `dotnet build` — 0 warnings, 0 errors. Full suite green (expect 764 + new tests). Commit: `test(app): restore flow guards + full verification`.

---

## Definition of done for Plan 5

- The Restore button runs the real flow: confirmation → four named stages → outcome strip.
- `ShowRestorePlaceholder()` is gone.
- No Wave Link process API is called from view or view-model code — only through `IRestoreService` → `RestoreOrchestrator`.
- **High contrast:** every colour the surface uses in dark/light is replaced by a shape or a verdict word in HC; tints are transparent; surfaces separate on 1px WindowText borders; an HC test pins each. (Standing rule — source of truth: [plan 10](2026-08-18-phase-5-plan-10-high-contrast.md) Task 3.)
- New tests green; full suite green; build clean.
