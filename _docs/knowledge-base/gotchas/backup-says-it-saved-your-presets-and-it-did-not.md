---
title: "The backup says it saved your presets, and your presets are not in it"
status: published
created: 2026-08-19
updated: 2026-08-19
related_adrs: [ADR-006, ADR-010]
tags: [gotcha, capture, presets]
---

# The backup says it saved your presets, and your presets are not in it

**Provenance:** **Observed**, 2026-08-19, on the reference rig, the same machine
[[ADR-006]]'s tier measurements were taken on. Found by running one capture and reading
`plugins.json`, which is the check [technical-debt.md](../../technical-debt.md) §4.18 was
written to make possible.

## Symptom

Tier 3 is on. A snapshot reports presets for every plug-in on the channel, with plausible
numbers beside each one:

```
FabFilter Pro-Q 4    presetFileCount: 3
FabFilter Pro-C 2    presetFileCount: 2
Supertone Clear      presetFileCount: 2
```

Nothing errors, nothing is skipped, and the Settings dialog prices the tier honestly. Restore
the snapshot on a clean machine and **none of your saved presets are there**, not the EQ
curves, not the gate thresholds, none of the work the tier exists to protect.

The three files that *were* captured for Pro-Q 4 are `InterfaceDefaults.ffd`,
`MidiControllerMap.ffm` and `PresetCache.dat`. The two for Clear are crash reports.

## Cause

Preset discovery looked in one root: `%APPDATA%\<Vendor>\`.

**FabFilter does not keep user presets there.** `%APPDATA%\FabFilter\Pro-Q 4\` holds an
interface default, a MIDI map and a cache. The 172 `.ffp` files live in
`Documents\FabFilter\Presets\Pro-Q 4\`, because FabFilter treats a preset as a *document*
rather than a *setting*, which is a defensible reading of what those two folders are for, and
the opposite of the assumption the code made.

Supertone Clear keeps nothing but `%APPDATA%\Supertone\Clear\Reports\`, a folder of crash
dumps. The heuristic found a directory where it expected presets, walked it, and reported what
it found as saved presets.

Across the five plug-ins on the reference rig's channel: **61 files captured, 491 that should
have been.**

## The plausible explanation, and why it is wrong

> *"The vendor-folder lookup must be failing. It is not finding
> `%APPDATA%\FabFilter\Pro-Q 4`, so tier 3 is falling through to something narrower."*

It is not failing. It finds that folder every time, first candidate, exactly as designed, and
`presetSource` in `plugins.json` says so. **The lookup was right and the destination was
wrong**, which is why nothing about the output looks broken: a correct-looking path, a non-zero
file count, no error anywhere.

This is the failure mode that makes the whole class dangerous. A heuristic that finds nothing
is loud, a zero in the manifest, a tier not claimed. A heuristic that finds *the wrong thing*
is silent, and stays silent until someone restores onto a machine that does not already have
the files.

The second plausible explanation is worse: *"ADR-006 measured 246 files in `%APPDATA%\FabFilter`,
so that is clearly the preset folder."* That measurement was correct. Those 246 files are caches
and factory component presets. **A number being right does not make the conclusion drawn from it
right**, and a measurement recorded without saying what was counted invites exactly this.

## Fix

Read both roots, take at most one folder from each, and record every folder read.

Additive rather than first-wins, because FabFilter genuinely uses both: the MIDI map in
`%APPDATA%`, the presets in Documents. A rule that took only the first match would have to
choose which half of the user's work to lose.

The roots get **different fallbacks**, and this is the part that is easy to get wrong in the
other direction:

| Root | Widest candidate | Why it stops there |
|---|---|---|
| `%APPDATA%\<Vendor>` | the vendor folder itself | Config-sized whatever it holds |
| `Documents\<Vendor>` | `<Vendor>\Presets` | A vendor folder in Documents is as likely to be a project library, sessions, renders, sample packs, and that fallback would turn a ten-megabyte tier into a hundred-gigabyte one |

A `Reports`, `Logs`, `Crashes` or `Diagnostics` directory is never captured, at any depth.
Clear now records its folder with a count of **zero**, which is the state `presetFileCount`
was designed to show: *we looked here and there was nothing worth keeping.*

Because presets can now come from two places, **a stored preset path has to say which one**,
`presets/appdata/…`, `presets/documents/…`, or restore writes a Documents preset into
`%APPDATA%`. See [[ADR-010]] for the layout and its compatibility rule.

## How to avoid it

- **Resolve Documents through `Environment.GetFolderPath(MyDocuments)`, never by composing
  `%USERPROFILE%\Documents`.** The reference rig has it redirected to another drive entirely.
  A composed path finds an empty folder and reports that the user has no presets, the same
  trap [technical-debt.md](../../technical-debt.md) §5 records for `%LOCALAPPDATA%`, failing
  more quietly. `TierCaptureTests` puts its Documents constant on `G:\` for this reason: a test
  that placed it beside Roaming would pass for code that composed the path.
- **Record what a heuristic read, beside what it found.** `presetSources` and `presetFileCount`
  in `plugins.json` are what made this a ten-minute diagnosis instead of a bug report from a
  user who had already lost the files. A heuristic whose result cannot be inspected is a
  heuristic nobody can improve.
- **Run it against a real machine before believing a synthetic tree.** Every test here passed,
  before and after. They asserted the mapping, which was correct; nothing could tell them the
  root was wrong. The check that found this was one capture and one look at a file.
- **When recording a measurement, record what was counted.** "246 files in `%APPDATA%\FabFilter`"
  was true and misleading. "246 files, of which 0 are `.ffp`" would have caught this a phase
  earlier.

## References

- [technical-debt.md](../../technical-debt.md) §4.18, the entry that specified this check, and
  the numbers it produced
- [[ADR-006]]: the four tiers, and the measurements that were misread
- [[ADR-010]]: two preset roots, and the snapshot layout that followed
- [[vst3-backs-up-as-nothing]]: the same shape one tier down: a plausible assumption about
  what a path points at
