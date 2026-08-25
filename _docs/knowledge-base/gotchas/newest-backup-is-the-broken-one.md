---
title: "Restoring the newest backup restores the broken config"
status: published
created: 2026-08-16
updated: 2026-08-16
related_adrs: [ADR-003, ADR-007]
tags: [gotcha, restore, ranking]
---

# Restoring the newest backup restores the broken config

**Provenance:** **Observed**, during the 2026-08-11 recovery. The three-file listing below is
the real one from that incident.

## Symptom

The mixer has collapsed to defaults. You go to Wave Link's own backups, take the most recent
one, obviously the closest to the last known-good state, and restore it.

The mixer is still collapsed. You try the next one. Same.

## Cause

**Wave Link writes a backup immediately after resetting.** So the newest backup is not the
last good configuration; it is a copy of the damage, taken seconds after it happened.

From the incident:

```
21:39  11819 b  inputs=2  [Elgato Wave:3, System]            ← post-reset. Newest.
21:36  40224 b  inputs=5  [Wave Mic 1, Voice, Browser, ...]  ← last good.
20:30  41805 b  inputs=5  [Wave Mic 1, Voice, Browser, ...]
```

Three minutes and 28 KB separate the newest file from the one you actually want. Sorting by
timestamp puts the useless one first, and, because rolling retention keeps roughly ten
copies covering about three days, repeatedly restoring the wrong one can age the good ones
out entirely.

## The plausible explanation, and why it is wrong

> *"The newest backup is closest to the last good state. Restore chronologically, working
> backwards."*

This is right for almost every backup system, which is why it is the first thing anyone tries.
It is wrong here for a specific structural reason: **the event that damages the config also
triggers a backup**. The reset writes defaults, Wave Link saves, and the save is backed up.
The damage is *inside* the backup timeline, not outside it.

The second wrong turn is trusting the UI after a restore. A mixer that looks correct can be a
freshly generated default, five channels named plausibly is not evidence. Verify from the
log, not the window.

## Fix

**Rank candidates by content, never by timestamp.** Input count and file size are enough:

| Signal | Healthy | Collapsed |
|---|---|---|
| Input count | 5 | 2 |
| Size | ~40 KB | ~11 KB |
| Input names | `Wave Mic 1, Voice, Browser, Game, System` | `Elgato Wave:3, System` |

Compute this at snapshot time, store it in the manifest (`inputCount`, `inputNames`), and
surface it in the list. That is what the five-slot health strip in the main window is for: a
collapsed configuration breaks the visual pattern of the whole column before any text is read.

**The threshold must be relative.** Five inputs and 43 KB is *one user's rig*. Compare a
snapshot against **that user's previous snapshots**, never against a constant, an absolute
threshold is a bug waiting for the first user with three inputs.

Verify a restore from the newest log, not from the UI:

```powershell
$log = Get-ChildItem "$ls\Logs" -File | Sort-Object LastWriteTime -Desc | Select-Object -First 1
Select-String $log.FullName -Pattern 'Failed to parse|Created a new backup file|Applied saved'
```

Success is the **absence** of `Failed to parse settings file` plus the presence of
`Applied saved friendly name 'Wave Mic 1'`.

## How to avoid it

- **Never sort a restore list by timestamp alone.** Date is a column, not the ranking.
- **Store the health fingerprint at write time**, so ranking never requires opening snapshots.
- **Make the collapsed case visually loud.** The design does this deliberately: amber row
  tint, amber left edge, warn-coloured input slots. A user should not have to compare numbers.
- **Take a pre-restore snapshot every time, automatically.** It converts "I restored the wrong
  one three times and lost the good copy" into a recoverable afternoon. This is why it is not
  a checkbox ([[ADR-003]]).

## References

- `SPEC.md` §2, §3, §4, §5, §11
- [[ADR-003]] · [[ADR-007]] · [[file-parses-but-wave-link-resets]] ·
  [[restore-a-settings-file-safely]]
- [glossary.md](../../glossary.md), *collapsed*, *health fingerprint*, *relative not absolute*
