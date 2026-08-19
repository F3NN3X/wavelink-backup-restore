---
title: "Wave Link Backup — Documentation Index"
status: published
created: 2026-08-16
updated: 2026-08-19
tags: [meta, index]
---

# Wave Link Backup — Documentation Index

**Start here.**

Wave Link Backup snapshots and restores Elgato Wave Link's mixer configuration. Wave Link
keeps about **three days** of its own rolling copies; a configuration that breaks over a long
weekend is unrecoverable by the time anyone notices. This app is the safety net: configured
once, then ignored until the day it saves someone's rig.

The whole payload is **one 43 KB JSON file**, and the entire backup set is about **470 KB** —
small enough to keep one snapshot per distinct content hash, indefinitely, forever.

---

## The three documents that matter most

| Document | What it is |
|---|---|
| **[SPEC.md](SPEC.md)** | The build specification. Where the settings live, what's inside them, the restore sequence, the validation traps, the VST3 tiering. **The authority on what to build.** Read its Provenance section before treating any number as a constant. |
| **[operations/design/README.md](operations/design/README.md)** | The visual and interaction design, part 1 — tokens, the four finished screens, copy. High fidelity: colours, type, spacing and wording are final. Part 2 is [screens/](operations/design/screens/00-index.md); read [CHANGES-SINCE-V1.md](operations/design/CHANGES-SINCE-V1.md) first. |
| **[dev-phases/README.md](dev-phases/README.md)** | What is left to build, phase by phase, with entry and exit criteria. Every phase is now detailed, and [spec-coverage.md](dev-phases/spec-coverage.md) maps each `SPEC.md` requirement to where it stands. |

Everything else in this folder explains *why*, records *what bit us*, or tracks *what
happened*. See [README.md](README.md) for how the system is organised and how to add to it.

---

## Current state

**Phases 0–6 are complete. Phase 7 (release) is next, and 1.0 is gated on the privacy work
rather than on features.**

**There is a working program with a window.** `wlbackup` backs up, lists, restores, renames,
deletes, verifies, prunes, empties the trash and watches from the CLI; the WPF shell does the
same from a tray app with a window — the four designed screens, the twelve errors, the settings
dialog, and high contrast, all built and tested. **1,207 tests** (Core 423, CLI 97, App 687).
Published as a **3.2 MB NativeAOT binary**, verified against a real install.

**All three founding problems are solved.** Snapshots survive an MSIX package reset
([[ADR-003]]), backups happen on their own ([[ADR-007]]), and there is something to run —
now a window as well as a CLI ([[ADR-004]]).

**Phase 5 closed 2026-08-19.** All ten plans landed: the backup list, the real restore flow,
delete/rename/trash, the twelve errors and first-run state, the settings dialog, the tray shell,
and high contrast as a fully verified third theme. The four Core changes in
[technical-debt.md](technical-debt.md) §7 — two-stage delete via `.trash`, lazy verification
during pruning, a watcher that no longer queues, and Windows-convention keyboard/focus — are all
shipped.

**0.5.1 audited the shell against that design and fixed what it found** — including a restore
dialog that could not open at all, dialogs that rendered on a black background, binding
expressions printed as text, selection that was per date group, and a proportion bar that had
never drawn. Every one lived in a view no test had ever constructed, which is the finding rather
than the list: see [the session note](sessions/2026-08-19-design-audit-and-ui-fixes.md) and the
four gotchas it produced. Motion and the missing-plug-in warning
([technical-debt.md](technical-debt.md) §4.12–4.13) closed with it.

**Phase 6 closed 2026-08-19, and the two things it deferred closed with it.** All four tiers
capture and restore. Tier 3 was then run against a real vendor folder for the first time and
found to be capturing the wrong files — an interface default and a MIDI map where 172 presets
should have been — so it reads **two roots** now ([[ADR-010]]), and a snapshot went from 61
preset files to 491. Tier 4 restore reached the shell once elevation had a designed surface
([[ADR-011]]). Automatic backups gained a settable interval and an optional daily time.

