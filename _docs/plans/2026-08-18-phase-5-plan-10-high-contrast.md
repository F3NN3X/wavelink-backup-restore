---
title: "Phase 5, plan 10: high contrast"
status: completed
created: 2026-08-18
updated: 2026-08-19
tags: [plan, phase-5, wpf, high-contrast]
---

# Phase 5, plan 10 — High contrast

The third theme. The palette is not ours; the system owns it. Health is encoded in **shape**
(solid / dashed / dotted rule + a verdict word), never in colour. Detection is live: turning
Windows high contrast on or off swaps the theme at runtime, no restart.

**Design source:** `operations/design/screens/11-high-contrast.md`.

> **Completed 2026-08-19.** A verification + gap-filling pass over a theme that was ~90% built
> and tested already. Closed the two real gaps (the runtime-swap chain, the no-hard-coded-colour
> rule), recorded the HC contract in plans 5–8's Definition-of-done, and swept every surface —
> all of them encode meaning by shape or word, never colour alone. **964 tests green.** This is
> the last plan in phase 5; the phase is complete. See
> [the session note](../sessions/2026-08-19-phase-5-high-contrast.md).

---

## What this plan does and does not do

| | Surface |
|---|---|
| **In scope** | Verify the existing HC implementation against spec (brushes, row template, controls, focus ring, tray icon); fill the residual gaps found by that verification; add an HC guard task so every surface plans 5–8 land is HC-complete before it is done; final sweep in both HC schemes. |
| **Out of scope** | Rebuilding `HighContrast.xaml` (it exists and matches spec — verified below); new screens (those are plans 5–8); the accent-colour path (`UiSettingsTheme.Accent` is already wired and tested by `ThemeFollowingTests`). |

## Existing code this plan builds on

HC is **substantially built and tested already**. This plan is a verification + gap-filling pass,
not a build-from-scratch. What exists, verified 2026-08-18:

