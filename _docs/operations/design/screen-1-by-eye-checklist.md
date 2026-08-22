---
title: "Screen 1 by-eye checklist"
status: published
created: 2026-08-22
updated: 2026-08-22
related_adrs: [ADR-013, ADR-014, ADR-015]
tags: [runbook, design, ui]
---

# Screen 1 by-eye checklist

The record for the looks nothing in the test suite can make. Every item on this page is a
surface that renders but that no assertion can say *looks right* — a layout, a blur, a contrast
call. The suite holds the geometry and the logic; this page holds the pixels. It exists so that
"needs a human" becomes "checked on this rig, 2026-08-XX" rather than a permanent state, and so
that when a look does find something wrong, the finding has a home instead of living in a chat
message.

**This is not a design spec.** It names what to look at and why it was never looked at; it does not
say what the answer should be. The design package ([`README.md`](README.md) and [`screens/`](screens/))
is the authority on intent. Where an item below finds that the shipped surface disagrees with the
package, that is a defect to fix or a design amendment to write — this page only records which.

## How to use it

Do the whole list in **one sitting, on one machine**, in the order given. The items share a setup
(a real Wave Link install, a store with several snapshots, both light and high-contrast themes
reachable) and splitting the sitting multiplies the cost of getting to that state. For each item:

1. Tick the box when you have looked, not when it looks right — a tick is *seen*, a note is *wrong*.
2. Write the **machine** (OS build + display scaling) and the **date** on the line under the item.
3. If something reads wrong, say what in the note field and link the technical-debt entry it
   belongs to. Do not fix it in the sitting unless the fix is a one-line deletion; otherwise open
   the entry and let the next commit carry it.

The order matters: §4.15 calibrates what "looks deliberate" means on this machine before the rest
of the looks, and the two behaviour items ride on the high-contrast switch that item 2 already
makes.

## Setup

- [ ] Wave Link is installed and has run at least once, so a settings file exists to read.
- [ ] The store holds **at least three snapshots**: one with five inputs, one with nine or more, and
      one older than both — the strip and the collapse rule are only legible against a spread.
- [ ] A rig with **nine or more channels** is reachable (add channels in Wave Link if none of the
      snapshots has them), so the strip can be seen past its five-cell design width.
- [ ] A rig with **several long effect chains** is reachable, for the details dialog's height cap.
- [ ] Both a light theme and a **real** high-contrast scheme (Windows' own, not the app's simulated
      one) are switchable without a reboot.

## The looks

### 1 — §4.15: 0.5.1's dialog frosting has never been seen

Open any dialog (delete, restore, settings) and look at the window **behind** it.

- [ ] There is a blur, not just the `WlScrim` dim.
- Machine: ____________________ · Date: ____________
- Note: If it is only the dim, the frost is silently doing nothing on this build and the call can be
  deleted in a follow-up commit — which drops the item to Tier 1. If the blur is there, tick and move
  on. This is the oldest open visual item (2026-08-19) and the cheapest look; doing it first also
  answers whether the rest of 0.5.1's visual work needs the same suspicion.

### 2 — §8.2: three surfaces built past the design package have never been looked at

These were built to the package's *rules* because the package has no drawing of them; none has had
a design pass look at the result. Check each in turn.

- [ ] **The four-segment theme control** (Settings → HOW IT LOOKS, [[ADR-013]]) at **100% and 150%**
      display scaling: the segments read as one control, not four buttons; the checked treatment is
      the same `WlToggle` shape the rest of the app uses.
- [ ] **The INPUTS strip** ([[ADR-014]]) at **nine cells** (four-character labels) and at **twelve or
      more** (three characters, then blank): the cell rules — solid, solid-warn, dashed — still read
      as health even where the words are gone; a missing channel reads as *missing*, not as an error.
- [ ] **The details dialog** ([[ADR-015]]) in **light**, and again in a **real high-contrast scheme**:
      it reads as the settings dialog's shape, not a new idea; nothing clips.
- [ ] **That dialog's height** on a rig with several long effect chains: it hits its 720px cap and
      scrolls rather than growing past the screen or cutting the last row off.
- Machine: ____________________ · Date: ____________
- Note: The largest batch of unchecked pixels and the one most likely to contain an actual defect,
  so it sits in the middle — after §4.15 has calibrated "looks deliberate", before the two behaviour
  items below.

### 3 — §8.2's §8.4 tail: the header-to-row alignment after the scroll fix

The list's column header and the rows beneath it, **after scrolling the list**.

- [ ] The header columns line up with the row columns now that the inner `ScrollViewer` owns the
      scroll (the outer `ListScrollViewer` is gone).
- Machine: ____________________ · Date: ____________
- Note: One surface and one glance, but it must be done *after* scrolling — the alignment is what
  changed with the scroll fix. This was audited as §1.1 of the design conformance pass, so a miss
  here is a regression against a known-good state, not an unknown.

### 4 — §4.9's high-contrast tail: `WlDangerSoft` in a real high-contrast theme

With the **real** high-contrast scheme active (item 2 already switched it), trigger a failed restore
and read the strip.

- [ ] The transparent fill still reads as *failed*, and has not become an empty gap.
- Machine: ____________________ · Date: ____________
- Note: The smallest look on this list and the one most likely to be fine — the rule was applied
  deliberately. If it reads as a gap, that is a design amendment for `11-high-contrast.md`, not a
  code fix.

## Record of sittings

Each completed sitting gets a line here so the checklist's own history is in the repo, not in
memory. A "clean" sitting ticks every box with no notes; a sitting that found something leaves the
note and the link to the entry it opened.

| Date | Machine (OS build · scaling) | Result | Entries opened |
|---|---|---|---|
| — | — | not yet done | — |
