---
title: "How this documentation is organised"
status: published
created: 2026-08-16
updated: 2026-08-25
tags: [meta, documentation]
---

# How this documentation is organised

Wave Link Backup is a free, open-source Windows utility that snapshots and restores Elgato Wave
Link's audio-mixer configuration, the one 43 KB JSON file that holds every channel, routing
assignment and effect chain, with optional capture of the VST3 presets and binaries it
references.

This folder holds the decisions, the knowledge base and the roadmap. None of it duplicates
[`SPEC.md`](SPEC.md), which is the authority on *what* to build. Where this repo departs from the
spec, say so and say why.

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
│   └── design/              # the design handoff (vendored export, NOT COMMITTED, see note)
├── README.md                # this file
├── index.md                 # the landing page: start here
├── SPEC.md                  # the build specification, the authority on *what*
├── templates.md             # ADR · gotcha · pattern · recipe · dev-phase templates
├── glossary.md              # the vocabulary this project uses precisely
└── technical-debt.md        # the honest list
```

A folder holding one thin file is worse than no folder, so `operations/diagrams/` does not exist
yet. Create it when a diagram earns its keep over prose.

No docs-site generator is wired up. This is a WPF repository and nothing would consume
`meta.json`. Frontmatter is kept anyway, because it is what makes the corpus greppable and it
would make adding a generator later a non-event.

---

## Finding something

| You want | Look in |
|---|---|
| What are we building? | `SPEC.md` |
| What does it look like? | `operations/design/README.md` |
| Why is it built this way? | `decisions/` |
| Has this bitten us before? | `knowledge-base/gotchas/` |
| How do I do X? | `knowledge-base/recipes/` |
| How do I ship a release? | `operations/runbooks/` |
| What is left? | `dev-phases/`, `technical-debt.md` |
| What does that word mean here? | `glossary.md` |

To add a document, copy the matching template from `templates.md`.

---

## Folder guides

### `decisions/`, Architecture Decision Records

Write one when you chose between real alternatives and the reasoning is not obvious from the
diff, when you made a security or performance trade-off, when you established a pattern that
affects more than one feature, or when someone will later ask why on earth it is done this way.

Record what the decision rules out, not just what it enables. That is the part future-you needs,
because it is the reason a "simple" change later turns out not to be.

Statuses are `proposed` (under discussion), `accepted` (implemented and active), `deprecated` (no
longer recommended) and `superseded` (replaced). Name the replacement in the body, not only in
the frontmatter.

Naming is `ADR-0NN-short-slug.md`, numbered in the order written. Never renumber.

---

### `knowledge-base/`, patterns, gotchas, recipes

#### `patterns/`, proven solutions

Shapes that work in this codebase, extracted from code that ships rather than from an intention.
Name the actual callers, because a pattern with no callers is a theory, and link the test that
holds it down.

Expect this folder to stop growing, and don't treat that as neglect. Patterns come from novelty.
Phase 1 produced four; phases 2 and 3 produced none, because both were composition of what
already existed.

#### `gotchas/`, mistakes to avoid

Bugs that actually happened. Title by the symptom, because the next person will be describing the
surface rather than the cause. Always include why the plausible explanation was wrong, or it gets
tried again, and that retry is most of the cost.

Every gotcha carries a `Provenance` line and it is not optional. The seed set was written before
any code existed, so some entries are observed incidents and some were read off a spec or off
someone else's source and never reproduced. Those are worth very different amounts when you are
deciding whether to trust one at 2am. `SPEC.md` sets the example in its own Provenance section.

#### `recipes/`, step-by-step guides

Tasks with an order that matters, with the reason attached wherever the order is load-bearing. If
the order does not matter, it is not a recipe, it is a paragraph.

---

### `operations/`, design and runbooks

Things done *to* a running system rather than to the code.

`operations/runbooks/` holds procedures for operating a release: cutting one, and what the app
does with it. A runbook is not a recipe. A recipe covers a task with a load-bearing order; a
runbook covers a system, and it carries the procedure, the contract the procedure has to satisfy,
and a symptom table for when it does not. No symptom table probably means you have written a
recipe.

Say what is verified. A runbook can describe a system that has not run yet, so mark what was
measured and what is still unverified, and update it the first time reality disagrees. An
untested procedure written in the confident voice is worse than no procedure.

`operations/design/` holds the design handoff: tokens, the four finished screens, eleven exported
state-group specs in `screens/` plus two written here, and a self-contained HTML prototype.
`README.md` and `screens/` are both current and non-overlapping. The README is the only spec for
the four finished screens and `screens/` the only spec for the states they lack; neither
supersedes the other. The prototype is a reference and must not be ported literally.

> **This folder is not in the repository, and that is deliberate.** `.git/info/exclude` carries
> `_docs/operations/design/`, commented *"design export, worked on outside the repo, never
> committed"*. The exclude file is machine-local, so the folder is absent from every clone and
> from CI, and roughly 40 documents link into it. Those links resolve only on a machine that holds
> the export. They are recorded rather than rewritten because the export is the authority they
> point at; a reader without it should treat them as citations, not paths.
>
> One file inside it *is* committed: `screen-1-by-eye-checklist.md`, force-added because it is
> authored here and is the record of which by-eye looks have happened.
>
> The risk this leaves: `13-elevation.md` and `14-backup-timing.md` are authored in this repo and
> unrecoverable from the design tool. Excluding them from git means the provenance banner is the
> only thing protecting them, and a banner does not survive a lost disk. Committing those two
> would close it.

> **This folder is a vendored package and is exempt from the frontmatter rule below.** It arrives
> as a drop-in export and gets replaced wholesale when the design changes. Patching frontmatter
> into it on every re-export would guarantee the copy in the repo drifts from the copy in the
> design tool, which is the one thing a handoff must not do. The same exemption applies to
> `third_party/`, for the same reason. `CHANGES-SINCE-V1.md` is the diff against the previous
> export; read it before re-reading the README, since it names which already-specified screens
> changed.

> **Two files in `screens/` are locally authored, and they are the exception to that exemption.**
> `13-elevation.md` and `14-backup-timing.md` were written in this repo because the code they
> specify could not be built without a design and the design tool was not going to produce one on
> its own schedule. They carry frontmatter and a provenance banner precisely so a re-export cannot
> silently delete them: a wholesale replacement of this folder must preserve any file whose first
> line is `---`. If the design tool later produces its own version of either screen, the exported
> one wins and the local file moves to `archive/`.
>
> Adding a locally authored file here is a last resort rather than a pattern. The alternative was
> inventing design in XAML, which [[ADR-004]] exists to prevent.

---

### `dev-phases/`, what is left to build

The roadmap, phase by phase, with entry and exit criteria. `dev-phases/README.md` is the index
and holds every phase in one paragraph each. A phase gets its own detailed file when it becomes
the current or the next phase, because writing phase 6 in detail while phase 1 is unbuilt
produces fiction rather than a plan.

Distinct from `technical-debt.md`, which is for things built and not right.

---

### `audits/`, `archive/`

**`audits/`.** Systematic passes over something, with findings. The upstream fork gets one of
these; when its findings are resolved here, record the resolution in the audit so the two can be
reconciled later.

**`archive/`.** Superseded documents. Preserve rather than delete when the reasoning still
explains something about how the project got here. Set `status: archived`.

---

### `templates.md`, `glossary.md`, `technical-debt.md`

**`templates.md`.** One template per document type, inlined. Keeping them in one file means there
is a single place to look and no template folder to keep in step.

**`glossary.md`.** Words this project uses precisely, where the everyday meaning is close enough
to mislead. Not a dictionary of obvious terms. This project has an unusual number of them:
"backup" alone means three different things depending on who wrote it.

**`technical-debt.md`.** What is built and not right, what has never run in production, and what
is known-wrong deliberately. Distinct from `dev-phases/`, which is for things not built yet.

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
tags: [decision | gotcha | pattern | recipe | runbook, area]
---
```

