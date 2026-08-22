---
title: "Development Phases"
status: published
created: 2026-08-16
updated: 2026-08-22
tags: [dev-phase, index]
---

# Development Phases

What is left to build, in the order it should be built. This is the index; each phase has its
own detailed file.

> **Every phase is now detailed.** The original rule — detail a phase only when it becomes
> current or next, because writing phase 6 while phase 1 is unbuilt produces fiction — held
> until 2026-08-19. With phases 0–5 shipped and phase 6 half built, phase 7 can be written
> against what exists rather than against assumptions, so it was.
>
> **What is still deliberately not written** is the per-section *build* plan: phase 5 wrote one
> `plans/` document per plan, immediately before building it, and phases 6 and 7 should do the
> same. The phase files below carry the decisions; a build plan carries the code.

Three companion documents finish the picture:

- [spec-coverage.md](spec-coverage.md) — every requirement in `SPEC.md` and where it stands.
  The line-by-line answer to "did we build the spec".
- [phase-7-release.md](phase-7-release.md) — the release phase in full, including the 1.0 gate
  table: which open debts must close before a release and which may ship open.
- [post-1.0.md](post-1.0.md) — what is refused, and what is deferred with the signal that
  would promote it.

Distinct from [technical-debt.md](../technical-debt.md), which is for things built and not
right.

---

## Status

| Phase | Name | Status | Detail |
|---|---|---|---|
| **0** | Foundation | ✅ Complete | [phase-0-foundation.md](phase-0-foundation.md) |
| **1** | Core: discovery, validation, safe write | ✅ Complete — 93 tests, 81.2% | [phase-1-core.md](phase-1-core.md) |
| **2** | Snapshot store | ✅ Complete — 186 tests, 83.0% | [phase-2-store.md](phase-2-store.md) · [design](../plans/2026-08-16-phase-2-store-design.md) |
| **3** | Automation: watcher, dedup, retention | ✅ Complete — 235 tests, 84.9% | [phase-3-automation.md](phase-3-automation.md) |
| **4** | CLI shell | ✅ Complete — 308 tests, AOT 3.2 MB | [phase-4-cli.md](phase-4-cli.md) |
| **5** | WPF shell | ✅ Complete — 964 tests, all ten plans landed | [phase-5-wpf.md](phase-5-wpf.md) |
| **6** | Plugin tiers | ✅ Complete — 1,146 tests, all four tiers capture and restore | [phase-6-plugin-tiers.md](phase-6-plugin-tiers.md) |
| **7** | Release | **Next** | [phase-7-release.md](phase-7-release.md) |
| — | Spec coverage | Living | [spec-coverage.md](spec-coverage.md) |
| — | After 1.0 | Living | [post-1.0.md](post-1.0.md) |

---

## Phase 0 — Foundation

Repository, documentation, solution layout, fork intake, CI. No product behaviour.

**Exits when** the three-project solution from [[ADR-004]] builds green in CI with the
upstream code merged and its tests passing unchanged.

[Full detail →](phase-0-foundation.md)

---

## Phase 1 — Core: discovery, validation, safe write

The library that everything else calls. Discovery that avoids the decoy, validation that
catches duplicate keys, and a write path that cannot race Wave Link's shutdown flush.

Upstream findings 2 and 3 are fixed here. The `JsonNode.Parse` question
([technical-debt.md](../technical-debt.md) §2.1) is answered here, because finding 3's fix
depends on it.

**Exits when** a settings file can be discovered, validated, fingerprinted and atomically
replaced — with Wave Link's exit verified before the write — and the whole path is covered by
tests through the seam interfaces.

[Full detail →](phase-1-core.md)

---

## Phase 2 — Snapshot store

**Depends on:** phase 1. ✅ **Planned in detail** —
[phase-2-store.md](phase-2-store.md) and
[the design](../plans/2026-08-16-phase-2-store-design.md).

