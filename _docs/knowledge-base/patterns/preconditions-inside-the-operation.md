---
title: "Preconditions inside the operation, not in the caller"
status: published
created: 2026-08-16
updated: 2026-08-16
tags: [pattern, architecture, safety]
---

# Preconditions inside the operation, not in the caller

## Problem

Writing `Settings.json` while Wave Link is still exiting loses the write: the app flushes its
in-memory config on the way out, over the top of yours. The write succeeds, verifies, and is
gone thirty seconds later ([[restored-settings-revert-seconds-later]]).

The natural fix is a documented sequence, *close, verify, then write*. Upstream does this,
and repeats the same four lines at five call sites:

```csharp
if (process?.IsRunning == true)
{
    if (!process.CloseGracefully(TimeSpan.FromSeconds(10))) process.KillTree();
    if (process.IsRunning) throw new InvalidOperationException("Wave Link is still running…");
}
```

Five copies is five chances to omit the third line, and a sixth caller added later inherits
none of them. The knowledge lives in the callers, where it has to be remembered.

## Solution

Make the dangerous operation **refuse to proceed** when its own precondition is unmet, and
return that refusal as an ordinary expected failure.

```csharp
// src/WaveLinkBackup.Core/Io/SettingsWriter.cs
public Result Write(SettingsLocation location, byte[] content)
{
    // PRECONDITION, not a caller's duty.
    if (process.IsRunning) return new WaveLinkStillRunning(process.RunningProcessNames);
    ...
}
```

`SettingsWriter` does not close Wave Link, closing is the caller's job, and phase 2 will
orchestrate it. What it guarantees is that a write *cannot happen* while Wave Link is up,
regardless of what the caller did or forgot.

Note this is not an assertion or an exception. It is a `Result`, because a caller racing a
restart is a situation to report, not a bug to crash on.

**It is also not a sleep.** A fixed delay fails exactly under the load that causes the race;
`IsRunning` is checked, not waited out.

## Callers

| Where | Why it uses this |
|---|---|
| `src/WaveLinkBackup.Core/Io/SettingsWriter.cs:Write` | Defines the precondition |
| `src/WaveLinkBackup.Core/Process/WaveLinkProcess.cs:CloseAndVerifyExited` | Same shape one level down: closes, kills on timeout, then **verifies** and returns `WaveLinkStillRunning` rather than trusting the kill |

## Held down by

- `tests/WaveLinkBackup.Core.Tests/SettingsWriterTests.cs:Refuses_to_write_while_Wave_Link_is_running`,
asserts the write is refused **and** that the file on disk is untouched.
- `…:A_process_that_survives_the_kill_blocks_the_write`, the compound case: a close that
  reports failure must not be followed by a successful write.
- `FakeWaveLinkProcess.StaysRunningAfterClose` exists solely to make this reachable, which is
  what the seam is for.

## When not to use it

When the check is expensive and the caller already knows the answer. `IsRunning` is a process
enumeration, microseconds, so paying it on every write is free.

Also not for preconditions that are genuinely a caller's decision: `SettingsWriter` validates
that *content is parseable* but takes no view on whether it is the content the user wanted.

## References

- [[restored-settings-revert-seconds-later]]: the race
- [[restore-a-settings-file-safely]]: the full sequence this is one step of
- [Audit](../../audits/2026-08-15-voltybat-wavelinksettingsutility.md), upstream's duplicated version
