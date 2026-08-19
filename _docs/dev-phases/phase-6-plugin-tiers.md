---
title: "Phase 6 — Plugin tiers"
status: review
created: 2026-08-19
updated: 2026-08-19
related_adrs: [ADR-006]
tags: [dev-phase]
---

# Phase 6 — Plugin tiers

**Status:** In progress. **§1 landed 2026-08-19** — `SettingsAnalysis.ReferencedPlugins`,
`PluginCache`, `PluginReferences.Resolve` and `PluginCacheReader`, with 22 tests
([session note](../sessions/2026-08-19-phase-6-plugin-discovery.md)). §2 onward is unbuilt.
**Entry criteria:** phase 5 complete. ✅ 2026-08-19. The store, the restore flow and the shell
exist; tier 1 (settings) captures and restores end to end.
**Exit criteria:** all four tiers from [[ADR-006]] capture and restore; a `.vst3` **bundle** is
covered by a synthetic fixture test; elevation is requested only for a tier 4 restore, never for
tiers 1–3; the Settings dialog's two locked rows unlock and their sizes are honest; and the
restore dialog's missing-plugin warning names exactly what is missing.

## Why this phase exists

Tier 1 alone produces the silent-missing-effect failure [[ADR-006]] exists to prevent: restore a
`Settings.json` onto a machine without FabFilter Pro-Q 4 and the channel loads with that effect
switched off, looking like an incomplete backup. Tiers 2–4 close that gap — at under half a
megabyte for tier 2, and only when the user opts into the larger tiers.

It also **unlocks two surfaces that are currently built but frozen**:

- The Settings dialog's *Effect presets* and *The effect plug-ins themselves* rows render off and
  unmovable (`SettingsViewModel.IncludePresets` / `IncludePluginFiles` reject every set).
- The main list's `PRESETS` / `PLUGINS` tier badges are always absent, because no snapshot ever
  carries them.

This phase is where the "NOT BUILT YET" copy stops being true.

## Scope

### In

- **Tier 2 · Plugin manifest** — read `AudioPluginConfigurations`, cross-reference
  `AudioPluginCache\AvailablePlugins.cache`, and write a per-snapshot `plugins.json`: name, vendor,
  version, uniqueId, path, SHA-256 per referenced plugin. Always on; not switchable.
