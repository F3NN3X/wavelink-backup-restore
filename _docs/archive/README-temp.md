<!--
  ============================================================================
  TEMPLATE — the docs-system README, project-agnostic.

  Copy to `_docs/README.md` in a new project and fill in the placeholders.

  Placeholders:      {{PROJECT_NAME}}  {{PROJECT_ONE_LINER}}  {{SPEC_SOURCE}}
  Optional blocks:   marked <!-- OPTIONAL: ... --> — delete if they don't apply
  Customisation:     the checklist at the bottom lists everything to change,
                     including deleting this comment and that checklist

  The prose is deliberately opinionated. The rules only stick when the reason
  travels with them, so keep the "why" lines even when trimming.
  ============================================================================
-->

---
title: "{{PROJECT_NAME}} Documentation System"
status: archived
created: 2026-08-16
updated: 2026-08-16
tags: [meta, template]
---

> **Archived 2026-08-16.** Consumed into [`_docs/README.md`](../README.md) when the Wave Link
> Backup documentation system was created. Kept unmodified — it is the project-agnostic
> source, and diffing it against the filled-in version shows exactly which of its
> recommendations this project took and which it declined.

# {{PROJECT_NAME}} Documentation System

**Welcome to the {{PROJECT_NAME}} internal documentation system.**

{{PROJECT_ONE_LINER}}

This folder holds all working documentation, architectural decisions, knowledge base and
session notes. It follows a structured approach designed to preserve context, capture
decisions, and make knowledge discoverable months later by someone who was not there —
including you.

<!-- OPTIONAL: delete if the project has no external spec source.
**Nothing here duplicates {{SPEC_SOURCE}}.** That is the authority on *what* to build;
this folder records what we decided, what bit us, and what happened when. Where this repo
departs from the specs, say so and why.
-->

---

## Directory structure

```
_docs/
├── archive/                 # superseded documents, kept for the record
├── audits/                  # codebase and third-party audits
├── decisions/               # Architecture Decision Records (ADRs)
├── knowledge-base/
│   ├── patterns/            # proven solutions, extracted from working code
│   ├── gotchas/             # mistakes made and fixed, so they are not repeated
│   └── recipes/             # step-by-step guides
├── operations/              # runbooks, diagrams, design refs, environment
│   ├── runbooks/
│   ├── diagrams/
│   └── design/
├── plans/                   # implementation plans, written before the work
├── sessions/                # structured session notes
├── README.md                # this file — instructions only, no running totals
├── index.md                 # the landing page: start here
├── templates.md             # ADR · gotcha · pattern · recipe · session templates
├── glossary.md              # the vocabulary this project uses precisely
├── technical-debt.md        # the honest list
├── documentation-stats.md   # the living tally + cross-reference index
└── {{STATUS_DOC}}           # dev-phases/ or milestones.md — whichever fits the project
```

**Adopt what you need.** `archive/`, `audits/` and `plans/` are often empty at the start;
create them when the first real document arrives rather than filling them with
placeholders. A folder holding one thin file is worse than no folder — merge it upward.

<!-- OPTIONAL: only if the project has a docs-site generator (Fumadocs, Docusaurus, …).
> **Auto-indexed by {{DOC_GENERATOR}}** — new `.md` files with proper frontmatter are
> discovered automatically for sidebar navigation and full-text search. No manual
> registration needed in most directories.

### Adding new docs (auto-indexed)

1. **New file in an existing directory** — create a `.md` with frontmatter; it appears in
   the sidebar and search automatically.
2. **New subdirectory** — create the folder, add a `meta.json` with
   `{ "title": "Folder Title", "pages": ["..."] }`, and add the folder name to the
   parent's `meta.json` `pages` array.
3. **Hide a file from the sidebar** — prefix it with `!` in the parent `meta.json`
   (e.g. `"!ENV_VARS"`).
-->

