---
title: "Session: Phase 5 part 3 — the tray shell, and making it follow Windows"
status: published
created: 2026-08-17
updated: 2026-08-17
related_adrs: [ADR-004, ADR-005, ADR-008]
tags: [session, app, tray, theming, phase-5]
---

# Session: Phase 5 part 3 — the tray shell, and making it follow Windows

**Date:** 2026-08-17

Eleven commits. **473 tests green** (295 Core, 91 CLI, **87 App** — up from 386 with no App
project at all), Release clean with zero warnings. Working tree clean, `main`, v0.4.0.
`H.NotifyIcon.Wpf` 2.3.0 is now the repo's first production dependency, resolved at the pinned
version.

Executed: [plans/2026-08-17-phase-5-plan-2-tray-shell.md](../plans/2026-08-17-phase-5-plan-2-tray-shell.md)
(six tasks) and [plans/2026-08-17-phase-5-plan-3-theme-following.md](../plans/2026-08-17-phase-5-plan-3-theme-following.md)
(five tasks, written this session).
Design: [plans/2026-08-17-phase-5-shell-design.md](../plans/2026-08-17-phase-5-shell-design.md).

## What shipped

**The app is now the process, not the window.** `ShutdownMode.OnExplicitShutdown`, the
coordinator lives in `App` and outlives every window, and closing hides to the tray. Three
shutdown entrances share one exit — the tray's *Quit*, a window close when hide-on-close is off,
and `SessionEnding`. That last one is not optional: the original incident happened during an
update while the machine was restarting, so a shell that only captures on a deliberate quit misses
the exact case `CaptureOnShutdown` exists for.

**Single instance is a mutex plus two named events.** Two watchers on one settings file is the
race being prevented, so the gate closes before any Core object is constructed. Two events rather
than one, so a second launch carrying `--tray` exits silently instead of forcing open a window
nobody asked for — verified live, along with hide-on-close and a second launch re-showing the
first instance's window.

**The tray's four states are a pure function**, derived rather than stored, so they cannot go
stale. `NEEDS YOU` outranks everything: a watcher that is failing must not be hidden by a quieter
state that also happens to be true. It is reachable at all only because §7.3 put the `CoreError`
on `TickResult`.

**Autostart reads back Task Manager's veto.** Three states, not two. Task Manager does not delete
the `Run` value when a user disables startup — it writes an approval record under
`StartupApproved`, and Windows honours that. A toggle that only read the `Run` key would read *on*,
do nothing at login, and look like our bug rather than their choice.

**Three theme dictionaries, and a guard that keeps them honest.** A source scan fails the build on
any `#RRGGBB` outside `Theming/`. High contrast binds to `SystemColors.*ColorKey` and every tint
goes transparent, because in a high-contrast theme the palette is not ours.

**Then plan 3 made all of that actually follow Windows.** `ISystemTheme` wraps `UISettings`
(dark/light and accent) and `SystemEvents.UserPreferenceChanged` (high contrast) behind one
`Changed` event. The accent enters the app in exactly one place: `AccentPalette` derives soft and
line from it at swap time — 12%/32% dark, 7%/24% light — so four authored values cannot drift.
`WlDanger` does not move, guarded by a whitelist rather than a `WlDanger`-shaped hole.

**And the tray menu got the Windows 11 treatment**, then got it corrected. See below.

## What broke, and what it taught

**`TaskbarIcon.IconSource` cannot take a generated image at all.** Two failed launches. The
library converts an `IconSource` with `new Uri(source.ToString())`, so it accepts only images that
*came from* a URI; a `RenderTargetBitmap` throws "not supported", and wrapping it in a
`BitmapFrame` only moves the throw into the `Uri` constructor. The fix is a
`System.Drawing.Icon` — PNG inside a hand-written one-entry ICO container — set on
`TaskbarIcon.Icon`. Also: an icon built in code is never loaded into a visual tree, so without
`ForceCreate()` nothing appears and nothing errors. Written up:
[the-tray-icon-refuses-every-image-you-draw.md](../knowledge-base/gotchas/the-tray-icon-refuses-every-image-you-draw.md).

**A detached `ContextMenu` never sees a theme swap.** The near-miss of the session. `DynamicResource`
is resolved through the *element tree*, and a tray icon's menu has no parent in any tree — so its
references resolve once, at load, and never again. Reopening the menu does not refresh them and
neither does `UpdateLayout`; both were tried, and the test failed on both before
`App.RebuildTrayMenu` existed. Styling without that would have shipped a menu permanently frozen in
whichever theme was current at startup, which is precisely what "follows the OS" must not mean.
Written up: [tray-menu-keeps-the-theme-it-started-with.md](../knowledge-base/gotchas/tray-menu-keeps-the-theme-it-started-with.md).

