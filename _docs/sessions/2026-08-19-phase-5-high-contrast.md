---
title: "Session: Phase 5 plan 10 — high contrast"
status: published
created: 2026-08-19
updated: 2026-08-19
related_adrs: [ADR-004, ADR-005]
tags: [session, app, wpf, high-contrast, phase-5]
---

# Session: Phase 5 plan 10 — high contrast

**Date:** 2026-08-19

**964 tests green** (296 Core, 91 CLI, **577 App**), build clean with zero warnings. This was the
last plan in phase 5; with it complete, every surface in the phase is built and verified in both
themes *and* high contrast.

Executed: [screens/11-high-contrast.md](../operations/design/screens/11-high-contrast.md) — a
verification + gap-filling pass over a third theme that was already ~90% built and tested, not a
build-from-scratch.

## What was already built (verified, not assumed)

The plan's own table is the record; what it confirmed: `HighContrast.xaml` matches spec key for
key (every fill → Transparent, every text/line role → a `SystemColors.*ColorKey`, no literal hex);
the row template encodes health in **shape** (solid / dashed / dotted rule) plus a verdict word,
with HC DataTriggers on `DataContext.IsHighContrast`; the focus ring flips to WindowText in HC and
to HighlightText when selected; buttons use HotTrack hover and full-opacity GrayText disabled; and
the tray's PAUSED glyph is already pinned at full opacity under HC. The three-theme brush guard
(`ThemeTests.Every_theme_declares_every_brush`) was already enforcing that no key could go missing
from any theme.

## What shipped

**The runtime swap, pinned end to end.** `UiSettingsTheme` now exposes the preference handler as an
internal seam (`HandleUserPreference`) so a test can fire the event without a real Windows session
change; `RaiseOnUiThread` gained a `CheckAccess()` fast path so a same-thread raise invokes
`Changed` synchronously (the off-thread production path still uses `BeginInvoke`). The new
`UiSettingsThemeTests` pins four things: a colour preference change fires `Changed` exactly once, a
non-colour (Accessibility) change does **not** fire it, two colour changes fire twice, and
`Dispose` stops firing. That is the one path that matters to a user — turning HC on in Windows and
watching the app swap without a restart — now pinned rather than assumed.

**The no-hard-coded-colour rule, guarded.** A new test in `ThemeTests`
(`High_contrast_carries_no_hard_coded_colour`) reads `Theming/HighContrast.xaml` as source and
asserts (1) no hex literal anywhere, and (2) every `<SolidColorBrush/>` resolves to exactly one of
two legal shapes — a `{DynamicResource {x:Static SystemColors.*ColorKey}}` or `Transparent`. A
future edit that introduces a literal colour fails the build, not the user's eyes.

**The HC contract recorded for plans 5–8.** Each of those plans' Definition-of-done now carries a
standing line — *every colour the surface uses in dark/light is replaced by a shape or a verdict
word in HC; tints are transparent; surfaces separate on 1px WindowText borders; an HC test pins
each* — with plan 10 as the source of truth. No new surface ships without its HC encoding.

## The sweep: no gaps found

The final pass walked every plans 5–8 surface in both HC schemes and confirmed each encodes meaning
by shape or word, never colour alone:

- **Restore-outcome strip.** Fills (`WlWarnSoft` / `WlDangerSoft`) go transparent in HC; the
  outcome rides on the **glyph shape** (ringed-check / hollow-circle / warning-triangle /
  close-in-circle) plus a title word ("Restore failed", "Wave Link rejected the settings file").
  Pinned by `RestoreOutcomeStripTests`.
- **Delete / empty-trash dialogs.** Word-driven models; the destructive weight is on the label and
  the button, not a colour. Native controls render with system contrast in HC for free.
- **Error screens.** The twelve errors' amber/danger tints go transparent; each error's meaning is
  its copy. Error 9/12 full screen separates on a WindowText border.
- **Settings dialog.** The proportion bar is a size visualization where every segment carries a
  word label; in HC the segments render as solid system-text blocks on transparent, distinguishable
  by width and gap.
- **Restore-stage view host.** The three treatments (done / current / pending) are driven by
  `Stage.Status` through brush keys that all auto-swap in HC.

## Decisions

| Decision | Reasoning |
|---|---|
| **Internal seam over re-architecture** | Exposing the preference handler as an internal method is a testability seam, not a design change — the plan's stated preference. `InternalsVisibleTo` to the test assembly already existed |
| **`CheckAccess()` fast path in `RaiseOnUiThread`** | A same-thread raise invoked via `BeginInvoke` would need dispatcher pumping to observe in a test. The fast path makes it deterministic; the off-thread production path is untouched |
| **Sweep recorded as verification, not new code** | Every surface already encoded meaning by shape/word and pinned its own tests. The honest outcome of a verification pass is sometimes "no gaps" — recording that (with the per-surface reasoning) is the deliverable |

## Still open

- **Both-schemes visual check by eye.** The sweep reasoned from brush keys and pins; a human
  flipping through High Contrast Black *and* White with the app running is the final confirmation.
  Nothing was authored against a specific background luminance, so both should invert for free —
  but that is a look-at-it item, not yet done by eye.
- **The two designed toast notifications** (`screens/12`) remain deferred to phase 7 — a Windows
  API WPF does not provide.

## References

- [phase-5-wpf.md](../dev-phases/phase-5-wpf.md) · [technical-debt.md](../technical-debt.md)
- [screens/11-high-contrast.md](../operations/design/screens/11-high-contrast.md)
- [[ADR-004]] thin shells · [[ADR-005]] WPF
