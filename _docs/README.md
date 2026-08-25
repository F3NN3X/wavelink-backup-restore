---
title: "Wave Link Backup Documentation System"
status: published
created: 2026-08-16
updated: 2026-08-25
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
│   ├── gotchas/             # mistakes made and fixed, so they are not repeated
│   ├── patterns/            # proven solutions, extracted from shipping code
│   └── recipes/             # step-by-step guides
├── operations/
│   ├── runbooks/            # operating a released system
│   └── design/              # the design handoff (vendored export, NOT COMMITTED — see note)
│       ├── README.md        #   part 1: the four finished screens
│       ├── CHANGES-SINCE-V1.md  # diff against the previous export — read first
│       ├── screens/         #   part 2: 11 exported state-group specs + PNGs,
│       │                     #   plus 13-/14- authored here (see note below)
│       ├── tokens/ assets/  #   token CSS and brand marks
├── plans/                   # designs and implementation plans, written before the work
├── sessions/                # structured session notes
├── README.md                # this file — instructions only, no running totals
├── index.md                 # the landing page: start here
├── SPEC.md                  # the build specification — the authority on *what*
├── templates.md             # ADR · gotcha · pattern · recipe · session · dev-phase templates
├── glossary.md              # the vocabulary this project uses precisely
├── technical-debt.md        # the honest list
└── documentation-stats.md   # the living tally + cross-reference index
```

### Folders deliberately absent

**Adopt what you need.** A folder holding one thin file is worse than no folder. These are
not here yet, and each has a trigger that creates it:

| Folder | Create it when |
|---|---|
| `operations/diagrams/` | A diagram earns its keep over prose. |

> `operations/runbooks/` **was** on this list. Its trigger — *"there is a running system to
> operate, realistically the first release"* — fired on 2026-08-20, when the release pipeline and
> the in-app updater were built. It holds one runbook.
>
> **Two triggers have now fired** (this one and `patterns/`), which is the argument for the row
> that remains: the mechanism works, so `operations/diagrams/` can wait until a diagram earns it
> rather than being created hopefully.

> `knowledge-base/patterns/` **was** on this list. Its trigger — *"the first line of production
> code ships"* — fired on 2026-08-16 with phase 1, and it now holds four patterns. Kept as a
> note rather than deleted, because a trigger that actually fired is evidence the mechanism
> works, and the two remaining rows are the same bet.

> No docs-site generator is wired up — this is a WPF repository and nothing would consume
> `meta.json`. Frontmatter is kept anyway: it is what makes the corpus greppable, and it
> would make adding a generator later a non-event.

---

## Quick start

**Finding something**

| You want | Look in |
|---|---|
| What are we building? | `SPEC.md` |
| What does it look like? | `operations/design/README.md` |
| Why is it built this way? | `decisions/` |
| Has this bitten us before? | `knowledge-base/gotchas/` |
| How do I do X? | `knowledge-base/recipes/` |
| What happened, and when? | `sessions/` |
| How do I ship a release? | `operations/runbooks/` |
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

Shapes that work in this codebase. **Only proven ones** — extract them from code that ships,
never from an intention. Name the actual callers; a pattern with no callers is a theory. Link
the test that holds it down.

> **Expect this folder to stop growing, and do not treat that as neglect.** Patterns come from
> novelty. Phase 1 produced four; phases 2 and 3 produced none, because both were composition
> of what already existed. Adding a fifth to keep the number moving would be documenting an
> intention, which is the one thing this folder exists to exclude.

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

### `operations/` — design and runbooks, and later diagrams

**Purpose:** things done *to* a running system rather than to the code.

`operations/runbooks/` holds procedures for operating a release: cutting one, and what the app
does with it. **A runbook is not a recipe.** A recipe is for a task with a load-bearing order
(`knowledge-base/recipes/`); a runbook is for a *system* — it carries the procedure, the contract
the procedure has to satisfy, and a symptom table for when it does not. If it has no symptom
table, it is probably a recipe.

**Say what is verified.** A runbook describes a system that may not have run yet. Mark what was
measured and what is still unverified, and update it the first time reality disagrees — an
untested procedure written in the confident voice is worse than no procedure.

`operations/design/` holds the design handoff — tokens, the four finished screens, eleven
exported state-group specs in `screens/` plus two written here, and a self-contained HTML
prototype. `README.md` and
`screens/` are **both current and non-overlapping**: the README is the only spec for the four
finished screens, `screens/` the only spec for the states they lack. Neither supersedes the
other. The prototype is a reference, **not production code, and must not be ported literally**.

Keep this one folder with subdirectories rather than several tiny top-level folders.

> **This folder is not in the repository, and that is deliberate.** `.git/info/exclude` carries
> `_docs/operations/design/`, commented *"design export — worked on outside the repo, never
> committed"*. The exclude file is machine-local, so the folder is absent from every clone and
> from CI, and **roughly 40 documents link into it** — `screens/00-index.md`, `13-elevation.md`,
> the tokens, the prototype. Those links resolve only on a machine that holds the export. They
> are recorded rather than rewritten because the export is the authority they point at; a reader
> without it should treat them as citations, not paths.
>
> **One file inside it *is* committed:** `screen-1-by-eye-checklist.md`, which was force-added
> because it is authored here and is the record of which by-eye looks have happened. It is the
> only one.
>
> **The risk this leaves.** `13-elevation.md` and `14-backup-timing.md` are, by the note below,
> authored in this repo and unrecoverable from the design tool. Excluding them from git means the
> provenance banner is the *only* thing protecting them, and a banner does not survive a lost
> disk. Committing those two — the exception the note below already carves out — would close it.

> **This folder is a vendored package and is exempt from the frontmatter rule below.** It
> arrives as a drop-in export from Claude Design and gets replaced wholesale when the design
> changes. Patching frontmatter into it on every re-export would guarantee the copy in the repo
> drifts from the copy in the design tool — which is the one thing a handoff must not do. The
> same exemption applies to `third_party/`, for the same reason.
>
> `CHANGES-SINCE-V1.md` is the diff against the previous export. Read it before re-reading the
> README; it names which of the already-specified screens changed.

> **Two files in `screens/` are locally authored, and they are the exception to the exemption
> above.** `13-elevation.md` and `14-backup-timing.md` were written in this repo, not exported
> from the design tool, because the code they specify could not be built without a design and
> the design tool was not going to produce one on its own schedule. **They carry frontmatter and
> a provenance banner precisely so a re-export cannot silently delete them** — a wholesale
> replacement of this folder must preserve any file whose first line is `---`. If the design tool
> later produces its own version of either screen, the exported one wins and the local file moves
> to `archive/`.
>
> Adding a locally authored file here is a **last resort**, not a pattern. The alternative was
> inventing design in XAML, which [[ADR-004]] exists to prevent; the cost is this paragraph and
> the risk it describes.

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

### `plans/`, `audits/`, `archive/`

**`plans/`** — designs and implementation plans, written *before* the work. A plan that was
executed either becomes a session note or moves to `archive/`. Naming:
`YYYY-MM-DD-<topic>-design.md` / `-plan.md`.

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
