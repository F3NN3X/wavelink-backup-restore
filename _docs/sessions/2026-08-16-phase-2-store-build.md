---
title: "Session: Phase 2 — the critical inherited defect is fixed"
status: published
created: 2026-08-16
updated: 2026-08-16
related_adrs: [ADR-003]
tags: [session, store, phase-2]
---

# Session: Phase 2 — the critical inherited defect is fixed

**Date:** 2026-08-16

## Goal

Build the snapshot store and the assembled restore sequence, per
[the phase 2 design](../plans/2026-08-16-phase-2-store-design.md).

## Result

| | |
|---|---|
| Tests | **186 passing** (up from 93), 0 failing, 0 skipped |
| Coverage | **83.0% line, 81.2% branch** |
| Release build | 0 errors, 0 warnings |

**[technical-debt.md](../technical-debt.md) §1.1 — the critical inherited defect — is closed.**
Snapshots live outside `LocalState`, and the property is pinned by a test that deletes the
entire `LocalState` directory and then verifies the snapshot still restores. Upstream's
backups would have gone with it.

## What happened

### The design was wrong about `waveLinkVersion`, and probing found a better answer

The design said the version "needs reading from the package manifest". Three sources were
checked before writing any code:

| Source | Result |
|---|---|
| `Settings.json` → `Update.LastUpdateVersion` | ✅ `3.3.0.4108` — **already in the file we read** |
| Log banner → `VERSION 3.3.0.4108 (Beta)` | ✅ authoritative for the running app, **plus the channel** |
| `C:\Program Files\WindowsApps\` | ❌ `UnauthorizedAccessException` — ruled out |

So the design's premise was wrong twice: `WindowsApps` is unreadable without elevation, and
WinRT `PackageManager` — the alternative — would have forced `net10.0-windows` on a library
that deliberately needs none, undoing phase 1's headless and AOT work.

Version extraction is now **pure**, in the `Analysis` layer, at zero IO cost. And the log
banner's `(Beta)` marker turned out to be the more valuable half: `SPEC.md` §5 says beta
channels ship new validators, and that is the first question when a restore fails.

### The guard catches something the regex never could

Upstream refuses to restore anything not matching
`^Settings\.json\.backup-\d{8}-\d{9}$` beside `Settings.json`. Replacing the *name* check with
a *contents* check was expected to be a lateral move — same safety, fewer constraints.

It is strictly better. `SnapshotGuard` verifies every recorded hash against the bytes on disk,
so it also catches a snapshot corrupted **after** it was written: a failed sync, a bad disk, or
a user editing a backup by hand. A filename cannot express that. Two tests pin it — one
tampering with content at identical length so size alone would not notice.

### Rename became a non-event, which was the point

`Rename` writes `manifest.json` and nothing else. The test asserts the file listing is
byte-identical before and after, and that `Mic chain 3/4"` round-trips. That property is the
whole reason the display name never appears in a path, and it took no special handling —
because the design put identity in the right place.

## Decisions made

| Decision | Recorded in |
|---|---|
| `waveLinkVersion` comes from `Update.LastUpdateVersion`, not the package | Design *as built* delta |
| Listing reads manifests only, never rehashes; verification is restore-time | `SnapshotStore.List` |
| One unreadable snapshot is skipped, not fatal to the listing | `SnapshotStore.List` |
| Settings written before the manifest, so a torn write fails the guard | `SnapshotStore.Write` |

## What did not work

**All 13 orchestrator tests failed at once**, and the cause was my own test wiring:
`SettingsInspector(IFileSystem)` is a convenience constructor that resolves `%LOCALAPPDATA%`
from the **real** environment, so it never saw the fake tree. The explicit constructor exists
for exactly this. Worth noting as a small trap: a convenience overload that reaches into the
environment is invisible at the call site.

**Branch coverage dropped to 75.7% while line coverage rose to 85.6%.** Adding the manifest
layer added a lot of validation branches — every "field missing", "wrong type", "unknown
trigger" — and none were exercised by the happy-path serializer tests. Chasing *line* coverage
would have missed it entirely. 36 further tests brought branch to 81.2%, and several of them
found real behaviour worth pinning: an unknown trigger must be **rejected**, not defaulted,
because defaulting would silently turn a pre-restore snapshot into a prunable one.

**A duplicated line survived into a commit-ready state** — `Store = new SnapshotStore(...)`
written twice in a test harness, the first using the property instead of the constant. The
compiler caught it. Mentioned only because it is the second time this session that a
same-named property and constant have confused a call site.

## Open questions

- **Dedup is recorded but not consulted.** `settingsSha256` is in every manifest and nothing
  reads it yet — deliberately, since deciding *when* to capture is phase 3.
- **`SnapshotId.LooksLikeSnapshotId` is unused in production.** It exists as a cheap listing
  filter and `List()` does not currently call it, because reading the manifest is already the
  authoritative test. It is tested and harmless, but it is a method looking for a caller —
  either phase 3 uses it or it should go.
- **§2.4 (`[ComImport]` under AOT)** remains untouched. Still no COM interop.

## Next

Phase 3: the watcher, dedup and retention — the phase that turns the fork into a different
product. Planned in [dev-phases/phase-3-automation.md](../dev-phases/phase-3-automation.md).

## References

- [Phase 2 design](../plans/2026-08-16-phase-2-store-design.md) — with its *as built* delta
- [[ADR-003]] · [technical-debt.md](../technical-debt.md) §1.1
- [[restore-a-settings-file-safely]] — the sequence this assembles
