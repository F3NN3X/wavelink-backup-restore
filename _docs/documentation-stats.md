---
title: "Documentation Stats"
status: published
created: 2026-08-16
updated: 2026-08-16
tags: [meta, stats]
---

# Documentation Stats

The living tally, the doc-ecosystem delta log, and the topical cross-reference index.

Update this file **in the same commit** as the document it counts. See
[README.md](README.md) → *Updating documentation stats* for the trigger table.

> This is the **doc-ecosystem** changelog. `CHANGELOG.md` at the repo root is the
> **engineering** changelog. Same commit is fine; different voices.

---

## Tally

*As of 2026-08-16.*

| Artifact | Count |
|---|---|
| ADRs | 8 |
| Gotchas | 9 |
| Patterns | 0 |
| Recipes | 1 |
| Audits | 1 |
| Sessions | 2 |
| Dev-phase documents | 3 (of 8 phases; 5 remain sketched in the index) |
| Tests | 0 |

**Patterns is deliberately zero.** No production code exists, and a pattern is extracted from
shipping code with named callers. The folder does not exist yet either — see
[README.md](README.md) → *Folders deliberately absent*.

**Tests is deliberately zero and will not stay that way.** The upstream we are forking carries
~30 KB of tests against 60 KB of code; inheriting that ratio is an explicit goal, not an
aspiration ([[ADR-004]]).

---

## Recent additions

### v0.0.2 — Probe corrections (2026-08-16)

A ten-minute probe run before designing phase 1 answered one open question and **invalidated
two documented decisions**. The doc-ecosystem effect is mostly *subtractive*, which is unusual
enough to note.

**Added**

- Gotcha 9 — [[capture-fails-while-wave-link-is-running]]. `Settings.json` is locked while
  Wave Link runs; `File.ReadAllBytes` fails on most captures. Not in `SPEC.md` at all.
- Session note — [phase-1 probe](sessions/2026-08-16-phase-1-probe.md).
- `LICENSE` at the repo root (MIT, upstream's copyright line verbatim).
- A **Corrections block** at the top of `SPEC.md`. The body is left unedited on purpose: it is
  the record of what was believed on 2026-08-15, and rewriting it would destroy the thing that
  makes the corrections legible.

**Withdrawn**

- **Audit finding 2 (JSON encoder)** — struck through, not deleted, in the audit,
  `technical-debt.md` §1.2 and `SPEC.md`. Wave Link writes with the *default* encoder;
  the recommended `UnsafeRelaxedJsonEscaping` would have caused the churn it was meant to
  prevent. A wrong recommendation that merely disappears gets re-derived by the next reader.

**Resolved**

- `technical-debt.md` §2.1 (`JsonNode.Parse` duplicates) — answered, and the question was
  mis-framed. New sub-finding 3b recorded instead.

**Rewritten**

- [[every-snapshot-differs-with-no-real-change]] — same symptom, opposite cause. The
  superseded version's `Provenance: read, not reproduced` line is what made this catchable.

**Counts moved:** gotchas 8 → 9 · sessions 1 → 2. Audit findings: 5 → 4 actionable, plus one
new sub-finding and one disputed.

---

### v0.0.1 — Documentation scaffold (2026-08-16)

The documentation system, seeded from `SPEC.md` and the design handoff. No application code.

**Added**

- The docs system itself: `README.md`, `index.md`, `templates.md`, `glossary.md`,
  `technical-debt.md`, this file.
- **8 ADRs**, `ADR-001` … `ADR-008` — every structural decision `SPEC.md` had already made
  but never recorded as a decision with alternatives and consequences attached.
- **8 gotchas**, each carrying a `Provenance` line: 3 observed, 4 read-not-reproduced,
  1 spec-derived. That split is itself the most useful thing in the set.
- **1 recipe** — the restore sequence, where the order is load-bearing at every step.
- **1 audit** — the read of `voltybat/WaveLinkSettingsUtility` at `main`.
- **3 dev-phase documents** — the 8-phase roadmap index plus detail for phases 0 and 1.
- **1 session note**.

**Moved**

- `design_handoff_wave_link_backup/` → `_docs/operations/design/`, its `README.md` renamed
  `design-handoff.md` so it does not read as a folder readme.
- `_docs/README-temp.md` → `_docs/archive/README-temp.md`, consumed.

**Counts moved:** ADRs 0 → 8 · gotchas 0 → 8 · recipes 0 → 1 · audits 0 → 1 · sessions 0 → 1.

---

## Related documentation

Topics spanning several artifacts. A single-file topic is discoverable by search and does not
belong here.

### Where the settings live, and where they don't

The decoy folder is the first thing that goes wrong and the easiest to get wrong silently.

| Artifact | Contribution |
|---|---|
| `SPEC.md` §1 | The paths, the sizes, the classification of every file under `LocalState` |
| [[ADR-003]] | Why the store is outside `LocalState` |
| [[backup-succeeds-but-protects-nothing]] | The symptom when discovery finds the decoy |
| [glossary.md](glossary.md) | `LocalState`, the decoy, package family name, backup store |
| [[phase-1-core]] | Where discovery is built |

### Validating a settings file

Three separate traps, one of which is the incident that started the project.

| Artifact | Contribution |
|---|---|
| `SPEC.md` §5 | Duplicate keys, round-trip loss, ranking by content |
| [[file-parses-but-wave-link-resets]] | Duplicate keys — the original incident |
| [[newest-backup-is-the-broken-one]] | Why timestamp ranking picks the reset config |
| [[every-snapshot-differs-with-no-real-change]] | The encoder mangling base64 state |
| [technical-debt.md](technical-debt.md) §1.3, §2.1 | The upstream gap and the unverified assumption blocking its fix |
| [Audit: voltybat](audits/2026-08-15-voltybat-wavelinksettingsutility.md) | Upstream `Validate()` and what it misses |

### Restoring safely

The part that looks obvious and fails.

| Artifact | Contribution |
|---|---|
| `SPEC.md` §4 | The sequence, and verification from the log |
| [[restore-a-settings-file-safely]] | The recipe, with the reason attached to each ordering constraint |
| [[restored-settings-revert-seconds-later]] | The flush race |
| [design-handoff.md](operations/design/design-handoff.md) Screen 2 | The confirmation dialog, and the automatic pre-restore snapshot |
| [glossary.md](glossary.md) | Verified exited, atomic write, shell AppID, pre-restore snapshot |

### VST3 capture

Four tiers, three ways it bites.

| Artifact | Contribution |
|---|---|
| `SPEC.md` §9 | The tiering, the measurements, the three warnings |
| [[ADR-006]] | The decision, and what it rules out |
| [[restored-plugin-demands-a-licence]] | Licences do not travel |
| [[vst3-backs-up-as-nothing]] | Bundles are directories |
| [technical-debt.md](technical-debt.md) §2.3 | The untested path, and why the author's machine will never catch it |

### Shipping publicly

| Artifact | Contribution |
|---|---|
| `SPEC.md` §11 | Numbers that are not constants, privacy, open questions |
| [[ADR-008]] | Windows-only, stated rather than implied |
| [[restored-backup-has-dead-channels]] | Machine-local snapshots |
| [technical-debt.md](technical-debt.md) §5, §6 | The constants list and the privacy debt that gates going public |
| `.gitignore` | Refuses real settings files, VST3 binaries and the backup store |
