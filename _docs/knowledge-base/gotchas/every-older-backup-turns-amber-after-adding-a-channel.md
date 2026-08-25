---
title: "Every older backup turns amber after adding a channel"
status: published
created: 2026-08-20
updated: 2026-08-20
related_adrs: [ADR-014]
tags: [gotcha, ui, health]
---

# Every older backup turns amber after adding a channel

**Provenance:** Observed, 2026-08-20, on a rig that went from five channels to nine. Seen in a
screenshot of the shipped app before it was traced in the source.

## Symptom

Add channels in Wave Link. Take one backup, and **every backup you already had turns amber**, the
whole INPUTS strip, on every older row at once.

Amber in this app means one thing: Wave Link fell back to device-derived names, which is what a
reset looks like. So the list now says your entire backup history is damaged, and it says it on the
day you did something perfectly ordinary.

The rows are otherwise fine. They restore. Their names, sizes and tier badges are right.

## Cause

Genericness was decided against the STORE'S PEAK:

```csharp
var collapsed = inputNames.Count < peakInputCount;   // peak = max across every snapshot
```

That is correct while a rig never changes, and wrong the moment one grows. Nine channels raises the
peak to nine, so every five-input backup is now "fewer inputs than the peak", retroactively, and
all at once. They had lost nothing; the rig had gained something.

## The plausible explanation, and why it is wrong

*"The new channels confused the analysis, so the old manifests are being re-read wrongly."*
Nothing was re-read: `manifest.json` is written once, and every one of those files still says
exactly what it said yesterday. The verdict is computed at display time, and it is the comparison
that moved, not the data.

The second wrong turn is to fix it by adding a threshold, "only mark collapsed if it dropped by
more than half", or "only within a week". A rule with a knob nobody can set correctly. The right
comparison was already written down twice: `HealthFingerprint` compares against the user's previous
snapshot, and `InputSlots`' own doc comment claimed the same, *"health is decided against that
user's previous snapshot, never against an absolute threshold"*, while the code beneath it did
something else.

## Fix

Compare against the snapshot immediately older than this one:

```csharp
public static bool IsCollapsed(int inputCount, int previousInputCount) =>
    previousInputCount > 0 && inputCount < previousInputCount;
```

The oldest snapshot has no predecessor, reads as 0, and is never collapsed. There is nothing to
have collapsed *from*.

The predecessor map is built from the whole store ordered by **capture time**, not by list order: a
pre-restore backup and the one before it can be a second apart, and comparing them the wrong way
round reports the collapse on the snapshot that recorded the rescue rather than the one that
recorded the damage.

## How to avoid it

**When a comparison is against "the biggest we have seen", ask what happens when the biggest
changes.** A high-water mark is a fine way to size a layout, the strip is still as wide as the
peak ([[ADR-014]]), and a poor way to decide a verdict, because it rewrites the verdict on
everything older every time it moves.

Held down by `InputSlotsTests.A_rig_that_grew_leaves_its_older_backups_alone` and its opposite,
`A_backup_with_fewer_inputs_than_the_one_before_it_reads_generic`, the same two input names read
green in one and amber in the other, which is the property that makes genericness a fact about the
row and never about the name.

## References

- [[ADR-014]]: the decision, including why the strip's *width* still comes from the peak
- `_docs/technical-debt.md` §5, *"5 inputs / 43 KB is one user's rig"*
- [[newest-backup-is-the-broken-one]]: the failure this amber treatment exists to make visible
