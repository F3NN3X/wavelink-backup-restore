# Plan 6 — Delete, rename, and empty trash

**Phase:** 5 · WPF shell, part 6 of the phase.
**Design source:** `_docs/operations/design/screens/05-delete-dialogs.md` (three delete variants, two-stage deletion), `08-settings-persistence.md` §"Empty trash" (the trash row, three volume states, confirmation only where the Recycle Bin can't catch it). Rename is specified in `_docs/operations/design/README.md` Interactions ("in place on the row's name; commit on Enter or blur, cancel on Escape").
**Depends on:** Plan 4 shipped (`136bed7`) — list, selection, bottom-bar actions. Delete and Rename currently open placeholder `MessageBox`es; there is no trash UI yet.
**Goal:** Replace the placeholders with: in-place rename, the three-variant delete confirmation (two-stage), and the empty-trash row in Settings' folder section — so deletion is reversible on every volume and the Recycle Bin is named in exactly one place in the whole app.

---

## What this plan does and does not do

| In scope | Out of scope (other plans / phases) |
| --- | --- |
| In-place rename on the selected row | The restore flow (Plan 5) |
| Delete confirmation, three variants, 480px | The twelve errors + first-run/empty state (Plan 7) |
| Two-stage delete: move into `.trash` (plain dir move) | Settings dialog chrome/other sections (Plan 8) — this plan only adds the trash row inside it |
| Empty-trash row, three volume states, per-volume detection | Any new Core delete logic — reuse `SnapshotStore.EmptyTrash` and the existing Delete move |
| `.trash` stays invisible to list/search/counts | A trash view / deleted-backup list (deliberately never built) |

---

## Existing code this plan builds on

- **`src/WaveLinkBackup.Core/Snapshots/SnapshotStore.cs`** — `EmptyTrash` at line 266 hands contents to the Recycle Bin via `SHFileOperation` + `FOF_ALLOWUNDO`, degrading to permanent deletion where Windows keeps no Recycle Bin. Delete is a plain directory move into `.trash` (already in Core).
- **`src/WaveLinkBackup.App/Views/MainWindow.xaml.cs`** — the Rename and Delete placeholders to replace; the list, search field, status strip, and bottom bar whose counts must ignore `.trash`.
- **`src/WaveLinkBackup.App/ViewModels/ShellViewModel.cs`** — owns selection and the bottom-bar commands.

---

## Task 1 — Rename in place (pure validation first)

Rename is free text with no validation beyond non-empty and filesystem-safe. Keep that rule pure and tested before touching the view.

- [ ] **Step 1:** Add `src/WaveLinkBackup.App/ViewModels/RenameRules.cs` with `Validate(string name)` returning a result: empty/whitespace → invalid; contains any of `\ / : * ? " < > |` → invalid (filesystem-safe); otherwise valid. No trimming beyond rejecting pure whitespace.
- [ ] **Step 2:** Unit tests (`RenameRulesTests.cs`): empty, whitespace-only, each illegal character, a valid name, a name with spaces and dots (valid). Commit: `feat(app): rename validation rules`.

## Task 2 — Wire rename to the store + commit on Enter/blur, cancel on Escape

- [ ] **Step 1:** In `ShellViewModel`, add a `RenameCommand` that puts the selected row into an editable state exposing a draft name. Commit on Enter or blur (if valid), cancel on Escape. On commit, call the existing rename path in Core (`SnapshotStore`) and refresh the list; the row's sub-line and any delete-dialog title pick up the new name.
- [ ] **Step 2:** If validation fails, keep the field editable and show an inline cue (no toast is designed); do not commit.
- [ ] **Step 3:** Unit test the command's state transitions with a stubbed store: valid commit persists + clears edit; Escape reverts; invalid stays in edit. Commit: `feat(app): in-place rename wired to store`.

## Task 3 — Delete confirmation model (pure, three variants)

The dialog never says "Recycle Bin" (05 §"Why the dialog never says Recycle Bin"). Its variant depends on facts about the selected backup and the rest of the list.

- [ ] **Step 1:** Add `src/WaveLinkBackup.App/ViewModels/DeleteDialogModel.cs` with:
  - `Title` (`Delete "X"?`), `Body`, and an optional context block (label + body).
  - A `Variant` enum: `Normal`, `OnlyBackup`, `PreRestore`.
  - `MetaLine` (mono): size · taken datetime · trigger.
- [ ] **Step 2:** Add `DeleteDialogModel.Build(Snapshot selected, int totalBackups)`:
  - `totalBackups == 1` → `OnlyBackup`: body "It moves to the trash in your backup folder. It is the only backup you have." + block label "WHAT YOU'D BE LEFT WITH" and the Wave-Link-copies body. Neutral, **not** amber (10-decisions.md §2).
  - `selected.Trigger == PreRestore` → `PreRestore`: normal body + block label "WHAT THIS ONE IS", body naming when it was taken and that it is the way back from that restore. No colour.
  - else → `Normal`: body "It moves to the trash in your backup folder and stops showing in the list. Your other N backups aren't affected." (N = totalBackups − 1).
- [ ] **Step 3:** Unit tests (`DeleteDialogModelTests.cs`) pinning all three variants' exact copy, the meta line format, and that the OnlyBackup variant is neutral (no amber flag) while PreRestore carries its block. Commit: `feat(app): delete confirmation model, three variants`.

## Task 4 — Delete dialog view (XAML), 480px

- [ ] **Step 1:** Add `src/WaveLinkBackup.App/Views/DeleteDialog.xaml(.cs)`: 480px wide, centred over a full-window scrim below the caption bar, window behind at 50% opacity. Title (Rubik 500 20px), one-sentence body (Rubik 400 14px/1.55), the context block when present (`--wl-sunken` fill, 1px `--wl-line`, radius 8, mono label + Rubik body), meta line, footer on `--wl-bg` with a 1px `--wl-line` top: Cancel (1px `--wl-line2`) + Delete (`--wl-danger` fill, #FFFFFF text, Rubik 500 13.5px).
- [ ] **Step 2:** Escape = Cancel; focus starts on Cancel. The OnlyBackup variant additionally renders the ghost "Back up now instead" pinned left of the footer (per 05 §2).
- [ ] **Step 3:** Verify both themes via a visual-QA pass (`visual-qa` skill). Commit: `feat(app): delete confirmation dialog view`.

## Task 5 — Wire Delete to the two-stage move

- [ ] **Step 1:** In `ShellViewModel`, replace the Delete placeholder with: build `DeleteDialogModel`, show the dialog; on confirm, call the existing Core delete (plain move into `.trash`), then refresh the list. The deleted row disappears from the list, search, and every count/size readout immediately.
- [ ] **Step 2:** Confirm `.trash` is excluded from: the list, the search index, the status-strip counts, the bottom-bar total size, and the keep-count (05 §".trash must be invisible to"). A folder containing only `.trash` still reads as a valid backup folder.
- [ ] **Step 3:** Unit test: after a delete the selected id clears, counts drop by one, and the trash is not counted. Commit: `feat(app): wire two-stage delete, hide .trash from all readouts`.

## Task 6 — Empty-trash row (inside Settings' folder section)

The row belongs beside WHERE BACKUPS ARE KEPT because it is a fact about that folder's volume. It stays visible even when empty (08 §"Empty").

- [ ] **Step 1:** Add `src/WaveLinkBackup.App/ViewModels/TrashRowModel.cs` with three states: `HasItems`, `Empty`, and a `VolumeKind` (`LocalRecycleBin`, `NoRecycleBin`). Expose Title, Description (volume-dependent), MonoLine (size + `.trash` path), and whether the action needs confirmation.
- [ ] **Step 2:** Add per-volume detection: use `GetDriveType` + a UNC check on the backup folder's volume; re-detect when the folder changes. If detection fails, treat as `NoRecycleBin` (confirm). Never assume.
- [ ] **Step 3:** Unit tests (`TrashRowModelTests.cs`): local drive with items → "hands them to the Windows Recycle Bin", no confirmation; network/removable with items → "deletes them for good", confirmation required; empty → action at 40% opacity, not interactive, row still present. Commit: `feat(app): empty-trash row model + per-volume detection`.

## Task 7 — Empty-trash action + confirmation (only where irreversible)

- [ ] **Step 1:** Local drive: "Empty trash" runs immediately with **no** confirmation, calling the existing `SnapshotStore.EmptyTrash` (Recycle Bin path).
- [ ] **Step 2:** Network/removable: open a 480px confirmation in the delete-dialog shape, focus on Cancel — Title "Empty the trash?", body naming the count and that Windows can't keep them, meta line, footer Cancel + "Delete N backups" (`--wl-danger` fill). On confirm, call `SnapshotStore.EmptyTrash`.
- [ ] **Step 3:** After emptying, refresh the row to the `Empty` state. Unit test the branch: local → no dialog; non-local → dialog required. Commit: `feat(app): empty-trash action + confirmation where irreversible`.

## Task 8 — Keyboard, focus, SR parity + full verification

- [ ] **Step 1:** Rename: Enter commits, Escape cancels, Tab moves out (blur commits). Delete/Empty-trash dialogs: Escape = Cancel, focus starts on Cancel, focus returns to the list on close.
- [ ] **Step 2:** `AutomationProperties.Name` on the trash row's action and each dialog button; the trash row announces its state.
- [ ] **Step 3:** Keyboard-only pass (Tab/Enter/Escape) across rename + both dialogs. Commit: `feat(app): delete/rename/trash keyboard + SR parity`.
- [ ] **Step 4:** `dotnet build` — 0 warnings, 0 errors. Full suite green (764 + new tests). Commit: `test(app): delete/rename/trash guards + full verification`.

---

## Definition of done for Plan 6

- Rename commits in place on Enter/blur, cancels on Escape, validates filesystem-safe names.
- Delete shows the correct variant, moves into `.trash`, and the row vanishes from every readout.
- The trash is invisible to list/search/counts; a `.trash`-only folder still counts as valid.
- Empty trash: immediate on local drives, confirmed only where the Recycle Bin can't catch it; the Recycle Bin is named in exactly one place (the trash row).
- New tests green; full suite green; build clean.
