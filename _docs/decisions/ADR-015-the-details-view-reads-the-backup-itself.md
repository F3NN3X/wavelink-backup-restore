---
title: "ADR-015: The details view reads the backup itself"
status: accepted
created: 2026-08-20
updated: 2026-08-20
related_adrs: [ADR-004, ADR-006, ADR-014]
tags: [decision, ui, analysis]
---

# ADR-015: The details view reads the backup itself

**Status:** Accepted
**Date:** 2026-08-20

## Context

The list answers *"is this the one I want?"* — a name, a time, five (now N) input slots and three
tier badges. It cannot answer the next question anybody asks: **what is actually in it?** Which
channels, what sits on each one and in what order, where each channel is heard, and what the mixes
play out of.

Everything needed is in the settings file the snapshot already holds. `MixerConfiguration
.InputSettings` carries each channel's `AudioPluginConfigurations` — an ordered array with a name,
a vendor, a category, a bypass flag and a `FilePath` that is empty for an Elgato built-in
([[ADR-006]]) — plus the `MixerIds` that route the channel, and `MixSettings` carries the mix names
and their output devices. None of it is in `manifest.json`, which records what the LIST needs:
counts, names, tiers.

So there was a choice about where the answer comes from, and a second about where it is shown.

## Decision

**Read the snapshot's own `settings.json` on demand, and analyse it with a new pure Core type**
(`ConfigurationDetail.Read`). `manifest.json` is not extended.

**Show it in a dialog off the row** — Ctrl+I, the row's overflow menu, and a double-click — using
the settings dialog's shape: 680px card, header, internally scrolling body, footer.

**Describe the BACKUP, not the live configuration.** The footer says so in as many words: *"read
from the backup itself, not from what Wave Link holds now."*

## Alternatives considered

| Option | Why not |
|---|---|
| Extend `manifest.json` to carry channels, chains and mixes | It is written once and read forever. Every backup already on disk would answer this question with a blank, and the ones that matter most are the old ones. It also inflates a file the list parses for every row, to serve a dialog opened for one. |
| Expand the selected row inline instead of a dialog | A nine-channel rig with an eleven-effect chain is taller than the window. The row expansion is one line by design — *"actions live in the bottom bar, not in the row"* — and this would make the selected row push every other backup off screen. |
| A permanent inspector panel beside the list | ~380px of width in a window that already needs 1152 for its six columns, and the design draws no such panel. |
| Describe the live Wave Link setup instead | Says nothing about any particular backup, which is what the list is for. The live configuration is already described in the status strip and, where it matters most, in the restore dialog's NOW/AFTER table. |
| Show backup and live side by side, with differences marked | That is the restore dialog's table widened to every channel and effect. Duplicating a surface that exists, for a question — "what changed?" — that the restore dialog already answers at the moment it matters. |
| Put the read in the view | `MainWindow` has neither the store nor the file system, and [[ADR-004]] keeps the shells thin. App reads; the dialog renders a model. |

## Consequences

**This enables:** the question answered for **every snapshot already on disk**, including ones
taken before this feature existed; a damaged backup that can still be *asked* what it held, with
the reason it cannot answer shown in place; and — because `ConfigurationDetail` is pure, IO-free
Core — the same description available to the CLI or a future report without touching a window.

**This rules out:** answering the question when the settings file cannot be read. There is no
cached copy in the manifest to fall back on, by construction. For a damaged backup that is the
honest outcome; it is worth naming because "why not just store it too" will be asked again.

**It costs one synchronous file read on a press** — typically 47 KB, always local. A store on a
sleeping network drive is the only case where that is felt, which is the trade every other row
action already makes.

**The read is tolerant per field.** Every property it looks for is missing on some real file — an
older Wave Link, a channel added by a beta, a key Elgato renamed. Only a file that is not a
settings file at all fails. A details view that refuses to open because one channel has no
`WaveDeviceType` is worse than one that shows the channel with its type blank.

**Revisit if:** the dialog grows a need to compare two backups, or the live setup, at which point
the model — not the read — is what changes.

## References

- [[ADR-006]] — the empty `FilePath` that distinguishes an Elgato built-in from a third-party VST3
- [[ADR-004]] — Core does the thinking; the shells stay thin
- [[ADR-014]] — the row-level half of the same problem: showing a rig bigger than five channels
- `src/WaveLinkBackup.Core/Analysis/ConfigurationDetail.cs` ·
  `src/WaveLinkBackup.App/ViewModels/SnapshotDetailsModel.cs`
