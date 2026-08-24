---
title: "Session: A restore that puts the service back, and a trash row that finally refreshes"
status: published
created: 2026-08-24
updated: 2026-08-24
tags: [session, wpf, restore, viewmodel]
---

# Session: Service Auto-start and Trash-empty Progress

**Date:** 2026-08-24

## Goal

Two features and one bug, in one sitting. First, make a **restore bring the Wave Link service back
before it relaunches the app**, so the user never lands on Wave Link's own "Start Service / Exit
App" box after a restore they just ran. Second, give the **trash-empty action a live progress bar**
instead of a frozen window while a full store is emptied. And fix a reported bug where the trash row
in Settings **kept showing its old count and size after emptying**.

## What happened

**The service auto-start is a new Core seam, owned by the orchestrator.** `IWaveLinkService` sits
beside `IWaveLinkProcess` in `Core/Process`, exposing `Exists`, `IsRunning`, and one action —
`EnsureStarted()`. The real implementation (`WaveLinkService`) goes through the Service Control
Manager with a 15-second start timeout; a fake stands in for tests. The decision to make it its own
seam, to call it from the orchestrator (not the shells), and to treat a failed start as reported-
never-fatal is recorded in [[ADR-016]].

The call site is one line in `RestoreOrchestrator.Restore`, immediately before `LaunchByAppId`:
`service?.EnsureStarted()`. The `?` is deliberate — the parameter is optional, so a caller that does
not care (a test harness, a future script) passes nothing and the relaunch is exactly as it was.
The real `WaveLinkService` is wired at every production site: in-process restore, elevated restore,
and the CLI's `CommandRunner`.

**The trash-empty progress is an optional callback on a Core method.** `SnapshotStore.EmptyTrash`
gained an `IProgress<(int Done, int Total)>?` parameter (default null) and reports `(i+1, total)`
after each successful removal. Backward-compatible: the CLI and every existing caller are untouched.
The App side captures the total up front, sets a `TrashEmptyProgress` model on the view model, and
runs the empty on a thread-pool task with a `Progress<>` that marshals reports back to the UI
thread. The bar is determinate — it knows the total before it starts — and the fill width is a
`MultiBinding` over the fraction, drawn in theme brushes only (the theme guard holds).

**The stale trash row was a one-line bug with a two-hour smell.** `SettingsViewModel.TrashRow` was
an auto-property (`{ get; set; }`), so its setter never raised `PropertyChanged`. The first bind
coincided with the window opening, which is why the value looked right at first and wrong after
every later re-assignment — initial open, folder change, and the post-empty refresh all wrote a new
object into a field the binding engine could not hear about. Converting it to a notifying property
via the class's existing `Set` helper fixed all three write sites at once, with no XAML changes.
The plausible-but-wrong explanation — that the refresh was computing the wrong count — is why this
one costs time: the data in memory was always correct; only the screen was stale. Recorded as a
gotcha.

## Decisions made

| Decision | Recorded in |
|---|---|
| The service is its own seam beside `IWaveLinkProcess`, not a method on it — different API, different failure shapes | [[ADR-016]] |
| The orchestrator owns the "service, then app" ordering; shells only hand it a real `WaveLinkService` | [[ADR-016]] |
| A failed service start is reported, never fatal — the settings are already written by that point | [[ADR-016]] |
| Progress reporting is an optional callback on `EmptyTrash`, not a new method or a second overload | this session (the parameter defaults to null; no caller changes) |
| The determinate bar is driven by a model record (`TrashEmptyProgress`), not raw ints on the view model | this session |

## What did not work

- **The first progress test used `System.Threading.Progress<T>` directly and the reports never
  arrived on the test thread.** `Progress<T>` captures the synchronization context at construction;
  on a bare test thread there is none, so its callback was posted to a context that never pumped.
  The fix is a local synchronous fake implementing `IProgress<(int,int)>` that records into a list —
  no marshalling, deterministic, and it asserts on the exact sequence of `(Done, Total)` pairs.

- **The trash-row fix was chased in the data first.** The instinct was to doubt the count and size
  recomputation after emptying; both were correct. The defect was one declaration away — an
  auto-property instead of a notifying one — and no amount of re-reading the trash would surface it.
  It is now pinned by a test that asserts the **rendered** text changes, not just the in-memory value.

## Verification

Build zero warnings; full suite green — **Core 504 passed / 1 skipped, App 994, CLI 100** (up from
1,587 total with the new orchestrator and trash tests). The theme guard is green: the progress bar's
fill uses only `WlDanger`/`WlLine`, no hex literals in view XAML.

## Version cut

Written against an `[Unreleased]` heading in [CHANGELOG.md](../../CHANGELOG.md) and renamed to
`[0.7.4] - 2026-08-24`; `<Version>` in [Directory.Build.props](../../Directory.Build.props) bumped
from `0.7.3` → `0.7.4`.

## Next

Commit the code, this note, the ADR, the gotcha and the stats update together; push and tag
`v0.7.4`. Nothing else outstanding from this session.
