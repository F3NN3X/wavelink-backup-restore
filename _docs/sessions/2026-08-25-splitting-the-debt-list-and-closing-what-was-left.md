---
title: "Session: Splitting the debt list, and closing what was left of it"
status: published
created: 2026-08-25
updated: 2026-08-25
tags: [session, technical-debt, interop, tooling, docs]
---

# Session: Splitting the debt list, and closing what was left of it

**Date:** 2026-08-25

## Goal

Protect `main`, then audit `technical-debt.md` so it holds only real debt — and then close as much
of what remained as could be closed.

## What happened

**`main` is protected.** A repository ruleset blocks force-pushes and deletion. Direct pushes still
work, and the repository-admin role can bypass — both chosen deliberately for a solo repo. The
bypass is worth naming: it applies to the account that would make the mistake, so the rules stop a
lesser-scoped token and a future collaborator, not a slip on this machine.

**The debt list was 1,646 lines and 36 of its 39 numbered entries were closed.** Reading it meant
scrolling past every closure to find the handful still owed. The closures moved verbatim to
[archive/technical-debt-closed.md](../archive/technical-debt-closed.md) — not summarised, because
several read as the record of *why* something is the way it is rather than as a ticked box. Section
numbering is preserved across both files, which matters more than it looks: around 120 references
from ADRs, audits and session notes cite sections in prose, and none uses an anchor link.

Then the entries themselves.

**§2.4 closed, and the answer was no.** See [[ADR-017]] and
[[com-interop-stops-compiling-the-moment-the-project-is-aot-compatible]]. Porting
`WindowsAudioEndpointInspector` is what made the question askable at all; classic `[ComImport]`
fails trim analysis two distinct ways, and source-generated COM works. Enumeration only — the
editing half stays post-1.0.

**§7.6 and §8.2 had their mechanical halves removed.** Neither can be closed by a commit; both were
expensive to *start*, which is most of why they kept not happening.
[`tools/plugin-resolution-experiment.ps1`](../../tools/plugin-resolution-experiment.ps1) runs §7.6's
reversible file surgery and keeps its state in a journal outside the repo, so `-Undo` works from a
fresh shell a week later. [`tools/seed-fixture-store.ps1`](../../tools/seed-fixture-store.ps1)
writes the five rigs §8.2's checklist item 5 needs, which otherwise meant half an hour of adding
and removing channels in Wave Link to look at four things.

**The design export turned out to be absent from the repository, not broken.**
`.git/info/exclude` carries `_docs/operations/design/`, so roughly 40 documents link into a folder
no clone has. Now recorded in [README.md](../README.md) rather than quietly repaired, because the
export is the authority those links cite.

## Decisions made

| Decision | Recorded in |
|---|---|
| COM interop is source-generated; Core gets `AllowUnsafeBlocks` | [[ADR-017]] |
| Closed debt entries live in an archive, with numbering preserved | `technical-debt.md` preamble |
| `main` is protected against force-push and deletion, admin may bypass | the repository ruleset |

## What did not work

**Reading `Settings.json` with `[IO.File]::ReadAllText`.** The first version of the experiment
script threw *"used by another process"* on its first run against the live install. That is exactly
the mistake `SourceGuardTests` has caught in C# since phase 1 — and it only ever scanned `*.cs`.
`ToolScriptGuardTests` now extends the rule to `tools/*.ps1`. **A guard that covers one language
covers one language**, and the second language arrives without announcing itself.

**That new guard then fired on the next script I wrote.** The seeder *writes* fixture
`settings.json` files and never opens a live one, so flagging it was the guard crying wolf. The
rule was narrowed to require an actual read rather than relaxed, both directions pinned by tests,
and re-verified against a probe. Worth recording because the temptation at that moment is to
weaken the guard, and a guard that has been weakened once is easier to weaken again.

**Two attempts at the COM port before the third built.** `Activator.CreateInstance` on a
CLSID-derived `Type` (IL2072), then classic `[ComImport]` behind a `CoCreateInstance` P/Invoke
(IL2050). Neither was a suppressible warning. The gotcha carries both, because the second attempt
looks like the fix for the first and is not.

**The rewritten tier list disagreed with the checklist it pointed at.** It listed three outstanding
looks; the checklist had items 1–4 ticked and dated. The stale expansion came from the old tier
list, which predated those ticks. Corrected in a follow-up commit — two documents disagreeing about
what a human still owes is worse than either being wrong alone.

## Open questions

**§7.6** — does Wave Link resolve a channel's plug-in by `PluginId` or by `FilePath`? The script
runs the experiment; the backup, the restart and the look at the channel are still a person's.

**§8.2** — item 5 of the by-eye checklist. The rigs are seeded; the eyes are not.

**Should `screens/13-elevation.md` and `14-backup-timing.md` be committed?** They are authored in
this repo, unrecoverable from the design tool, excluded from git, and absent from this working
tree. Their only protection is a provenance banner telling a future re-export not to delete them,
which does not survive a lost disk. Unresolved, and it needs the files to exist somewhere first.

**What version this ships as.** The branch adds a feature (endpoint enumeration in `diagnostics`)
without completing a phase, so it sits under `Unreleased` in `CHANGELOG.md` rather than claiming a
number.
