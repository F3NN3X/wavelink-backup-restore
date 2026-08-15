---
title: "The restore writes cleanly, then the old settings come back"
status: published
created: 2026-08-16
updated: 2026-08-16
related_adrs: [ADR-002]
tags: [gotcha, restore, process]
---

# The restore writes cleanly, then the old settings come back

**Provenance:** **Read, not reproduced.** The rule comes from `SPEC.md` §4, which explicitly
corrects an earlier draft of itself, and from upstream's shutdown sequence — which gets this
right and documents why. The race has not been triggered deliberately here.

## Symptom

The restore reports success. The bytes were written, and re-reading the file immediately
afterwards confirms the new content.

Wave Link relaunches — and the mixer shows the *old* configuration. Reading `Settings.json`
again now shows the old content. Your write is simply gone, with no error anywhere.

## Cause

Wave Link was still exiting when you wrote, and **flushed its in-memory configuration on the
way out, over the top of your file.**

Closing a process is not instant. Between "close requested" and "process gone" the app is
still running, still holding its config in memory, and will write it out as part of a clean
shutdown. If your write lands inside that window, the app's flush wins because it happens
last.

The window is small, which is the worst possible size: large enough to happen, small enough
that it will not reproduce while you are watching.

## The plausible explanation, and why it is wrong

> *"The write failed silently — a permissions or file-locking problem."*

The write succeeded. You can prove it by reading the file back, which is exactly what makes
this so disorienting: verification immediately after the write **passes**, and the file is
wrong thirty seconds later.

There is a second, more tempting wrong turn, and an earlier draft of `SPEC.md` made it:

> *"Force-kill the process, never let it quit gracefully — then it can't flush over us."*

That is an overstatement, and it trades one problem for a worse one. A force-kill denies the
app the chance to checkpoint cleanly, which risks leaving other state inconsistent. Upstream
does the right thing: close gracefully, allow 10 seconds, force-kill **only** on timeout.

**The invariant is exit, not kill method.** A graceful flush is harmless if it happens before
your write and fatal if it races it. What matters is not *how* the process ended but that it
*has* ended, confirmed, before a single byte is written.

## Fix

Order, with the reason attached where the order is load-bearing:

1. **Validate the source first** ([[file-parses-but-wave-link-resets]]) — before anything is
   closed. Restoring a file the app will reject looks identical to the snapshot being broken.
2. **Close both processes** — `Elgato.WaveLink` **and** `WavelinkSEService`. Two processes,
   and the service is the one that gets forgotten.
3. **Wait for exit, then re-check `IsRunning`.** ← the fix. Not a sleep: an assertion.
4. **Snapshot the current file**, even though it is the bad one. Rollback and evidence.
5. **Write atomically** — temp file in the same directory, then
   `File.Replace(temp, target, backupPath)`. Atomic on NTFS, and it produces the rollback copy
   in the same operation, so there is no window where the target is half-written.
6. **Relaunch via the shell AppID** — `shell:AppsFolder\<packageFamilyName>!App`. An MSIX app
   cannot be started from its `.exe` path.
7. **Verify from the new log**, not from the UI.

```csharp
await process.CloseGracefullyAsync(TimeSpan.FromSeconds(10));
if (process.IsRunning) process.KillTree();
if (process.IsRunning) throw new RestoreAbortedException("Wave Link did not exit.");
// only now
fileOps.ReplaceAtomic(tempPath, settingsPath, rollbackPath);
```

A sleep is not a substitute for step 3. `Start-Sleep -Milliseconds 1500` is fine in a one-off
script and is not a utility's exit condition — on a loaded machine the exit takes longer, and
a fixed sleep fails exactly when the machine is under the kind of load that caused the
problem.

## How to avoid it

- **Make "verified exited" a precondition of the write function**, not a step the caller is
  trusted to have performed. Enforce it at the boundary and the race cannot be reintroduced by
  a future caller.
- **Test it through `IWaveLinkProcess`** — a fake that reports `IsRunning == true` after a
  close must make the write throw. That is the whole seam's purpose ([[ADR-002]]).
- **Never verify a restore from the UI.** A mixer that looks correct can be a freshly
  generated default. Verify from the log.

## References

- `SPEC.md` §4, §7
- [[ADR-002]] · [[restore-a-settings-file-safely]] · [[newest-backup-is-the-broken-one]]
- [glossary.md](../../glossary.md) — *verified exited*, *atomic write*, *shell AppID*