ADRs use `proposed | accepted | deprecated | superseded` for `status` instead, and repeat it in
the body so it is visible when reading the file rather than only in its metadata.

`author` is deliberately not part of the schema. This is a solo project and the field would say
the same thing on every file.

### Cross-reference syntax

Two forms, and the distinction is deliberate:

- **`[[slug]]`** is a conceptual reference, resolved by the reader. The slug is a document's
  filename without its extension, except for ADRs, which are referenced by id alone
  (`[[ADR-003]]`, not `[[ADR-003-backup-store-outside-localstate]]`). Nothing resolves these
  automatically. There is no generator, so they are readable prose that stays correct if a file is
  renamed for clarity.
- **`[text](relative/path.md)`** is a navigable link. Use it whenever the reader will actually
  want to click through, and for anything outside `_docs/`.

When both would work, prefer the markdown link in indexes and tables, and `[[slug]]` inline in
prose where a URL would interrupt the sentence.

---

## Best practices

### Do

- Document decisions when they are made. The reasoning evaporates within a day, and what is left
  is a rationalisation.
- Extract patterns from working code, not from theory.
- Cross-reference. A gotcha that produced a pattern links to it, and back.
- Say what did not happen, and why. That is the whole value of a gotcha.
- State provenance: measured, read, or assumed.
- Search before creating. Two half-documents on one subject is worse than neither.
- Use the templates. The headings are the questions that turned out to matter.

### Don't

- Create documentation without frontmatter.
- Duplicate knowledge across files. Link instead, because two copies means one is wrong and you
  cannot tell which.
- Document hypothetical patterns. If it has not happened, it is a plan.
- Skip the why. The what is in the diff; the why only ever lives here.
- Leave a superseded ADR unmarked. Set its status and name its replacement.

`CHANGELOG.md` at the repo root is the engineering changelog: what code shipped per version,
broad enough for release notes. Documentation changes ride along in the same commit as the code
they describe rather than getting their own log.
