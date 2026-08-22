---
title: "ADR-012: Update by staging beside the install and swapping, never elevated"
status: accepted
created: 2026-08-20
updated: 2026-08-22
related_adrs: [ADR-011, ADR-008, ADR-004, ADR-003]
tags: [decision, updates, security, ci]
---

# ADR-012: Update by staging beside the install and swapping, never elevated

**Status:** Accepted
**Date:** 2026-08-20 · updated 2026-08-22 (the publish shape this assumes changed — see Context)

## Context

`screens/12-tray-autostart-update.md` specifies a Settings `UPDATES` section with three rows and a
failed-update block. None of it was built — [technical-debt.md](../technical-debt.md) §4.21 item 5
— which also meant error 8's *Get the update* deep-linked to a section that did not exist. Error 8
is "this backup was made by a newer version"; its whole recovery *is* updating, so that button
landing nowhere was the most concrete cost.

The design fixes three things that constrain any answer:

- **"It never installs anything without you."**
- **An available update is never a notification, a badge or a banner.** The one exception is error
  8, where the user cannot restore until they update.
- **A failed update is neutral, not amber**: "a failed update leaves a working app, so nothing is
  un-whole."

Two facts about the environment matter as much:

**A process cannot overwrite its own executable while it is running.** Windows holds the image
file. So *any* self-update is at minimum two processes; the decision is which one does the writing
and what state the disk passes through.

**This app has no installer and is not code-signed.** It ships as a publish in a folder —
framework-dependent since 2026-08-22 (v0.7.2), self-contained before that; the swap mechanism does
not care which, it renames directories either way. There is no MSI, no MSIX identity, no Start-menu
shortcut, and no certificate. The framework-dependent choice means a machine without the .NET 10
Desktop Runtime fails at native load before managed code runs — there is no in-app surface to offer
a friendly prompt, which is why the prerequisite lives in the README rather than in a dialog.

[[ADR-011]] had already built an elevation path for tier 4 restore, so "just elevate and write to
`Program Files`" was available and cost nothing to implement.

## Decision

**Check on request or weekly; install only on a press; stage the new version beside the install and
swap by renaming; never elevate.**

- **Feed:** GitHub `releases/latest`, read with `JsonDocument`. Owner and repository come from the
  environment (`WLBACKUP_UPDATE_OWNER` / `_REPO`); unset hides the whole section.
- **Verification:** the release must publish a `.sha256` beside the archive. **No checksum, no
  install.** Hashed while streaming, so the file is never resident and never read twice.
- **Install:** expand to `<install>.staged`; hand over to the staged copy via
  `--apply-update <pid> <install>`; it waits for the old process, renames `<install>` to
  `<install>.previous`, renames `<install>.staged` to `<install>`, deletes the previous, and
  relaunches.
- **Never elevated.** An install the user cannot write reports the designed failed-update block and
  offers *Download it yourself*.

**Ordering is the load-bearing part.** The previous install is moved, not deleted, and removed only
once the new one is in place; a failed second rename puts it back and relaunches it. There is no
instant at which the user has no app.

## Alternatives considered

