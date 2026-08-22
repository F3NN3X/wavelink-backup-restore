---
title: "ADR-013: A theme preference, behind the system-theme seam"
status: accepted
created: 2026-08-20
updated: 2026-08-20
related_adrs: [ADR-003, ADR-005]
tags: [decision, ui, theming]
---

# ADR-013: A theme preference, behind the system-theme seam

**Status:** Accepted
**Date:** 2026-08-20

## Context

The app followed Windows and offered no choice. `01-tokens-and-mapping.md` specifies one palette
per OS state — dark, light, and the high-contrast scheme that outranks both — and `ThemeManager
.Follow` applied whichever Windows reported, live. That is a complete design and it was built
faithfully; what it has no answer for is a user who wants the app light while Windows is dark.

The ask was for a four-way choice — Auto, Dark, Light, High contrast — in Settings.

Two things made it more than a switch:

1. **Six places read the palette**, and they read it through `ISystemTheme`: `ThemeManager.Follow`,
   the main window's DWM chrome and its `ShellViewModel.IsHighContrast`, the tray menu's material,
   the tray icon's colours, and the settings dialog's own high-contrast triggers. A preference that
   lived beside that interface would mean finding all six and teaching each one the precedence
   rule.
2. **High contrast is not a peer of dark and light.** `ISystemTheme`'s own doc says it: *"it is
   Windows saying the palette is no longer ours."* A preference that could paint over a
   high-contrast scheme would paint over the one scheme somebody turned on because they cannot
   read ours.

## Decision

The preference is a **decorator over `ISystemTheme`**. `PreferredTheme` wraps the real
`UiSettingsTheme`, resolves preference-over-OS in one pure function (`ThemeChoice.Resolve`), and
re-raises the same `Changed` event an OS switch raises. Nothing downstream changed.

It is stored in **`shell.json`**, beside the window rectangle — not in `settings.json`.

Windows' own high-contrast scheme **outranks all four choices**. Choosing High contrast turns the
app's high-contrast RENDERING rules on (no fills, shape-first health, disabled at full opacity),
because those belong to the palette being drawn rather than to the setting that usually turns it
on.

## Alternatives considered

| Option | Why not |
|---|---|
| A second interface (`IThemePreference`) read alongside `ISystemTheme` | Every one of the six consumers would have to consult both and combine them the same way. Six copies of a precedence rule is six chances to disagree, and the disagreement would show up as one surface staying dark. |
| A property on `ThemeManager`, applied at `Apply` time | Fixes the theme swap and nothing else. `MainWindow.ApplyChrome` and the tray menu ask `ISystemTheme` directly for dark-versus-light, so the window frame and the menu material would have kept following Windows while the app's own surfaces followed the user. |
| Store it in `settings.json` | That file describes itself two sections below, on the same screen: *"the folder, the automatic-backup switch, how many to keep and which Wave Link you picked"*. A theme in there makes that sentence false — the same argument [[ADR-003]] and `ShellState` already made for the window rectangle. |
| Let the preference override Windows' high-contrast scheme | It would let the app paint its own colours over a scheme somebody turned on because they cannot read ours. The one case where a user's earlier choice must lose to their current need. |
| A theme toggle in the caption bar, as the design prototype draws it | The prototype has a sun icon where the shipped app has the gear the README specifies. Settings is where every other persisted preference in this app lives, and a second control surface for one of them is how two controls end up disagreeing. |

## Consequences

**This enables:** a light app on a dark desktop and the reverse; a user-chosen high-contrast
rendering without changing Windows; and — because the preference travels through the existing
`Changed` event — a pick that repaints the window, the dialogs, the tray menu and the tray icon by
exactly the route a Windows dark/light switch already took. One route, so a preference change
cannot repaint less than an OS change does.

**This rules out:** any future consumer reading `UiSettingsTheme` directly. The wrapper is the
only correct source, and a consumer that reaches past it gets Windows' answer rather than the
app's. It also rules out a per-window theme — the palette is a single merged dictionary in slot 0
(`ThemeManager.Apply`), and that stays true.

**It also means `IsHighContrast` no longer means "Windows is in high contrast."** It means "the
high-contrast palette is being drawn." Everything that reads it wanted the second meaning; nothing
in the app wanted the first.

**Revisit if:** Windows gains a per-application theme setting that WPF honours, or if a future
surface genuinely needs to know what the OS said as distinct from what is being drawn — at which
point the wrapper needs a second property rather than a second implementation.

## References

- `_docs/operations/design/screens/01-tokens-and-mapping.md` — the palette, and the one brush that
  follows the Windows accent
- `_docs/operations/design/screens/11-high-contrast.md` — the rendering rules the effective theme
  now carries
- [[ADR-003]] — the same "which file describes what" argument, for the backup store
- `src/WaveLinkBackup.App/Theming/PreferredTheme.cs` · `ThemePreference.cs`
