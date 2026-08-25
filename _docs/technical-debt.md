---
title: "Technical Debt"
status: published
created: 2026-08-16
updated: 2026-08-25
tags: [meta, technical-debt, priority]
---

# Technical Debt

What is built and not right, what has never run, and what is known-wrong deliberately.
Distinct from [dev-phases/](dev-phases/README.md), which is for things not built yet.

Be blunt. A debt list that flatters the project is useless.

**Closed entries live in [archive/technical-debt-closed.md](archive/technical-debt-closed.md).**
Phases 1, 6 shipped `Core`, `Cli` and a WPF shell, and the debt-clearing passes of 2026-08-17
through 2026-08-22 closed everything in §1, §4, §5, §6 and §7 that a commit can close, plus most
of §8. Those entries moved out on 2026-08-25 with their reasoning intact, because a list of ticked
boxes buries the few things still owed. Section numbers are unchanged in both files, so a
reference to §4.18 from an ADR or a commit message still resolves.

**Three closed on 2026-08-25, and the list is now empty of work.**

- **§2.4.** Porting `WindowsAudioEndpointInspector` answered it, and the answer was no: classic
  `[ComImport]` does not survive trimming, in two distinct ways, and source-generated COM does.
- **§7.6.** The experiment ran on the reference rig. Wave Link resolves a plug-in by `PluginId`
  and repairs `FilePath` behind it, so the user-level folder is a viable fallback, and the
  recommendation not to build one is unchanged, now resting on a measurement.
- **§8.2.** Item 5 of the by-eye checklist was worked. The verdict reads as specified at two,
  five, nine and twelve inputs, and the routing matrix draws a dot exactly where each channel
  feeds.

All three evidence tables are in [the archive](archive/technical-debt-closed.md).

## What is actually left

**Nothing that is work.** Two sections stay here permanently, and neither is owed a commit.

| | Why it lives here |
|---|---|
| **§3**, the known-wrong list | Four choices made with eyes open, recorded so they are not "discovered" later and fixed by someone who does not know they were decided. Permanent by design. |
| **§5**, numbers that are not constants | A standing hazard list. Four of the five are enforced by guard tests and the fifth by `HealthFingerprint` and `InputSlotsTests`; the table stays because the traps do. |

### What closing looked like

The tier list this section used to carry had three tiers, and all three are now empty.

- **Tier 1, closeable by a commit.** Emptied 2026-08-22: §8.1's crash report, §8.3's watch rule,
  and the by-eye checklist that Tier 2 depended on.
- **Tier 2, closeable by a human with eyes.** Emptied 2026-08-25 with item 5. The rigs it needed
  were seeded rather than built by hand, which is most of why it finally happened.
- **Tier 3, closeable only by a fact from outside this repo.** Emptied 2026-08-25. §2.4 and §7.6
  had sat there because nothing in this repo could answer them, which was true right up until
  somebody went and looked.

**Read the archive before adding a new entry.** Sixty-odd closures are in there with their
reasoning, and several read as the record of *why* something is the way it is rather than as a
ticked box. A debt list that flatters the project is useless, so is one that has forgotten what it
already decided.

## 3 · Known-wrong deliberately

Choices made with eyes open. Listed so they are not "discovered" later and fixed by someone
who does not know they were decided.

**Snapshots are not portable between machines, and we are not fixing that.** Endpoint IDs
embed device serials; plugin paths are absolute. "Export a chain" is a *different feature*
built on `AudioPluginConfigurations` alone, and it is out of scope. The UI labels snapshots
machine-local instead. See [[restored-backup-has-dead-channels]].

**Licences are not captured, and the UI says so rather than working around it.** Copying a
`.vst3` restores the code, not the authorisation, those vendors authorise via registry,
machine-bound licence files elsewhere, or an online account. Nothing licence-shaped exists in
the vendor folders we copy. Tier 4 gets a working plugin on the same machine; on a rebuild the
user still reinstalls and re-authorises. See [[restored-plugin-demands-a-licence]].

**`EBWebView\` is never captured**, despite being inside `LocalState`. ~100 MB of WebView2
browser profile, shader caches, IndexedDB, code caches. Capturing it turns a 470 KB snapshot
into a 100 MB one, and restoring it can wedge the UI.

**Wave Link's own AutoBackups are captured but never managed.** We copy them as payload
because they carry history our first run will not have. We do not prune, rotate or write them, and
a restore never puts them back. They are evidence, not payload. *(True since 0.6.0. It was stated
here as settled fact from 2026-08-16, and the code did not do it until phase 6 §8, found by the
spec-coverage pass, which is the argument for having written one.)*

---

## 5 · Numbers that are not constants

**This is a standing hazard list, not an open debt.** Four of the five are enforced by guard tests
in `SourceGuardTests`; the fifth is held by `HealthFingerprint` and `InputSlotsTests`. The table
stays because the traps do, the audit and the correction that closed it are in
[the archive](archive/technical-debt-closed.md).

Measured on one machine on 2026-08-15. Each becomes a bug if hard-coded. Reproduced from
`SPEC.md` §11 because this is the list most likely to be violated by someone moving fast.

| Looks like a constant | Actually | Do this instead |
|---|---|---|
| `Elgato.WaveLink_g54w8ztgkx496` | Stable per Store identity, but never assume | Glob `Elgato.WaveLink_*` |
| 5 inputs / 43 KB | One user's rig | Compare against *that user's* previous snapshot |
| `C:\Program Files\Common Files\VST3` | Default, and overridable | Resolve from `FilePath`; standard dirs are fallback only |
| 3.3.0.4108 | A beta; release users are on 3.2.9 | Record it, warn on mismatch, never gate on it |
| `%LOCALAPPDATA%` | Redirected on some corporate/OneDrive setups | `Environment.GetFolderPath`, never a composed string |

---
