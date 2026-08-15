---
title: "Session: Documentation scaffold"
status: published
created: 2026-08-16
updated: 2026-08-16
tags: [session, documentation]
---

# Session: Documentation scaffold

**Date:** 2026-08-16

## Goal

Stand up the documentation system for Wave Link Backup, from a project-agnostic template,
against an existing build specification and a completed design handoff. No application code.

## What happened

Four scoping decisions were taken before writing anything:

1. **`dev-phases/` over `milestones.md`.** The build has natural sequential gates, and each
   carries enough design detail to bloat a single file quickly.
2. **Seed from `SPEC.md` rather than create empty folders.** The spec already contained eight
   real decisions and eight real traps; leaving them as prose in one 25 KB document means
   nobody finds the one they need at the moment they need it.
3. **Move both existing artifacts into the structure.** The design handoff went to
   `_docs/operations/design/`, its `README.md` renamed `design-handoff.md`. `SPEC.md` stays at
   `_docs/SPEC.md` as a named top-level document.
4. **Initialise git and commit.** The `.gitignore` already existed and already carried
   project-specific rules about never committing real settings files — clearly written for a
   repository that had not been created yet.

Then the extraction. `SPEC.md` was written as a build specification, so its decisions are
stated as conclusions with the reasoning inline. Converting them to ADRs meant recovering the
alternatives — which were mostly there, and in two cases (the backup store shape, the tier
model) had to be reconstructed from what the spec was arguing *against*.

Every gotcha got a **`Provenance` line**, and this turned out to be the most useful thing in
the set. Splitting eight traps into *observed* (3), *read but not reproduced* (4) and
*spec-derived, never seen* (1) changes how much each is worth at 2am. The VST3 bundle case is
the clearest: it has never happened, cannot happen on the author's machine, and will silently
capture nothing when it does.

## Decisions made

| Decision | Recorded in |
|---|---|
| C# / .NET over Rust | [[ADR-001]] |
| Fork `voltybat/WaveLinkSettingsUtility` | [[ADR-002]] |
| Backup store outside `LocalState`, identity in `manifest.json` | [[ADR-003]] |
| Headless core library, thin WPF and CLI shells | [[ADR-004]] |
| WPF over WinUI 3 / Avalonia / WinForms | [[ADR-005]] |
| Four switchable VST3 tiers, capture what is referenced | [[ADR-006]] |
| Content-hash dedup and a file watcher, not a schedule | [[ADR-007]] |
| Windows-only, stated rather than implied | [[ADR-008]] |

All eight were already made in `SPEC.md`. What this session added is the *alternatives* and
the *consequences* — specifically what each rules out, which is the part that explains why a
"simple" change later turns out not to be.

## What did not work

**`knowledge-base/patterns/` was not created, and neither were `plans/`,
`operations/runbooks/` or `operations/diagrams/`.** The first attempt at the structure created
all of them with placeholder files. That is worse than nothing: a pattern extracted from code
that does not exist is a theory, and a folder holding one thin file makes the corpus look
fuller than it is. Each absent folder now has a written trigger in `_docs/README.md` saying
what creates it.

**Writing all eight dev-phase documents in detail was started and abandoned.** Phases 2–7
produced plausible-looking work items derived from assumptions phases 0 and 1 will invalidate.
Only phases 0 and 1 are detailed; the rest are sketched in `dev-phases/README.md` with enough
to know what they are for and what they depend on.

**`technical-debt.md` needed reframing.** There is no code, so nothing has been *incurred*. It
now separates debt agreed to but not yet taken (the fork's five defects) from assumptions
nobody has checked — which is a more honest document and, as it happens, a more useful one.

## Open questions

- **Does `JsonNode.Parse` collapse duplicate keys?** Ten-minute check, and it blocks the fix
  for upstream finding 3. Both answers are bad in opposite ways. First item of phase 1.
- **Do non-MSIX Wave Link installs exist?** If they do, discovery returns "not found" and the
  app is useless to those users. The escape hatch is scoped regardless.
- **Does `[ComImport]` interop survive NativeAOT?** Decides whether the AOT packaging option
  in the audit's finding 5 is real. Cheap to check early, worth doing before the packaging
  decision is framed.

## Next

**Phase 0, remaining items:** `LICENSE` (MIT, preserving upstream's notice), fork intake with
tests passing **unchanged**, the four-project solution layout, and the CI reference guard that
stops `Core` acquiring a UI or console dependency.

The app itself is architectural and has not been through a design cycle — only its
documentation has. Before any C# is written beyond the solution skeleton, phase 1 needs its
own brainstorm and implementation plan, which lands in `_docs/plans/` and creates that folder.

## References

- [phase-0-foundation.md](../dev-phases/phase-0-foundation.md)
- [Audit: voltybat/WaveLinkSettingsUtility](../audits/2026-08-15-voltybat-wavelinksettingsutility.md)