**The right material was the wrong answer.** Plan 3 reasoned correctly that Windows 11 uses Acrylic
for transient surfaces and Mica for window backgrounds, and shipped Acrylic on the menu. Seen by
eye, it read as neither native Windows nor this app — a flat grey box belonging to nothing. The
cause was the *surface role*, not the material: `WlChrome` is defined as the "Mica caption/strip
tint" and only means anything with Mica behind it. The menu is now an opaque `WlCard` panel with a
`WlLine2` hairline, and asks for no backdrop. **Correct reasoning about Windows is not the same as
a correct decision for this app**, and only looking at it caught the difference.

**Plan 3's finding B diagnosed the wrong half of a real bug.** It blamed the missing input count on
having no `SnapshotStore` reference — true — and missed that `BackupHost.LastBackupAt` only knows
about captures made during the current run. A freshly started app therefore said "no backup yet"
with backups on disk, in the tooltip as well as the header. Both now read the store.

**Four smaller corrections to plan 2, each caught by a failing build or test.** `SystemColors.*Color`
used as a `DynamicResource` key never resolves, so every high-contrast surface would have rendered
black — it must be `*ColorKey`. A pack URI without an assembly name resolves against the *entry*
assembly, which under a test host is the runner. `CaptureDecision.NotDue` does not exist; the
member is `NothingPending`. And `NOT --green-400` is invalid inside an XML comment.

**The design's one open question is closed.** xunit.v3 3.2.2 ships no STA attribute, and an STA
thread alone is not enough: the `pack` scheme *and* its `WebRequest` prefix are both registered by
constructing a `System.Windows.Application`. `tests/Wpf.cs` is that harness, and every test that
touches resources keeps its mutation and its assertions inside one `Wpf.Run` — the dispatcher runs
one delegate at a time, which is what stops them interfering.

## Decisions

| Decision | Reasoning |
|---|---|
| **The tray menu is opaque `WlCard`, not Acrylic on `WlChrome`** | A translucent menu shows the *desktop's* palette through it, and this menu's job is to look like Wave Link Backup. The rounded corner and theme-matched frame stay, being things DWM does that the app cannot, and neither is a colour decision |
| **Mica stays the answer for the main window** | Where a backdrop earns its keep, and where the design names it. `ChromeChoice` holds both decisions so plan 4's caption bar inherits a contract rather than re-deciding |
| **The menu is rebuilt on every theme change** | The only fix that covers `ControlTemplate` internals. `SetResourceReference` per property works but decays the moment a template gains a colour |
| **Pause lives in the host, not Core** | The coordinator owns no timer and waits to be ticked, so pausing is *not ticking it*. A pause concept in `AutoBackupPolicy` would put a UI affordance in a library with no UI ([[ADR-004]]) |
| **A pending write survives a pause untouched** | Asserted. Discarding it would mean an hour's pause quietly threw away the change that was waiting to be captured |
| **`ChromeChoice` extracted as a pure decision** | The interop is untestable by design §E; which-surface-gets-what is not, and it is exactly what a refactor loses |
| **A count of zero inputs is shown as no count** | An unreadable store and a backup of nothing are different claims, and only one of them is ours to make |

## Still open

- **Plan 4 (the backup list) and plan 5 (the Settings dialog) are unwritten.** Plan 3's finding A
  table is a live contract for plan 4's caption bar: `DWMSBT_MAINWINDOW`, `DWMWCP_DEFAULT`,
  immersive dark from `ISystemTheme`.
- **Five deferred minors**, now recorded in [technical-debt.md](../technical-debt.md) §4.8 — fixed
  32px tray icon, mono letter-spacing, the check-vs-toggle reading, the `Settings…` placeholder,
  and the raw error `MessageBox`.
- **One plan-3 Done-when item is unverified**: nobody has watched the tray icon repaint while
  flipping Windows to light mode. The wiring is asserted; the pixels are not.
- **High contrast has not been seen by a human.** Every rule in it is tested, and `11` is the
  section where a test passing and the screen being usable diverge most easily.
- **`WlAccent` follows the OS accent, so the menu's check is the user's accent colour, not the
  brand red.** That is `01-tokens-and-mapping.md` as written; flagged because it surprised on first
  sight.
- **§7.4 keyboard and focus** — still open. The tray menu has no accelerators and no
  `AutomationProperties` names.
- **`watch` is still the least-covered verb**, and the shell now duplicates its host. Deleting it
  rather than maintaining two is still worth deciding.

## References

- [phase-5-wpf.md](../dev-phases/phase-5-wpf.md) · [technical-debt.md](../technical-debt.md) §4.8, §7
- [screens/12-tray-autostart-update.md](../operations/design/screens/12-tray-autostart-update.md) ·
  [screens/11-high-contrast.md](../operations/design/screens/11-high-contrast.md)
- [[ADR-004]] thin shells · [[ADR-005]] WPF · [[ADR-008]] Windows-only
