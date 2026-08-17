---
title: "Phase 5 Shell — Design"
status: published
created: 2026-08-17
updated: 2026-08-17
related_adrs: [ADR-004, ADR-005, ADR-008]
tags: [plan, design, shell, wpf, tray]
---

# Phase 5 Shell — Design

Covers the tray shell, theming, settings persistence, the backup list (screen 1) and the
Settings dialog. Everything in [phase-5-wpf.md](../dev-phases/phase-5-wpf.md) **except** the
restore confirmation, the delete dialogs, first run, the twelve errors, the restore-outcome
strip and the in-progress states — those keep their own session against a shell that already
works.

**Read first:** [screens/01-tokens-and-mapping.md](../operations/design/screens/01-tokens-and-mapping.md).
**Do not re-derive:** [screens/10-decisions.md](../operations/design/screens/10-decisions.md)
closes every open question, and [CHANGES-SINCE-V1.md](../operations/design/CHANGES-SINCE-V1.md)
§3 lists what the v1 handoff got wrong.

## Why this grouping

The app's shape is *"configured once, then ignored"*, so the process — not the window — is the
app. That single sentence decides the lifetime model, and the lifetime model has to exist
before any screen can be hung off it. Theming has to exist before any screen can be drawn
without colour literals. Persistence has to exist before any toggle can be honest. So the four
land together, and the list and the dialog are what prove they work.

## A · Shape and lifetime

Three long-lived things — settings, a backup host, a tray presenter. Windows are transient
views onto them, and `App` outlives all of them.

### Startup

`App.OnStartup`, in order:

1. Parse `ShellArguments` — `--tray`, `--store`, `--settings`, `--keep`. **Flags apply to this
   run and are never written back**, per `08-settings-persistence.md`.
2. Single-instance gate: a `Mutex` at `Local\WaveLinkBackup.instance`. Not first → signal the
   activation event and exit **before any Core object is constructed**.
3. `ShutdownMode = OnExplicitShutdown`, set before anything exists that could close.
4. Load `settings.json`; overlay flags.
5. Compose Core: `SettingsInspector` → `SnapshotStore` → `BackupService` →
   `FileSystemSettingsWatcher` → `AutoBackupCoordinator`.
6. `BackupHost.Start()` — a `DispatcherTimer` at 15s calling `Tick()`.
7. Tray icon up.
8. `--tray` ? nothing : show `MainWindow`.

`Local\`, not `Global\`: settings and the store are per-user, so two users on one machine
should each get an instance. The race the mutex prevents is two watchers over *one* user's
settings file.

### Shutdown — three entrances, one exit

| Entrance | Trigger |
|---|---|
| Tray *Quit — stops backing up* | Menu item |
| Window close | Only when `ClosingHidesToTray` is off |
| `Application.SessionEnding` | Windows logoff, restart, shutdown |

All three route through one method: `host.Stop()` → `CaptureOnShutdown()` → dispose tray →
`Shutdown()`.

**`SessionEnding` is not optional.** `CaptureOnShutdown` exists because the original incident
happened during an update, while the machine was restarting. A shell that only captures on a
deliberate Quit misses the exact case the method was written for. The CLI gets this right
through `Ctrl+C`; the shell gets it right here or not at all.

### Single instance and activation

`Mutex` detects; a named `EventWaitHandle` activates. No IPC payload is needed because the only
message is *"show yourself"*. Two named events, so a second launch carrying `--tray` exits
silently instead of forcing a window open that the user did not ask for.

### Hide on close

`ClosingHidesToTray` on (default) → `e.Cancel = true; Hide()`. Off → the close routes to the
shutdown path above, including the shutdown capture. That is coherent rather than dangerous:
the user turned the behaviour off deliberately in Settings, and the setting's own description
says automatic backups only happen while the app is running.

### Project layout

```
Startup/    ShellArguments · SingleInstance
Hosting/    BackupHost · TrayState · ShellState
Windows/    IAutostart·RunKeyAutostart · ISystemTheme·UiSettingsTheme · MicaChrome
Theming/    Dark.xaml · Light.xaml · HighContrast.xaml · ThemeManager
ViewModels/ Shell · SnapshotList · SnapshotRow · Settings · TrayMenu
Views/      MainWindow · SettingsWindow · TrayIcon
```

### Target framework

`WaveLinkBackup.App` moves to **`net10.0-windows10.0.19041.0`**.
`01-tokens-and-mapping.md` names `UISettings.GetColorValue(UIColorType.Accent)`, and
`UISettings.ColorValuesChanged` is what makes live theme following an event rather than a poll.
That WinRT projection needs an OS-versioned TFM.

`Core` stays `net10.0`. `GuardNoDesktopFramework` is untouched and still meaningful.

## B · The Windows seams

Four, each an interface with a real implementation and a fake, so the logic is testable without
a desktop.

### IAutostart — three states, not two

```
HKCU\Software\Microsoft\Windows\CurrentVersion\Run
  WaveLinkBackup = "<exe>" --tray