**The design is complete** (package v5): thirteen state-group specs, nothing undesigned. Two
further specs — elevation and backup timing — were written in this repo rather than exported;
see [README.md](README.md) → *operations/* for why that is a last resort and how they survive a
re-export.
**It is a tray app with a window**, not the reverse — `screens/12` is explicit, and that framing
lands scope the original four screens did not carry.

| | |
|---|---|
| What shipped | [Phase 5](dev-phases/phase-5-wpf.md) · [CHANGELOG](../CHANGELOG.md) |
| What is next | [Release](dev-phases/phase-7-release.md) — the privacy gate first |
| Did we build the spec | [spec-coverage.md](dev-phases/spec-coverage.md) — every `SPEC.md` requirement, line by line |
| What is refused or deferred | [post-1.0.md](dev-phases/post-1.0.md) |
| How Core is shaped | [Phase 1](plans/2026-08-16-phase-1-core-design.md) · [Phase 2](plans/2026-08-16-phase-2-store-design.md) designs |

> **Read the Corrections block at the top of [SPEC.md](SPEC.md) before relying on it.** Three
> of its claims were measured against a live install on 2026-08-16 and did not survive — most
> importantly the JSON encoder recommendation in §5 and §7·2, which is **inverted** and would
> cause the problem it describes. The spec body is left deliberately unedited.

---

## Decisions

The shape of the project in eleven records. Read `ADR-001` and `ADR-002` first — the rest
follow from them.

| ADR | Decision |
|---|---|
| [ADR-001](decisions/ADR-001-csharp-over-rust.md) | C# / .NET over Rust |
| [ADR-002](decisions/ADR-002-fork-wavelinksettingsutility.md) | Fork `voltybat/WaveLinkSettingsUtility` rather than write fresh |
| [ADR-003](decisions/ADR-003-backup-store-outside-localstate.md) | The backup store lives outside `LocalState`, identified by manifest |
| [ADR-004](decisions/ADR-004-core-library-thin-shells.md) | A headless core library with thin WPF and CLI shells |
| [ADR-005](decisions/ADR-005-wpf-for-the-gui.md) | WPF over WinUI 3, Avalonia and WinForms |
| [ADR-006](decisions/ADR-006-vst3-four-tier-capture.md) | Four independently switchable VST3 tiers; capture what is referenced, not what is installed |
| [ADR-007](decisions/ADR-007-hash-dedup-and-file-watching.md) | Content-hash dedup and a file watcher, not a schedule |
| [ADR-008](decisions/ADR-008-windows-only-scope.md) | Windows-only, and say so out loud |
| [ADR-009](decisions/ADR-009-hand-rolled-cli-parsing.md) | Hand-rolled command-line parsing, no dependency |
| [ADR-010](decisions/ADR-010-two-preset-roots-and-a-rooted-snapshot-layout.md) | Two preset roots, and a snapshot layout that names them — corrects ADR-006's tier 3 |
| [ADR-011](decisions/ADR-011-elevate-by-relaunching-the-shell.md) | Elevate by relaunching the shell, for one restore, and never otherwise |

---

## Gotchas

Eighteen ways this goes wrong. Titled by symptom, because that is what you will be searching
for at the time — you do not know the cause yet, which is why you are searching.

Grouped by where they bite. **The whole table is here on purpose**: it listed ten of sixteen
between 0.5.1 and 0.6.0, which made it look complete and quietly hid the six the design audit
produced.

### Capture and restore

| Symptom | Gotcha |
|---|---|
| The backup tool runs, reports success, and protects nothing | [backup-succeeds-but-protects-nothing.md](knowledge-base/gotchas/backup-succeeds-but-protects-nothing.md) |
| The file parses fine but Wave Link resets to defaults | [file-parses-but-wave-link-resets.md](knowledge-base/gotchas/file-parses-but-wave-link-resets.md) |
| Restoring the newest backup restores the broken config | [newest-backup-is-the-broken-one.md](knowledge-base/gotchas/newest-backup-is-the-broken-one.md) |
| Every snapshot differs from the last, and diffs are useless | [every-snapshot-differs-with-no-real-change.md](knowledge-base/gotchas/every-snapshot-differs-with-no-real-change.md) |
| Capture fails with "being used by another process" | [capture-fails-while-wave-link-is-running.md](knowledge-base/gotchas/capture-fails-while-wave-link-is-running.md) |
| The restore writes cleanly, then the old settings come back | [restored-settings-revert-seconds-later.md](knowledge-base/gotchas/restored-settings-revert-seconds-later.md) |
| The plugin is restored but refuses to run | [restored-plugin-demands-a-licence.md](knowledge-base/gotchas/restored-plugin-demands-a-licence.md) |
| A plugin backs up as zero bytes | [vst3-backs-up-as-nothing.md](knowledge-base/gotchas/vst3-backs-up-as-nothing.md) |
| Someone else's backup produces dead channels | [restored-backup-has-dead-channels.md](knowledge-base/gotchas/restored-backup-has-dead-channels.md) |
| Deleting one backup takes its neighbours with it | [deleting-one-backup-takes-its-neighbours.md](knowledge-base/gotchas/deleting-one-backup-takes-its-neighbours.md) |
| The backup says it saved your presets, and they are not in it | [backup-says-it-saved-your-presets-and-it-did-not.md](knowledge-base/gotchas/backup-says-it-saved-your-presets-and-it-did-not.md) |

### The shell

Four of these came out of the 0.5.1 design audit — the first three below, plus the selection one
— and the finding was the group rather than any member: every one lived in a view no test had
ever constructed. The two tray entries were hit while building phase 5, and the settings one two
phases later, by the same gap.

| Symptom | Gotcha |
|---|---|
| The window never opens and nothing says why | [the-window-never-opens-and-nothing-says-why.md](knowledge-base/gotchas/the-window-never-opens-and-nothing-says-why.md) |
| A dialog opens as a black rectangle | [a-dialog-opens-as-a-black-rectangle.md](knowledge-base/gotchas/a-dialog-opens-as-a-black-rectangle.md) |
| A binding expression appears on screen | [a-binding-expression-appears-on-screen.md](knowledge-base/gotchas/a-binding-expression-appears-on-screen.md) |
| Three backups look selected at once | [three-backups-look-selected-at-once.md](knowledge-base/gotchas/three-backups-look-selected-at-once.md) |
| A control in the Settings dialog moves and nothing happens | [a-settings-control-moves-and-nothing-happens.md](knowledge-base/gotchas/a-settings-control-moves-and-nothing-happens.md) |
| The tray icon refuses every image you draw | [the-tray-icon-refuses-every-image-you-draw.md](knowledge-base/gotchas/the-tray-icon-refuses-every-image-you-draw.md) |
| The tray menu keeps the theme it started with | [tray-menu-keeps-the-theme-it-started-with.md](knowledge-base/gotchas/tray-menu-keeps-the-theme-it-started-with.md) |

---

## Recipes

| Recipe | When |
|---|---|
| [Restore a settings file safely](knowledge-base/recipes/restore-a-settings-file-safely.md) | Every restore. The order is load-bearing at every step. |

---

## Patterns

Extracted from shipped code, each naming its real callers.

| Pattern | What it makes impossible |
|---|---|
| [pure-analysis-core.md](knowledge-base/patterns/pure-analysis-core.md) | Re-serializing the file you are backing up |
| [named-method-seams.md](knowledge-base/patterns/named-method-seams.md) | Choosing the wrong file share mode |
| [preconditions-inside-the-operation.md](knowledge-base/patterns/preconditions-inside-the-operation.md) | Writing while Wave Link is still exiting |
| [guards-that-can-fail.md](knowledge-base/patterns/guards-that-can-fail.md) | A guard that silently never matches |

## Plans

| Plan | Status |
|---|---|
| [Phase 1 Core — Design](plans/2026-08-16-phase-1-core-design.md) | **Implemented** — the shape of `WaveLinkBackup.Core`, with an *as built* delta |
| [Phase 2 Snapshot Store — Design](plans/2026-08-16-phase-2-store-design.md) | **Implemented** — store layout, manifest, the guard, the restore sequence |

## Audits

| Audit | Subject |
|---|---|
| [2026-08-15 — voltybat/WaveLinkSettingsUtility](audits/2026-08-15-voltybat-wavelinksettingsutility.md) | The upstream we are forking: what to take, what to fix first |

---

## Sessions

| Date | Session |
|---|---|
| 2026-08-17 | [Phase 5 part 1 — the Core changes, and a design that answered back](sessions/2026-08-17-phase-5-core-changes.md) |
| 2026-08-17 | [Design integration, and deciding what phase 5 actually is](sessions/2026-08-17-design-integration-and-phase-5-scope.md) |
| 2026-08-16 | [Phase 4 — Core gets a caller, and AOT lands at 3.2 MB](sessions/2026-08-16-phase-4-cli-build.md) |
| 2026-08-16 | [Phase 3 — it now backs up on its own](sessions/2026-08-16-phase-3-automation-build.md) |
| 2026-08-16 | [Phase 2 — the critical inherited defect is fixed](sessions/2026-08-16-phase-2-store-build.md) |
| 2026-08-16 | [Phase 1 — Core built, 93 tests green](sessions/2026-08-16-phase-1-core-build.md) |
| 2026-08-16 | [Phase-1 probe — three documented decisions overturned](sessions/2026-08-16-phase-1-probe.md) |
| 2026-08-16 | [Documentation scaffold](sessions/2026-08-16-documentation-scaffold.md) |

---

## The rest

- [glossary.md](glossary.md) — the words this project uses precisely. "Backup" alone means
  three different things; start here if a document reads oddly.
- [technical-debt.md](technical-debt.md) — inherited defects and unverified assumptions.
- [documentation-stats.md](documentation-stats.md) — the tally and the cross-reference index.
- [templates.md](templates.md) — copy from here when adding a document.
- [archive/](archive/) — superseded documents.
