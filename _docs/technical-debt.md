---
title: "Technical Debt"
status: published
created: 2026-08-16
updated: 2026-08-25
tags: [meta, technical-debt, priority]
---

# Technical Debt

What is built and not right, what has never run, and what is known-wrong deliberately. This is
not the same list as [dev-phases/](dev-phases/README.md), which covers things not built yet.

Closed entries live in [archive/technical-debt-closed.md](archive/technical-debt-closed.md).
The debt-clearing passes between 2026-08-17 and 2026-08-22 closed everything in §1, §4, §5, §6
and §7 that a commit can close, plus most of §8, and those entries moved out on 2026-08-25 with
their reasoning intact. Section numbers are unchanged in both files, so a reference to §4.18 from
an ADR or a commit message still resolves.

Read the archive before adding an entry. Sixty-odd closures are in there, and several of them
record why something is the way it is rather than just that it got done.

## What is left

No open work. The last three entries closed on 2026-08-25:

- **§2.4.** Porting `WindowsAudioEndpointInspector` answered it, and the answer was no. Classic
  `[ComImport]` does not survive trimming, in two distinct ways, and source-generated COM does.
- **§7.6.** The experiment ran on the reference rig. Wave Link resolves a plug-in by `PluginId`
  and repairs `FilePath` behind it, so the user-level folder is a viable fallback. The
  recommendation not to build one is unchanged, and now rests on a measurement.
- **§8.2.** Item 5 of the by-eye checklist was worked. The verdict reads as specified at two,
  five, nine and twelve inputs, and the routing matrix draws a dot exactly where each channel
  feeds.

All three evidence tables are in [the archive](archive/technical-debt-closed.md).

Two sections stay here permanently. Neither is owed a commit.

| | Why it lives here |
|---|---|
| **§3**, the known-wrong list | Four choices made with eyes open, recorded so nobody "discovers" them later and fixes them without knowing they were decided. |
| **§5**, numbers that are not constants | A standing hazard list. Four of the five are enforced by guard tests and the fifth by `HealthFingerprint` and `InputSlotsTests`. The table stays because the traps do. |

## 3 · Known-wrong deliberately

**Snapshots are not portable between machines, and we are not fixing that.** Endpoint IDs embed
device serials and plugin paths are absolute. "Export a chain" would be a different feature built
on `AudioPluginConfigurations` alone, and it is out of scope. The UI labels snapshots
machine-local instead. See [[restored-backup-has-dead-channels]].

**Licences are not captured, and the UI says so rather than working around it.** Copying a
`.vst3` restores the code, not the authorisation. Those vendors authorise through the registry,
through machine-bound licence files elsewhere, or through an online account, and nothing
licence-shaped exists in the vendor folders we copy. Tier 4 gets a working plugin on the same
machine; on a rebuild the user still reinstalls and re-authorises. See
[[restored-plugin-demands-a-licence]].

**`EBWebView\` is never captured**, despite living inside `LocalState`. It holds roughly 100 MB of
WebView2 browser profile, shader caches, IndexedDB and code caches. Capturing it would turn a
470 KB snapshot into a 100 MB one, and restoring it can wedge the UI.

**Wave Link's own AutoBackups are captured but never managed.** We copy them because they carry
history our first run will not have. We do not prune, rotate or write them, and a restore never
puts them back. They are evidence, not payload. This has been true since 0.6.0. It was written
here as settled fact on 2026-08-16 and the code did not actually do it until phase 6 §8, which
the spec-coverage pass caught.

---

## 5 · Numbers that are not constants

A standing hazard list rather than an open debt. Four of the five are enforced by guard tests in
`SourceGuardTests`; the fifth is held by `HealthFingerprint` and `InputSlotsTests`. The audit and
the correction that closed it are in [the archive](archive/technical-debt-closed.md).

Measured on one machine on 2026-08-15. Each of these becomes a bug if hard-coded. Reproduced from
`SPEC.md` §11 because it is the list most likely to be violated by someone moving fast.

| Looks like a constant | Actually | Do this instead |
|---|---|---|
| `Elgato.WaveLink_g54w8ztgkx496` | Stable per Store identity, but never assume | Glob `Elgato.WaveLink_*` |
| 5 inputs / 43 KB | One user's rig | Compare against *that user's* previous snapshot |
| `C:\Program Files\Common Files\VST3` | Default, and overridable | Resolve from `FilePath`; standard dirs are fallback only |
| 3.3.0.4108 | A beta; release users are on 3.2.9 | Record it, warn on mismatch, never gate on it |
| `%LOCALAPPDATA%` | Redirected on some corporate/OneDrive setups | `Environment.GetFolderPath`, never a composed string |

---
