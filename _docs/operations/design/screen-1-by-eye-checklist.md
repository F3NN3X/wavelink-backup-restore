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

**The rigs can be seeded rather than built.** Every snapshot below — five named inputs, a collapsed
two-input rig, nine channels, twelve, and one with long effect chains on every channel — is written
by [`tools/seed-fixture-store.ps1`](../../../tools/seed-fixture-store.ps1) into a throwaway store,
so the sitting does not start with half an hour of adding and removing channels in Wave Link:

```powershell
.\tools\seed-fixture-store.ps1
# then: app -> Settings -> change the backup folder to the path it prints
# after the sitting: change it back, and delete the folder
```

The seeder refuses to write inside the real store, and `FixtureStoreSeederTests` holds it to the
manifest shape the app reads. The snapshots are for looking at, **not for restoring** — the
endpoint ids are invented, so a restore would describe channels no device on this rig has.

- [ ] Wave Link is installed and has run at least once, so a settings file exists to read.
- [ ] The store holds **at least three snapshots**: one with five inputs, one with nine or more, and
      one older than both — the strip and the collapse rule are only legible against a spread.
      *(Seeded, or your own.)*
- [ ] A rig with **nine or more channels** is reachable, so the strip can be seen past its
      five-cell design width. *(Seeded as "Nine channels" and "Twelve channels".)*
- [ ] A rig with **several long effect chains** is reachable, for the details dialog's height cap.
      *(Seeded as "Long effect chains": six effects on each of five channels.)*
- [ ] Both a light theme and a **real** high-contrast scheme (Windows' own, not the app's simulated
      one) are switchable without a reboot.

## The looks

### 1 — §4.15: 0.5.1's dialog frosting has never been seen

Open any dialog (delete, restore, settings) and look at the window **behind** it.

- [x] There is a blur, not just the `WlScrim` dim.
- Machine: Windows 11 · Date: 2026-08-22
- Note: The blur is there — the window behind a dialog blurs, it is not only the `WlScrim` dim. The
  `SetWindowCompositionAttribute` call is doing its job on this build, so the frost stays and §4.15
  closes. The other 0.5.1 visual work named in the entry (motion timings, the scrollbar, the restored
  letter-spacing) read as deliberate in the same sitting; they were never separate debt items.

### 2 — §8.2: three surfaces built past the design package have never been looked at

These were built to the package's *rules* because the package has no drawing of them; none has had
a design pass look at the result. Check each in turn.

- [x] **The four-segment theme control** (Settings → HOW IT LOOKS, [[ADR-013]]) at **100% and 150%**
      display scaling: the segments read as one control, not four buttons; the checked treatment is
      the same `WlToggle` shape the rest of the app uses.
- [x] **The INPUTS strip** ([[ADR-014]]) at **nine cells** (four-character labels) and at **twelve or
      more** (three characters, then blank): the cell rules — solid, solid-warn, dashed — still read
      as health even where the words are gone; a missing channel reads as *missing*, not as an error.
- [x] **The details dialog** ([[ADR-015]]) in **light**, and again in a **real high-contrast scheme**:
      it reads as the settings dialog's shape, not a new idea; nothing clips.
- [x] **That dialog's height** on a rig with several long effect chains: it hits its 720px cap and
      scrolls rather than growing past the screen or cutting the last row off.
- Machine: Windows 11 · Date: 2026-08-22
- Note: Theme control, details dialog (light) and the height cap all read as deliberate — no defect.
  **The INPUTS strip reads cramped** at nine-plus cells: the five-slot design width is doing its job
  but the labels crowd once a rig grows past it. The fix is a design choice, not a one-line deletion —
   it was recorded as an open entry in `technical-debt.md` (variation 2B's verdict replaces the strip;
   the details dialog gains an input/output matrix so the names are never lost). §8.2's own look — does
   the strip read as health — passes; this is a *legibility* finding, not a failure of that rule.
   **Shipped 2026-08-22** as item 5 below: the verdict and the matrix are in, and this item's ticks
   describe the *old* strip — re-run them against the new surfaces before closing §8.2.

### 3 — §8.2's §8.4 tail: the header-to-row alignment after the scroll fix

The list's column header and the rows beneath it, **after scrolling the list**.

- [x] The header columns line up with the row columns now that the inner `ScrollViewer` owns the
      scroll (the outer `ListScrollViewer` is gone).
- Machine: Windows 11 · Date: 2026-08-22
- Note: Header and rows line up after scrolling — no regression against the §1.1 known-good state.

### 4 — §4.9's high-contrast tail: `WlDangerSoft` in a real high-contrast theme

With the **real** high-contrast scheme active (item 2 already switched it), trigger a failed restore
and read the strip.

- [x] The transparent fill still reads as *failed*, and has not become an empty gap.
- Machine: Windows 11 · Date: 2026-08-22
- Note: In a real high-contrast scheme the `WlDangerSoft` strip still reads as *failed*, not an
  empty gap — no amendment to `11-high-contrast.md` needed.

### 5 — §8.6's new surfaces: the INPUTS verdict and the details dialog's matrix (2026-08-22)

Item 2's finding shipped the same day as a commit, so the two surfaces it opened have their own
look: the ticks below are **owed**, not done. The suite holds the geometry and the data; this is
the pixels.

- [ ] **The INPUTS verdict** ([[ADR-014]]) on a row with **five inputs**: check-circle in the ok
      colour, "Complete", mono sub-line reads `5 INPUTS · ALL NAMED` — and on a row with **fewer**
      (a collapsed rig): warning triangle in warn, "Only part of your setup", the sub-line's
      `UNNAMED` in warn. The word stays full-strength either way; colour is never the only signal.
- [ ] **The verdict at nine-plus channels**, where the old strip read cramped: the cell no longer
      prints a name per channel, so it should read as *less* crowded than item 2's finding — the
      legibility fix, confirmed on pixels rather than by inference.
- [ ] **The details dialog's matrix** ("WHERE EACH INPUT IS HEARD", [[ADR-015]]): each channel row
      has one cell per mix column; a dot lands exactly where that channel's routing line says it
      feeds; a channel in no mix shows all-empty cells. In light and again in a real high-contrast
      scheme: nothing clips, the grid reads as the board it is.
- Machine: ____________________ · Date: ____________
- Note: Owed by §8.6's closure — the verdict replaced the strip item 2 checked, and the matrix
  joined the dialog item 2 checked. When done, tick each box and close §8.2's remaining tail in
  `technical-debt.md`.

## Record of sittings

Each completed sitting gets a line here so the checklist's own history is in the repo, not in
memory. A "clean" sitting ticks every box with no notes; a sitting that found something leaves the
note and the link to the entry it opened.

| Date | Machine (OS build · scaling) | Result | Entries opened |
|---|---|---|---|
| 2026-08-22 | Windows 11 · scaling n/a | Item 1 only — §4.15 frosting confirmed, blur present | none — §4.15 closed |
| 2026-08-22 | Windows 11 · scaling n/a | Items 2–4 — theme control, details dialog (light), height cap, header alignment and `WlDangerSoft` all read as deliberate; INPUTS strip reads cramped past nine cells | one entry in `technical-debt.md` — variation 2B verdict + input/output matrix in the details dialog; **shipped the same day**, re-opened as item 5 (the new surfaces are owed their own look) |
