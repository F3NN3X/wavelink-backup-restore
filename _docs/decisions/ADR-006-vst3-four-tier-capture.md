---
title: "ADR-006: Four switchable VST3 tiers, capturing what is referenced"
status: accepted
created: 2026-08-16
updated: 2026-08-19
related_adrs: [ADR-010]
tags: [decision, capture, vst3]
---

# ADR-006: Four switchable VST3 tiers, capturing what is referenced

**Status:** Accepted, **with tier 3's location corrected by [[ADR-010]] on 2026-08-19**

**Date:** 2026-08-16

> **Its own "Revisit if" clause fired.** This ADR closed by saying: *revisit if a plugin ecosystem
> appears where presets are not under `%APPDATA%\<Vendor>\`.* On 2026-08-19 tier 3 was run
> against the reference rig for the first time and FabFilter turned out to be exactly that
> ecosystem, its user presets are in `Documents\FabFilter\Presets\<Plugin>\`, and what is in
> `%APPDATA%` is an interface default, a MIDI map and a cache.
>
> **The four tiers, their sizes, the referenced-not-installed rule and every decision below still
> stand.** What [[ADR-010]] replaces is one location: tier 3 reads **two roots** now, and a
> snapshot records which root each file came from.
>
> **The measurement in the table below is also worth reading carefully.** *"`%APPDATA%\FabFilter`
> is 246 files of presets"*, the count was right, the classification was not. None of those 246
> is a `.ffp`. A measurement recorded without saying what was counted is how a correct number
> licenses a wrong conclusion, and this is the example.

## Context

`Settings.json` records which effects sit on each channel, but not the effects themselves.
Restore it onto a machine missing FabFilter Pro-Q 4 and the channel loads with that effect
switched off, silently, and looking like the backup was incomplete.

The naive fix is to capture the VST3 tree. Measured on the reference machine:

```
entire VST3 tree                       4,887.0 MB
referenced set                            39.8 MB   ← 123× smaller
```

The saving comes from one fact: **Wave Link records the absolute `FilePath` of every
third-party plugin actually in use** in `AudioPluginConfigurations` (empty for Elgato
built-ins). That single field turns a 4.9 GB problem into a 40 MB one.

But 40 MB is still ~85× a settings-only snapshot, and the value is uneven. A plugin *list*
costs 4 KB and answers "why are my effects gone?" completely. The *presets* are the user's
own work, their EQ curves, their gate thresholds, and are irreplaceable. The *binaries* are
re-downloadable from the vendor, and copying them does not copy the licence
([[restored-plugin-demands-a-licence]]).

Different value, different cost, different answers per user. One switch cannot express that.

## Decision

Four tiers, independently switchable, captured per snapshot and recorded in its manifest:

| Tier | Content | Size | Default | Switchable |
|---|---|---|---|---|
| **1 · Settings** | `Settings.json` + Wave Link's own backup copies | ~470 KB | On | **No** |
| **2 · Plugin manifest** | Name, vendor, version, uniqueId, path, SHA-256 per referenced plugin | ~4 KB | On | **No** |
| **3 · Plugin presets** | `%APPDATA%\<Vendor>\<Plugin>\` for referenced vendors | ~10 MB | On | Yes |
| **4 · Plugin binaries** | The `.vst3` at each `FilePath` | ~40 MB | **Off** | Yes |

**Tiers 1 and 2 are not switchable, deliberately.** Together they are under half a megabyte
and they are the difference between a restore that works and a restore that leaves the user
guessing. A switch implies a meaningful choice; there isn't one.

**Tier 2 earns its keep at 4 KB.** It converts *"my effects are gone and I don't know why"*
into *"install FabFilter Pro-Q 4 v4.x, it's missing"*, which is the entire restore dialog's
missing-plugin warning. Build it from `FilePath` cross-referenced against
`AudioPluginCache\AvailablePlugins.cache` (a JUCE `<KNOWNPLUGINS>` XML carrying `name`,
`manufacturer`, `version`, `file`, `uniqueId`). On restore, check each resolves and flag
version drift.

**Always resolve from `FilePath`.** `C:\Program Files\Common Files\VST3` is a default, not a
location, standard directories are a fallback only.

## Alternatives considered

| Option | Why not |
|---|---|
| **Settings only** | The status quo, and it produces the silent-missing-effect failure this ADR exists to prevent. |
| **Capture the whole VST3 tree** | 4.9 GB per snapshot, 123× larger, and it destroys the "snapshot on every change, keep them forever" premise the whole project rests on. |
| **One "include plugins" switch** | Conflates presets (the user's own irreplaceable work, 10 MB) with binaries (re-downloadable, 40 MB, licence-less). Users would either lose their presets or pay 40 MB to keep them. |
| **Capture licence material too** | Nothing licence-shaped exists in these folders, checked. `%APPDATA%\FabFilter` is 246 files of presets; `%APPDATA%\Supertone\Clear` holds only crash reports. Those vendors authorise via registry, machine-bound files elsewhere, or an online account. |

## Consequences

**This enables:** an honest size estimate in Settings, derived from the enabled tiers and
recomputed rather than hard-coded; and a restore dialog that can name exactly what is missing.

**This rules out:**

- Snapshots as a complete machine rebuild. Tier 4 gets a *working plugin on the same machine*.
  On a rebuild the user reinstalls and re-authorises regardless. **The UI must say so**, the
  Settings dialog's "Licences are never included" note is not optional copy.
- A uniform restore privilege model. `C:\Program Files\Common Files\VST3` is not user-writable,
  so **tier 4 restore needs elevation and tiers 1, 3 must not**. Keeping the common path
  admin-free is most of the value; prompt only when a tier 4 restore is actually requested.

**Two traps this decision creates**, both in [technical-debt.md](../technical-debt.md) §2.3:

- **A `.vst3` may be a directory.** The VST3 spec defines a bundle
  (`Plugin.vst3\Contents\x86_64-win\Plugin.vst3`). All six plugins observed are single files,
  so **the author's machine will never exercise the bundle path**. Assuming "file" does not
  throw; it silently captures nothing. Test for directory and recurse.
  See [[vst3-backs-up-as-nothing]].
- **`ParameterState` is written by a specific plugin version.** Restoring an older settings
  file against a newer plugin normally works, plugins version their own state, but it is not
  guaranteed. This is why tier 2 records the version it was captured against.

**Revisit if:** ~~a plugin ecosystem appears where presets are not under
`%APPDATA%\<Vendor>\`, which would make tier 3's discovery heuristic unreliable rather than
merely imperfect.~~, **fired 2026-08-19; see [[ADR-010]].** The successor condition is a **third**
root, or a vendor that keeps presets directly in `Documents\<Vendor>\` with no `Presets`
subfolder.

## References

- `SPEC.md` §9
- [README.md](../operations/design/README.md). Screen 3 "What goes in a backup"
- [[restored-plugin-demands-a-licence]] · [[vst3-backs-up-as-nothing]]
