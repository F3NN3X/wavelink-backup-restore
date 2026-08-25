---
title: "ADR-016: A restore brings the service back before it relaunches"
status: accepted
created: 2026-08-24
updated: 2026-08-24
related_adrs: [ADR-004, ADR-011]
tags: [decision, restore, process, windows-service]
---

# ADR-016: A restore brings the service back before it relaunches

**Status:** Accepted
**Date:** 2026-08-24

## Context

A restore closes **both** of Wave Link's processes, the app and its background service,
`WavelinkSEService`, because the settings file is only safe to write while nothing has it open
(SPEC.md §4). It then relaunches the app. Left as it was, that relaunch came up against a machine
where the service was still down: Wave Link's own startup check found no `WavelinkSEService` and
showed its "Start Service / Exit App" box. The restore had done everything right and still ended
with the user staring at a dialog they could not tell apart from a broken install.

So there is a question of **where** to put the service back, and a second of **what to do when it
cannot be put back**.

The rights shape matters. `WavelinkSEService` runs as LocalSystem, and starting a Windows service
needs rights an ordinary user process does not hold. But a restore already runs elevated. It is
closing a System process, which it cannot do without that elevation ([[ADR-011]]). So on the path
where it actually matters, the start succeeds with no second prompt. On the paths where it does
not (a CLI run as a plain user). It will fail with access denied, and that failure has to mean
something rather than being swallowed.

## Decision

**A new seam, `IWaveLinkService`, sits beside `IWaveLinkProcess` in Core.** It exposes `Exists`,
`IsRunning`, and one action, `EnsureStarted()`, which starts the service if it is not running and
waits for it to come up. The real implementation (`WaveLinkService`) goes through the Service
Control Manager; a fake stands in for tests.

**The orchestrator calls `service?.EnsureStarted()` immediately before it relaunches the app**,
after the settings are written, before `LaunchByAppId`. That is the one moment where "the service
is up" changes what the user sees: any earlier and a later step could still drop it, any later and
the app has already come up against its absence.

**A failed start is reported, never fatal.** The settings file, the product of the restore, is
already written by the time this runs. A service that will not start (no rights, a dependency that
will not come up, a timeout) does not roll any of that back; it means the user may see Wave Link's
own prompt, which is exactly the state they were in before this seam existed. The restore proceeds
and launches the app regardless. A machine with no Wave Link at all, `Exists` false, is a
no-op that succeeds: there is nothing to start, and nothing for the relaunch to complain about.

**The service is its own seam, not a method on `IWaveLinkProcess`.** The two are different kinds of
thing. The process is closed and verified *by name*; the service is started *through the Service
Control Manager*, a different API with different failure shapes (a service that will not start
reports a timeout or an access-denied, not a still-running process). Merging them would force one
interface to speak two dialects of "process".

## Alternatives considered

| Option | Why not |
|---|---|
| Start the service from the relaunch step itself (`LaunchByAppId`) | Couples two unrelated lifecycles into one call and hides the ordering. The relaunch is about *the app*; starting a System service is a different act with its own rights and failure modes. Keeping them separate is what lets the orchestrator say "service, then app" in two readable lines. |
| Make a failed start fatal to the restore | The settings are already written. Failing here would roll back nothing and report a failure for a state that is no different from "Wave Link shows its own box". That is a refusal that changed nothing, the exact shape [[ADR-011]] exists to avoid. |
| Prompt the user to start the service themselves | The restore already holds the elevation that would start it; asking a human to do what the code can do, at the one moment they are not looking, is noise. And on an unelevated CLI run there is no prompt to offer, access denied is the whole story. |
| Put the call in the shells (App / CLI) rather than the orchestrator | Both shells would have to remember it, and a third caller (a future script, a test harness) would not. The orchestrator is where "close both, write, relaunch" already lives; the service belongs to that sequence, not to whoever happens to be driving it. |
| Start the service *before* closing Wave Link | A step in between could still leave it down, and starting it early buys nothing, the app that matters is the one relaunched at the end. |

## Consequences

**This enables:** a restore that ends with Wave Link actually usable, no "Start Service" box on
the elevated path where most restores run; a clean, testable seam for the service lifecycle (a fake
stands in, so the orchestrator's ordering is asserted without a real SCM); and an honest report on
the unelevated path instead of a silent swallow.

**This rules out:** promising that Wave Link will never show its own service prompt. On a machine
where the start is denied or times out, the user sees exactly what they always did. The seam makes
the common case clean; it does not and cannot make the uncommon one disappear.

**It costs one service start on every restore that relaunches.** Typically a no-op when the
service is already running, and a 15-second-bounded wait otherwise. A restore is a deliberate,
infrequent action; the wait is bounded and runs while nothing else is in flight.

**The failure type is a `CoreError`, not an exception.** `WaveLinkServiceStartFailed` carries the
reason (access denied, dependency, timeout) so a caller that surfaces failures can say *why* the
user might still see the box. It is a `record`, sealed, in Core, no Windows-only type leaks into
the error's shape, which keeps it usable from any shell.

**The class is `[SupportedOSPlatform("windows")]`**, because `ServiceController` is. Core targets
plain `net10.0`; the attribute is what lets a Windows-only API live in a cross-platform-targeted
library without an analyzer error on a non-Windows build. The fake does not carry it, so tests run
anywhere.

**Revisit if:** Wave Link ever stops having a separate service (a single-process build), at which
point the seam has nothing to start and should be deleted rather than left as a no-op ceremony, or
if a restore path appears that runs *unelevated* but still needs the service up, which would mean
the "elevation already held" assumption this decision leans on is not actually universal.

## References

- [[ADR-004]]. Core does the thinking; the shells stay thin. The orchestrator owns the sequence;
  the App and CLI only hand it a real `WaveLinkService`.
- [[ADR-011]]: elevation, probed rather than assumed. A restore that closes a System process is
  already elevated, which is why the start succeeds there without a second prompt.
- `src/WaveLinkBackup.Core/Process/IWaveLinkService.cs` ·
  `src/WaveLinkBackup.Core/Process/WaveLinkService.cs` ·
  `src/WaveLinkBackup.Core/Restore/RestoreOrchestrator.cs`