<!-- OPTIONAL: use this instead when there is NO docs-site generator.
> No docs-site generator is wired up. Frontmatter is kept anyway — it is what makes the
> corpus greppable, and it would make adding a generator later a non-event. There are no
> `meta.json` files because nothing would consume them.
-->

---

## Quick start

**Finding something**

| You want | Look in |
|---|---|
| Why is it built this way? | `decisions/` |
| Has this bitten us before? | `knowledge-base/gotchas/` |
| How do I do X? | `knowledge-base/recipes/` |
| What is the proven approach? | `knowledge-base/patterns/` |
| How do I operate it? | `operations/runbooks/` |
| What happened, and when? | `sessions/` |
| What is left? | `{{STATUS_DOC}}`, `technical-debt.md` |

**Writing something.** Copy the matching template from `templates.md`, then add the file.
Update `documentation-stats.md` in the same commit.

---

## Folder guides

### `decisions/` — Architecture Decision Records

**Purpose:** document significant architectural and technical decisions.

**Write one when:**

- you chose between real alternatives and the reasoning is not obvious from the diff;
- you made a security or performance trade-off;
- you established a pattern that affects more than one feature;
- someone will later ask "why on earth is it done this way?"

**Record what it rules out**, not just what it enables. That is the part future-you needs,
because it is the reason a "simple" change later turns out not to be.

**Statuses:** `proposed` under discussion · `accepted` implemented and active ·
`deprecated` no longer recommended · `superseded` replaced — and name the replacement in
the body, not only in the frontmatter.

**Naming:** `ADR-0NN-short-slug.md`, numbered in the order written. Never renumber.

---

### `knowledge-base/` — patterns, gotchas, recipes

**Purpose:** extract reusable knowledge from sessions and from the codebase.

#### `patterns/` — proven solutions

Shapes that work in this codebase. **Only proven ones** — extract them from code that
ships, never from an intention. Name the actual callers; a pattern with no callers is a
theory. Link the test that holds it down.

#### `gotchas/` — mistakes to avoid

Bugs that actually happened. **Title by the symptom**, because that is what the next
person types into a search box — they will be describing the surface, not the cause.
Always include why the plausible explanation was wrong, or it gets tried again, and that
retry is most of the cost.

#### `recipes/` — step-by-step guides

Tasks with an order that matters, with the reason attached wherever the order is
load-bearing. If the order does not matter, it is not a recipe, it is a paragraph.

---

### `operations/` — runbooks, diagrams, design, environment

**Purpose:** things done *to* a running system rather than to the code — deployment and
incident runbooks, architecture diagrams, design references, environment variables.

Keep it one folder with subdirectories rather than several tiny top-level folders.

---

### `sessions/` — structured session notes

**Purpose:** concise summaries of development sessions.

**Write one when** you completed a significant feature or fix, made key decisions, or
worked through a problem whose *process* is worth keeping.

**Naming:** `YYYY-MM-DD-short-slug.md`. Two sessions on one date are distinguished by
slug, never by a suffix number.

---

### `plans/`, `audits/`, `archive/`

**`plans/`** — implementation plans and designs, written *before* the work. A plan that
was executed either becomes a session note or moves to `archive/`.

**`audits/`** — systematic passes over something, with findings. If the audit is of an
upstream dependency you also own, file the findings *there* with the resolution applied
here, so the two can be reconciled later.

**`archive/`** — superseded documents. Preserve rather than delete when the reasoning
still explains something about how the project got here. Set `status: archived`.

---

### `templates.md`, `glossary.md`, `technical-debt.md`

**`templates.md`** — one template per document type, inlined. Keeping them in one file
means there is a single place to look and no template folder to keep in step.

**`glossary.md`** — words this project uses *precisely*, where the everyday meaning is
close enough to mislead. Not a dictionary of obvious terms.

**`technical-debt.md`** — what is built and not right, what has never run in production,
and what is known-wrong deliberately. Distinct from `{{STATUS_DOC}}`, which is for things
not built yet. Be blunt; a debt list that flatters the project is useless.

---

## Document frontmatter

