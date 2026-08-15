---
title: "Wave Link Backup Documentation System"
status: published
created: 2026-08-16
updated: 2026-08-16
tags: [meta, documentation]
---

# Wave Link Backup Documentation System

**Welcome to the Wave Link Backup internal documentation system.**

Wave Link Backup is a free, open-source Windows utility that snapshots and restores Elgato
Wave Link's audio-mixer configuration — the one 43 KB JSON file that holds every channel,
routing assignment and effect chain — with optional capture of the VST3 presets and binaries
it references.

This folder holds all working documentation, architectural decisions, knowledge base and
session notes. It follows a structured approach designed to preserve context, capture
decisions, and make knowledge discoverable months later by someone who was not there —
including you.

**Nothing here duplicates [`SPEC.md`](SPEC.md).** That is the authority on *what* to build;
this folder records what we decided, what bit us, and what happened when. Where this repo
departs from the spec, say so and why.

---

## Directory structure

```
_docs/
├── archive/                 # superseded documents, kept for the record
├── audits/                  # codebase and third-party audits
├── decisions/               # Architecture Decision Records (ADRs)
├── dev-phases/              # what is left to build, phase by phase
├── knowledge-base/
│   └── gotchas/             # mistakes made and fixed, so they are not repeated
│   └── recipes/             # step-by-step guides
├── operations/
│   └── design/              # the design handoff: tokens, screens, prototype
├── sessions/                # structured session notes
├── README.md                # this file — instructions only, no running totals
├── index.md                 # the landing page: start here
├── SPEC.md                  # the build specification — the authority on *what*
├── templates.md             # ADR · gotcha · pattern · recipe · session templates
├── glossary.md              # the vocabulary this project uses precisely
├── technical-debt.md        # the honest list
└── documentation-stats.md   # the living tally + cross-reference index
```

### Folders deliberately absent

**Adopt what you need.** A folder holding one thin file is worse than no folder. These are
not here yet, and each has a trigger that creates it:

| Folder | Create it when |
|---|---|
| `knowledge-base/patterns/` | The first line of production code ships. A pattern is extracted from working code with named callers — before that it is a theory, and the place for theories is `SPEC.md` or an ADR. |
| `plans/` | The first implementation plan is written, which is the architectural brainstorm for the core library. |
| `operations/runbooks/` | There is a running system to operate — realistically, the first release. |
| `operations/diagrams/` | A diagram earns its keep over prose. |

> No docs-site generator is wired up — this is a WPF repository and nothing would consume
> `meta.json`. Frontmatter is kept anyway: it is what makes the corpus greppable, and it
> would make adding a generator later a non-event.

---

## Quick start

**Finding something**

| You want | Look in |
|---|---|
| What are we building? | `SPEC.md` |
| What does it look like? | `operations/design/design-handoff.md` |
| Why is it built this way? | `decisions/` |
| Has this bitten us before? | `knowledge-base/gotchas/` |
| How do I do X? | `knowledge-base/recipes/` |
| What happened, and when? | `sessions/` |
| What is left? | `dev-phases/`, `technical-debt.md` |
| What does that word mean here? | `glossary.md` |

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

Not yet created; see *Folders deliberately absent*. Shapes that work in this codebase.
**Only proven ones** — extract them from code that ships, never from an intention. Name the
actual callers; a pattern with no callers is a theory. Link the test that holds it down.

#### `gotchas/` — mistakes to avoid

Bugs that actually happened. **Title by the symptom**, because that is what the next
person types into a search box — they will be describing the surface, not the cause.
Always include why the plausible explanation was wrong, or it gets tried again, and that
retry is most of the cost.

> **This project's gotchas carry a `Provenance` line, and it is not optional.** The seed set
> was written before any code existed, so some entries are *observed incidents* and some are
> *read off a spec or off someone else's source and never reproduced*. Those two are worth
> very different amounts when you are deciding whether to trust one at 2am. Say which you
> have. `SPEC.md` sets the example in its own Provenance section — follow it.

