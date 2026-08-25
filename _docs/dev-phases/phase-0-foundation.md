---
title: "Phase 0 — Foundation"
status: published
created: 2026-08-16
updated: 2026-08-16
tags: [dev-phase]
---

# Phase 0 — Foundation

**Status:** ✅ **Complete — 2026-08-16.**
**Entry criteria:** a specification and a design. Both exist.
**Exit criteria:** the three-project solution builds green in CI, with upstream's code merged
and **its tests passing unchanged**.

> **The exit criterion was met in a different form, deliberately.** Intake was chosen as a
> *vendored snapshot excluded from the build* ([VENDOR.md](../../third_party/WaveLinkSettingsUtility/VENDOR.md)),
> so there is no upstream csproj in our solution to run. Upstream's suite was instead run in
> the clone **at the vendored SHA `211a18c4`, before vendoring: 40 tests green**. That proves
> the same thing — the snapshot is sound and any later failure is ours — without keeping a
> second solution alive forever. Recorded here rather than quietly reinterpreted.

## Why this phase exists

To get every structural decision made before there is code arguing against it. Two of them —
the project split ([[ADR-004]]) and where backups live ([[ADR-003]]) — are cheap now and
expensive once code has grown around their absence.

The unchanged-tests exit criterion is deliberate: it proves the fork was taken cleanly, and it
establishes the baseline that later phases must not regress.

## Scope

### In

- The documentation system (this folder and everything beside it).
- Git repository, `.gitignore`, root `README.md`, `CHANGELOG.md`.
- Fork intake: upstream merged, licence and attribution preserved.
- Solution layout — the four projects from [[ADR-004]].
- CI: build, test, and the reference guard below.

### Out — and where it went instead

- Any behaviour change to upstream code → **phase 1**. Intake is a move, not a rewrite; mixing
  them makes it impossible to tell which failure came from which.
- The `JsonNode.Parse` duplicate-key check → **phase 1**, where its answer is used.
- Anything a user can see → phases 4 and 5.

## Work

### Documentation — done 2026-08-16

The `_docs/` system, seeded from `SPEC.md` and the design handoff: 8 ADRs, 8 gotchas, 1
recipe, 1 audit, this roadmap. The design handoff moved to `operations/design/`.


### Repository

- [x] `.gitignore` — build output, IDE files, packaging artifacts, and the project-specific
      rules refusing real settings files, VST3 binaries and the backup store.
- [x] Root `README.md` — **Windows-only above the fold** ([[ADR-008]]), upstream attribution.
- [x] `CHANGELOG.md` — the engineering changelog.
- [x] `LICENSE` — MIT, carrying upstream's copyright line verbatim
      (`Copyright (c) 2026 WaveLinkSettingsUtility contributors`, fetched from their LICENSE
      rather than reconstructed) alongside ours, plus an Attribution section naming which
      components each covers.

      > **Written before the code it attributes.** Upstream's notice is in place ahead of the
      > merge, deliberately: over-attributing costs nothing, under-attributing is a licence
      > breach, and the window between "we declared this a fork" and "the code actually
      > landed" is exactly where that gets forgotten.

### Fork intake — done

- [x] Vendored `voltybat/WaveLinkSettingsUtility` at `211a18c4af4da9c05ad8d08de6e50740ccaa933f`
      (2026-07-18) into `third_party/`, verbatim and excluded from the build.
- [x] Ran its tests at that SHA before vendoring: **40 passed, 0 failed**.
- [x] Recorded the commit in `CHANGELOG.md` and
      [VENDOR.md](../../third_party/WaveLinkSettingsUtility/VENDOR.md), so the audit can be
      re-run against a known base.
- [x] Resolved audit finding 5 and found finding 6 in the process.

**Nothing upstream was changed.** The snapshot is byte-identical to the source; the port lives
separately in `src/`, so "we moved the code" and "we changed the code" are different commits
and different directories.

### Solution layout

```
WaveLinkBackup.sln
├── src/
│   ├── WaveLinkBackup.Core/     ← class library. No UI, no console.
│   ├── WaveLinkBackup.Cli/      ← thin shell
│   └── WaveLinkBackup.App/      ← thin shell, WPF
└── tests/
    └── WaveLinkBackup.Core.Tests/
```

- [x] Upstream's behaviour is ported into `Core`; `Cli` is a stub until phase 4.
- [x] `App` is a WPF stub — window opens, nothing more. It exists to prove the reference
      graph and to give the headless guard something real to guard against.
- [x] **Enforced in the csproj**: `GuardNoDesktopFramework` fails the build if `Core` resolves
      anything from `Microsoft.WindowsDesktop.App`. **Verified to fire** by temporarily
      enabling `UseWPF`. `Core` also targets `net10.0` rather than `net10.0-windows`, so the
      desktop ref pack is not in reach at all.

### CI — done

- [x] Builds all four projects on `windows-latest` ([[ADR-008]]).
- [x] Runs `Core.Tests`. `RealInstallTests` skip there, by design.
- [x] Guard 1 fails the build inside compilation; guards 2 and 3 are source-scan tests.

## Risks

| Risk | Early signal | Response |
|---|---|---|
| Upstream tests fail after intake | The first CI run | Stop. Intake is wrong — a path assumption, a target framework, a test fixture. Do not "fix" the tests. |
| The console entry point resists splitting from the logic | Circular references, or `Core` wanting `Console.WriteLine` | Take it as information about where the real boundary is. Logging goes through an abstraction; that is the fix, not a `Core` console reference. |
| The `Core`-must-not-reference rule is added "later" | It never is | Do it in the same commit as the project split. |
| Phase 0 quietly absorbs phase 1 | A duplicate-key validator appearing in an intake commit | The scope list above. Intake is a move. |

## References

- [[ADR-002]] · [[ADR-004]] · [[ADR-008]]
- [Audit: voltybat/WaveLinkSettingsUtility](../audits/2026-08-15-voltybat-wavelinksettingsutility.md)