- **Tier 3 · Plugin presets** — capture `%APPDATA%\<Vendor>\<Plugin>\` for the referenced vendors.
  Switchable; on by default.
- **Tier 4 · Plugin binaries** — copy the `.vst3` at each `FilePath`, handling the bundle case.
  Switchable; off by default. Restore needs elevation.
- The restore-side check: does each referenced plugin resolve, and has its version drifted? This
  feeds the restore dialog's missing-plugin warning.
- Unlocking the two Settings rows with honest, recomputed sizes (never hard-coded).
- Extending `BackupSettings`, `SettingsSerializer` and `SnapshotManifest` for the tier toggles —
  hand-written JSON only (the source-scan guard forbids reflection; [technical-debt.md](../technical-debt.md) §2.4).

### Out — and where it went instead

- **Tier 1 capture/restore** → already built (phases 1–3, 5). This phase does not touch the
  settings path except to add files beside it.
- **Licence capture** → never. Nothing licence-shaped exists in these folders ([[ADR-006]]); the
  "Licences are never included" note stays.
- **Moving a setup to another machine** → out of scope forever; snapshots name this machine's
  audio devices.
- **Toasts, autostart, update mechanics** → phase 7.
- **The first-run "Wave Link not found" variant** → open design debt ([technical-debt.md](../technical-debt.md) §4.10), not part of this phase.

## Work

Grouped; each item names its source. Order matters: tier 2 is the foundation the restore warning
and the honest sizes both read from, so it lands first.

### 1 · Discover what the settings actually reference (Core) — [[ADR-006]], SPEC §9

`SettingsAnalysis` already reads `AudioPluginConfigurations` for the effect count
(`SettingsAnalysis.cs:102`). Extend it to also extract the **referenced plugin set**: every entry
whose `FilePath` is non-empty (empty = Elgato built-in, never captured), carrying `Name`, `Vendor`,
`FilePath`.

- **Always resolve from `FilePath`.** `C:\Program Files\Common Files\VST3` is a default, not a
  location — standard directories are a fallback only.
- Cross-reference against `AudioPluginCache\AvailablePlugins.cache` (a JUCE `<KNOWNPLUGINS>` XML:
  `name`, `manufacturer`, `version`, `file`, `uniqueId`) to attach version and uniqueId. A plugin
  in the settings but absent from the cache is still recorded — its `FilePath` is authoritative —
  with version unknown, flagged as drift.

**Pure where it can be.** Parsing the two inputs into a referenced-plugin list is pure analysis
(testable with fixture bytes); reading them off disk goes through the existing `IFileSystem` seam.

### 2 · Tier 2 manifest — `plugins.json` (Core) — [[ADR-006]]

A new per-snapshot file, written by `SnapshotStore.Write` alongside `settings.json`, recorded in
the manifest's `Files` dictionary and in `Tiers` as `"plugin-manifest"`. Per plugin: name, vendor,
version, uniqueId, path, SHA-256 of the binary (when present).

Hand-written serializer, matching `ManifestSerializer` — no reflection. Tolerant read: a missing
or partial field falls back independently; tier 2 is always on, so a malformed `plugins.json` must
degrade to "unknown" per plugin, never fail the snapshot.

**This is what earns its keep at ~4 KB.** It converts *"my effects are gone and I don't know why"*
into *"install FabFilter Pro-Q 4 v4.x, it's missing."*

### 3 · Tier 3 presets (Core) — [[ADR-006]]

For each referenced vendor, capture `%APPDATA%\<Vendor>\<Plugin>\`. Discovery is a heuristic and is
imperfect by design ([[ADR-006]] "revisit if"): record what was found, and record per-plugin size
and file count in `plugins.json` so an empty capture is visible rather than silent.

Switchable via `BackupSettings.IncludePresets`; on by default. Recorded in `Tiers` as `"presets"`.

### 4 · Tier 4 binaries — the bundle problem (Core) — [[ADR-006]], [[vst3-backs-up-as-nothing]]

Copy the `.vst3` at each `FilePath`. **This is the phase's defining risk and its required test.**

A `.vst3` may be a **directory** (a VST3 bundle: `Plugin.vst3\Contents\x86_64-win\Plugin.vst3`).
All six plugins on the reference machine are single files, so the author's setup will never
exercise the bundle path — which is exactly why it must be tested with a fixture.

```csharp
if (Directory.Exists(filePath))          // ← check FIRST: a bundle is a directory
    CopyDirectoryRecursive(filePath, destination);
else if (File.Exists(filePath))
    File.Copy(filePath, destination, overwrite: true);
else
    throw new PluginNotFoundException(filePath);   // fail the tier, never silently skip
