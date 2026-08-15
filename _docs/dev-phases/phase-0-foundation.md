---
title: "Phase 0 — Foundation"
status: published
created: 2026-08-16
updated: 2026-08-16
tags: [dev-phase]
---

# Phase 0 — Foundation

**Status:** In progress
**Entry criteria:** a specification and a design. Both exist.
**Exit criteria:** the three-project solution builds green in CI, with upstream's code merged
and **its tests passing unchanged**.

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

See the [session note](../sessions/2026-08-16-documentation-scaffold.md).

### Repository

- [x] `.gitignore` — build output, IDE files, packaging artifacts, and the project-specific
      rules refusing real settings files, VST3 binaries and the backup store.
- [x] Root `README.md` — **Windows-only above the fold** ([[ADR-008]]), upstream attribution.
- [x] `CHANGELOG.md` — engineering changelog, distinct in voice from
      [documentation-stats.md](../documentation-stats.md).
- [ ] `LICENSE` — MIT, preserving upstream's copyright notice alongside ours.

### Fork intake

- [ ] Merge `voltybat/WaveLinkSettingsUtility` at `main` (2026-07-19), history preserved.
- [ ] Confirm its tests pass **unchanged**. A failure here is intake done wrong, not a bug
      found — investigate before touching anything.
- [ ] Record the exact upstream commit in `CHANGELOG.md`, so the audit can be re-run against a
      known base.

**Change nothing.** The five known defects ([audit](../audits/2026-08-15-voltybat-wavelinksettingsutility.md))
are already written down and assigned to phases. Fixing one during intake conflates "we moved
the code" with "we changed the code" in the same diff.

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

- [ ] Upstream's logic lands in `Core`; its entry point becomes `Cli`.
- [ ] `App` is a WPF stub — window opens, nothing more. It exists to prove the reference
      graph, not to do anything.
- [ ] **Enforce in the csproj** that `Core` cannot reference `PresentationFramework` or
      `System.Console`. Intention is not enforcement, and an accidental reference is invisible
      until AOT or the test host fails much later.

### CI

- [ ] Build all four projects.
- [ ] Run `Core.Tests`.
- [ ] Fail the build on a forbidden reference from `Core`.

Windows runner only ([[ADR-008]]).

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
