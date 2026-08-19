---
title: "Phase 5 Plan 9 — The tray shell"
status: completed
created: 2026-08-18
updated: 2026-08-19
related_adrs: [ADR-004, ADR-005]
tags: [plan, implementation, app, wpf, tray, autostart, phase-5]
---

# Phase 5 Plan 9 — The Tray Shell Implementation Plan

**Goal:** Finish the tray shell so the app is a *tray app with a window* — the icon states,
the context menu as the primary interface, hide-on-close, single-instance and autostart all
working end to end — **and make the tray icon the app icon**, so the shield-check mark appears
in the taskbar button, Alt-Tab and the Start list, not only in the notification area.

**Architecture:** Most of this plan is *wiring and verification*, not new machinery. The tray's
behaviour is already a pure function (`TrayState`), its icon is already rendered from the theme
brushes (`TrayIconRenderer`), single-instance exists (`SingleInstance`), autostart exists
(`RunKeyAutostart`) and the context menu is already built and wired in `App.xaml.cs`. What does
not exist yet is: (1) a real **app icon asset** — there is no `.ico` anywhere under `src/`, so
the window's caption button, Alt-Tab and the taskbar show the WPF default, and (2) the pieces
that make the *shell* behave as designed — a second launch activating the first instance, the
autostart toggle surfaced in Settings with the Task Manager veto, and the icon state being
driven by the live host on every tick rather than only after a manual capture. This plan
extends the existing code; it does not rebuild it.

**Tech Stack:** C# / .NET 10, WPF, `H.NotifyIcon.Wpf` 2.3.0 (the tray), `System.Drawing.Icon`,
xunit.v3.

**Spec:** [screens/12-tray-autostart-update.md](../operations/design/screens/12-tray-autostart-update.md) ·
[screens/11-high-contrast.md](../operations/design/screens/11-high-contrast.md) (the tray's HC
icon rule) · [README §Interactions](../operations/design/README.md)