The store from [[ADR-003]]: snapshots outside `LocalState`, identity in `manifest.json`,
machine-generated directory names, free-text display names. Restore reads from it. The
pre-restore snapshot becomes automatic and unconditional. `IClock` arrives here, because
snapshot timestamps are the first thing that genuinely needs it.

This is where upstream's critical finding is resolved, and the filename regex, the backup
path and the managed-backup enumeration are replaced **together** — changing one alone leaves
restore refusing its own files.

**Exits when** a snapshot can be written, listed, renamed, restored and deleted; every
restore takes a pre-restore snapshot first; and the manifest-hash guard refuses a directory we
did not write.

---

## Phase 3 — Automation: watcher, dedup, retention

**Depends on:** phase 2. ✅ **Planned in detail** —
[phase-3-automation.md](phase-3-automation.md).

[[ADR-007]] made real. `FileSystemWatcher` on `LocalState`, ~60s debounce, at most one
automatic snapshot per hour, dedup by `settingsSha256` (recorded in phase 2, consulted here
for the first time), capture on shutdown, prune to the configured count — **never pruning
manual or pre-restore snapshots**.

This is the phase that turns the fork into a different product. Everything before it, a person
could do by hand.

**Exits when** the app can run unattended for a week, capture every distinct configuration,
store no duplicates, and prune correctly.

---

## Phase 4 — CLI shell

**Depends on:** phase 3. ✅ **Planned in detail** — [phase-4-cli.md](phase-4-cli.md).

A thin shell over Core: back up, list, restore, rename, delete, verify, prune, watch. The
`--settings-path` escape hatch for the multiple-package and non-MSIX cases
([technical-debt.md](../technical-debt.md) §2.2).

Small, and it earns its place three times over — it makes the app scriptable, it makes phases
2–3 testable end-to-end without a GUI, and it is the **only AOT-eligible artifact**, since WPF
does not support NativeAOT.

It is also where Core finally gets a caller: `Tick()` and `CaptureOnShutdown()` have never run
outside a test.

**Exits when** every Core capability is reachable from the command line, the CLI drives the
integration tests, and a published single file runs on a machine without the SDK.

---

## Phase 5 — WPF shell

**Depends on:** phase 4. ✅ **Complete** — all ten plans landed, 964 tests green in both themes plus high contrast. Detail: [phase-5-wpf.md](phase-5-wpf.md).
**The biggest phase.**

The four designed screens at the handoff's fidelity: main list, restore confirmation,
settings, first run. Brush resources per theme, live OS theme following, OS accent bound to
one role with `--wl-danger` fixed, custom 34px caption bar with Mica, the five-slot health
strip, tier badges, row expansion.

**All thirteen state groups are now designed** — the six gaps that used to live here are closed
([technical-debt.md](../technical-debt.md) §4). It is a **tray app with a window**: closing the
window hides it, quitting is a menu item that says what it stops, and single-instance is
mandatory because two watchers would race on one file.

**Four Core changes come first** ([technical-debt.md](../technical-debt.md) §7): the two-stage
delete, lazy verification during pruning, the watcher's queuing fix, and Windows keyboard
conventions. The tray's `NEEDS YOU` state is blocked on the third.

**Deferred to phase 7:** the two toast notifications and the update mechanism — both need
Windows APIs WPF does not provide, and neither is day-one.

**Exits when** the four screens and thirteen state groups match the handoff in both themes plus
high contrast, the tray shell works, and no Core logic has leaked into the shell.

---

## Phase 6 — Plugin tiers

**Depends on:** phase 2 (tier 2 manifest can start earlier). ✅ **Complete 2026-08-19**, shipped as
0.6.0.

Tiers 2, 3 and 4 from [[ADR-006]]. Read `AudioPluginConfigurations`, cross-reference
`AudioPluginCache\AvailablePlugins.cache`, build `plugins.json`, capture presets and binaries.
Restore checks each plugin resolves and flags version drift; the restore dialog's
missing-plugin warning becomes real.

**Carries the bundle problem** — a `.vst3` may be a directory, and the author's machine will
never exercise that path ([[vst3-backs-up-as-nothing]]). A synthetic bundle fixture is not
optional here.

