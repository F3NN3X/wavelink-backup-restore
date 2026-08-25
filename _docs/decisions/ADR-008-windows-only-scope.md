---
title: "ADR-008: Windows-only, stated rather than implied"
status: accepted
created: 2026-08-16
updated: 2026-08-16
tags: [decision, scope, platform]
---

# ADR-008: Windows-only, stated rather than implied

**Status:** Accepted
**Date:** 2026-08-16

## Context

Wave Link ships on macOS as well as Windows. Everything in `SPEC.md`, the MSIX package
family, `LocalState`, `shell:AppsFolder` activation, Core Audio COM, `File.Replace` atomicity
on NTFS, `%APPDATA%` VST3 preset paths, is Windows. None of it transfers.

The risk is not that someone tries to port it. The risk is **ambiguity**: a repo that says
"Wave Link Backup" without saying which platform collects macOS bug reports, macOS feature
requests and macOS disappointment, all of which cost time and none of which produce anything.

A second, quieter scoping question sits underneath. `SPEC.md` §11 flags it as open: whether
non-MSIX Wave Link installs exist at all. Everything assumes the Store package. If older or
enterprise builds install as conventional Win32, discovery returns "not found" and the app is
useless to those users, **Windows users**, who were promised support.

## Decision

**Windows-only, said out loud in the root `README.md`** rather than left to be inferred from
a `.sln`.

Within Windows, the supported target is the **MSIX/Store package**, discovered by globbing
`Elgato.WaveLink_*` under `Packages`. Non-MSIX installs are **unverified, not excluded**,
which obliges a manual settings-path escape hatch rather than a dead end. A user whose install
we cannot find gets "Choose the settings file…", not "not installed".

macOS is out of scope. Not "later", out of scope, and the README says so.

## Alternatives considered

| Option | Why not |
|---|---|
| **Cross-platform from the start** | The macOS config location, format and process model are entirely unexamined. Designing for a platform nobody has inspected produces abstractions shaped by guesses, and those are harder to remove than to add. |
| **Windows now, structure for macOS later** | The honest version of this is [[ADR-004]]. Core is already separated from its shells, which is as much portability as is worth paying for. Anything beyond that is an abstraction over one implementation. |
| **Say nothing about platform** | The default, and the one that generates the issues. Ambiguity is not neutral; it is a promise the reader makes on your behalf. |
| **Exclude non-MSIX explicitly** | Nobody has checked whether such installs exist. Excluding something unverified is as much a guess as supporting it, and here the cost of being wrong falls on a user who was told they were supported. |

## Consequences

**This enables:** using every Windows-specific mechanism directly and without apology, COM
interop, `File.Replace`, `FileSystemWatcher`, shell activation, Mica, with no abstraction
layer justifying itself against a hypothetical second platform.

**This rules out:**

- Avalonia and any cross-platform UI framework ([[ADR-005]]).
- The macOS user base, permanently as far as this repo is concerned. If someone wants it,
  that is a separate project sharing a name and nothing else.

**This creates two obligations:**

1. **The root `README.md` states Windows-only above the fold.** Not in a requirements section
   at the bottom.
2. **Discovery failure must offer a way forward.** Upstream already models this: it refuses to
   guess between multiple packages and demands `--settings-path`. The same escape covers the
   non-MSIX case. The empty state reserves the place for the message, the amber "not found"
   variant is **not yet designed**; see [technical-debt.md](../technical-debt.md) §2.2 and §4.

**Revisit if:** someone actually inspects a macOS Wave Link installation and finds the config
is portable in a way nobody expects. That is a spike, not a plan.

## References

- `SPEC.md` §11
- [technical-debt.md](../technical-debt.md) §2.2
- [[ADR-001]] · [[ADR-004]] · [[ADR-005]]