#### `recipes/` — step-by-step guides

Tasks with an order that matters, with the reason attached wherever the order is
load-bearing. If the order does not matter, it is not a recipe, it is a paragraph.

---

### `operations/` — design, and later runbooks and diagrams

**Purpose:** things done *to* a running system rather than to the code.

`operations/design/` holds the design handoff — the full token set, the four screens, and a
self-contained HTML prototype. `design-handoff.md` wins on values and layout; the prototype
is a reference, **not production code, and must not be ported literally**.

Keep this one folder with subdirectories rather than several tiny top-level folders.

---

### `dev-phases/` — what is left to build

**Purpose:** the roadmap, phase by phase, with entry and exit criteria.

`dev-phases/README.md` is the index and holds every phase in one paragraph each. A phase
gets its own detailed file **when it becomes the current or the next phase** — writing
phase 6 in detail while phase 1 is unbuilt produces fiction, not a plan.

Distinct from `technical-debt.md`, which is for things built and not right.

---

### `sessions/` — structured session notes

**Purpose:** concise summaries of development sessions.

**Write one when** you completed a significant feature or fix, made key decisions, or
worked through a problem whose *process* is worth keeping.

**Naming:** `YYYY-MM-DD-short-slug.md`. Two sessions on one date are distinguished by
slug, never by a suffix number.

---

### `audits/`, `archive/`

**`audits/`** — systematic passes over something, with findings. The upstream fork gets one
of these; when its findings are resolved here, record the resolution in the audit so the two
can be reconciled later.

**`archive/`** — superseded documents. Preserve rather than delete when the reasoning
still explains something about how the project got here. Set `status: archived`.

---

### `templates.md`, `glossary.md`, `technical-debt.md`

**`templates.md`** — one template per document type, inlined. Keeping them in one file
means there is a single place to look and no template folder to keep in step.

**`glossary.md`** — words this project uses *precisely*, where the everyday meaning is
close enough to mislead. Not a dictionary of obvious terms. This project has an unusual
number of them: "backup" alone means three different things depending on who wrote it.

**`technical-debt.md`** — what is built and not right, what has never run in production,
and what is known-wrong deliberately. Distinct from `dev-phases/`, which is for things
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
related_adrs: [ADR-001, ADR-002] # optional
tags: [decision | gotcha | pattern | recipe | runbook | session, area]
---
```

ADRs use `proposed | accepted | deprecated | superseded` for `status` instead, and repeat
it in the body so it is visible when reading the file rather than only in its metadata.

`author` is deliberately not part of the schema — this is a solo project and the field would
say the same thing on every file.

### Cross-reference syntax

Two forms, and the distinction is deliberate:

- **`[[slug]]`** — a *conceptual* reference, resolved by the reader. The slug is a document's
  filename without its extension, except for ADRs, which are referenced by id alone
  (`[[ADR-003]]`, not `[[ADR-003-backup-store-outside-localstate]]`). Nothing resolves these
  automatically — there is no generator — so they are readable prose that stays correct if a
  file is renamed for clarity.
- **`[text](relative/path.md)`** — a *navigable* link. Use it whenever the reader will actually
  want to click through, and for anything outside `_docs/`.

When both would work, prefer the markdown link in indexes and tables, and `[[slug]]` inline
in prose where a URL would interrupt the sentence.

---

## Best practices

### Do

- **Document decisions when they are made**, not later — the reasoning evaporates within a
  day, and what is left is a rationalisation.
- **Extract patterns from working code**, not from theory.
- **Cross-reference.** A gotcha that produced a pattern links to it, and back.
- **Say what did not happen, and why.** That is the whole value of a gotcha.
- **State provenance.** Measured, read, or assumed — say which.
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
topic spans several artifacts (an ADR *and* a gotcha *and* a recipe). A single-file topic
is already discoverable by search and does not need an index entry.