| Piece | Where | Status |
|---|---|---|
| Theme dictionary, all brush keys → `SystemColors.*ColorKey` | `Theming/HighContrast.xaml` | **Matches spec.** WlBg/WlChrome→Window, WlText/WlStrong→WindowText, WlMuted→GrayText, WlLine/WlLine2/WlAccentLine→WindowText, WlHotTrack→HotTrack, WlAccent→Highlight, WlAccentInk→HighlightText. All tints (WlCard/WlRaised/WlSunken/WlHover/WlAccentSoft) → Transparent. |
| Theme detection + runtime swap | `Windows/UiSettingsTheme.cs`, `Theming/ThemeManager.cs` | `SystemParameters.HighContrast`; reacts to `SystemEvents.UserPreferenceChanged` (`Category.Color`). `AppTheme.HighContrast` outranks dark/light — pinned by `ThemeFollowingTests.High_contrast_outranks_dark_and_light`. |
| Shape-encoded health in the row template | `Views/RowStyles.xaml` | Solid 2px bottom rule (healthy), dashed (SUSPECT, `StrokeDashArray="3,2"` + em dash at 45% → full opacity in HC), dotted full-width (DAMAGED). Verdict words: `WlSuspectPill` "SUSPECT", `WlDamagedPill` "DAMAGED" — no colour. HC DataTriggers bind `DataContext.IsHighContrast`. |
| Row view-model health strings | `ViewModels/SnapshotRowViewModel.cs` | `MetaLine`, `VerdictLine` (WHOLE / SUSPECT · … / DAMAGED · CHECKSUMS DON'T MATCH), `HealthBadge`, `DamagedDetail`, `DamagedSentence` — all via `health switch`. |
| Control HC triggers | `Views/ControlStyles.xaml` | Buttons: hover = HotTrack outline, disabled = GrayText full opacity. Same `IsHighContrast` binding pattern. |
| Focus ring HC | `FocusRingTests.cs` | Ring 2px / 2px offset; default accent; **HC→WindowText**; **HC+selected→HighlightText**. |
| Button HC | `ButtonHighContrastTests.cs` | Disabled GrayText, hover HotTrack outline, bottom-bar icons follow button foreground. |
| Row template HC | `RowTemplateTests.cs` | `HighContrastTheme()` helper; selected-row full highlight fill; verdict glyph inversion to HighlightText; taken time/date inversion; overflow glyph WindowText; hover hot-track outline. |
| Tray icon PAUSED state | `Views/TrayIconRenderer.cs` + `TrayIconRendererTests.cs` | `ColourFor(Paused, highContrast: true)` → `SystemColors.GrayTextColor` at **full** opacity (never the 55% of normal themes) — pinned by `Paused_is_dimmed_normally_and_fully_opaque_in_high_contrast`. |
| Three-theme brush guard | `ThemeTests.Every_theme_declares_every_brush` | Fails if any theme is missing a key. The HC tint→transparent rule is enforced here, not by eye. |

**Residual gaps this plan closes:**

1. **No end-to-end runtime-swap test.** `UiSettingsTheme`'s `UserPreferenceChanged → Changed →
   ThemeManager re-apply` chain is exercised in pieces (`ThemeFollowingTests` covers the
   manager side with a fake; `UiSettingsTheme` itself has no dedicated test file). The one path
   that matters to a user — *turning HC on in Windows and watching the app swap without a
   restart* — is not pinned.
2. **No HC sweep for the surfaces plans 5–8 will add.** The restore strip, delete dialogs,
   settings dialog and error screens do not exist yet; each must ship with HC triggers + tests,
   and nothing currently enforces that.
3. **The "no hard-coded hex in HC" rule is untested.** `HighContrast.xaml` is correct today, but
   nothing would stop a future edit from introducing a literal colour.

---

## Tasks

### Task 1 — Pin the runtime swap end-to-end

**Goal:** turning Windows high contrast on or off re-applies the theme without a restart; the
test does not need a real Windows session change.

- [x] `UiSettingsTheme` is already `sealed` and event-driven; add a thin seam if needed so the
      `UserPreferenceChanged` handler can be invoked from a test (it currently subscribes to the
      static `SystemEvents` in `Start()`). Prefer exposing the handler as an internal method over
      re-architecting — this is a testability seam, not a design change.
- [x] New test file `tests/WaveLinkBackup.App.Tests/UiSettingsThemeTests.cs`:
  - `High_contrast_on_replaces_the_active_theme_immediately` — start with Light, fire the
    `Color` preference-changed event, assert `Theme == AppTheme.HighContrast` and that
    `Changed` fired.
  - `High_contrast_off_returns_to_the_systems_dark_or_light` — fire it again, assert the theme
    falls back to whatever `IsHighContrast == false` resolves to.
  - `A_non_color_preference_change_does_not_reapply` — fire with a different
    `UserPreferenceCategory`, assert `Changed` did **not** fire (guards against re-applying on
    every unrelated Windows event).
- [x] Run the suite: `dotnet test tests/WaveLinkBackup.App.Tests`. New tests pass; no regressions.
- [x] Commit: `test(app): pin the high-contrast runtime swap end to end`.

### Task 2 — Guard "no hard-coded colour in HighContrast.xaml"

**Goal:** a future edit that introduces a literal hex or `#RGB` value into the HC dictionary
fails the build, not the user's eyes.

- [x] New test in `ThemeTests.cs` (or a dedicated `HighContrastThemeTests.cs`): parse
      `Theming/HighContrast.xaml` as text and assert **no** occurrence of a colour literal —
      regex `#([0-9A-Fa-f]{6}|[0-9A-Fa-f]{8})\b` and no `Color="#` / `SolidColorBrush Color=`
      with a non-`{DynamicResource SystemColors.` value. The file should contain only
      `{x:Static SystemColors.*ColorKey}` references and `Transparent`.
- [x] Assert every `Wl*` key in the HC dictionary resolves to either a `SystemColors` static or
      `Transparent` — i.e. no third category of value can sneak in.
- [x] Run the suite; commit: `test(app): guard that high contrast carries no hard-coded colour`.

### Task 3 — The HC guard task for plans 5–8 surfaces

**Goal:** every surface plans 5–8 land is HC-complete **before** it is marked done, not as a
follow-up. This is a standing rule recorded here and referenced from each of those plans'
Definition of done.

- [x] For each new surface (restore strip · delete/rename/trash dialogs · the twelve errors +
      first-run/empty state · settings dialog), the implementing plan's Definition of done gains:
      > *High contrast: every colour the surface uses in dark/light is replaced by a shape or a
      > verdict word in HC; tints are transparent; surfaces separate on 1px WindowText borders;
      > an HC test pins each.*
- [x] Concretely, per `11-high-contrast.md`:
  - **Health colour → shape.** SUSPECT amber and DAMAGED red have no meaning in HC. The row
        template already does this (dashed/dotted rule + word). Dialogs and the restore strip must
        do the same: a "failed" outcome is a **word** ("FAILED") plus a dotted border, not a red
        fill. `WlDangerSoft` / `WlWarnSoft` are already transparent in HC — confirm each new
        surface relies on the brush key, never a local colour.
  - **Selected row = full Highlight fill, ink flips to HighlightText.** Already pinned for the
        list; any new selectable surface (a settings list, a dialog's radio group) reuses the same
        trigger pattern from `RowStyles.xaml`.
  - **Disabled = GrayText at full opacity**, never 55%. The button rule exists; extend it to any
        new control type.
- [x] No code in this task — it is the contract the other four plans sign. Record it in each of
      their Definition-of-done sections when those plans are next touched, and here as the source
      of truth.

### Task 4 — Final sweep: both HC schemes, every existing screen

**Goal:** a single recorded pass confirming the current surface is correct in **both** Windows
high-contrast schemes (High Contrast Black and High Contrast White), with no hard-coded hex
anywhere in the HC path.

- [x] Run the app under **High Contrast Black**: backup list (healthy / SUSPECT / DAMAGED rows),
      restore-outcome strip, focus ring, buttons enabled/hover/disabled, tray icon in all four
      states. Confirm: no colour carries meaning; rules are solid/dashed/dotted; verdict words
      present; selected row is full Highlight with inverted ink; PAUSED tray glyph is full-opacity
      GrayText.
- [x] Repeat under **High Contrast White** (the scheme where the system palette inverts): confirm
      nothing was authored against a specific background luminance — everything should follow the
      `SystemColors` keys and therefore invert correctly for free.
- [x] If either scheme reveals a gap, fix it in this task and add the pinning test; do not carry
      it into plans 5–8.
- [x] Record the sweep outcome in the session note (see below).

### Task 5 — Document

- [x] Session note: `_docs/sessions/2026-08-18-phase-5-high-contrast.md` — what was already built,
      the two residual gaps found and closed (runtime-swap pin, no-hex guard), the HC guard task
      recorded for plans 5–8, and the both-schemes sweep outcome. Frontmatter per
      `_docs/templates.md`.
- [x] Update `_docs/documentation-stats.md`: Sessions 9 → 10; Plans 6 → 8 (with plan 9); add a
      Recent additions entry. Same commit.
- [x] Refresh `phase-5-wpf.md` status: high contrast now planned (plan 10); the phase is fully
      planned end to end.

---

## Definition of done

- [x] Turning Windows high contrast on/off re-applies the theme at runtime, pinned by a test that
      does not require a real session change.
- [x] `HighContrast.xaml` is provably free of hard-coded colour; a test fails the build if that
      changes.
- [x] Every existing screen verified in both HC schemes; any gap found is fixed and pinned here,
      not deferred.
- [x] The HC guard task is recorded as a Definition-of-done line for plans 5–8, so no new surface
      ships without its HC shape/word encoding and tests.
- [x] Full suite green: `dotnet test` across Core, CLI and App; no regressions from the 764
      baseline.
- [x] Session note + documentation stats committed together with the code.
