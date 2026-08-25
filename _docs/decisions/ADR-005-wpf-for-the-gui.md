---
title: "ADR-005: WPF over WinUI 3, Avalonia and WinForms"
status: accepted
created: 2026-08-16
updated: 2026-08-16
tags: [decision, ui, platform]
---

# ADR-005: WPF over WinUI 3, Avalonia and WinForms

**Status:** Accepted
**Date:** 2026-08-16

## Context

The GUI shell from [[ADR-004]] needs a framework. The design handoff specifies four screens
with real demands: a custom 34px caption bar, Mica backdrop on that bar, live OS theme
following with no restart, the OS accent colour bound to one brush role, a list with grouped
rows and per-row expansion, and two modal dialogs over a scrim.

None of that is exotic. All four candidates can do most of it. The decision is made on
packaging and friction rather than capability.

## Decision

**WPF**, on .NET 10.

Design values become **brush resources declared once per theme**, swapped via
`DynamicResource`, never literals repeated at call sites. That is what makes live theme
switching a resource swap rather than a window rebuild, and it is what the handoff means when
it says to treat every `--wl-*` name as a resource key.

## Alternatives considered

| Option | Why not |
|---|---|
| **WinUI 3** | The modern answer, and it drags in the Windows App SDK for no benefit at this scale. It also pushes toward MSIX packaging, which for *this* app is quietly ironic, since [[ADR-003]] exists because MSIX package resets destroy `LocalState`. Its Mica and theming story is better out of the box; that is one afternoon of WPF work, not a framework choice. |
| **Avalonia** | Buys cross-platform that [[ADR-008]] explicitly does not want, and gives up first-party Win32 interop we need for the custom caption bar and Mica. |
| **WinForms** | Perfectly capable of the mechanics, and you will fight it for a week on one list view that needs to look pleasant. The design is high-fidelity, hairline borders at 12% opacity, a 3px left selection edge, five-cell health strips, 140/220ms transitions. That is a templating problem, and templating is WPF's actual strength. |

## Consequences

**This enables:** single-file publish with no packaging requirement; first-party interop for
the custom caption bar, Mica and `UISettings.GetColorValue`; and control templating strong
enough to hit the handoff's fidelity bar rather than approximate it.

**This rules out:**

- **NativeAOT for the GUI.** WPF does not support it. The CLI remains AOT-eligible, which is
  exactly why [[ADR-004]] keeps them in separate projects.
- Reusing the shell on macOS, where Wave Link also ships. [[ADR-008]] declines that scope for
  independent reasons; this decision makes it structural.

**Work this creates that the framework does not give free:**

| Handoff requirement | WPF cost |
|---|---|
| Mica / `SystemBackdrop` on the caption bar | `DwmSetWindowAttribute` interop |
| Custom 34px caption bar, 46 × 34 buttons | `WindowChrome` plus hit-testing for the drag region |
| Live OS theme following, no restart | Watch the OS setting, swap a merged resource dictionary |
| OS accent bound to `--wl-accent`, `--wl-danger` fixed | `UISettings.GetColorValue(UIColorType.Accent)`, one brush |
| Windows high-contrast mode | Not designed; see [technical-debt.md](../technical-debt.md) §4 |

**Fonts:** Rubik and JetBrains Mono are open-licensed and embedded with the app. The fallbacks
if footprint rules them out are Segoe UI Variable and Cascadia Mono, geometry holds, warmth
is lost. Decide once, in phase 5, not per-screen.

**Revisit if:** WPF stops being supported on a .NET version we need, or the app grows a
requirement, a modern in-box control, a Windows-11-only shell integration, that WinUI 3
gives free and WPF cannot reach. Neither is on the horizon.

## References

- `SPEC.md` §10
- [README.md](../operations/design/README.md), tokens, four screens, window geometry
- [[ADR-004]] · [[ADR-008]]
