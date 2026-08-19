---
title: "Session: Phase 5 plan 9 — the tray shell"
status: published
created: 2026-08-19
updated: 2026-08-19
related_adrs: [ADR-004, ADR-005]
tags: [session, app, wpf, tray, autostart, phase-5]
---

# Session: Phase 5 plan 9 — the tray shell

**Date:** 2026-08-19

**959 tests green** (296 Core, 91 CLI, **572 App**), build clean with zero warnings. The app is
now a *tray app with a window* end to end: the shield-check mark appears in the taskbar button,
Alt-Tab and the Start list as well as the notification area; a second launch activates the first
instance instead of starting a watcher twice; autostart is surfaced in Settings with the Task
Manager veto; and the tray icon tracks the live host on every tick rather than only after a
manual capture.

Executed: [screens/12-tray-autostart-update.md](../operations/design/screens/12-tray-autostart-update.md)
(the four icon states, the context menu as primary interface, hide-on-close, single-instance,
autostart with veto) and the tray's half of
[screens/11-high-contrast.md](../operations/design/screens/11-high-contrast.md) (the HC icon rule —
full-opacity `GrayText`/`WindowText`, never the 55% PAUSED alpha).

## What shipped

**One asset, two jobs.** The shield-check mark is authored once from the *same* geometry
`TrayIconRenderer` already draws (`ShieldPath` + `CheckPath`) so the static asset and the four
live states read as one object. It is the exe's `<ApplicationIcon>` (file properties, taskbar,
Alt-Tab) — but **not** `Window.Icon`. A WPF resource-pack URI for an `<ApplicationIcon>`-only
asset fails at runtime (dotnet/wpf#209), so the window's caption glyph is rendered from geometry
in code (`AppCaptionGlyph.Render()`) instead. The exe icon via the linker works fine; only the
WPF resource pipeline chokes.

**Second launch activates, never duplicates.** `SingleInstance` already detected a second
instance and could signal it; this plan made the App *act* on both sides. The first instance
subscribes `ActivationRequested → Dispatcher.BeginInvoke(ShowMainWindow)`; the second signals
(`wantsWindow: !args.StartInTray`) and exits with no window of its own. A plain launch brings the
existing window forward; a `--tray` launch (autostart at boot) is a silent no-op to the user.

**Autostart with the Task Manager veto.** The toggle lives in the shell view model, reads live off
`RunKeyAutostart`, and models the veto: a Task-Manager-disabled entry reports
`BlockedByTaskManager`, renders **off**, and `CanEnableAutostart` is false — so the control is
disabled and a note says Task Manager won, instead of fighting it. Toggling on writes
`HKCU\...\Run · WaveLinkBackup = "…exe" --tray`; off deletes it.

**The tray icon follows the live host.** `timer.Tick` calls `host.Tick()` then `RefreshTray()`, so
the four states (NEEDS YOU > BACKING UP > PAUSED > WATCHING) track the running host continuously —
BACKING UP while a capture is in flight, NEEDS YOU the moment `LastError` is set, PAUSED when
paused or auto-backup is off. The icon and the status strip are two readouts of one state; neither
goes stale while the other updates. The HC rule holds through the live path: `ColourFor` receives
the current `IsHighContrast` on every refresh.

**The context menu pinned item-for-item.** Beyond order and checkability (already tested), the
menu is now pinned against `screens/12` for its two load-bearing labels: **Quit — stops backing up**
(the consequence rides on the label, not a confirmation dialog) and **Pause for an hour** (the
designed starting label; `RefreshTray` rewrites it to "Resume" while paused).

## What broke, and what it taught

**A `<ApplicationIcon>`-only asset cannot also be a WPF resource.** Setting `Window.Icon="app.ico"`
in XAML — and the code-behind `BitmapImage(pack://application:,,,/app.ico)` fallback — both threw
at runtime. The `.ico` is embedded by the linker, not the WPF resource pipeline, so no pack URI
resolves to it. The fix renders the caption glyph from geometry in code; the exe/taskbar/Alt-Tab
icon keeps working via `<ApplicationIcon>`.

**The hide branch of `OnClosing` cannot be unit-tested here.** It needs a real `App` installed as
`Application.Current`, but WPF allows exactly one `Application` per AppDomain and the test harness's
shared bare `Application` occupies the slot — `new App()` throws `InvalidOperationException`
(confirmed with a throwaway probe; `Application.Current` is not settable). The hide-vs-exit
behaviour is therefore a look-at-it item, same class of exclusion as the DWM interop and unshown-
window geometry already documented in `MainWindowGeometryTests`. The exit branch *is* exercised by
the existing crash-regression test (on this harness `Application.Current` is never an App, so
`OnClosing` returns before reaching the hide path).

## Decisions

| Decision | Reasoning |
|---|---|
| **Caption glyph rendered from geometry, not `Window.Icon`** | A pack URI for an `<ApplicationIcon>`-only asset fails at runtime (dotnet/wpf#209). Rendering from the same shield-check geometry keeps the caption and the live tray icon one object without fighting the WPF resource pipeline |
| **Second launch signals then exits; `--tray` carries `wantsWindow: false`** | An autostart at boot must not force a window open. The decision (what a parsed launch implies) is pinned by test next to `ShellArgumentsTests` |
| **Vetoed autostart renders off and disabled, with a note** | Task Manager wins. Re-enabling from here would be fighting the OS; the note says so instead |
| **Hide branch documented as manual-verify-only** | WPF's one-Application-per-AppDomain rule makes `new App()` impossible in the harness. Documenting the constraint (with the probe evidence) is more honest than a test that cannot run |

## Still open

- **High contrast for the tray icon and the whole shell** is part of plan 10's verification pass.
  The HC *rule* (full-opacity `GrayText`/`WindowText`) is already encoded in
  `TrayIconRenderer.ColourFor` and pinned by test; whether it reads well in a real high-contrast
  theme is not yet checked by eye.
- **The two designed toast notifications** (`screens/12`) remain deferred to phase 7 — a Windows
  API WPF does not provide.

## References

- [phase-5-wpf.md](../dev-phases/phase-5-wpf.md) · [technical-debt.md](../technical-debt.md)
- [screens/12-tray-autostart-update.md](../operations/design/screens/12-tray-autostart-update.md) ·
  [screens/11-high-contrast.md](../operations/design/screens/11-high-contrast.md)
- [[ADR-004]] thin shells · [[ADR-005]] WPF
