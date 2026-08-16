---
title: "Session: Phase 4 — Core gets a caller, and AOT lands at 3.2 MB"
status: published
created: 2026-08-16
updated: 2026-08-16
related_adrs: [ADR-004, ADR-009]
tags: [session, cli, phase-4]
---

# Session: Phase 4 — Core gets a caller, and AOT lands at 3.2 MB

**Date:** 2026-08-16

## Goal

Build the CLI, per [the phase 4 plan](../dev-phases/phase-4-cli.md): make every Core
capability reachable from a command line, and finally exercise the NativeAOT question the
project has been protecting since phase 1.

## Result

| | |
|---|---|
| Tests | **308 passing** — 228 Core, 80 CLI |
| Coverage | Core **84.8% line / 81.6% branch** · CLI **83.6% / 81.6%** |
| Self-contained single file | **70.2 MB** |
| **NativeAOT** | **3.2 MB**, runs correctly against a real install |
| Release build | 0 errors, 0 warnings |

**Three phases of library finally have a caller.** `Tick()` and `CaptureOnShutdown()` had
never run outside a test until the `watch` verb.

The published binary was smoke-tested against the real Wave Link install: it discovered the
package, wrote a real backup, listed it with five named inputs, and emitted valid JSON — with
no device IDs anywhere in the output.

## What happened

### NativeAOT works, and ADR-001 was wrong about the size

3.2 MB, zero IL/trim warnings. [[ADR-001]] estimated 10–15 MB for AOT and credited Rust with
2–5 MB as **the one row Rust won**. That row is now roughly a tie, and the ADR records the
measurement rather than leaving the estimate standing.

**It does not close [technical-debt.md](../technical-debt.md) §2.4**, and saying otherwise
would be the more satisfying lie. §2.4 asks whether `[ComImport]` survives AOT. There is no
COM interop in the codebase — endpoint inspection was never ported, because it is a repair
feature. So this measures AOT-cleanliness of code that does not contain the risky part.
Recorded as *partially answered*.

**One build-environment trap worth knowing:** the AOT link step invokes `vswhere.exe`
unqualified and dies with `MSB3073 ... exited with code 123` if it is not on `PATH` — even
with the MSVC toolset installed. The message names the linker, not the missing tool, which
sends you looking in the wrong place. Adding
`%ProgramFiles(x86)%\Microsoft Visual Studio\Installer` to `PATH` fixes it.

### The same bug, twice, so the API changed

All 13 phase-2 orchestrator tests failed at once with "Wave Link not found". All 15 phase-4
CLI tests failed at once with the same thing. Both times the cause was
`SettingsInspector(IFileSystem)` — a convenience constructor that silently resolves
`%LOCALAPPDATA%` **from the real environment**, so a test wired entirely against a fake
filesystem quietly consulted the developer's actual machine.

In phase 2 I worked around it in the test. Doing that a second time would have been a
decision to keep the trap. Instead the constructor is gone, replaced by
`SettingsInspector.For(fileSystem, localAppDataPath)` and an explicit
`SettingsLocator.SystemLocalAppData` called at the composition root.

The general shape: **a convenience overload that reaches into the environment looks identical
at the call site to one that does not.** That is what made it worth removing rather than
documenting.

### A dependency not taken

The plan required evaluating hand-rolled parsing before reaching for `System.CommandLine`.
Eight verbs, five options, and a hard NativeAOT requirement on the one artifact that has it —
so hand-rolled, recorded in [[ADR-009]] with the revisit condition named, since a future
reader will otherwise reasonably ask why.

The cost is real and paid rather than ignored: help text is hand-maintained and **can drift
from the parser**, so a test asserts every verb and option appears in it.

## Decisions made

| Decision | Recorded in |
|---|---|
| Hand-rolled parsing, no dependency | [[ADR-009]] |
| `SettingsInspector.For(...)` replaces the environment-reading constructor | This note; `SettingsLocator.SystemLocalAppData` |
| `PublishSelfContained` rather than `SelfContained` | `WaveLinkBackup.Cli.csproj` |
| Confirmation refuses on redirected stdin | `ConsoleOutput.Confirm` |
| Exit codes mapped from `CoreError` types | `ExitCode.For` |

## What did not work

**`SelfContained=true` made the CLI untestable.** `NETSDK1151`: a non-self-contained test
project cannot reference a self-contained executable. The property was set deliberately in
phase 1 to stop the csproj and the release pipeline disagreeing (audit finding 5), so removing
it was not an option. `PublishSelfContained` gets the same shipped artifact and applies only
at publish time.

**`ExitCode` constants collided with `CoreError` type names.** `WaveLinkNotInstalled =>
WaveLinkNotInstalled` in a switch arm cannot tell a constant from a type, and the compiler
error — *"cannot implicitly convert type 'int' to 'CoreError'"* — points at the symptom rather
than the collision. The constants are now named for the *condition* (`NotInstalled`,
`StillRunning`, `Damaged`) rather than mirroring the error types.

**CLI coverage came in at 78.3%**, under target, and the gaps were instructive:
`ConsoleOutput` at 0% included the rule that a piped invocation must never count as consent to
a restore. That is a safety property, and leaving it untested on "it's just console I/O"
grounds would have repeated the `FileSystemSettingsWatcher` mistake from phase 3. It is now
tested — and the test host redirects stdin, so the condition asserts itself.

## Open questions

- **`Watch` is the least-covered verb.** It is a loop with a `Ctrl+C` handler; the coordinator
  beneath it is fully tested, but the verb's own wiring is exercised only by hand. An
  integration test that runs it for one tick is possible and was not written.
- **§2.4 remains genuinely open** — see above.
- **No config file yet.** Settings pass as flags. Where they persist is phase 5's problem,
  because that is where a user first changes one without a command line.

## Next

Phase 5: the WPF shell — the biggest phase, and the one carrying six undesigned surfaces.
Planned in [dev-phases/phase-5-wpf.md](../dev-phases/phase-5-wpf.md).

## References

- [[ADR-004]] · [[ADR-009]] · [[ADR-001]] — corrected with a measurement
- [technical-debt.md](../technical-debt.md) §1.5, §2.2, §2.4