Tier 4 restore needs elevation; tiers 1–3 must not.

**It also carried a tier 1 gap found while building §2.** [[ADR-006]], `SPEC.md` §1, the Settings
dialog and `technical-debt.md` §3 all say tier 1 is `Settings.json` **plus Wave Link's own backup
copies**, ~470 KB — and only the 43 KB settings file was captured. §8 closed it in this phase,
because §7's "honest, recomputed sizes" and that figure are the same fact.

**Exited when** all four tiers captured and restored, the bundle path was covered by fixtures on
both sides, elevation was requested only for tier 4, and the Settings dialog's sizes became true.
**Two things it deliberately did not do**, both recorded rather than dropped: the shell cannot ask
for a tier 4 restore (elevation has no designed surface — `technical-debt.md` §4.17) and the preset
heuristic has never met a real vendor folder (§4.18).

[Full detail →](phase-6-plugin-tiers.md)

---

## Phase 7 — Release

**Depends on:** phases 5 and 6.

**Gated on the privacy work** ([technical-debt.md](../technical-debt.md) §6): snapshots
contain hardware serial numbers and the Windows username, and users will attach them to bug
reports. "Copy diagnostics" with redaction ships **before** the repo is public, not after.

**Inherited from phase 5**, both deferred because they need Windows APIs WPF does not provide
and neither is day-one:

- **The two toast notifications** — nine days of silence, and a rejected restore. Designed in
  `screens/12`; the tray's `NEEDS YOU` icon and tooltip carry the same information passively
  until then.
- **The update mechanism** — check, download, install, restart. Phase 5 builds only the static
  `UPDATES` section, because error 8 deep-links into it.

Also here: the packaging decision left open by upstream finding 5 — self-contained,
framework-dependent, or NativeAOT for the CLI. **Resolved 2026-08-22 (v0.7.2): framework-dependent**,
the app and CLI both resolve the .NET 10 Desktop Runtime from the machine, and the release carries
two archives instead of one ([technical-debt.md](../technical-debt.md) §8.5, closed). **NativeAOT
remains an open option for the CLI at 3.2 MB**, but `[ComImport]`-under-AOT is still unanswered
([technical-debt.md](../technical-debt.md) §2.4) because no COM interop has been ported. MIT
attribution. README stating Windows-only above the fold ([[ADR-008]]). Icon from the brand mark.

**Three surfaces are modelled in code with no control bound to them**, all found on 2026-08-19:
the `WHEN WINDOWS STARTS` section (so **autostart cannot be switched on from anywhere in the app**,
though `ToggleAutostart` exists and is tested), the `UPDATES` section, and the close-to-tray
preference. That is XAML plus bindings, not new machinery.

**Exits when** a stranger can download it, run it, and not accidentally publish their own
hardware serial number.

[Full detail →](phase-7-release.md) · including the **1.0 gate table** — which open debts must
close before a release, and which may ship open.

---

## Ordering notes

**Why the CLI before the GUI.** The CLI is a few days and makes phases 2–3 testable
end-to-end. Building the GUI first means testing the store through a UI, which is slow and
tests the wrong thing.

**Why plugin tiers late.** Tiers 1 and 2 carry most of the user value at under half a
megabyte. Tiers 3 and 4 are the largest, slowest and most failure-prone code in the project,
and none of it is needed for the app to be useful.

**Why release is a phase and not a step.** It contains a hard gate: the privacy work. Treating
it as a checklist at the end of phase 6 is how it gets skipped.

**What happens after 1.0** is written down too, in [post-1.0.md](post-1.0.md) — separated into
*refused* (reopening one needs a new argument) and *deferred* (each names the signal that promotes
it). It exists so that phases 6 and 7 cannot quietly absorb work that was decided against.

## References

- `SPEC.md` — the authority on what to build
- [[ADR-003]] · [[ADR-004]] · [[ADR-006]] · [[ADR-007]] · [[ADR-008]]
- [technical-debt.md](../technical-debt.md) — inherited defects and unverified assumptions