```

The veto lives elsewhere:

```
HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run
  WaveLinkBackup = <binary; first byte marks the user's disable>
```

`Read()` returns `On` | `Off` | `BlockedByTaskManager`. `Enable()` **refuses** when blocked and
reports why, so the toggle renders the design's note rather than flipping itself back on and
losing. Per-user, never per-machine, never a scheduled task.

### ISystemTheme

Wraps `UISettings` for dark/light and accent (`ColorValuesChanged`), plus
`SystemParameters.HighContrast` and `SystemEvents.UserPreferenceChanged(Category.Color)`. One
`Changed` event feeds `ThemeManager`.

### The tray icon is generated, not shipped

`11-high-contrast.md`: *"The tray icon follows the system icon contrast."* Four static `.ico`
files cannot satisfy that — the glyph has to be drawn against whatever the taskbar currently
is. H.NotifyIcon's dynamic icon generation does this.

A **fixed GUID pinned in code**, per the library's documented gotcha: the default GUID is
derived from the executable path, so the icon's registered settings reset when the exe moves.

Implementation is **H.NotifyIcon.Wpf** — the maintained continuation of the inactive Hardcodet
library, and this repo's first production NuGet dependency. It handles taskbar restart with no
developer intervention, which is the thing the phase plan warns naive implementations miss. It
is a pure WPF control, which the design requires: the context menu has a mono readout header,
a Rubik 500 default item and an inline toggle, none of which a WinForms `ContextMenuStrip` can
render.

### Tray state is a pure function

```
(folder usable?, last TickResult.Error, paused until, auto enabled?, capture running?)
    → WATCHING | BACKING UP | NEEDS YOU | PAUSED
