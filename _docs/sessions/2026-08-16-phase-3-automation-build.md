---
title: "Session: Phase 3 — it now backs up on its own"
status: published
created: 2026-08-16
updated: 2026-08-16
related_adrs: [ADR-007]
tags: [session, automation, phase-3]
---

# Session: Phase 3 — it now backs up on its own

**Date:** 2026-08-16

## Goal

Build the watcher, debounce, rate limit, dedup and retention, per
[the phase 3 plan](../dev-phases/phase-3-automation.md).

## Result

| | |
|---|---|
| Tests | **235 passing** (up from 186), 0 failing, 0 skipped |
| Coverage | **84.9% line, 81.8% branch** |
| `Automation/` components | **100% line**, 88–100% branch |
| Release build | 0 errors, 0 warnings |

**[technical-debt.md](../technical-debt.md) §1.4 is closed.** Upstream is a manual tool;
backups happen only when invoked. That gap is what this project exists to fill, and it is now
filled. Everything before this phase, a person could have done by hand.

## What happened

### The suite stayed instant, which was the design constraint

The plan opened with *"no test may depend on real elapsed time"*. That held: **235 tests run
in about a second**, and the automation tests are the fast ones.

It worked because `AutoBackupPolicy` is a pure function of three timestamps — `lastWriteAt`,
`lastAutoCaptureAt`, `now` — and `AutoBackupCoordinator` **does not own a timer**. It exposes
`Tick()`; the host decides when to call it. In production that is a shell's timer; in tests it
is a method call after moving `FakeClock`. There is no delay to wait through because there is
no delay.

The only real waits in the whole suite are in `FileSystemSettingsWatcherTests`, and they wait
for OS filesystem events rather than for a policy interval — the only way to know the
`NotifyFilter` set is actually right.

### Two "edge cases" that are the whole point

**A skipped duplicate must not restart the rate limit.** Wave Link rewrites `Settings.json` on
every launch with near-identical bytes. If a skipped duplicate reset the hour, a launch-time
rewrite would mask a genuine edit made moments later — the exact configuration change worth
capturing. So `lastWriteAt` clears on any capture attempt, but `lastAutoCaptureAt` advances
only when bytes were actually stored. One test pins it.

**Manual capture is never deduplicated.** The user pressed a button. Answering *"nothing
changed, so I did nothing"* makes it feel broken — and the design says success is quiet, that
**the new row appearing IS the confirmation**. That only works if a row appears. The dedup rule
and its exception live in the same class so they cannot drift apart.

### The watcher needed more than `LastWrite`

Filtering on `NotifyFilters.LastWrite` alone is the obvious choice and it is wrong here. Wave
Link's atomic-save **replaces** `Settings.json` rather than writing through it, so a
LastWrite-only watcher would miss exactly the saves that matter most — the ones written
carefully. The filter also includes `CreationTime` and `FileName`, and there is a test that
performs a real `File.Replace` against a temp directory to prove it.

`FileSystemWatcher.Error` — buffer overflow under load — also raises a change rather than being
swallowed. A dropped burst then costs one extra evaluation instead of a missed snapshot.

## Decisions made

| Decision | Recorded in |
|---|---|
| The coordinator owns no timer; the host calls `Tick()` | `AutoBackupCoordinator` |
| A skipped duplicate clears the pending write but not the rate limit | `AutoBackupCoordinator.Tick` |
| Manual capture bypasses dedup; automatic does not | `BackupService` |
| Watcher filters on LastWrite + CreationTime + FileName | `FileSystemSettingsWatcher` |
| Pruning is best-effort per snapshot | `BackupService.Prune` |

## What did not work

**The xUnit analyzer failed the build, and it was right.** `ManualResetEventSlim.Wait` without
a `CancellationToken` trips `xUnit1051`, which `TreatWarningsAsErrors` turns into an error.
Passing `TestContext.Current.CancellationToken` makes the watcher tests cancellable rather than
hanging a run for ten seconds. Worth noting as a case where the strict-warnings setting paid
for itself rather than being an obstacle.

**A raw interpolated string literal did not survive contact with JSON.** `$$"""..."""` with
`{{name}}` inside a body full of braces produced `CS9007`. A plain `.Replace` on a raw literal
is duller and reads better.

**`FileSystemSettingsWatcher` was initially left at 0% coverage**, on the same reasoning that
excuses `WaveLinkProcess` at 5%. That reasoning does not transfer: closing the user's Wave Link
to test a shutdown is unacceptable, but *watching a temp directory is harmless*. The exemption
was laziness wearing a principle's clothes. Seven tests later it is at 100% line — and one of
them is what proved the `LastWrite`-only filter would have been a bug.

## Open questions

- **`SnapshotId.LooksLikeSnapshotId` still has no caller.** Flagged in phase 2, unchanged:
  phase 3 did not need it either. It should probably go in phase 4 rather than accumulate
  another phase of "not yet".
- **Nothing calls `Tick()` in production yet.** By design — the host is a shell, and there is
  no shell. Phase 4's CLI is the first real caller, and it will decide the interval.
- **Capture-on-shutdown has no caller either**, for the same reason.

## Next

Phase 4: the CLI — the first shell, and the first thing that makes any of this reachable
without writing C#. Planned in [dev-phases/phase-4-cli.md](../dev-phases/phase-4-cli.md).

## References

- [[ADR-007]] — the decision this implements
- [technical-debt.md](../technical-debt.md) §1.4
- [design-handoff.md](../operations/design/README.md) — Screen 3, whose copy is a
  specification this phase had to match