Every `.md` file carries it:

```yaml
---
title: "Document Title"
status: draft | review | published | archived
created: YYYY-MM-DD
updated: YYYY-MM-DD
author: "@{{USERNAME}}"          # optional; drop it on solo projects
related_adrs: [ADR-001, ADR-002] # optional
tags: [decision | gotcha | pattern | recipe | runbook | session, area]
---
```

ADRs use `proposed | accepted | deprecated | superseded` for `status` instead, and repeat
it in the body so it is visible when reading the file rather than only in its metadata.

---

## Best practices

### Do

- **Document decisions when they are made**, not later — the reasoning evaporates within a
  day, and what is left is a rationalisation.
- **Extract patterns from working code**, not from theory.
- **Cross-reference.** A gotcha that produced a pattern links to it, and back.
- **Say what did not happen, and why.** That is the whole value of a gotcha.
- **Search before creating.** Two half-documents on one subject is worse than neither.
- **Use the templates.** The headings are the questions that turned out to matter.

### Don't

- **Create documentation without frontmatter.**
- **Duplicate knowledge across files.** Link instead. Two copies means one is wrong and
  you cannot tell which.
- **Document hypothetical patterns.** If it has not happened, it is a plan.
- **Skip the "why".** The *what* is in the diff; the *why* only ever lives here.
- **Leave a superseded ADR unmarked.** Set its status and name its replacement.
- **Put running totals in this file.** They live in `documentation-stats.md`, which is why
  this one stays instructions-only.

---

## Updating documentation stats

The living tally of ADRs / patterns / gotchas / recipes / sessions, the per-version
**Recent additions** log, and the topical **Related documentation** cross-reference index
all live in `documentation-stats.md`.

Update it whenever you:

| Event | What to update |
|---|---|
| Ship a new ADR | ADR count, and add the id to the recent list |
| Add a pattern | Pattern count, and name the new pattern |
| Add a gotcha | Gotcha count, and name the new gotcha |
| Add a recipe | Recipe count |
| Write a session note | Session count, and the "latest" line |
| Apply a database migration | Migration count, and describe it |
| Change the test count meaningfully | The tests line |
| Cut a new version | A new `### Recent additions (vX.Y.Z — title)` block at the top |
| Add a topic spanning several documents | A row in `## Related documentation` |

**Keep the two changelogs distinct.** Don't write the same entry in both:

- **`CHANGELOG.md`** (repo root) is the **engineering changelog** — what code shipped per
  version, broad enough for release notes.
- **`documentation-stats.md` → Recent additions** is the **doc-ecosystem delta** — what
  new documentation landed and which counts moved.

Updating both in one commit is fine; just keep their voices different.

**Cross-reference index heuristic:** add a row to `## Related documentation` only when a
topic spans several artifacts (an ADR *and* a pattern *and* a recipe). A single-file topic
is already discoverable by search and does not need an index entry.

---

<!--
  ============================================================================
  CUSTOMISATION CHECKLIST — work through this, then delete it and the header
  comment block.

  [ ] {{PROJECT_NAME}}      → the project's name (3×: frontmatter title, H1, intro)
  [ ] {{PROJECT_ONE_LINER}} → one sentence on what the project is (1×)
  [ ] {{SPEC_SOURCE}}       → the external spec repo/folder, or delete that block (1×)
  [ ] {{STATUS_DOC}}        → `dev-phases/` or `milestones.md` (3×)
  [ ] {{DOC_GENERATOR}}     → Fumadocs / Docusaurus / …, or use the no-generator block
  [ ] {{USERNAME}}          → or drop `author` from the frontmatter on solo projects
  [ ] Pick ONE of the two optional auto-indexing blocks; delete the other
  [ ] Delete folders from the structure that this project will not use
  [ ] Add any project-specific top-level docs to the structure listing
  [ ] Create `templates.md`, `index.md`, `documentation-stats.md` alongside this
  [ ] Delete this checklist and the comment block at the top of the file
  ============================================================================
-->
