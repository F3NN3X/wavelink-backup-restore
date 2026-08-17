---
title: "Session: Phase 5 part 1 — the Core changes, and a design that answered back"
status: published
created: 2026-08-17
updated: 2026-08-17
related_adrs: [ADR-003, ADR-004]
tags: [session, core, phase-5]
---

# Session: Phase 5 part 1 — the Core changes, and a design that answered back

**Date:** 2026-08-17

Two commits. **351 tests green** (266 Core, 85 CLI), Release clean with zero warnings after
each. Core **85.7% line / 82.3% branch**, CLI **84.1% / 82.0%**. NativeAOT still publishes at
**3.2 MB** despite gaining shell interop. Working tree clean, `main`, v0.4.0.

Governing plan: [dev-phases/phase-5-wpf.md](../dev-phases/phase-5-wpf.md).
Design: [operations/design/screens/](../operations/design/screens/00-index.md) at v5.

## What shipped

**Three of the four Core changes phase 5 depends on** — §7.1, §7.2 and §7.3 of
[technical-debt.md](../technical-debt.md). Only §7.4 (keyboard and focus) remains, and it is
WPF work that arrives with the shell.

**Two-stage delete.** `SnapshotStore.Delete` moves a snapshot to `<store>/.trash/<id>/` — a
plain directory move, no interop, no rename. It stays a *verifiable* snapshot there, because
ids carry a timestamp plus a content hash and identity lives in the manifest ([[ADR-003]]).
`EmptyTrash` forwards to the Recycle Bin behind a new `IRecycleBin`, and deletes outright where
no Recycle Bin exists. `TrashGoesToRecycleBin` lets a caller ask **before promising an undo**,
which is what the CLI copy now does. Smoke-tested end to end on the real AOT binary.

**Lazy prune verification.** The pruner verifies only the candidates it is about to delete and
refuses any that fail, so a corrupted backup never pushes a good one out. A test asserts
**exactly one** `settings.json` read while pruning one of five — that number is the whole
argument that this does not reintroduce the cost phase 2 declined to pay.

**The watcher stopped queuing.** `Tick` clears `lastWriteAt` on failure and `TickResult` now
carries the `CoreError`. The error is not plumbing: it is what feeds the tray's `NEEDS YOU`
state, which was previously unreachable.

**Design v5 integrated**, closing the loop on the amendment. The trash decision is now
*upstream* rather than only in this repo, so the code and the design no longer disagree.

## What broke, and what it taught

**The design answered back, and was right.** `screens/08` specifies **no confirmation on a
local drive** for Empty trash — *"a dialog guarding a reversible action is the noise that
teaches people to click through the ones that matter."* My CLI confirmed unconditionally. Found
by reading the spec rather than assuming it matched what I had already built.

**The designer solved a sentence I had flagged as possibly unsolvable**, and declined the
fallback I offered:

> *"After that it is gone" is exactly true on a network share and slightly **pessimistic** on
> a local disk… Pessimism is the safe direction in a destructive dialog, and it is the one
> sentence that never breaks on any volume.*

They also framed the underlying reason better than I had: after the change, pressing Delete
puts nothing in the Recycle Bin **for anyone** — so it was never a network edge case, it was
that the old sentence became false universally.

**Two of my own test-design bugs, both instructive.** `CaptureAutomatic` prunes internally, so
the first prune tests asserted against a store that had already been pruned — the harness now
builds with a generous keep-count and prunes through a second service. And `FakeRecycleBin`
initially only *recorded* the call, but `SHFileOperation` **moves** the directory, so `Send`
*is* the removal and `EmptyTrash` must not delete afterwards. A fake that did not model that
would have let a double-delete through unnoticed.

**A list I nearly skipped.** `screens/05` closes with *".trash must be invisible to the list,
the search, every count and size readout, and the keep-count."* The obvious ones were already
handled, so writing tests felt redundant. Wrote five anyway; all passed first time. *"It
already works"* is not *"it is pinned"*, and each is a place where a trashed backup leaking
back would read as a bug in **deletion** rather than in **counting** — the dedup one least
obviously, since without it deleting a backup and immediately re-taking it would silently do
nothing.

**New gotcha:** [[deleting-one-backup-takes-its-neighbours]]. `SHFileOperation`'s `pFrom` is a
double-null-terminated *list*; a single terminator reads past the buffer and deletes whatever
follows. Never happened here — the sibling test was written alongside the code — but it is
guarded only by a test whose name nobody would search for, and the symptom is unrecoverable.

## Decisions

| Decision | Reasoning |
|---|---|
| **`RecycleBin` lives in Core**, not per-shell | §7.1 assumed interop needs the Windows Desktop ref pack. It does not — P/Invoke works from plain `net10.0`, and `GuardNoDesktopFramework` guards the *ref pack*. Per-shell would duplicate the interop; a fourth project for one class is worse than either. Core already shells to `explorer.exe` in `LaunchByAppId` |
| **`DllImport`, not `LibraryImport`** | The generator requires `AllowUnsafeBlocks` for the whole project. Granting unsafe to a conservative library for one call that runs when someone clicks *Empty trash* is a poor trade. Equally AOT-compatible — verified at 3.2 MB |
| **`SnapshotStore.Verify`** rather than threading `IFileSystem` into `BackupService` | The store already owns the filesystem; adding a dependency to build a guard is dependency for its own sake |
| **Confirm only where the Recycle Bin cannot catch it** | Following `screens/08` rather than my own instinct |

## Still open

- **§7.4 — keyboard and focus.** Windows conventions generally, plus screen-reader labels; the
  five-slot health strip reads as five unlabelled cells without an `AutomationProperties` name.
- **`watch` is the least-covered verb.** Its loop and `Ctrl+C` handling are hand-tested only.
  Phase 5 replaces its host, so it may be worth deleting rather than porting.
- **§2.4 `[ComImport]` under AOT** — unchanged. `RecycleBin` is P/Invoke, not COM, so it does
  not answer the question.
- Nothing in `.trash` is surfaced to a user yet beyond the CLI. The Settings row is phase 5.

## Next

The tray shell: `ShutdownMode.OnExplicitShutdown`, hide-on-close, single-instance, `--tray`,
and the icon's four states. `NEEDS YOU` is now reachable.

## References

- [phase-5-wpf.md](../dev-phases/phase-5-wpf.md) · [technical-debt.md](../technical-debt.md) §7
- `operations/design/screens/05-delete-dialogs.md` · `08-settings-persistence.md`
- [[deleting-one-backup-takes-its-neighbours]] · [[ADR-003]] · [[ADR-004]]
