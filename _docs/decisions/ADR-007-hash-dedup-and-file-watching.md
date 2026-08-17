---
title: "ADR-007: Content-hash dedup and a file watcher, not a schedule"
status: accepted
created: 2026-08-16
updated: 2026-08-16
tags: [decision, capture, automation]
---

# ADR-007: Content-hash dedup and a file watcher, not a schedule

**Status:** Accepted
**Date:** 2026-08-16

## Context

The product is *"configured once, then ignored until the day it saves someone's rig"*. That
sentence is the whole requirement, and it rules out anything the user has to remember to do —
which is what upstream is ([[ADR-002]]), and the gap this project exists to fill.

Two facts shape how automatic capture must work:

**Wave Link rewrites `Settings.json` on every launch**, usually with near-identical bytes.
Capture on every write and you accumulate thousands of identical 43 KB copies.

**The failure this app protects against happens during restarts.** The original incident
occurred during an update, while the app was restarting — so a strategy that only captures
while the app runs steadily misses the exact moment that matters.

And one constraint that makes the whole thing cheap: **the payload is 43 KB.** A year of
daily changes is under 16 MB. There is no reason to be clever about retention when storage is
this irrelevant — the design target is one snapshot per distinct content hash, kept
indefinitely.

## Decision

**Watch, don't poll.** A `FileSystemWatcher` on `LocalState`, filtered to `Settings.json`.

**Dedup by content hash.** `settingsSha256` in the manifest ([[ADR-003]]) is the key. When a
new capture's hash matches the newest existing snapshot, skip the write entirely.

**Debounce, then rate-limit.** Wave Link writes the file the moment a channel is touched, and
often several times in a burst. Wait ~60s after the last write, then capture at most one
automatic snapshot per hour.

**Capture on shutdown too** — the app's own and ours. That is when the original incident
happened.

**Prune automatic snapshots only.** Default keep-count 30. **Manual and pre-restore snapshots
are never pruned**, at any count, ever. A user who names a snapshot has told you it matters.

## Alternatives considered

| Option | Why not |
|---|---|
| **Manual only** | What upstream is. It fails the exact scenario the project was built for: a bad config surviving a long weekend unnoticed, by which time every good copy has aged out. |
| **Scheduled (Task Scheduler / timer)** | Captures at times unrelated to when anything changed, so it both misses bursts and wastes captures on idle hours. It also needs a scheduled-task registration to explain, install and uninstall. The watcher needs none. |
| **Poll every N minutes** | Strictly worse than the watcher on both latency and cost, with no compensating simplicity — `FileSystemWatcher` is first-party and the file is in one known directory. |
| **Capture every write, dedup on read** | Thousands of identical directories, each with a manifest, making the list load slow and the store unreadable in Explorer. Dedup at write is the same check done once. |
| **Time-based retention** ("keep 90 days") | Uniform-looking and wrong: it deletes the last good config from four months ago while keeping ninety identical copies from this week. Hash-dedup plus a count already produces the right behaviour, because identical days cost nothing. |

## Consequences

**This enables:** the "ignore it until you need it" promise; a store where every entry is a
*distinct* configuration, which is what makes the list scannable; and months of history at
trivial cost.

**This rules out:**

- Capturing a change the user made and immediately reverted within the debounce window. That
  is a deliberate trade — capturing it would mean capturing every intermediate state of a
  fader drag.
- A useful automatic capture in the first 60 seconds after a change. The design accounts for
  this: **"Back up now" is always enabled**, so the impatient path is one click.
- Relying on the watcher alone for correctness. `FileSystemWatcher` can miss events under
  load or buffer overflow. Treat a missed event as a *latency* problem, not a data-loss one —
  the next write, the next shutdown or the next launch reconciles by hash.

**This creates an obligation:** the watcher must run headless, which is why Core carries no UI
reference ([[ADR-004]]).

**Rate limits are user-visible and must be worded honestly.** The Settings copy says it
"notices, waits a minute, then keeps a copy — at most one an hour". If the debounce or the
rate limit changes, that sentence changes with it.

**Revisit if:** `FileSystemWatcher` proves unreliable enough under real use that missed events
become common rather than occasional, at which point a low-frequency reconciliation sweep
backs it up rather than replaces it.

## References

- `SPEC.md` §2, §6
- [README.md](../operations/design/README.md) — Screen 3 "When to back up", *Interactions*
- [[ADR-002]] · [[ADR-003]] · [[ADR-004]]
