---
title: "ADR-017: COM interop is source-generated, and Core gets AllowUnsafeBlocks"
status: accepted
created: 2026-08-25
updated: 2026-08-25
tags: [decision, interop, aot, core]
---

# ADR-017: COM interop is source-generated, and Core gets AllowUnsafeBlocks

**Status:** Accepted
**Date:** 2026-08-25

## Context

`WindowsAudioEndpointInspector` is the first COM in this codebase. Everything before it was
P/Invoke — `SHFileOperationW` in `RecycleBin`, `ServiceController` in `WaveLinkService` — and
P/Invoke raised none of the questions COM does.

Two constraints met here and disagreed.

**Core carries `IsAotCompatible`.** It has since phase 1, to keep NativeAOT open for the CLI, and
`TreatWarningsAsErrors` means a trim warning is a build failure rather than a note. That is the
setting doing its job: it turns "this will break at publish time, months from now" into "this does
not compile today".

**Upstream's inspector is classic `[ComImport]`**, activated through
`Activator.CreateInstance(Type.GetTypeFromCLSID(clsid))`. Porting it was the plan of record, and
[technical-debt.md](../technical-debt.md) §2.4 had been open since phase 4 asking whether that
survives AOT — a question nothing could answer while the codebase contained no COM at all.

Porting it answered §2.4, and the answer was no. Two mechanisms, two build errors:

| Mechanism | Result |
|---|---|
| `Activator.CreateInstance(Type.GetTypeFromCLSID(clsid))` — upstream's activation | **IL2072.** A CLSID resolved at runtime yields a `Type` the trimmer cannot prove has a parameterless constructor |
| Classic `[ComImport]` interfaces via a `CoCreateInstance` P/Invoke declaring `[MarshalAs(UnmanagedType.Interface)]` | **IL2050.** Built-in COM marshalling cannot be verified after trimming — the interfaces and their members might be removed |

Neither is a warning to suppress. Both describe something the trimmer genuinely cannot see through,
and suppressing either would move the failure from build time to a user's machine.

## Decision

COM interop in this codebase is **source-generated** — `[GeneratedComInterface]`, with blittable
parameters only and interface out-parameters as raw pointers wrapped by hand through
`StrategyBasedComWrappers`.

`WaveLinkBackup.Core` gains **`AllowUnsafeBlocks`**, which the COM interface generator requires.

## Alternatives considered

| Option | Why not |
|---|---|
| Drop `IsAotCompatible` from Core | Trades a compiler setting that has caught real problems for one class. It also silently closes the NativeAOT option the CLI has kept open since phase 1, and does it in a way nobody would notice until a publish |
| Suppress IL2050 and ship classic `[ComImport]` | The warning is true. Trimming can remove the interfaces, and the result is a `NullReferenceException` in a released binary rather than an error on a developer's machine |
| Hand-roll the vtable calls through function pointers | Fully AOT-safe with no generator, but roughly 200 lines of `delegate* unmanaged` and manual slot arithmetic where a wrong index calls the wrong function and does not fail to compile. It also needs `AllowUnsafeBlocks` anyway, so it pays the same price for a worse result |
| Put the inspector in the shells instead of Core | Duplicates it across CLI and App, and `RecycleBin` already settled this shape: interop needs nothing from the Windows Desktop ref pack, and `GuardNoDesktopFramework` guards the ref pack rather than interop |

## Consequences

**This enables:** COM interop that survives NativeAOT. Verified rather than assumed — the AOT
publish is clean, and the resulting 7.68 MB native binary enumerated 96 endpoints on the reference
rig. §2.4 closes with evidence.

**This reverses a documented refusal, deliberately.** `RecycleBin` says in a comment that it uses
`DllImport` rather than `LibraryImport` specifically to avoid granting `AllowUnsafeBlocks` to "a
carefully conservative library", for one call that runs when someone clicks *Empty trash*. That
reasoning still holds where it was written: there, unsafe would have bought marshalling speed.
Here it is not an optimisation but the price of compiling at all, which is a different trade with a
different answer. The comment stays, and the csproj now says why it no longer decides this.

**This rules out:** the assumption that unsafe is off in Core. Anything reviewed on the basis of
"this project does not use unsafe" needs re-reading — the flag is project-wide and cannot be scoped
to one file. Nothing today relies on that assumption, but a future reviewer might.

**It does not enable pointer arithmetic by habit.** The flag exists for the generator. Hand-written
`unsafe` blocks in Core should be argued on their own terms, and there are none today.

**Revisit if:** .NET ships COM source generation that does not require `AllowUnsafeBlocks`, or if
Core ever needs to stop being AOT-compatible for an unrelated reason — at which point classic
`[ComImport]` becomes available again and the simpler declaration wins.

## References

- `SPEC.md` §7 (endpoint inspection) · §11
- [technical-debt.md](../technical-debt.md) §2.4, and the evidence table in
  [the closed-entry archive](../archive/technical-debt-closed.md)
- [[ADR-004]] — Core is a headless library behind thin shells
- [[ADR-008]] — Windows-only scope
- [[com-interop-stops-compiling-the-moment-the-project-is-aot-compatible]]
