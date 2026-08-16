---
title: "Phase 3 — Automation: watcher, dedup, retention"
status: published
created: 2026-08-16
updated: 2026-08-16
related_adrs: [ADR-007]
tags: [dev-phase]
---

# Phase 3 — Automation: watcher, dedup, retention

**Status:** ✅ **Complete — 2026-08-16.** 235 tests green, 84.9% line / 81.8% branch, every
`Automation/` component at 100% line. [technical-debt.md](../technical-debt.md) §1.4 closed.
See the [session note](../sessions/2026-08-16-phase-3-automation-build.md).

> **The constraint held:** the full suite runs in about a second. No test waits on a policy
> interval, because `AutoBackupPolicy` is pure and the coordinator owns no timer.

**Entry criteria:** phase 2 complete. ✅ 2026-08-16.
**Exit criteria:** the app can run unattended for a week, capture every distinct
configuration, store no duplicates, and prune correctly — never touching a manual or
pre-restore snapshot.

## Why this phase exists

**This is the phase that turns the fork into a different product.** Everything so far, a
person could do by hand. Upstream already backs up on demand; the gap this project exists to
fill is that a bad config surviving a long weekend has no good copy left by the time anyone
notices ([[ADR-007]]).

After this phase the promise in the README — *configured once, then ignored until the day it
saves your rig* — is actually true.

## Scope

### In

- `FileSystemWatcher` on `LocalState`, filtered to `Settings.json`.
- Debounce (~60s after the last write) and rate limiting (at most one automatic snapshot per
  hour).
- **Dedup by `settingsSha256`** — recorded in phase 2, consulted here for the first time.
- Capture on shutdown, ours and Wave Link's.
- Retention: prune automatic snapshots to a configured count, default 30.
- A settings object for the store path, auto-backup toggle, keep-count.

### Out — and where it went instead

- CLI verbs → **phase 4**. Tests drive the watcher directly.
- Any UI → **phase 5**. The Settings dialog's copy describes this phase's behaviour, so the
  wording and the implementation have to agree — see the note below.
- Tier 2–4 capture → **phase 6**.

## Work

### 1 · The watcher

`FileSystemWatcher` on `LocalState`, filtered to `Settings.json`. **Behind a seam** —
`ISettingsWatcher` — because a test must be able to raise "the file changed" without a real
filesystem event, and because the watcher has to be startable and stoppable from a shell that
does not exist yet.

`FileSystemWatcher` can miss events under load or buffer overflow. **Treat a missed event as a
latency problem, not data loss:** the next write, the next shutdown or the next launch
reconciles by hash. Do not build a retry around it; do log it.

### 2 · Debounce and rate limit

Wave Link writes the file the moment a channel is touched, often several times in a burst.

- Wait **~60s after the last write** before capturing.
- Capture **at most one automatic snapshot per hour**.

Both are `IClock`-driven, which is why the seam exists. Neither may be implemented with
`Task.Delay` in a way a test has to wait through — a test suite that takes 60 seconds to prove
a debounce is a test suite nobody runs.

> **The Settings dialog already describes this behaviour in user-facing copy:** *"Wave Link
> writes its file the moment you touch a channel. This notices, waits a minute, then keeps a
> copy — at most one an hour."* If the debounce or the rate limit changes, **that sentence
> changes with it.** The copy is a specification, not decoration.

### 3 · Dedup

Before writing, compare the candidate's SHA-256 with the newest snapshot's `settingsSha256`.
Equal means skip the write entirely.

This is the whole reason the store can keep snapshots indefinitely: Wave Link rewrites
`Settings.json` on every launch with near-identical bytes, and without dedup the store fills
with thousands of identical 43 KB copies.

**Dedup applies to automatic captures. A manual "Back up now" always writes** — the user asked,
and refusing with "nothing changed" is a worse experience than a duplicate row.

### 4 · Retention

Prune **automatic** snapshots to the configured count, default 30.

**Never prune `Manual` or `PreRestore`, at any count, ever.** `SnapshotManifest.IsPrunable`
already encodes this and is covered by a phase 2 test; phase 3 must consult it rather than
re-deriving the rule.

Time-based retention is explicitly rejected: it deletes the last good config from four months
ago while keeping ninety identical copies from this week. Hash-dedup plus a count already
produces the right behaviour, because identical days cost nothing.

### 5 · Capture on shutdown

The original incident happened during an update, while the app was restarting. A strategy that
only captures during steady-state operation misses the exact moment that matters.

## Testing

**No test may depend on real elapsed time.** Everything runs off `FakeClock` and a fake
watcher raising events directly.

| Test | Pins |
|---|---|
| A burst of five writes in ten seconds produces **one** snapshot | Debounce |
| Two changes 30 minutes apart produce one automatic snapshot | Rate limit |
| A manual capture during the rate-limit window still writes | Manual is not rate-limited |
| Identical content produces no second snapshot | Dedup |
| Identical content via **manual** capture *does* write | The dedup exception |
| 31 automatic snapshots prune to 30 | Retention |
| 40 manual snapshots prune to 40 | **Manual is never pruned** |
| A pre-restore snapshot survives pruning at any count | Same |
| A missed watcher event is reconciled on the next capture | Not data loss |
| Pruning removes the **oldest** automatic snapshot, not the newest | Obvious, and easy to invert |

**Coverage ≥80% line and branch.** Phase 2 showed line coverage rising while branch coverage
fell; watch both.

## Risks

| Risk | Early signal | Response |
|---|---|---|
| Tests that actually wait | A test taking more than a second | All timing through `IClock` |
| Dedup silently skipping a manual capture | "Back up now" appearing to do nothing | Manual bypasses dedup, and there is a test |
| Retention pruning a manual snapshot | Any prune path not consulting `IsPrunable` | It is one property; use it |
| The Settings copy drifting from behaviour | A constant changed without touching the string | Both live in this phase; change them together |
| Watcher complexity leaking into the store | `SnapshotStore` gaining timing logic | The store writes when told; deciding when is this phase's job |

## References

- [[ADR-007]] — the decision this implements
- `SPEC.md` §2, §6 · [design-handoff.md](../operations/design/design-handoff.md) — Screen 3, *Interactions*
- [technical-debt.md](../technical-debt.md) §1.4
