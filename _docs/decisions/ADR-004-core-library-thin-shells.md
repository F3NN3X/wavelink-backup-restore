---
title: "ADR-004: A headless core library with WPF and CLI shells"
status: accepted
created: 2026-08-16
updated: 2026-08-16
tags: [decision, architecture]
---

# ADR-004: A headless core library with WPF and CLI shells

**Status:** Accepted
**Date:** 2026-08-16

## Context

Upstream is a single console application. The obvious path is to add a GUI to it, and the
obvious path is wrong in a way that is cheap to avoid on day one and expensive to fix later.

Three forces:

1. **The upstream's test coverage exists because of its seams.** `IFileOperations`,
   `IWaveLinkProcess` and `Func<DateTime> clock` are why 60 KB of source carries 30 KB of
   tests. Bolting a GUI onto the same assembly puts UI concerns in reach of that code and the
   seams erode, not deliberately, just gradually, the way they always do.
2. **Unattended operation is a requirement, not a nice-to-have.** The watcher ([[ADR-007]])
   must run without a window. So must anything scheduled.
3. **NativeAOT is CLI-only.** WPF does not support it. If the CLI and the GUI share one
   assembly, the ~10, 15 MB AOT option in [technical-debt.md](../technical-debt.md) §1.5 is
   foreclosed before that decision is even framed.

## Decision

Three projects from the first commit:

```
WaveLinkBackup.Core      ← class library. Headless, testable, no UI references.
WaveLinkBackup.Cli       ← shell. Argument parsing → Core.
WaveLinkBackup.App       ← shell. WPF, MVVM → Core.
WaveLinkBackup.Core.Tests
```

**Core owns everything that is not presentation:** discovery, validation, the snapshot store,
hashing and dedup, the watcher, process lifecycle, atomic write, tier capture, endpoint
inspection. It has no reference to WPF or to any console API.

**Neither shell holds backup logic.** A shell translates input into a Core call and renders the
result. When a shell starts to hold logic, that logic belongs in Core.

> **Measured 2026-08-25, and half of this ADR's original wording did not survive it.** It said
> "both shells stay thin enough to be uninteresting". Core is about 6,000 lines of C#. The CLI is
> about 840, so that half holds. The WPF app is about 11,200 lines of C# plus 6,500 of XAML, which
> makes it nearly twice the size of the thing it is a shell over, and `App.xaml.cs` alone is 1,793
> lines with 65 members.
>
> The decision this ADR records is still the right one and is still being kept: Core has no
> reference to WPF, the seams survived, the watcher runs headless and the CLI still publishes
> under NativeAOT. What was wrong was the prediction about size. A window needs themes, dialogs,
> view models, a tray host and an update path, and none of that is backup logic that leaked
> outward. "Thin" was the wrong word for presentation code; "does not hold backup logic" is the
> claim that was actually worth making, and it is the one that can be checked.

## Alternatives considered

| Option | Why not |
|---|---|
| **One project, add WPF to it** | Cheapest today, and it costs the seams, the headless watcher and the AOT option. All three are hard to recover once code has grown around their absence. |
| **Core + WPF only, drop the CLI** | The CLI is nearly free once Core exists, and it is what makes the app scriptable and unattended-testable. Dropping it also drops the only AOT-eligible artifact. |
| **Core + CLI now, GUI later** | The GUI is the product, the design handoff specifies four finished screens, and the whole value proposition is "configured once, then ignored". A CLI-only tool is what upstream already is. |
| **Plugin/extension architecture** | Nothing here has a second implementation. YAGNI. |

## Consequences

**This enables:** unattended and scheduled operation for free; a testable Core with the same
seam discipline inherited from upstream; NativeAOT kept open for the CLI; and a GUI that can
be rebuilt or replaced without touching a line of backup logic.

**This rules out:**

- Quick UI-driven shortcuts. Anything the GUI wants, Core must expose deliberately. That
  friction is the point, and it will feel like overhead the first three times.
- Sharing UI-shaped types across the boundary. Core returns its own models; the WPF shell
  maps them to view models. The design handoff's `Backup` and `Settings` state shapes are
  *view* state, not Core's storage model, they overlap heavily and must not be unified.

**Discipline this requires:** `WaveLinkBackup.Core` must not reference `PresentationFramework`
or `System.Console`. Enforce it in the csproj rather than by intention, an accidental
reference is invisible until the day AOT or the test host fails.

**Revisit if:** never, realistically. This is the structural decision `SPEC.md` singles out as
worth making on day one, and its cost is a few hours at the start against a rewrite later.

## References

- `SPEC.md` §10
- [technical-debt.md](../technical-debt.md) §1.5, §2.4
- [[ADR-002]] · [[ADR-005]] · [[ADR-007]]
