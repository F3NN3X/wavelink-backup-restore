---
title: "ADR-010: Two preset roots, and a snapshot layout that names them"
status: accepted
created: 2026-08-19
updated: 2026-08-19
related_adrs: [ADR-006, ADR-003]
tags: [decision, capture, presets, snapshot-format]
---

# ADR-010: Two preset roots, and a snapshot layout that names them

**Status:** Accepted
**Date:** 2026-08-19

## Context

[[ADR-006]] defined tier 3 as *"the presets each effect saved under `%APPDATA%\<Vendor>\`"* and
priced it at ~10 MB. That sentence had never been checked against a real vendor folder;
[technical-debt.md](../technical-debt.md) §4.18 existed to say so, and specified the check as
one capture and one look at `plugins.json`.

Run on the reference rig. It was wrong, and wrong in the quiet direction. The lookup found the
folder it was aiming at every time. What was in that folder was not what the tier promises:

| Plug-in | Captured under `%APPDATA%` | What the user's work actually is |
|---|---|---|
| FabFilter Pro-Q 4 | 3 files: an interface default, a MIDI map, a cache | 172 `.ffp` in `Documents\FabFilter\Presets\Pro-Q 4\` |
| FabFilter Pro-C 2 | 2 | 109 |
| FabFilter Saturn 2 | 53 factory `Component Presets` | 78 |
| Supertone Clear | 2 **crash reports** | none. Clear saves no presets on this machine |

FabFilter treats a preset as a *document* rather than a *setting*. That is a defensible reading
of what those two Windows folders are for, and it is the opposite of the assumption the code
made. [[ADR-006]]'s own measurement, *"`%APPDATA%\FabFilter` at 246 preset files"*, was
numerically correct and described caches.

So the question was not "is the heuristic right" but "how much does fixing it cost", because any
fix that adds a second source location changes what a snapshot contains *and how it is laid
out*: a file stored at `presets/<Vendor>/…` cannot be put back correctly once there are two
places it could have come from.

## Decision

**Tier 3 reads two roots, `%APPDATA%` and Documents, and takes at most one folder from each,
additively.** Every stored preset path names the root it came from:
`presets/appdata/…`, `presets/documents/…`. `plugins.json` records `presetSources` as an array
and is **schema 2**.

The two roots deliberately do **not** get the same candidate list:

| Root | Candidates, narrowest first | Widest, and why it stops there |
|---|---|---|
| `%APPDATA%` | `<Vendor>\<Plugin>`, `<Vendor>\<file name>`, `<Vendor>` | The vendor folder. Config-sized whatever it holds. |
| Documents | `<Vendor>\Presets\<Plugin>`, `<Vendor>\Presets\<file name>`, `<Vendor>\<Plugin>`, `<Vendor>\<file name>`, `<Vendor>\Presets` | `<Vendor>\Presets`, a folder that says what it is. **Never the bare vendor folder.** |

Both roots resolve through `Environment.GetFolderPath`. Directories named `Reports`, `Logs`,
`Crashes` or `Diagnostics` are never captured, at any depth.

## Alternatives considered

| Option | Why not |
|---|---|
| **Documents first, still one source per plug-in** | Smaller diff, and it captures the `.ffp` files. But FabFilter genuinely uses both roots, the MIDI map and interface defaults in one, the presets in the other, so first-wins means choosing which half of the user's work to lose. It also does not avoid the layout change: the root still has to be recorded for restore, so the expensive part was unavoidable either way. That last point is what settled it. |
| **Same candidate list for both roots** | Consistent, and dangerous. `Documents\<Vendor>` is as likely to be a project library, sessions, renders, sample packs, as a preset folder. A user with `Documents\Ableton\` would have found tier 3 quietly grow from ten megabytes to a hundred gigabytes. Asymmetry here is the safety property, not an inconsistency to tidy away. |
| **A per-vendor exception list** | §4.18 offered this as the likely fix. It is the wrong shape: it makes correctness a function of how many vendors someone has enumerated, and it fails silently for vendor number twenty-one. The root rule generalises; a list does not. |
| **Ask the plug-in where its presets are** | There is no such interface. VST3 has no preset-location query that works without hosting the plug-in, and hosting an arbitrary `.vst3` to ask it a question is a far larger risk than a wrong folder. |
| **Record the finding and change nothing** | Considered seriously, because the fix touches the snapshot format. Rejected: the tier's whole justification in [[ADR-006]] is that presets are *the one thing in a snapshot nobody can re-download*, and it was capturing 2% of them while telling the user otherwise. |

## Consequences

**This enables:** a snapshot that actually holds the user's presets, 61 files to 491 on the
reference rig, 4.4 MB, still inside [[ADR-006]]'s ~10 MB estimate. It also enables a vendor
whose presets are split across both roots to be captured whole, which is the common case rather
than an edge one.

**This rules out:**

- **A flat `presets/<Vendor>/` layout, permanently.** Every future reader of a snapshot must go
  through `PresetFiles.RootOf`. A path with no root segment is a schema-1 snapshot and means
  `%APPDATA%`, that fallback is load-bearing and cannot be removed while any user has an old
  snapshot on disk, which is forever, because [[ADR-003]] keeps snapshots outside `LocalState`
  precisely so they survive.
- **Treating `presetSource` as one value.** It is a list now. The singular property is kept as a
  convenience for diagnostics and reads the *first* source, which is `%APPDATA%` whenever both
  exist.
- **Capturing a vendor's Documents folder wholesale**, even where that would be correct. A vendor
  that keeps presets directly in `Documents\<Vendor>\` with no `Presets` subfolder and no
  per-plug-in subfolder is not captured at all. That is a deliberate miss: silent under-capture
  is recoverable, and a hundred-gigabyte snapshot is not.
- **A one-line size estimate.** `Measure` is now an upper bound across two roots and can
  double-count a folder shared by two plug-ins from one vendor. Over-estimating a tier the user
  is about to switch on is the safe direction, and the Settings dialog says "about".

**Revisit if:** a third root appears (a vendor using `%LOCALAPPDATA%` or `%PROGRAMDATA%` for user
presets), or if a real vendor is found that keeps presets directly in `Documents\<Vendor>\`. The
first is a new entry in the root list; the second needs evidence that the project-library risk
does not apply to it, not just a report that one user's presets were missed.

## References

- [technical-debt.md](../technical-debt.md) §4.18, the measurement, before and after
- [[ADR-006]]: the four tiers, and the sentence this ADR corrects
- [[ADR-003]]: why old snapshots are permanent, and therefore why the schema-1 fallback is
- [[backup-says-it-saved-your-presets-and-it-did-not]]: the same finding as a symptom
