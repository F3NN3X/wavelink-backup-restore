# Plan 8 — The Settings dialog

**Phase:** 5 · WPF shell, part 8 of the phase (the last surface).
**Design source:** `_docs/operations/design/README.md` Screen 3 (base settings layout) + `_docs/operations/design/screens/08-settings-persistence.md` (WHERE THESE SETTINGS LIVE, WHICH WAVE LINK, the empty-trash row, unbuilt tiers, error-12 missing folder). The trash row's *behaviour* is implemented in Plan 6; this plan hosts it inside the dialog.
**Depends on:** Plan 4 shipped (`136bed7`) — the gear button in the status strip exists and opens a placeholder. Plan 6 supplies `TrashRowModel` + the empty-trash action. Plan 7 supplies the error-12 full screen this dialog's folder section can fall back to.
**Goal:** Replace the Settings placeholder with the real 680px modal: every control commits immediately (no Save button), settings persist atomically to `%LOCALAPPDATA%\WaveLinkBackup\settings.json`, and the two new sections (WHERE THESE SETTINGS LIVE, WHICH WAVE LINK) render per spec.

---

## What this plan does and does not do

| In scope | Out of scope (other plans / phases) |
| --- | --- |
| 680px modal, internal scroll when it exceeds the window | The trash row's *behaviour* (Plan 6) — this plan hosts `TrashRowModel` |
| WHERE BACKUPS ARE KEPT (read-only path + Change folder…/Open) | Error 12 full screen (Plan 7) — the folder section falls back to it |
| WHEN TO BACK UP (auto-backup toggle + keep-count stepper) | The restore/delete flows (Plans 5–6) |
| WHAT GOES IN A BACKUP (four rows, proportion bar, two notes) | Any new Core settings logic — reuse `SettingsReader`/`SettingsWriter` |
| Unbuilt tiers (PRESETS/PLUGINS) shown off + unmovable | Autostart / tray / update mechanics (deliberately out of scope) |
| WHICH WAVE LINK section (hidden when one install) | The "Wave Link not found" chooser flow beyond persisting the choice |
| WHERE THESE SETTINGS LIVE section | A Save button — there is none; every control commits on change |

---

## Existing code this plan builds on

- **`src/WaveLinkBackup.Core`** — `SettingsReader` / `SettingsWriter` already read/write the settings file. The writer must write atomically (temp file + replace) and **on change, never on exit**; a command-line flag overrides the file for that one run and is never written back (08 §"Where these settings live").
- **`src/WaveLinkBackup.App/ViewModels/TrashRowModel.cs`** — from Plan 6; hosted under WHERE BACKUPS ARE KEPT.
- **`src/WaveLinkBackup.App/Views/MainWindow.xaml(.cs)`** — the gear button (36 × 36 ghost) in the status strip that opens Settings.
- **`src/WaveLinkBackup.App/ViewModels/ShellViewModel.cs`** — exposes the settings state and the detected Wave Link installation(s).

---

## Task 1 — Settings view-model (in-place commit)

The dialog has no Save button; every control commits immediately on change. Model that as a set of bindable properties each of which writes through on change.

- [ ] **Step 1:** Add `src/WaveLinkBackup.App/ViewModels/SettingsViewModel.cs` exposing: `BackupFolder` (read-only display), `AutoBackupEnabled` (bool), `AutoBackupKeepCount` (int, stepper), `IncludePresets`, `IncludePluginFiles` (both bound but **locked** — see Task 5), and derived `EstimatedBackupBytes`. Each settable property writes through to `SettingsWriter` on change (atomic).
- [ ] **Step 2:** Add the two read-only section models: `WhereSettingsLiveModel` (the file path + size + the "a command-line flag overrides this file for that one run" line) and `WhichWaveLinkModel` (version, ellipsised path, "CHOSEN …" date, Change… action, and the note). `WhichWaveLinkModel.Visible` is false when only one installation exists.
- [ ] **Step 3:** Unit tests (`SettingsViewModelTests.cs`): toggling auto-backup writes the file immediately (no explicit save call); changing the keep count writes; a CLI-flag override for the process does not get written back; `WhichWaveLinkModel.Visible` is false for one install, true for two. Commit: `feat(app): settings view-model with in-place commit`.

## Task 2 — WHERE BACKUPS ARE KEPT + WHEN TO BACK UP sections

- [ ] **Step 1:** Build the WHERE BACKUPS ARE KEPT section: read-only path field (`--wl-sunken`, hairline, 8px radius, 15px folder icon + mono 12.5px ellipsised path) + Change folder… (secondary) + Open (ghost); below, mono 11.5px "N BACKUPS · X MB USED · Y GB FREE ON THIS DRIVE". Host the Plan-6 trash row directly under it.
- [ ] **Step 2:** Build WHEN TO BACK UP: two `--wl-bg` hairline rows — the auto-backup toggle (on by default) with its description, and the keep-count stepper (− / value / +, 32 × 34 segments, hairline-24% border, mono value) with "Backups you took yourself are never deleted."
- [ ] **Step 3:** Change folder… opens a folder picker; on pick, write the new folder through and re-detect the trash row's volume (Plan 6 re-detects on folder change). Commit: `feat(app): settings — folder + when-to-back-up sections`.

