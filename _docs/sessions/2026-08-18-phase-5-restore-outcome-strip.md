---
title: "Session: Phase 5 part 4 — the restore-outcome strip"
status: published
created: 2026-08-18
updated: 2026-08-18
related_adrs: [ADR-004, ADR-005]
tags: [session, app, restore, theming, phase-5]
---

# Session: Phase 5 part 4 — the restore-outcome strip

**Date:** 2026-08-18

**764 tests green** (296 Core, 91 CLI, **377 App**), Release clean with zero warnings. The
strip ships as a **dormant seam**: it is fully built, themed and tested, but nothing feeds it yet —
the restore button still shows the placeholder this repo has used for every unwired action.

Executed: [screens/03-restore-outcomes.md](../operations/design/screens/03-restore-outcomes.md),
the design's spec for the four restore outcomes and the strip that reports them.

## What shipped

**The four outcomes are a state model, not four pieces of UI.** `RestoreOutcomeStrip` holds
exactly the states `03` names — *Succeeded confirmed*, *Succeeded unconfirmed*, *Rejected*,
*Failed* — plus the empty `None`. The distinction that matters is between the first two: a
restore whose log verdict could not be read is **not** the same claim as one that was verified,
so an unreadable verdict lands in *unconfirmed* rather than silently passing as success. That is
the whole reason the model carries both instead of collapsing them into "worked".

**Each outcome decides its own chrome.** `HasLeftEdge` (the coloured bar), `TurnsStatusAmber`
(the status strip below it warms to match), `AutoDismisses`, `Dismissible`, `HasAction` and
`ActionLabel` are all derived per state, not stored. *Rejected* is the one that refuses to
dismiss on its own — a restore the user turned down must stay visible until they acknowledge it,
so `Dismiss()` declines to fire while the strip is in that state and only `AcknowledgeReject()`
clears it. Everything else clears itself after `AutoDismissAfter` (6 s) or on a click.

**The XAML renders the model with four DataTriggers**, one per non-empty outcome, driving the
left edge, glyph, title/detail text and the action/dismiss buttons off the same properties the
ViewModel exposes. No outcome-specific code paths in the view — add a fifth state and only the
model and one trigger change.

**A new brush, `WlDangerSoft`, in all three themes.** The *Failed* state needed a soft red that
the existing palette did not carry. It is added to Dark, Light and HighContrast together, and in
HighContrast it binds to **Transparent**, per `11-high-contrast.md` — the rule that in a
high-contrast theme the tint layer is not ours to author, so every soft fill goes away and only
the system colour remains. `ThemeManager.BrushKeys` lists it so the existing
"every theme declares every brush" test covers it for free; that test passing across all three
dictionaries is what proves the key resolves everywhere, without anyone looking at a screen.

**The wiring is real but deliberately idle.** `MainWindow` owns the strip and an auto-dismiss
timer, and exposes `Strip` on `ShellViewModel`. But the restore button still calls
`ShowRestorePlaceholder()` — the same visible-do-nothing answer this repo has used for *Settings…*
and every other unwired action — so the strip cannot currently light up from a user gesture. It
exists to be called the moment plan 4's real restore flow lands, rather than being invented then.

## What broke, and what it taught

**Three compile errors, all small, all caught by the build.** A missing `using` for the Core
`Restore` namespace; `"0".repeat(64)` — C# has no string `.repeat`, so a zeroed hash is
`new string('0', 64)`; and an `IReadOnlyDictionary` collection expression, which does not exist
for that interface type, replaced with a concrete `Dictionary<string, SnapshotFile>`. None were
design problems; they are the cost of the App project being young enough that its idioms are not
yet muscle memory.

**The test that pins the null-verdict branch is the one worth keeping.** A new Core test asserts
that `RestoreOutcome.Confirmed` tracks the verdict's success **including when the verdict is
null** — which is exactly the *unconfirmed* path above, fixed at the model level rather than left
to a UI guess. It is the assertion that would have passed while the bug remained if the branch
had been inverted.

## Decisions

| Decision | Reasoning |
|---|---|
| **The strip is dormant by design, not an accident** | Wiring it to a fake restore now would mean unwiring it when the real flow lands. A tested seam that nothing calls yet is this repo's established shape for unwired actions — same as *Settings…* in §4.8 item 4 |
| **`WlDangerSoft` is Transparent in HighContrast** | `11-high-contrast.md` says the palette is not ours there. A soft red we authored would fight the user's system colour; removing the tint and keeping only the edge + text is the rule applied, not an exception |
| **Confirmed vs unconfirmed are separate states** | An unreadable log verdict is a different claim than a verified one. Collapsing them would let "we could not check" read as "it worked", which is the exact failure a backup tool must not make |
| **`Rejected` cannot auto-dismiss or be dismissed by `Dismiss()`** | A restore the user turned down is information they owe themselves until they act on it. Only `AcknowledgeReject()` clears it; everything else respects the 6-second self-clear |

## Still open

- **Nothing feeds the strip yet.** The real restore flow — plan 4's list row action calling
  `RestoreOrchestrator` and then `Strip.Show(outcome)` / `ShowFailure(message)` — is the work
  that turns this from a seam into a feature. Tracked in [technical-debt.md](../technical-debt.md)
  §4.9 as dormant, not broken.
- **The strip has not been seen by a human.** Every state is tested and the three-theme brush
  test passes, but no one has watched it light up — for the same reason `11` is the section where
  "the test passes" and "the screen is usable" diverge most.
- **High contrast's *Failed* state is unverified by eye.** The Transparent tint is correct per
  the rule; whether the remaining edge + text reads as "failed" in a real high-contrast theme is
  not yet checked.

## References

- [phase-5-wpf.md](../dev-phases/phase-5-wpf.md) · [technical-debt.md](../technical-debt.md) §4.9
- [screens/03-restore-outcomes.md](../operations/design/screens/03-restore-outcomes.md) ·
  [screens/11-high-contrast.md](../operations/design/screens/11-high-contrast.md)
- [[ADR-004]] thin shells · [[ADR-005]] WPF
