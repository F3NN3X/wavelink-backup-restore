---
title: "Phase 5 Plan 11 - Help and About dialogs"
status: published
created: 2026-08-22
updated: 2026-08-22
related_adrs: [ADR-004, ADR-005]
tags: [plan, implementation, app, wpf, dialogs, help, about, phase-5]
---

# Phase 5 Plan 11 - Help and About Dialogs Implementation Plan

**Goal:** Give the shell its two missing information surfaces - a **Help** dialog that says what
the app does in the user's words (what is backed up, how snapshots are kept, how restoring works,
what the tray icon is for), and an **About** dialog that states the facts about this build (name,
version, licence, not-affiliated line) - reachable from the tray menu and, for Help, from a "?"
button in the main window's caption bar.

**Architecture:** Both dialogs are *static content behind a model record*, the same shape as the
rest of the shell: a pure `record` view model (`HelpDialogModel`, `AboutDialogModel`) with no I/O
and no WPF, a thin view that binds to it and computes nothing ([[ADR-004]]), and an `App` seam
(`OpenHelp()`, `OpenAbout()`) so the tray menu and the caption button open the same dialog. The
only non-static facts are the version (read from `ReleaseVersion.Current` - the same source the
updater compares against, so the number cannot drift) and two links read from environment
variables (`WLBACKUP_REPO_URL`, `WLBACKUP_RELEASES_URL`) rather than compiled in - a link that is
absent hides itself instead of pointing at nothing.

**Tech Stack:** C# / .NET 10, WPF, xunit.v3.

**Spec:** no design screen exists for either dialog (the handoff predates them); the copy follows
the README's own rule - say WHAT happens, not HOW.

> **Executed 2026-08-22.** All tasks complete in one pass: both view models with `Build()` seams,
> both views (modal dialogs over the main window when one is open, standalone otherwise), the tray
> menu entries (Help / About…), the "?" caption button beside the Settings gear, and the shared
> owner-handling seam `ShowOverMainWindow(Window)` that the three "open a dialog" call sites now
> share instead of each repeating two lines. Five App.Tests view tests pin the models and the
> views (App suite 957 → 974; total **1,568**), Release zero warnings. One deviation worth
> recording: the design package has no help icon, so the caption button's glyph is a question mark
> drawn in the mono font - text, not an invented icon. The Settings gear itself was left exactly as
> shipped (an earlier pass had altered its attributes; it was restored to the committed markup).