```

Not a stored field. That makes the tray's entire behaviour a table test with no WPF in sight.
`NEEDS YOU` is reachable only because §7.3 put `CoreError` on `TickResult`.

Automatic-backup-off and paused both render `PAUSED` — one icon state, two tooltips.

### Pause for an hour — shell-owned, zero Core change

`AutoBackupCoordinator` owns no timer and waits for a host to call `Tick()`. Pausing is the
host not calling it. Putting a pause concept into `AutoBackupPolicy` would move a UI affordance
into Core for no gain.

### SettingsRepository lives in Core

`08-settings-persistence.md`: *"a command-line flag overrides this file for that one run and
isn't saved."* That sentence is about the CLI as much as the GUI. If the file lives in the App
project, `wlbackup list` keeps ignoring the folder chosen in the GUI and the design's own
sentence is false.

Cost, stated plainly: the CLI's flag handling layers over the file, which touches its existing
tests.

Atomic write — temp file plus `File.Replace`, mirroring `SettingsWriter`. Write on change,
never on exit. A source-generated `JsonSerializerContext`, since
`JsonSerializerIsReflectionEnabledByDefault` is off.

## C · Theming and the screens

### Three dictionaries

`Dark`, `Light`, `HighContrast`, each declaring the same 20 `Wl*` brush keys from
`01-tokens-and-mapping.md`, referenced only by `DynamicResource`. `ThemeManager` swaps the
merged dictionary; nothing rebuilds.

`WlAccent` follows the OS accent. `WlAccentSoft` and `WlAccentLine` are **derived at swap
time** — 12%/32% dark, 7%/24% light — rather than authored, so the accent enters the app in
exactly one place. **`WlDanger` never follows it.**

### High contrast is in, and the reason is structural

HC is not a skin. It deletes the 3px left edge, removes every tint and fill, and turns the
row's meta line into a **verdict word** (`WHOLE` / `SUSPECT` / `DAMAGED`) inside the NAME cell.
Building screen 1's row template without it means reopening every row template, pill and slot
later.

The design encoded health in *shape* precisely so this would work. Taking the payment for that
decision while the templates are open costs less than taking it twice.

`HighContrast.xaml` maps the same 20 keys onto `SystemColors.*Brush`. Structural differences
(edge removal, verdict word, disabled as `GrayText` at full opacity rather than 40% opacity)
are template switches driven by a flag on the shell view model.

### Screen 1 — the row is the screen

`SnapshotRowViewModel` emits **exactly five slot objects, always**, padding with `Missing`. The
fixed-slot pattern is the information design, so it is structural in the view model rather than
an accident of a template.

The damaged row's single dotted `CONTENTS UNKNOWN` cell is the one deliberate break, so the
INPUTS cell is a template-switched `ContentControl`, not an `ItemsControl` in an odd state.

> **Trap.** README §Screen 1 still specifies the SUSPECT pill in `--wl-accent-soft` /
> `--wl-accent` — the red-inside-amber version that `10-decisions.md` §1 and `02` overturned.
> Anyone building from README alone reproduces the bug the design already fixed. **Amber.**

Damaged rows stay in date order. Damaged rows enable Delete and disable Rename and Restore.

### Search

Names only. The view model emits **before/match/after segments**, not a raw string, so the
`--wl-accent-soft` highlight is testable rather than hidden in a converter. Escape clears the
search when the list has focus.

### Settings — nine sections

Modal, 680px, internally scrolling, no Save button, footer `CHANGES APPLY AS YOU MAKE THEM` +
Close. Every setter writes through the repository atomically on change.

- The proportion bar is **recomputed** from enabled tiers, never hard-coded.
- WHICH WAVE LINK hides itself when one installation exists.
- Empty trash rides on Core's existing `TrashSize()`, `ListTrash()` and
  `TrashGoesToRecycleBin()` — no new Core work. Confirmation **only** where the Recycle Bin is
  absent.
- PRESETS and PLUGINS keep the `NOT BUILT YET` badge and the 40% non-interactive treatment.

### Two extrapolations, flagged rather than smuggled

**UPDATES rows that cannot work yet.** "Check now" and the weekly toggle have no mechanism
until phase 7. They get the `NOT BUILT YET` badge and the 40% treatment from `08` — this
repo's established answer to *"visible on purpose, never silently non-functional"*. The design
does not say to do this here; it is the consistent choice, not a specified one.

**Shell-only state gets its own file.** `08` enumerates `settings.json` as folder / auto
switch / keep-count / chosen installation, and shows it at 1 KB. Two things the shell needs are
absent from that list: **window geometry** (README: remembered between runs) and
**`ClosingHidesToTray`**. Both go in an App-owned file beside it.

This keeps `settings.json` matching its own on-screen description, and keeps `BackupSettings`
free of concepts Core cannot have an opinion about — Core has no window to hide and no tray to
hide it in ([[ADR-004]]).

### Keyboard, focus and screen readers — §7.4, scoped to what is built

`10-decisions.md` §6 pins Escape, Enter, F5 and the 2px/2px focus ring. §7.4 adds Windows
conventions generally — accelerators, `Space`, `Home`/`End`, `Shift+F10`, `Ctrl+F`, `Delete` —
and is explicit that screen-reader labels are part of this, not a follow-up: the five-slot
strip reads as five unlabelled cells without an `AutomationProperties` name.

Applied to screen 1, the Settings dialog and the tray. Retrofitting it across a list and a
nine-section dialog costs more than doing it inline.

## D · Core additions

Four, each with tests.

| Addition | Notes |
|---|---|
| `SettingsRepository` | Atomic write, source-generated JSON context. Read by the CLI too. |
| `BackupSettings` + `ChosenWaveLinkPath` | **Not** the tier toggles — the existing comment is right, those are phase 6 and would be settings nothing reads. **Not** `ClosingHidesToTray` either; see below. |
| Snapshot search | Pure function over the list, names only, returns match segments. |
| Disk-free | `IFileSystem` gains free space, for `4 BACKUPS · 12.4 MB USED · 118 GB FREE`. |

## E · Testing

A new `WaveLinkBackup.App.Tests`, following the existing xunit.v3 + coverlet setup.

**What earns a test**

- The tray-state function, as a table.
- Autostart's three states against a fake registry — including that `Enable()` refuses when
  Task Manager has vetoed.
- Every Settings setter writing through to disk.
- Search segments.
- Row view model: five-always-five, the damaged break, amber suspect, damaged rows staying in
  date order.
- Two guards in particular:
  - **`WlDanger` does not move when the accent changes.** Two reds in one window is a bug the
    design calls out explicitly, which makes it an assertion rather than a note.
  - **A XAML source scan for `#RRGGBB` outside the theme dictionaries**, mirroring Core's
    `SourceGuardTests`. That is what keeps the no-literals rule true in six months rather than
    only today.

**Not tested:** XAML layout, Mica, real registry writes.

**Unknown to resolve first, not last.** Resource-dictionary assertions need an STA thread, and
whether xunit.v3 ships an STA fact attribute is unconfirmed. If it does not, the fallback is
running those assertions on a manually created STA thread. Confirm in the first hour.

## Out of scope

| Deferred | To |
|---|---|
| Restore confirmation, delete dialogs, first run, the twelve errors, restore outcomes, in-progress states | A later phase 5 session |
| The two toast notifications | Phase 7 |
| Update mechanics beyond the static row | Phase 7 |
| Tier 2–4 capture | Phase 6 |

## Risks

| Risk | Signal | Response |
|---|---|---|
| The session is four chunks in one | Losing the thread mid-build | Execute in staged checkpoints, not one pass |
| Building screen 1 from README alone | A red SUSPECT pill | `02` and `10-decisions.md` §1 override README |
| Colour literals creeping into XAML | Any `#RRGGBB` outside a theme dictionary | The source-scan guard test |
| Logic migrating into view models | A view model computing what a restore changes | `RestorePlan` already exists |
| CLI regressions from the shared settings file | CLI tests failing on flag precedence | Flags win for one run; that is the pinned behaviour |

## References

- [phase-5-wpf.md](../dev-phases/phase-5-wpf.md) · [technical-debt.md](../technical-debt.md) §7
- [screens/00-index.md](../operations/design/screens/00-index.md) — the whole package, v5
- [[ADR-004]] thin shells · [[ADR-005]] WPF · [[ADR-008]] Windows-only
