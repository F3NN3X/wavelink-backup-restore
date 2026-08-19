---
title: "Session: Phase 6 §1 — discovering what the settings reference"
status: published
created: 2026-08-19
updated: 2026-08-19
related_adrs: [ADR-006]
tags: [session, phase-6, core, vst3]
---

# Session: Phase 6 §1 — discovering what the settings reference

**Date:** 2026-08-19

**One commit** (`1e1f691`), 4 new Core files and 2 extended, **22 new tests** — Core 296 → 318.
Suite green and the Release build clean at the commit. Governed by
[dev-phases/phase-6-plugin-tiers.md](../dev-phases/phase-6-plugin-tiers.md) §1.

## Goal

Build §1 of [phase 6](../dev-phases/phase-6-plugin-tiers.md): extract the **referenced-plugin
set** from `AudioPluginConfigurations`, cross-reference it against Wave Link's plugin-scanner
cache for version and uniqueId, and do it without a Core-logic leak or a reflection-based
serializer. This is the foundation everything else in the phase reads from — tier 2's
`plugins.json`, tier 3's vendor folders, tier 4's binaries, and the restore-side warning.

## What happened

Three new Core files and one extension, all pure except the one that touches disk.

**`SettingsAnalysis` gained `ReferencedPlugins`** — a third walk over the already-parsed
`JsonDocument`, alongside the duplicate scan and the fingerprint. Every
`AudioPluginConfigurations` entry with a non-empty `FilePath`, deduplicated by path in channel
order. An empty `FilePath` is an Elgato built-in and is never a member: it ships with Wave Link,
so capturing it would be paying to back up the installer ([[ADR-006]]).

Deduplication is **by path, not by name**, because a real rig repeats the same compressor across
channels and tier 2 describes a set of plugins rather than a list of placements.

**`PluginCache`** reads the JUCE `<KNOWNPLUGINS>` XML. It never throws and never fails: malformed,
empty and BOM-prefixed input all yield an empty list. That is not defensive habit — tier 2 is
always on, and this file is a cache Wave Link rebuilds by rescanning, so a truncated one says
nothing about whether the settings are sound. Reflection-free via `XDocument`, per
[technical-debt.md](../technical-debt.md) §2.4.

**`PluginReferences.Resolve`** joins the two. Matching is **by path before name**: two builds of
one plugin share a name, and a name match that picks the wrong one records a version the
`ParameterState` was not written by — which is the exact drift tier 2 exists to make visible. A
plugin absent from the cache is still returned, version null, because the settings file's
`FilePath` is the authority on what is in use.

**`PluginCacheReader`** puts that behind `IFileSystem`. It returns a list rather than a `Result`,
deliberately: absent (a rig with no third-party plugins), locked (Wave Link rescanning, which is
exactly when a capture may fire) and denied all have to be survivable, and each degrades to "no
versions known".

## Decisions made

| Decision | Recorded in |
|---|---|
| Path-first matching, name as fallback | `PluginReferences.Resolve`, and the test `Matching_is_by_path_before_name` |
| The cache degrades to empty rather than failing a capture | `PluginCache.Parse`, `PluginCacheReader` |
| Vendor falls back to the cache's `manufacturer` when the settings omit it | `PluginReferences.Resolve` |
| The referenced set is a set, deduplicated by path | [[ADR-006]] · `SettingsAnalysis.ReadReferencedPlugins` |

## What did not work

Nothing was abandoned, but two things were nearly written wrong:

- **A `Result<T>` on the cache reader.** It reads like the house style — every other reader in
  `Io/` returns one. It is wrong here: a `Result` makes the caller decide what an unreadable cache
  means, and there is only one right answer, so encoding it in the type is the honest version.
- **Matching by name first**, because the name is what a user recognises. It is also what makes
  the version wrong on the one machine that has two builds installed.

## Open questions

- The cache's element/attribute spelling is read from [[ADR-006]] and `SPEC.md` §9, not measured
  against a live `AvailablePlugins.cache`. `uid` is accepted alongside `uniqueId` for older
  scanners; whether either appears on this machine's file is unverified.
- Nothing consumes `ReferencedPlugins` yet. §2 (`plugins.json`) is the first caller.

## Next

Phase 6 §2: the tier 2 manifest. A per-snapshot `plugins.json` written by `SnapshotStore.Write`
beside `settings.json`, recorded in `Files` and in `Tiers` as `"plugin-manifest"`, hand-written
with `Utf8JsonWriter` to match `ManifestSerializer`. Tolerant read: a malformed `plugins.json`
degrades to "unknown" per plugin and must never fail the snapshot.
