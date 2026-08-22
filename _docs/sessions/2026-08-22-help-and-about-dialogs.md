---
title: "Session: Help and About dialogs, and the gear that was not changed"
status: published
created: 2026-08-22
updated: 2026-08-22
tags: [session, wpf, xaml, dialogs]
---

# Session: Help and About Dialogs

**Date:** 2026-08-22

## Goal

Add the two information surfaces the shell was missing - a **Help** dialog (what the app does, in
the user's words) and an **About** dialog (the facts about this build) - reachable from the tray
menu, with Help also reachable from a "?" button in the main window's caption bar. The plan is
[phase-5-plan-11](../plans/2026-08-22-phase-5-plan-11-help-and-about.md).

## What happened

**The two dialogs are static content behind a model record**, the same shape as every other
surface in the shell. `HelpDialogModel` is pure constant copy - four sections (what gets backed
up, how snapshots are kept, how restoring works, the tray icon) plus one footer link.
`AboutDialogModel` adds the two facts that are not constants: the version, read from
`ReleaseVersion.Current` (the same source the updater compares against, so the number shown in
About can never drift from the UPDATES section), and the links, read from environment variables
(`WLBACKUP_REPO_URL`, `WLBACKUP_RELEASES_URL`) rather than compiled in - a link that is absent
hides itself instead of pointing at nothing. The copy follows the README's own rule for this app:
say WHAT happens, not HOW.

**One seam on App, shared by three call sites.** `OpenHelp()` and `OpenAbout()` sit beside
`OpenSettings()`, and the two lines every "open a dialog" call site used to repeat - *if the main
window is open and loaded, own it; otherwise show standalone* - were pulled into
`ShowOverMainWindow(Window)`. The tray menu's Help/About… items, the Settings button, the new "?"
button and both new dialogs all go through it.

**The "?" glyph is text, not an icon.** The design package has no help icon of its own, and
inventing one would be decoration - so the caption button draws a question mark in the mono font,
which reads as text on purpose. It sits beside the Settings gear using the same
`WlIconGhostButton` style.

**The Settings gear was changed and then restored.** An earlier pass in this session had altered
the gear's `Path` attributes (size 16 → 17, dropped the round line caps and `Stretch="Uniform"`,
changed the margin). The user asked for the original cogwheel back; the markup was restored to
exactly what is at HEAD, keeping only the new "?" button beside it.

## Decisions made

| Decision | Recorded in |
|---|---|
| Help and About are static-content dialogs: a pure model record + a thin binding view, no logic in either | [plan 11](../plans/2026-08-22-phase-5-plan-11-help-and-about.md) |
| The version is read from `ReleaseVersion.Current`, never hard-coded - one place it is written, everywhere else reads it | [plan 11](../plans/2026-08-22-phase-5-plan-11-help-and-about.md) |
| Links come from the environment, not the build; absent means the link hides itself | [plan 11](../plans/2026-08-22-phase-5-plan-11-help-and-about.md) |
| Owner handling for every dialog lives in one place (`ShowOverMainWindow`), so a fourth dialog does not copy two lines again | [plan 11](../plans/2026-08-22-phase-5-plan-11-help-and-about.md) |

## What did not work

- **The first draft of the view tests failed five ways**, all in the same family as the 0.5.1
  design audit's finding - a view no test had ever constructed: footer buttons carried access-key
  underscores that rendered literally (fixed by dropping them), a link-collapse trigger keyed on
  `Tag="{x:Null}"` never fired (replaced with code-behind `Loaded` handlers), the help model
  exposed a non-nullable URL that was always empty in tests (now nullable, and an absent link
  hides its row), and two `Run.Text` bindings used `Mode=OneTime`, which resolved before the
  model's properties were set (now `OneWay`).

## Verification

Build zero warnings; full suite **1,568 passing** (Core 494, CLI 100, App 974 - up from 957 with
the five new dialog tests). The two new view-test files force each dialog through a real layout
pass offscreen under the theme resources, the same guard shape as `SettingsDialogViewTests`.

## Version cut

The work was written against an `[Unreleased]` heading in [CHANGELOG.md](../../CHANGELOG.md).
When the user confirmed there is no unreleased section, that block was renamed to
`[0.7.2] - 2026-08-22` and `<Version>` in [Directory.Build.props](../../Directory.Build.props)
was bumped from `0.7.1` → `0.7.2`. The About dialog's version line reads from
`ReleaseVersion.Current`, so it picks up the new number automatically; no test pins a literal
version string (the one `"0.7.1"` in `AboutDialogViewTests.cs` is an inline fixture for the
link-rendering test, not an assertion on the running build).

## Next

Commit the code, this note, the plan and the stats update together. Nothing else outstanding from
this session; the gear revert is part of the same commit.