```

Order matters: `Directory.Exists` **before** `File.Exists`. A plugin that cannot be captured must
fail the tier, not quietly reduce it. Assert non-zero size and a file count after every capture —
a zero-byte "success" is the bug this exists to catch.

Switchable via `BackupSettings.IncludePluginFiles`; off by default. Recorded in `Tiers` as
`"plugins"`.

**The synthetic bundle fixture is not optional.** Build a directory named `Fake.vst3` containing
`Contents\x86_64-win\Fake.vst3`, assert the capture recurses it and records a non-zero size. This
is the only way the path gets exercised.

### 5 · Restore-side resolution and version drift (Core) — [[ADR-006]], SPEC §9

On restore, for each plugin in the snapshot's `plugins.json`: does its `FilePath` resolve on this
machine? If not → missing. If it resolves but the current version differs from the recorded one →
version drift. Produce a structured result the shell can render — the restore dialog's
missing-plugin warning (`RestoreDialogModel.MissingPluginWarning`) becomes real instead of null.

### 6 · Settings and manifest extension (Core) — [[ADR-006]], technical-debt §2.4

- `BackupSettings`: add `IncludePresets = true` and `IncludePluginFiles = false`. The doc comment
  at `BackupSettings.cs:9` ("Tier toggles … arrive in phase 6") is retired.
- `SettingsSerializer`: write and tolerate the two new booleans, hand-written. **Bump
  `CurrentSchemaVersion` only if a field's meaning changes** — adding fields with defaults does not;
  the tolerant read already ignores unknown keys. Keep it reflection-free or the source-scan guard
  fails the build.
- `SnapshotManifest`: no schema change needed for the toggles themselves (they live in
  `settings.json`); the captured tiers are already expressed by `Tiers` and `Files`.

### 7 · The shell unlocks (App) — design README, Screen 3 + tier badges

- **Settings dialog:** unlock `IncludePresets` / `IncludePluginFiles`. Their sizes become honest
  figures recomputed from what a current snapshot would capture — never the hard-coded 10 MB / 40 MB
  the design mock shows. The proportion bar already recomputes from enabled tiers
  (`SettingsViewModel`), so it follows automatically.
- **Main list:** `SnapshotRowViewModel.TierOrder` is already `["settings", "presets", "plugins"]`;
  once snapshots carry those tiers, the `PRESETS` / `PLUGINS` badges render present/absent per the
  design (present = card fill; absent = dashed hairline). No badge code should be needed — verify.
- **Restore dialog:** wire the tier 5 result into `MissingPluginWarning`.

**No Core logic may move into the shell.** If a verb or a view wants something Core cannot do, that
is a Core change with its own tests ([[ADR-004]]).

## Testing

| Test | Pins |
|---|---|
| Referenced set = only entries with non-empty `FilePath` | Built-ins never captured |
| `plugins.json` records name/vendor/version/uniqueId/path/sha per plugin | Tier 2 shape |
| Malformed `plugins.json` degrades to per-plugin "unknown", does not fail the snapshot | Tolerant read |
| **Synthetic bundle fixture recurses and records non-zero size** | [[vst3-backs-up-as-nothing]] |
| A missing plugin fails the tier, never silently skips it | No silent absence |
| Zero-size capture is a failure | The silent-success bug |
| Tier 4 restore requests elevation; tiers 1–3 do not | The privilege model |
| Restore flags a missing plugin by name + version | The warning becomes real |
| Restore flags version drift when the current version differs | ParameterState trap |
| `IncludePresets` / `IncludePluginFiles` round-trip through `SettingsSerializer` | Settings persist |
| Adding the two booleans does not require a schema bump (tolerant read ignores unknown keys) | Backward compat |
| Settings sizes are recomputed from enabled tiers, not hard-coded | Honest size |

## Risks

| Risk | Early signal | Response |
|---|---|---|
| Bundle path never exercised on the author's machine | No test covers a directory `.vst3` | The synthetic fixture is a hard exit criterion, not a nice-to-have |
| Preset discovery heuristic finds the wrong vendor folder | Captured size wildly off from ~10 MB, or empty | Record per-plugin size/count in `plugins.json`; surface empties; revisit the heuristic ([[ADR-006]]) |
| Tier 4 restore silently needs elevation and fails on a normal account | Restore of binaries throws access-denied | Prompt for elevation only when tier 4 is actually requested; keep tiers 1–3 admin-free |
| Reflection sneaks into the new serializer | Source-scan guard fails the build | Hand-written `Utf8JsonWriter`/`JsonDocument`, matching `ManifestSerializer` (technical-debt §2.4) |
| Logic leaks into the shell to "make the badge show" | A view reaching past Core for plugin data | Push it into Core with tests; the shell renders, it does not discover ([[ADR-004]]) |
| Version-drift check is too strict and flags every restore | Warning fires on a no-op restore | Compare recorded vs current version only when both are known; unknown → "drift", never a hard failure |

## References

- [[ADR-006]] — the four tiers, what is switchable, and the two traps it creates
- `SPEC.md` §9 — the authority on *what* to build: the tier table, `AudioPluginConfigurations`, the three things that bite
- [[vst3-backs-up-as-nothing]] · [[restored-plugin-demands-a-licence]]
- [technical-debt.md](../technical-debt.md) §2.3 (the bundle trap), §2.4 (NativeAOT / no reflection)
- [operations/design/README.md](../operations/design/README.md) — Screen 3 "What goes in a backup", the tier badges, the restore missing-plugin warning