## Task 3 — WHAT GOES IN A BACKUP section (proportion bar + notes)

- [ ] **Step 1:** Build the bordered group, four rows on a `1fr 84px 52px` grid: Your setup (locked on), A list of your effects (locked on), Effect presets (on), The effect plug-ins themselves (off). Sizes are honest and right-aligned in mono 12px.
- [ ] **Step 2:** Below the group, the 6px stacked proportion bar on `--wl-sunken` — **recompute it from the enabled tiers, don't hard-code the percentages** — with "EACH BACKUP: ABOUT X MB" left and "+ Y MB IF YOU ADD THE PLUG-IN FILES" right.
- [ ] **Step 3:** The two plain-language notes in one `--wl-bg` block (Licences are never included / A backup describes this computer), lead clause in `--wl-strong`.
- [ ] **Step 4:** Unit test the proportion bar: enabling/disabling a tier changes the computed widths; the locked rows can't be toggled. Commit: `feat(app): settings — what-goes-in section + computed proportion bar`.

## Task 4 — WHICH WAVE LINK + WHERE THESE SETTINGS LIVE sections

- [ ] **Step 1:** Build WHICH WAVE LINK (08 §"WHICH WAVE LINK"): the `--wl-bg` row with version (Rubik 400 14px strong), ellipsised path, "CHOSEN …" date, Change… secondary, and the note. Hide the whole section when only one installation exists. Choosing a different install persists it (this is what error 2 in Plan 7 resolves to).
- [ ] **Step 2:** Build WHERE THESE SETTINGS LIVE (08 §"Where these settings live"): the `--wl-bg` block with the "On this computer, for this account." body, the mono file path + size, and the mono 80% "A COMMAND-LINE FLAG OVERRIDES THIS FILE FOR THAT ONE RUN AND ISN'T SAVED" line.
- [ ] **Step 3:** Unit test: the section visibility rule; choosing an install persists and survives a reload. Commit: `feat(app): settings — which-wave-link + where-settings-live sections`.

## Task 5 — Unbuilt tiers (honest, not hidden)

PRESETS and PLUGINS stay on screen, off, and unmovable (08 §"Unbuilt tiers").

- [ ] **Step 1:** Render the "NOT BUILT YET" badge (transparent, 1px `--wl-line2`, radius 4, mono 500 10px ls .12em, `--wl-muted`) on both rows; the toggle in off treatment at 40% opacity, not interactive; the size figure also at 40%.
- [ ] **Step 2:** The footnote: "Both switches are off and can't be moved yet. They stay on screen because hiding them would make the backup look more complete than it is."
- [ ] **Step 3:** Unit test: the two toggles report disabled and a programmatic set is rejected (they cannot be moved). Commit: `feat(app): settings — unbuilt tiers locked + labelled`.

## Task 6 — Dialog chrome, footer, scroll, both themes

- [ ] **Step 1:** Build `src/WaveLinkBackup.App/Views/SettingsDialog.xaml(.cs)`: 680px wide, same surface treatment as the other modals; header "Settings" (dialog title) + a 30 × 30 close button; body padding `0 24px 20px`, 22px between sections; **scrolls internally when it exceeds the window height**.
- [ ] **Step 2:** Footer: "CHANGES APPLY AS YOU MAKE THEM" (mono 11.5px `--wl-muted`) left, Close (primary) right. **No Save button.** Escape and Close both dismiss.
- [ ] **Step 3:** Verify both themes + the internal-scroll case (tall window content) via a visual-QA pass (`visual-qa` skill). Commit: `feat(app): settings dialog chrome + footer + scroll`.

## Task 7 — Keyboard, focus, SR parity + full verification

- [ ] **Step 1:** Every control is reachable by Tab; toggles/stepper respond to keyboard; Escape and Close dismiss with focus returning to the list.
- [ ] **Step 2:** `AutomationProperties.Name` on each control and section; the unbuilt-tier toggles announce as disabled.
- [ ] **Step 3:** Keyboard-only pass through every section. Commit: `feat(app): settings keyboard + SR parity`.
- [ ] **Step 4:** `dotnet build` — 0 warnings, 0 errors. Full suite green (764 + all new tests from Plans 5–8). Commit: `test(app): settings guards + full verification`.

---

## Definition of done for Plan 8

- The gear button opens the real 680px Settings modal; every control commits immediately and there is no Save button.
- Settings persist atomically to `%LOCALAPPDATA%\WaveLinkBackup\settings.json` on change, never on exit; a CLI flag overrides for one run without being written back.
- WHERE THESE SETTINGS LIVE and WHICH WAVE LINK render per spec (the latter hidden when one install).
- Unbuilt tiers are shown off + unmovable with the NOT BUILT YET badge and footnote.
- The proportion bar is computed from enabled tiers, not hard-coded.
- New tests green; full suite green; build clean.