| Option | Why not |
|---|---|
| **MSIX / the Microsoft Store** | On its face the best answer to all of this: signed identity, differential updates, real toast notifications with buttons, and no self-update code at all. **Already refused, for a better reason than the obvious ones** — see [post-1.0.md](../dev-phases/post-1.0.md)'s *Refused* table and [[ADR-003]]: an MSIX package writes into a redirected `LocalState` that **an uninstall or a reset deletes wholesale**, which is the exact defect this whole project exists because upstream had. A backup tool whose store can be erased by repairing the app is not a trade worth signing for. The certificate and the changed install story are real costs too, but they are the smaller ones. If this is ever revisited it needs an answer to the store-location problem FIRST, not a certificate. |
| **Squirrel / Velopack / ClickOnce** | Mature, and they solve the two-process problem properly. Each brings an install-time footprint (a shortcut stub, an `Update.exe`, a deployment manifest) that changes the app from "a folder you can delete" into something with an uninstall story — a real cost for a utility whose whole shape is "configured once, then ignored". They also assume a release channel this project does not yet have, so adopting one now would be committing to their layout before the first release exists. Reconsider if the update path grows past what one class can hold. |
| **Elevate and write to `Program Files`** ([[ADR-011]]'s path, reused) | Free to implement and rejected on principle. Tier 4 restore writes *files the user chose, from their own disk*; an update writes **this program's own binaries, fetched from the network**. A program that silently escalates to administrator to replace its own executable is the shape a supply-chain attack wants it to have — and with no code signing, the app cannot even prove the bytes are its own. It would be defensible *after* signing; it is not before. The honest answer to an unwritable install is the failed-update block the design already draws. |
| **Download only — open the releases page and let the user install** | Smallest and safest, and genuinely tempting. Rejected because the design draws *Install and restart* as the primary action on that row, and because "download it yourself" is what the app already falls back to when it *cannot* write. Making the fallback the only path would leave the primary action undrawn — the exact debt this closes. |
| **A background poller** | Rejected by the design's own restraint rule, and by the shape of the app: it sits in the tray for weeks, and a poller would be network traffic nobody asked for in service of a banner the design forbids. The weekly check happens when Settings is opened, which is the only moment anyone can act on it. |
| **Delete the install, then extract in place** | The obvious two-line version, and the one with a window where the user has no app. If the extract fails after the delete — a full disk, a lock, a power cut — there is nothing to relaunch and nothing to roll back to. |
| **Signature verification instead of a checksum** | What this *should* have. Not possible without a signing certificate. The checksum is what is available today, and `UpdateRelease.Sha256` says in as many words that it proves integrity and not authenticity, so it cannot be misread later as a security guarantee. |

## Consequences

**This enables:** the designed UPDATES section in full; error 8's *Get the update* landing
somewhere; an update path that leaves a working app under every interruption; and a release
pipeline whose output shape is CI's responsibility rather than a person's memory.

**This rules out, and the cost is real:**

- **Installing to a machine-wide location.** Per-user installs update themselves; an install under
  `Program Files` will always report the failed block. If this app is ever deployed by an
  administrator, updating becomes their job.
- **Pre-release channels.** `ReleaseVersion.Parse` reads `1.4.0-beta.2` as `1.4.0` rather than
  ordering it, deliberately — inventing an ordering would silently decide whether a beta counts as
  newer than the release it precedes. Publishing a pre-release tag today would offer it to
  everyone. That needs its own decision before a `-beta` tag exists.
- **Differential updates.** Every update is the whole archive — ~7.6 MB since v0.7.2 made the app
  framework-dependent (it was ~101 MB self-contained before that). Acceptable for something that
  happens a few times a year either way. It is **not** an argument for MSIX, whose own objection is
  above and is structural rather than a matter of cost.
- **Any release not in the shape §1 of the runbook describes** is invisible to the updater. That is
  why the shape is produced by CI and pinned by the workflow rather than documented and hoped for.

**Unverified.** The loop has never run end to end — no remote, no release. The download, the swap
and the relaunch have met fixtures and temp directories only. First real release is owed a
confirmation, recorded in the runbook.

## References

- [`operations/runbooks/releasing-and-updating.md`](../operations/runbooks/releasing-and-updating.md)
  — the operational half: cutting a release, the shape, and what to do when it fails
- [`operations/design/screens/12-tray-autostart-update.md`](../operations/design/screens/12-tray-autostart-update.md)
  — the designed section and its restraint rules
- [[ADR-011]] — the elevation path this deliberately does not reuse
- [[ADR-008]] — Windows-only, which is why the swap can assume NTFS rename semantics
- [technical-debt.md](../technical-debt.md) §4.21 item 5, §5
