---
title: "ADR-014: The health strip is as wide as the rig, and collapse is a drop"
status: accepted
created: 2026-08-20
updated: 2026-08-20
related_adrs: [ADR-005]
tags: [decision, ui, health]
---

# ADR-014: The health strip is as wide as the rig, and collapse is a drop

**Status:** Accepted
**Date:** 2026-08-20

## Context

Screen 1's INPUTS column is *"five equal flex cells, 4px apart, always in the same order and the
same place, so a gap breaks the pattern of the whole column before any text is read"* — the design
package's own description of the piece it calls the core information design, and the piece
[the conformance audit](../audits/2026-08-19-design-conformance.md) §3 singled out as *"the part of
the design that was hardest to get right and it is right."*

Then a user added four channels to Wave Link, and two things broke at once.

**The row showed five of nine.** `InputSlots.Build` allocated exactly `SlotCount = 5` and the panel
was a `UniformGrid Columns="5"`. Meld Studio, Media Player, Aux 1 and Aux 2 were not truncated with
a marker — they were absent, and nothing in the row said a channel was missing from it. The
constant carried a comment saying five is a layout width and not a claim about a rig, and a test
named `More_than_five_inputs_shows_the_first_five` pinned the truncation in place.

**And every older backup turned amber.** Genericness — the collapsed treatment, the app's word for
*Wave Link reset your configuration* — was `inputNames.Count < peakInputCount`, the peak taken
across the whole store. Adding channels raised the peak to nine, so every backup taken before that
moment became "fewer inputs than the peak" retroactively. They had lost nothing; the rig had
gained something.

## Decision

**The strip is as wide as the widest configuration in the store, never narrower than five.** Every
row draws the same number of cells (`InputSlots.SlotsFor`), so an older backup shows the channels
it does not hold as missing — which is the true statement: restoring it would not bring them back.

**The labels yield to the extra channels, never the channels themselves.** `LabelBudget` divides
the fixed 300px strip by the cell count and yields nine characters at five cells, four at nine, and
none past about a dozen — at which point the cells keep their solid/solid-warn/dashed rules and
lose their words. Below six characters a label drops its spaces before its letters, so AUX 1 and
AUX 2 stay distinguishable as AUX1 and AUX2 rather than collapsing into two cells both reading AUX.

**Collapse is a drop against the SNAPSHOT BEFORE IT**, not against the store's peak
(`InputSlots.IsCollapsed`). The oldest snapshot has nothing to compare against and is never
collapsed.

## Alternatives considered

| Option | Why not |
|---|---|
| Keep five labelled cells and add a `+4` chip | Scans identically at any rig size, and the extra channels are only ever a number. The column exists to answer "does this backup have my rig in it" at a glance, and on a nine-channel rig it would answer for five of them and count the rest. |
| Cells sized per row — a row draws only its own channels | Destroys the one property the strip is built on. Two rows are comparable at a glance only because cell *n* is in the same place on both; variable widths make a five-input row and a nine-input row two unrelated pictures. |
| Never drop a label — shorten and ellipsise indefinitely | One- and two-character labels read as noise, and "MU"/"ME" cannot tell MUSIC from MEDIA PLAYER. A wrong-looking abbreviation is worse than an honest blank, and the rules carry the health meaning without any label at all. |
| Measure each cell at layout time and decide per cell | The answer has to be the same for every row. A per-cell measurement lets one row label a channel its neighbour blanked — the column stops being scannable for exactly the reason above. Arithmetic over a measured character width keeps it uniform. |
| Judge collapse against the peak, but only within a time window | A rule with a knob nobody can set correctly. The previous snapshot is what `HealthFingerprint` already compares against, and what `InputSlots`' own comment already claimed it did. |
| Widen the INPUTS column instead | The column is 300px because the row grid is the design's, and the four other columns are spoken for. Taking width from NAME to fit labels the user can read in the details dialog anyway is the wrong trade. |

## Consequences

**This enables:** a rig of any size drawn whole; a growing rig that leaves its own history alone;
and an amber strip that means what it says again — Wave Link lost channels between two backups.

**This rules out:** treating the five-cell strip as a fixed layout anywhere else. Anything that
assumes five — a hard `Columns="5"`, an array of five, a sentence saying "of 5 inputs" — is now
wrong, and the panel is `Rows="1"` precisely so the count comes from the data. It also rules out
labels as the strip's primary channel of meaning: past a dozen inputs the row carries shape only,
and the names live in the tooltip and [the details dialog][details].

**It costs one comparison per row on every rebuild** — a dictionary of predecessor input counts,
built from the store ordered by capture time. Ordered by capture time and not by list order,
because two backups a second apart is the pre-restore pair, and comparing them the wrong way round
would report the collapse on the snapshot that recorded the rescue rather than the one that
recorded the damage.

**Revisit if:** a rig turns up big enough that even the rules are unreadable — roughly twenty
channels in 300px. The answer then is probably a per-row overflow, not a narrower cell.

## References

- `_docs/operations/design/README.md` §Screen 1 — the five-slot strip as drawn
- `_docs/technical-debt.md` §5 — *"5 inputs / 43 KB is one user's rig"*, the note this decision
  finally acts on
- [[every-older-backup-turns-amber-after-adding-a-channel]] — the reported symptom
- `src/WaveLinkBackup.App/ViewModels/InputSlots.cs` · `tests/…/InputSlotsTests.cs`

[details]: ADR-015-the-details-view-reads-the-backup-itself.md
