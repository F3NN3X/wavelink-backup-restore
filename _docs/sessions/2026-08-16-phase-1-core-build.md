---
title: "Session: Phase 1 — Core built, 93 tests green"
status: published
created: 2026-08-16
updated: 2026-08-16
tags: [session, core, phase-1]
---

# Session: Phase 1 — Core built, 93 tests green

**Date:** 2026-08-16

## Goal

Complete phase 1: fork intake, the four-project solution, `WaveLinkBackup.Core` built to the
approved design under strict TDD, tests green, documentation written, and phase 2 planned.

## Result

| | |
|---|---|
| Tests | **93 passing, 0 failing, 0 skipped** |
| Coverage | **81.2% line, 81.8% branch** |
| Release build | 0 errors, 0 warnings, four projects |
| Upstream baseline | 40 tests green at `211a18c4` before vendoring |

Coverage is weighted as intended rather than uniformly: the pure `Analysis` layer is 96–100%,
and the remainder is thin adapters over the OS. `WaveLinkProcess` sits at 5% deliberately —
testing `CloseAndVerifyExited` for real would close the user's Wave Link, so its contract is
exercised through `FakeWaveLinkProcess` instead. That is what the seam is for.

## What happened

### Fork intake resolved two open questions

Cloned upstream at `211a18c4af4da9c05ad8d08de6e50740ccaa933f` (2026-07-18) and ran its suite:
**40 tests green**. That is phase 0's exit criterion satisfied — though not in the form phase 0
worded it, since a vendored snapshot excluded from our solution leaves no upstream csproj in
our build to run. Verifying at the SHA before vendoring is the honest equivalent, and phase 0
now records the change.

**Audit finding 5 dissolved.** The csproj *does* set `SelfContained=false`, and the README
*does* claim no runtime is needed. Both are true: `.github/workflows/release.yml` passes
`--self-contained true`, overriding the csproj at publish time. Neither source was wrong; the
audit simply never read the release workflow. The real (smaller) issue is that a local
`dotnet publish` produces a different artifact from CI's. Our `Cli` states it in the csproj so
the two cannot disagree.

**New audit finding 6.** `ProcessControl.FindGuiProcess` only ever looks for
`Elgato.WaveLink`. `WavelinkSEService` is never closed — so upstream's "verified exited" check
can pass with half of Wave Link still running, and a write can still race the service's flush.
`SPEC.md` §4 is explicit that both must close. Ours covers both, and it is worth offering back.

### The headless guard caught something on its first run — then turned out to be wrong

`WaveLinkBackup.Core` failed to build: *"must stay headless but references WindowsBase"*.

The first instinct was to loosen the guard. Investigating instead showed **two** things:

1. `net10.0-windows` pulls the Windows Desktop ref pack into a library that needs nothing from
   it. Core now targets **`net10.0`** — Windows-only in *behaviour* (ADR-008), but using no
   Windows-only API surface. Better for the AOT story and more honest.
2. The guard was still a **false positive**. `WindowsBase.dll` and `System.Windows.dll` also
   ship in `Microsoft.NETCore.App.Ref` as legacy type-forwarding shims, present in every .NET
   application. Matching by assembly filename can never work; the guard now matches on
   `Microsoft.WindowsDesktop.App` appearing in the reference path.

Then the guard was **verified to fire** by temporarily setting `UseWPF=true`, which produced
the intended error, and reverting, which produced a clean build. A guard nobody has watched
reject something is a guess. See [[guards-that-can-fail]].

### The file lock is now pinned by an executing test

`RealInstallTests.The_naive_read_fails_while_Wave_Link_is_running` asserts that
`File.ReadAllBytes` throws `IOException` against the live settings file. It passed — Wave Link
was running, all seven real-install tests executed, none skipped.

The gotcha discovered yesterday by probe is no longer a claim in a document; it is a test that
will fail if Wave Link ever changes its share mode, which is exactly when the source-scan
guard could be relaxed and not before.

## Decisions made

| Decision | Recorded in |
|---|---|
| Vendor upstream verbatim, excluded from the build, port behaviour across | [VENDOR.md](../../third_party/WaveLinkSettingsUtility/VENDOR.md) |
| `Core` targets `net10.0`, not `net10.0-windows` | csproj comment + this note |
| `--settings-path` bypasses discovery, unlike upstream | [VENDOR.md](../../third_party/WaveLinkSettingsUtility/VENDOR.md) divergence 3 |
| Four patterns extracted from shipped code | `knowledge-base/patterns/` |

## What did not work

**Two tests failed on the first full run, and neither was a code bug.** Both were assertions I
had written minutes earlier that encoded a wrong model of my own API.

The interesting one: `Healthy.CompareTo(Collapsed)` reports `NamesLost: ["Elgato Wave:3"]`, and
I had asserted `Assert.Empty`. The code is right — recovering from a collapse genuinely does
lose the generic placeholder name. The temptation was to filter known-generic names out of
`NamesLost`; that would hard-code a magic constant into the health check, which is the
absolute-threshold mistake wearing a different hat. The assertion changed; the behaviour did
not, and the test now says why.

**Coverage came in at 76.4% first time.** The gap was not in adapters, as expected, but in
`FingerprintComparison` at 62% — the collapse-detection logic, which is risk-carrying code.
Seven tests later it is covered and the total is 81.2%. Chasing the *number* would have meant
testing the adapters; chasing the *risk* found a real hole.

**A guard-verification attempt was inconclusive before it was conclusive.** Overriding
`-p:TargetFramework=net10.0-windows` on the command line failed at restore
(`NETSDK1005`) and never reached reference resolution, so "no error" meant nothing. Editing
the csproj, restoring properly, and reverting gave a real answer.

## Open questions

- **§1.1 remains the critical inherited defect** — backups inside `LocalState`. Untouched by
  phase 1 by design; it is phase 2's reason for existing.
- **§2.2 (non-MSIX installs)** is mitigated but unanswered. `--settings-path` now bypasses
  discovery entirely, so such a user has a route; whether they exist is still unknown.
- **§2.4 (`[ComImport]` under AOT)** is untouched — no COM interop was ported, since endpoint
  inspection is a repair feature rather than phase 1.

## Next

Phase 2: the snapshot store. Planned in detail in
[dev-phases/phase-2-store.md](../dev-phases/phase-2-store.md) and
[plans/2026-08-16-phase-2-store-design.md](../plans/2026-08-16-phase-2-store-design.md).

## References

- [Phase 1 design](../plans/2026-08-16-phase-1-core-design.md) — retained, with an *As built* delta
- [Probe session](2026-08-16-phase-1-probe.md) — the measurements this was built on
- [[pure-analysis-core]] · [[named-method-seams]] · [[preconditions-inside-the-operation]] ·
  [[guards-that-can-fail]]
