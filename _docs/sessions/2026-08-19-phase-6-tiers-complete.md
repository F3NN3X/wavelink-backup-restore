---
title: "Phase 6 §3–§8 — the rest of the tiers"
status: published
created: 2026-08-19
updated: 2026-08-19
tags: [session]
---

# Phase 6 §3–§8 — the rest of the tiers

**Shipped 0.6.0.** 1,146 tests (Core 399, CLI 97, App 650), up from 1,050. Release build clean,
zero warnings. Phase 6 is complete: all four tiers capture **and restore**.

The session before this one landed §1–§2 ([note](2026-08-19-phase-6-plugin-discovery.md)) and then
wrote the plans for everything left in the project. This one built §3 through §8.

## What changed, and why it is shaped that way

### One payload, and a store that knows nothing about tiers

`SnapshotStore.Write` takes a `SnapshotPayload` — the plugin manifest, a list of captured files,
and the tier names those files earn. It writes them, hashes and sizes every one into the manifest
identically, and has no idea which tier any of them came from. That is what let `SnapshotGuard`
verify a four-tier snapshot with the code it already had for a one-tier one.

The decisions all live in `TierCapture`, deliberately in one class: **a tier that is claimed but
empty, or captured but not claimed, is the kind of quiet dishonesty that only stays fixed if one
place decides it.**

The null-versus-empty rule from §2 survived the reshape and is the reason the payload type exists:
**a payload means a capture looked; no payload means nobody looked.** A restore reading an empty
`plugins.json` can say "nothing is missing" and be believed.

### Nothing in the new tiers can fail a capture

Tier 1's extras and tier 3 are best effort per file. Tier 4 is all-or-nothing, but its failure
costs only tier 4. The settings file is the product; no plug-in, preset or stale copy of Wave
Link's own is worth losing it over.

### The tier-1 gap the coverage pass found

[[ADR-006]], `SPEC.md` §1, the Settings dialog's "470 KB" row and `technical-debt.md` §3 all
described tier 1 as `Settings.json` **plus Wave Link's own backup copies**. Only the 43 KB settings
file was captured — and §3 of the debt file stated the opposite **as settled fact**. Writing the
spec-coverage table is what surfaced it, which is the argument for having written one.

Now: the newest ten `AutoBackup` files and the newest ten `.bak` atomic-save artifacts, under
`wavelink-backups/`. Captured, never managed, never written back on restore — they are Wave Link's
files in Wave Link's directory, and here they are evidence rather than payload.

### The bundle, on both sides

Capture tests `Directory.Exists` **first** and recurses; restore rebuilds the tree. An empty bundle
directory counts as a failure rather than a zero-byte success. All six plug-ins on this machine are
single files, so both paths exist only because a fixture exercises them ([[vst3-backs-up-as-nothing]]).
That closes `technical-debt.md` §2.3.

### The privilege model, as a test rather than an intention

Tiers 1–3 write to `LocalState` and `%APPDATA%`, which the user owns. Tier 4 writes into
`C:\Program Files\Common Files\VST3` and is the only thing in the program that can need
administrator rights — so it is opt-in (`wlbackup restore <id> --with-plugins`), an
`UnauthorizedAccessException` is reported as **needs elevation** (distinct from an `IOException`,
because "run it again elevated" and "something else has the file" are different answers), and it
**never fails the restore**, which by then has already written the settings file.

### The warning that has been null since phase 5

`RestoreDialogModel.MissingPluginWarning` is real. Core resolves and words it, as `RestorePlanner`
already did for the Wave Link version note; the shell renders and decides nothing. Naming the
channel — *"The Voice channel will load with that effect switched off"* — is why `plugins.json`
records channels at all.

Version drift is **not** amber: it joins the quiet mono line the version mismatch uses, because a
plug-in that updated is not missing. That placement is the one thing here the design does not
specify, so it is written down rather than assumed.

## Two defects found on the way

- **The "Your setup" row was measuring the wrong file** — Wave Link Backup's own preferences file,
  a few hundred bytes, rather than the Wave Link settings the row describes. Fixed by the measured
  sizes replacing it.
- **`WhatGoesInRow.Locked` meant "not built yet"** while the view tests already modelled the
  design's meaning ("no switch, always on") — the app and its own tests disagreed and both passed.
  `Locked` now means what the design says, and the `NOT BUILT YET` badge is deleted.

## Deliberately not done

Both recorded rather than dropped:

- **The shell cannot ask for a tier 4 restore** (`technical-debt.md` §4.17). Elevation has no
  designed surface — no UAC prompt in any screen, no error state for a declined one among the
  twelve in `06-errors.md`. Building one in XAML during phase 6 would have been inventing design in
  code. The capability is in Core and reachable from the CLI, and the restore dialog already tells
  the user to install the plug-in themselves, which is the design's own answer.
- **The preset heuristic has never met a real vendor folder** (§4.18). Every test is a synthetic
  tree. The check is one capture and one look at `presetSource` in `plugins.json` — which is why
  every plug-in records it.

New debt from this work: §4.19 (tier 4 reads whole binaries into memory) and §4.16 got worse (tier
4 can read the same 40 MB a second time to copy it).

## Where things stand

Phase 7 — **release** — is next, and it starts with the privacy gate:
[phase-7-release.md](../dev-phases/phase-7-release.md). The 1.0 gate table there says which open
debts block a release and which may ship open; none of §4.16–§4.19 does.

Still wanted before a release, unchanged from 0.5.1: **a human at a desktop**. The dialog frosting,
the motion timings, the scrollbar, the restored letter-spacing (§4.15) — and now the preset
heuristic (§4.18) — are all things no test can see.