> **Executed 2026-08-19.** All five tasks complete: `ccca041` (Task 1, app icon asset),
> `6a574c0` (Task 2, second-launch activation), `5e79975` (Task 3, autostart toggle with Task
> Manager veto), `ee8703f` (Task 4, tray tracks the live host on every tick), `f801e5b`
> (Task 5, hide-on-close + context menu verified against the spec). Suite at **959 green**
> (296 Core, 91 CLI, 572 App), Release zero warnings. One deviation worth recording: the
> window's caption glyph is rendered from geometry in code (`AppCaptionGlyph`) rather than set
> as `Window.Icon="app.ico"` — a WPF resource-pack URI for an `<ApplicationIcon>`-only asset
> fails at runtime (dotnet/wpf#209), and the exe/taskbar/Alt-Tab icon via `<ApplicationIcon>`
> works fine. The hide branch of `OnClosing` is manual-verify-only: the test harness's shared
> bare `Application` occupies WPF's one-per-AppDomain slot, so `new App()` throws
> `InvalidOperationException`; documented in `MainWindowClosingTests.cs`.

---

## Global Constraints

- `WaveLinkBackup.Core` stays **`net10.0`**. **Nothing in this plan touches Core.**
- `TreatWarningsAsErrors` is on, repo-wide.
- **No colour literals outside `Theming/*.xaml`.** The tray icon's colours come from the theme
  brushes (`WlWarn`, `WlMuted`, `WlText`) or `SystemColors.*` in high contrast — never a hex in
  C#. `ThemeTests.No_xaml_outside_the_theme_dictionaries_contains_a_colour_literal` still scans.
- **The tray icon and the app icon are ONE asset.** The static `.ico` (the WATCHING mark, the
  shield + check) is the `<ApplicationIcon>` for the exe *and* the fallback the window's caption
  shows. The four live states are still rendered at runtime by `TrayIconRenderer`; the static
  asset only covers "the app as an object" (taskbar, Alt-Tab, file properties).
- **High contrast outranks dark/light.** In HC the icon uses `WindowText`/`GrayText` at full
  opacity — never the 55% PAUSED alpha of the normal themes (`screens/11`, and already encoded
  in `TrayIconRenderer.ColourFor`). This plan must not regress that.
- **No count badges.** The icon is a state, never a number (`screens/12`).
- **Autostart is per-user, Run key only, never a scheduled task.** Task Manager's veto wins;
  the toggle reads off and cannot be switched on while vetoed (`RunKeyAutostart` already models
  `BlockedByTaskManager`).
- Build: `dotnet build WaveLinkBackup.slnx` · Test: `dotnet test WaveLinkBackup.slnx`
- Baseline: **764 tests green** (296 Core, 91 CLI, 377 App), Release zero warnings.

## What this plan does and does not do

| In scope | Out of scope (and where it lives) |
|---|---|
| Author the app/tray `.ico` from the brand shield-check mark; set `<ApplicationIcon>`; wire `Window.Icon` | Toast notifications — deferred to **phase 7** (needs a Windows API WPF lacks) |
| A second launch activates the first instance (`--tray` exits silently) | The update mechanism — deferred to **phase 7**; only the static `UPDATES` section is built in plan 8 |
| Autostart toggle surfaced in Settings, with the Task Manager veto state | The two designed toast notifications (`screens/12`) — phase 7 |
| Icon state driven by the live host on every tick (not just after a capture) | Redesigning `TrayState` / `TrayIconRenderer` — they are correct as-is; this plan only feeds them |
| Verifying hide-on-close, single-instance and the context menu against the spec | The Settings *dialog* itself — that is **plan 8**; this plan only adds the autostart rows it owns |

## Existing code this plan builds on

Read these before starting. Each already exists and is tested; extend, don't fork.

- `src/WaveLinkBackup.App/App.xaml.cs` — WPF entry point. Holds the pinned `TrayIconId` GUID
  (`2f8b6f4e-9d3a-4c17-9b52-6a1d4f0e7c38`, fixed so H.NotifyIcon's path-derived id does not
  reset it), the `SingleInstance` field, `RefreshTray()`, `WireMenu()`, `ShowMainWindow()`,
  `SaveGeometry()` and the theme-changed rebuild. **This is where most wiring lands.**
- `src/WaveLinkBackup.App/Hosting/TrayState.cs` — `TrayStatus` (Watching/BackingUp/NeedsYou/
  Paused), `TrayConditions`, `From(conditions)` and `Tooltip(...)`. Pure, fully tested. The
  icon's four states are decided here.
- `src/WaveLinkBackup.App/Views/TrayIconRenderer.cs` — `Render(status, colour, pixelSize)`
  draws the shield + mark to a `RenderTargetBitmap`, wraps it in a one-entry PNG-compressed ICO;
  `ColourFor(status, highContrast)` resolves the brush (and the HC full-opacity rule). Tested.
- `src/WaveLinkBackup.App/Startup/SingleInstance.cs` — Mutex detects, two named events
  activate (`wantsWindow`). `ActivationRequested` fires on the first instance. **No covering
  test for the App's use of it** — that is Task 2.
- `src/WaveLinkBackup.App/Startup/ShellArguments.cs` — parses `--tray`, `--store`, `--settings`,
  `--keep`. `StartInTray` decides whether a second launch asks for the window. Tested.
- `src/WaveLinkBackup.App/Windows/RunKeyAutostart.cs` + `IAutostart.cs` — writes
  `HKCU\...\Run · WaveLinkBackup --tray`, checks the Task Manager veto
  (`StartupApproved\Run`) → `AutostartState.BlockedByTaskManager`. Tested in
  `tests/WaveLinkBackup.App.Tests/AutostartTests.cs`.
- `src/WaveLinkBackup.App/Views/MainWindow.xaml(.cs)` — the window. **`Window.Icon` is not set**;
  closing hides (via `App.SaveGeometry` + `Hide()`). Task 1 sets the icon; Task 4 verifies close.
- `src/WaveLinkBackup.App/ViewModels/ShellViewModel.cs` — `Apply(ShellFacts)` refreshes the strip
  and button states; the tray's `RefreshTray` is its passive twin (both read the same host).

---

## Task 1 — The app icon asset, and `<ApplicationIcon>` + `Window.Icon`

**Why first:** every later task renders or references "the app"; until the mark exists as an
asset there is nothing to point at. This also closes the gap that the window currently shows the
WPF default glyph in the caption bar, Alt-Tab and the taskbar.

The mark is the design's `shield-check` (README §Assets: "shield-check (title bar + app icon)").
It must match what `TrayIconRenderer` already draws for WATCHING — same shield outline, same
check, 1.75px monoline on a 24px grid — so the static asset and the live icon read as one object.

- [x] **Step 1: Author `src/WaveLinkBackup.App/app.ico`.**
  Generate a multi-resolution `.ico` (16/20/24/32/40/48/64/128/256) of the shield-check mark.
  The cleanest source is the *same* geometry `TrayIconRenderer` uses (`ShieldPath` + `CheckPath`),
  rendered at each size with `WlText` on transparent — so author it once from that path rather
  than by hand, guaranteeing the asset and the runtime icon agree. The mark sits centred with a
  small even margin; no background fill (the OS composites it over any theme).
  - MUST NOT: bake in a colour that only suits one theme — the OS tints/recolors where it can,
    and a hard white or hard black shield would vanish on a matching taskbar. Keep it neutral
    (`WlText`-equivalent grey) so it reads on light and dark alike.

- [x] **Step 2: Reference it in the csproj.**
  Add to `src/WaveLinkBackup.App/WaveLinkBackup.App.csproj`, inside the existing `<PropertyGroup>`:
  ```xml
  <ApplicationIcon>app.ico</ApplicationIcon>
  ```
  This makes the exe carry the icon (file properties, taskbar, Alt-Tab). No `Resource` include is
  needed for `<ApplicationIcon>` — it is embedded by the linker, not the WPF resource pipeline.

- [x] **Step 3: Set `Window.Icon` on the main window.**
  In `src/WaveLinkBackup.App/Views/MainWindow.xaml`, add to the `<Window ...>` element:
  ```xml
  Icon="app.ico"
  ```
  The caption bar's own 14px shield-check glyph (drawn, per README §Screen 1) is separate and
  stays; `Window.Icon` is what the *OS chrome* shows.

- [x] **Step 4: Add a test that the asset exists and is a valid multi-size icon.**
  In `tests/WaveLinkBackup.App.Tests/`, add `AppIconAssetTests.cs`:
    - assert the `.ico` file is present in the App project (so a deleted asset fails the build's
      test run, not a user's first launch),
    - load it via `System.Drawing.Icon` and assert it yields at least one usable size.
  Keep it free of colour assertions — this test guards *presence and validity*, not pixels.

- [x] **Step 5: Build + verify the icon actually lands.**
  ```
  dotnet build WaveLinkBackup.slnx -c Release
  ```
  Then launch the app and confirm: the exe's file-properties icon is the shield-check; the window
  caption/Alt-Tab shows it. (Visual check — no automated test can assert the OS taskbar glyph.)

- [x] **Step 6: Commit.**
  ```
  git add src/WaveLinkBackup.App/app.ico src/WaveLinkBackup.App/WaveLinkBackup.App.csproj \
          src/WaveLinkBackup.App/Views/MainWindow.xaml \
          tests/WaveLinkBackup.App.Tests/AppIconAssetTests.cs
  git commit -m "app: give the window and exe the shield-check app icon"
  ```

---

## Task 2 — A second launch activates the first instance

**Why:** `SingleInstance` already detects a second instance and can signal it, but the App must
*act* on both sides: the first instance shows its window when asked, and the second exits without
flashing a window of its own. `--tray` launches must exit silently (`wantsWindow: false`) so an
autostart at boot never forces a window open.

- [x] **Step 1: Wire the first instance to show the window on activation.**
  In `App.xaml.cs`, after acquiring `SingleInstance` and calling `StartListening()`, subscribe:
  ```csharp
  instance.ActivationRequested += (_, _) => Dispatcher.BeginInvoke(ShowMainWindow);
  ```
  (`ShowMainWindow` already de-minimizes and activates — reuse it, don't re-show logic.)

- [x] **Step 2: Make the second instance signal then exit.**
  On startup, if `instance.IsFirst` is false: call
  `instance.SignalExistingInstance(wantsWindow: !args.StartInTray)` and shut down immediately
  (no window, no tray icon — the first instance already owns both). A plain launch (`wantsWindow:
  true`) brings the existing window forward; a `--tray` launch is a no-op to the user.

- [x] **Step 3: Add tests for the App's two-sided behaviour.**
  `SingleInstanceTests` covers the primitive (first wins, second knows, activation raises). Add a
  focused test that the *decision* is right: given `StartInTray == true`, the signal carries
  `wantsWindow: false`; given `false`, it carries `true`. Put it next to `ShellArgumentsTests`
  (it is about what a parsed launch implies), or a small `SecondLaunchTests.cs`.

- [x] **Step 4: Build + run the suite.**
  ```
  dotnet build WaveLinkBackup.slnx -c Release && dotnet test WaveLinkBackup.slnx
  ```

- [x] **Step 5: Manual verify.** Launch the app; launch it again — the first window comes to the
  front, no second tray icon appears. Then `dotnet run -- --tray` while one is running: nothing
  visible happens (silent).

- [x] **Step 6: Commit.**
  ```
  git add src/WaveLinkBackup.App/App.xaml.cs tests/WaveLinkBackup.App.Tests/
  git commit -m "app: a second launch activates the first; --tray exits silently"
  ```

---

## Task 3 — Autostart, surfaced in Settings with the Task Manager veto

**Why:** `RunKeyAutostart` already writes the Run key and models the veto. What's missing is the
*user-facing* part: the two `WHEN WINDOWS STARTS` rows from `screens/12`, and the rule that a
vetoed entry reads **off and cannot be switched on here** — Task Manager wins, and the note says
so instead of fighting it.

The Settings *dialog* is plan 8's; this task only produces what those rows bind to (the state +
the toggle action) so plan 8 can drop them in. Keep it a small, testable seam, not a full dialog.

- [x] **Step 1: Expose autostart state + toggle on the shell.**
  Add to `ShellViewModel` (or a small dedicated `AutostartState` view model if plan 8's Settings
  needs it isolated):
    - `AutostartState State { get; }` — mirrors `IAutostart`'s enum (Off / On / BlockedByTaskManager),
    - `bool CanEnableAutostart => State != AutostartState.BlockedByTaskManager`,
    - a `ToggleAutostart()` that flips via the injected `IAutostart` and re-raises.
  The veto case: `State == BlockedByTaskManager` renders as **off** (`IsChecked == false`) with
  `CanEnableAutostart == false`, so the control is disabled and a note explains Task Manager won.

- [x] **Step 2: Wire the real `RunKeyAutostart` in `App.xaml.cs`.**
  Construct it with the resolved `IRegistryKeys` and the running exe path; call its read on start
  and on every relevant refresh so `State` is live, not stale.

- [x] **Step 3: Add tests.**
    - a vetoed entry reports `BlockedByTaskManager`, renders off, and `CanEnableAutostart` is false,
    - toggling on writes the Run value with the `--tray` command line,
    - toggling off deletes it.
  Extend `AutostartTests.cs` (it already drives `RunKeyAutostart` through a fake registry) and add
  view-model tests for the render/enable rules.

- [x] **Step 4: Build + run the suite.**
  ```
  dotnet build WaveLinkBackup.slnx -c Release && dotnet test WaveLinkBackup.slnx
  ```

- [x] **Step 5: Manual verify.** Toggle on → confirm `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`
  has `WaveLinkBackup` = `"…exe" --tray`. Toggle off → it's gone. (Disable via Task Manager in a
  scratch run to confirm the veto path reads off and won't re-enable.)

- [x] **Step 6: Commit.**
  ```
  git add src/WaveLinkBackup.App/ViewModels/ src/WaveLinkBackup.App/App.xaml.cs \
          tests/WaveLinkBackup.App.Tests/
  git commit -m "app: autostart toggle with the Task Manager veto, for Settings"
  ```

---

## Task 4 — Icon state follows the live host on every tick

**Why:** `RefreshTray()` currently re-renders after a manual capture and on theme change. The
design's four states must track the *running* host continuously — BACKING UP while a capture is
in flight, NEEDS YOU the moment `LastError` is set, PAUSED when paused or auto-backup is off — so
the tray never lags the truth it already has in `host.Conditions`.

- [x] **Step 1: Drive `RefreshTray()` from the host's tick.**
  In `App.xaml.cs`, ensure the periodic refresh (the same 15s cadence that calls
  `RefreshShellFacts`) also calls `RefreshTray()`, and that any host state change (capture start/
  end, pause/resume, auto-backup toggle, error) triggers it. The icon and the status strip are two
  readouts of one state; neither may go stale while the other updates.

- [x] **Step 2: Confirm the four states are reachable and correct.**
  Walk `TrayState.From` against live conditions and assert (in a test that feeds synthetic
  `TrayConditions`) the priority: NEEDS YOU (error) > BACKING UP (capturing) > PAUSED (paused or
  auto off) > WATCHING. This is already unit-tested in `TrayStateTests`; add an integration-style
  assertion that the *App's* refresh path produces the expected `TrayStatus` for each of the four
  host situations.

- [x] **Step 3: Verify the HC icon rule still holds through the live path.**
  Confirm `ColourFor` is called with the current `IsHighContrast` on every refresh (it already is,
  via `systemTheme?.IsHighContrast ?? SystemParameters.HighContrast`) so PAUSED stays full-opacity
  `GrayText` and NEEDS YOU is `WindowText` in HC. Add a test if one does not already pin this for
  the *refresh* path specifically.

- [x] **Step 4: Build + run the suite.**
  ```
  dotnet build WaveLinkBackup.slnx -c Release && dotnet test WaveLinkBackup.slnx
  ```

- [x] **Step 5: Manual verify.** Start a backup → icon goes BACKING UP, returns to WATCHING. Pause
  for an hour → PAUSED (and the menu item flips to "Resume"). Force an error path (point at a bad
  folder) → NEEDS YOU with the naming tooltip.

- [x] **Step 6: Commit.**
  ```
  git add src/WaveLinkBackup.App/App.xaml.cs tests/WaveLinkBackup.App.Tests/
  git commit -m "app: tray icon tracks the live host on every tick"
  ```

---

## Task 5 — Hide-on-close and the context menu, verified against the spec

**Why:** Both are largely built (close hides via `App.SaveGeometry` + `Hide()`; the menu is built
and wired in `WireMenu`). This task is a *verification pass* — confirm they match `screens/12`
exactly and add the thin tests that currently don't exist, rather than re-implementing.

- [x] **Step 1: Verify hide-on-close.**
  Confirm closing the window calls `Hide()` (not `Close()`/shutdown), geometry is saved so it
  survives, and the app keeps running in the tray. Add a test in `MainWindowClosingTests.cs` if the
  hide-vs-exit distinction is not already pinned: cancel the `Closing` event, call `Hide()`, assert
  the window is hidden and the process/app stays alive.

- [x] **Step 2: Verify the context menu matches the spec item for item.**
  Against `screens/12`, confirm the menu has, in order: the `LAST BACKUP` readout header (a
  readout, not an item), then **Back up now** (default, Rubik 500), **Open Wave Link Backup**,
  **Open the backup folder**, a separator, **Back up automatically** [toggle], **Pause for an
  hour** (flips to "Resume" while paused), a separator, **Settings…**, and **Quit — stops backing
  up**. Left-click opens the window; right-click opens the menu. Add/extend `TrayMenuStyleTests.cs`
  to assert the item set, order and the Pause↔Resume header swap.

- [x] **Step 3: Verify "Quit" is the only exit and says what it stops.**
  The Quit item's label must carry the consequence ("stops backing up"), because quitting halts
  the watcher. Confirm `ShutdownEverything()` is the sole path to exit (not the window close).

- [x] **Step 4: Build + run the suite.**
  ```
  dotnet build WaveLinkBackup.slnx -c Release && dotnet test WaveLinkBackup.slnx
  ```

- [x] **Step 5: Manual verify.** Right-click the tray icon → menu matches the spec; left-click →
  window opens. Close the window → it hides, tray icon remains. Quit from the menu → process ends.

- [x] **Step 6: Commit.**
  ```
  git add src/WaveLinkBackup.App/ tests/WaveLinkBackup.App.Tests/
  git commit -m "app: verify hide-on-close and the tray context menu against the spec"
  ```

---

## Definition of done

- [x] `app.ico` exists, is set as `<ApplicationIcon>` and as `Window.Icon`; the exe, taskbar and
      Alt-Tab show the shield-check mark (Task 1).
- [x] A second launch activates the first instance; a `--tray` launch exits silently (Task 2).
- [x] The autostart toggle is live in the shell, writes/deletes the Run key with `--tray`, and a
      Task Manager-vetoed entry reads off and cannot be enabled here (Task 3).
- [x] The tray icon reflects the live host state on every tick — all four states reachable and
      correct, HC rule intact (Task 4).
- [x] Hide-on-close and the context menu verified item-for-item against `screens/12`, with tests
      pinning hide-vs-exit and the menu set (Task 5).
- [x] `dotnet build WaveLinkBackup.slnx -c Release` is zero-warning; `dotnet test WaveLinkBackup.slnx`
      is green (baseline 764, plus this plan's additions).
- [x] No Core changes; no colour literals outside `Theming/*.xaml`; no count badges.

## Deferred to phase 7 (do not build here)

- The two toast notifications (`screens/12`) — a Windows API WPF does not provide.
- The update mechanism (check / download / install / restart) and its live rows — plan 8 builds
  only the static `UPDATES` section; error 8 deep-links into it.
