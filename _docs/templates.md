---
title: "Document Templates"
status: published
created: 2026-08-16
updated: 2026-08-16
tags: [meta, templates]
---

# Document Templates

Copy the matching block, fill it in, and update `documentation-stats.md` in the same commit.

The headings are the questions that turned out to matter. Deleting one because you have
nothing to say for it is usually the moment you discover you have not finished thinking.
If a heading genuinely does not apply, write "n/a" and why — that is information too.

---

## ADR

**File:** `decisions/ADR-0NN-short-slug.md` — numbered in the order written, never renumbered.

````markdown
---
title: "ADR-0NN: Short Decision Title"
status: proposed | accepted | deprecated | superseded
created: YYYY-MM-DD
updated: YYYY-MM-DD
tags: [decision, area]
---

# ADR-0NN: Short Decision Title

**Status:** Accepted
**Date:** YYYY-MM-DD

## Context

What forced a decision. The constraint, the discovery, the thing that stopped working.
Enough that someone who was not there understands why this was not obvious.

## Decision

What we are doing, stated flatly in one or two sentences.

## Alternatives considered

| Option | Why not |
|---|---|
| … | … |

The real ones only. An alternative nobody seriously weighed is padding, and it makes the
ones that were weighed look less considered.

## Consequences

**This enables:** …

**This rules out:** … ← the part future-you needs. It is the reason a "simple" change later
turns out not to be.

**Revisit if:** the condition that would make this decision wrong.

## References

- `SPEC.md` §N
- [[related-adr]] · [[related-gotcha]]
````

---

## Gotcha

**File:** `knowledge-base/gotchas/symptom-phrased-as-a-symptom.md`

**Title by the symptom.** The next person will be describing the surface, not the cause —
they do not know the cause yet, which is why they are searching.

````markdown
---
title: "Symptom, phrased the way you would describe it to someone"
status: published
created: YYYY-MM-DD
updated: YYYY-MM-DD
related_adrs: [ADR-00N]
tags: [gotcha, area]
---

# Symptom, phrased the way you would describe it to someone

**Provenance:** Observed | Read, not reproduced | Assumed — and the date and source.
Not optional in this project. See `README.md` for why.

## Symptom

What you actually see. No diagnosis yet.

## Cause

What is really happening.

## The plausible explanation, and why it is wrong

The thing you will try first. Say why it is not that, or it gets tried again — and that
retry is most of the cost of the bug.

## Fix

What to do. Code where code is clearer than prose.

## How to avoid it

The guard, test or design choice that makes it impossible rather than merely known.

## References

- `SPEC.md` §N · [[related-adr]] · [[related-recipe]]
````

---

## Pattern

**File:** `knowledge-base/patterns/pattern-name.md`

**Only proven patterns.** Extract from code that ships, never from an intention. Name the
actual callers — a pattern with no callers is a theory, and belongs in an ADR or the spec.

`knowledge-base/patterns/` does not exist yet; create it when the first pattern is real.

````markdown
---
title: "Pattern Name"
status: published
created: YYYY-MM-DD
updated: YYYY-MM-DD
tags: [pattern, area]
---

# Pattern Name

## Problem

The recurring situation.

## Solution

The shape, with a minimal working example from the codebase — real code, not pseudocode.

## Callers

| Where | Why it uses this |
|---|---|
| `Path/To/File.cs:NN` | … |

If this table is empty, this is not a pattern yet.

## Held down by

The test that fails if someone breaks it: `Path/To/Tests.cs::TestName`.

## When not to use it

The boundary. A pattern without one gets applied everywhere and stops meaning anything.

## References

- [[related-adr]] · [[related-gotcha]]
````

---

## Runbook

**File:** `operations/runbooks/verb-the-system.md`

**A runbook is not a recipe.** A recipe is a task whose order is load-bearing; a runbook is a
*system* — the procedure, the contract it has to satisfy, and what to do when it does not. If
there is nothing to put in the symptom table, write a recipe instead.

**State provenance at the top.** A runbook often describes something that has not run in anger
yet. Say which parts are measured and which are unverified, and correct it the first time reality
disagrees.

````markdown
---
title: "Verb the system"
status: published
created: YYYY-MM-DD
updated: YYYY-MM-DD
related_adrs: [ADR-0NN]
tags: [runbook, area]
---

# Verb the system

One sentence on what loop this describes, and why its halves belong in one document.

> **Provenance.** What has been run, and what has not. Update this the first time it runs for
> real.

## 1 · The contract

What has to be true for this to work at all — the shape, the names, the invariants. Put it first:
everything below is only correct if this is.

## 2 · The procedure

The steps. Numbered only where the order matters.

## 3 · What the system does with it

The other half of the loop.

## Decisions worth knowing before you change it

The things that look arbitrary and are not. Link the ADR rather than restating it.

## Owed before this is used in anger

| | What | Why |
|---|---|---|

## When it goes wrong

| Symptom | Likely cause |
|---|---|

## References
````

---

## Recipe

**File:** `knowledge-base/recipes/verb-the-thing.md`

If the order does not matter, it is not a recipe, it is a paragraph.

````markdown
---
title: "Verb the thing"
status: published
created: YYYY-MM-DD
updated: YYYY-MM-DD
tags: [recipe, area]
---

# Verb the thing

**When:** the situation that sends you here.
**Prerequisites:** what must already be true.

## Steps

1. **Step.** What to do.
   > **Why this order:** only where the order is load-bearing. Where it is not, say nothing —
   > an unexplained warning on every step trains people to skip all of them.

2. **Step.** …

## Verifying it worked

How you know — from evidence, not from the thing looking right. In this project that
distinction is load-bearing: a UI that looks correct can be a freshly generated default.

## If it goes wrong

The rollback, and the state you land in.

## References

- `SPEC.md` §N · [[related-gotcha]]
````

---

## Session note

**File:** `sessions/YYYY-MM-DD-short-slug.md` — two sessions on one date are distinguished by
slug, never by a suffix number.

````markdown
---
title: "Session: What Happened"
status: published
created: YYYY-MM-DD
updated: YYYY-MM-DD
tags: [session, area]
---

# Session: What Happened

**Date:** YYYY-MM-DD

## Goal

What we set out to do.

## What happened

The narrative, briefly. Where a decision was made, link the ADR rather than re-arguing it.

## Decisions made

| Decision | Recorded in |
|---|---|
| … | [[ADR-00N]] |

## What did not work

The dead ends. This is often the most valuable section — it is the part that is nowhere in
the diff, and the reason someone repeats the attempt six months later.

## Open questions

What is still unresolved, and what would settle it.

## Next

The immediately next thing, concretely enough to start from cold.
````

---

## Dev phase

**File:** `dev-phases/phase-N-short-slug.md`

Written when the phase becomes current or next. Writing phase 6 in detail while phase 1 is
unbuilt produces fiction.

````markdown
---
title: "Phase N — Name"
status: draft | review | published
created: YYYY-MM-DD
updated: YYYY-MM-DD
tags: [dev-phase]
---

# Phase N — Name

**Status:** Not started | In progress | Complete
**Entry criteria:** what must be true to begin.
**Exit criteria:** what must be true to call it done. Testable statements, not intentions.

## Why this phase exists

What it unblocks.

## Scope

### In

- …

### Out — and where it went instead

- … → phase M

The "out" list is what stops a phase quietly absorbing the next one.

## Work

Grouped, not a flat checklist. Each item names the spec section or ADR it comes from.

## Risks

What could make this phase overrun, and the early signal for each.
````
