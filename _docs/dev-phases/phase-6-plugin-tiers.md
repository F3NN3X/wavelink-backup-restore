---
title: "Phase 6 — Plugin tiers"
status: review
created: 2026-08-19
updated: 2026-08-19
related_adrs: [ADR-006]
tags: [dev-phase]
---

# Phase 6 — Plugin tiers

**Status:** ✅ **Complete 2026-08-19** — all eight sections, shipped as **0.6.0** with **1,146
tests** (Core 399, CLI 97, App 650) and a clean Release build. §1–§2 landed first
([session note](../sessions/2026-08-19-phase-6-plugin-discovery.md)); §3–§8 followed in one pass
([session note](../sessions/2026-08-19-phase-6-tiers-complete.md)).
**Entry criteria:** phase 5 complete. ✅ 2026-08-19. The store, the restore flow and the shell
exist; tier 1 (settings) captures and restores end to end.
**Exit criteria:** all four tiers from [[ADR-006]] capture and restore; a `.vst3` **bundle** is
covered by a synthetic fixture test; elevation is requested only for a tier 4 restore, never for
tiers 1–3; the Settings dialog's two locked rows unlock and their sizes are honest — including
the first row, which promised 470 KB and delivered 43 KB until §8; and the restore dialog's
missing-plugin warning names exactly what is missing. **All met.**

## Why this phase exists

Tier 1 alone produces the silent-missing-effect failure [[ADR-006]] exists to prevent: restore a
`Settings.json` onto a machine without FabFilter Pro-Q 4 and the channel loads with that effect
switched off, looking like an incomplete backup. Tiers 2–4 close that gap — at under half a
megabyte for tier 2, and only when the user opts into the larger tiers.

It also **unlocked two surfaces that were built but frozen**:

- The Settings dialog's *Effect presets* and *The effect plug-ins themselves* rows rendered off and
  unmovable (`SettingsViewModel.IncludePresets` / `IncludePluginFiles` rejected every set).
- The main list's `PRESETS` / `PLUGINS` tier badges were always absent, because no snapshot ever
  carried them.

This phase is where the "NOT BUILT YET" copy stopped being true — the badge is deleted.

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
- **Tier 1 completeness** — capture Wave Link's own backup copies alongside `Settings.json`, which
  is what makes tier 1 the ~470 KB [[ADR-006]] and the Settings dialog both describe (§8).
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

### 2 · Tier 2 manifest — `plugins.json` (Core) — [[ADR-006]] ✅ 2026-08-19

A new per-snapshot file, written by `SnapshotStore.Write` alongside `settings.json`, recorded in
the manifest's `Files` dictionary and in `Tiers` as `"plugin-manifest"`. Per plugin: name, vendor,
version, uniqueId, path, SHA-256 of the binary (when present).

Hand-written serializer, matching `ManifestSerializer` — no reflection. Tolerant read: a missing
or partial field falls back independently; tier 2 is always on, so a malformed `plugins.json` must
degrade to "unknown" per plugin, never fail the snapshot.

**This is what earns its keep at ~4 KB.** It converts *"my effects are gone and I don't know why"*
into *"install FabFilter Pro-Q 4 v4.x, it's missing."*

**As built.** `SnapshotStore.Write` takes the resolved plugin set as an optional argument;
`SettingsInspector` resolves it, so every capture the application makes — manual, automatic and
pre-restore — carries it. Non-null *including empty* writes the file and claims the tier; null
writes neither, which is the honest distinction between "we looked and found none" and "this
caller never looked". Every snapshot written before this is the second case, and the restore
warning (§5) has to tell them apart. The binary's hash comes from `PluginBinaryFiles.HashOf` —
the one part of tier 2 that touches disk: a missing binary, a locked one, or a bundle directory
each leave it null rather than throwing.

**Reshaped by §3.** The argument became a `SnapshotPayload` once tiers 3 and 4 had files and tier
names of their own to carry, and `TierCapture` builds the whole manifest because tier 2 records
what the other tiers found. The null-versus-empty rule is unchanged and is the reason the type
exists at all.

### 3 · Tier 3 presets (Core) — [[ADR-006]] ✅ 2026-08-19

For each referenced vendor, capture `%APPDATA%\<Vendor>\<Plugin>\`. Discovery is a heuristic and is
imperfect by design ([[ADR-006]] "revisit if"): record what was found, and record per-plugin size
and file count in `plugins.json` so an empty capture is visible rather than silent.

Switchable via `BackupSettings.IncludePresets`; on by default. Recorded in `Tiers` as `"presets"`.

**As built.** `PresetFiles` looks in the narrowest place first and stops at the first hit:
`<AppData>\<Vendor>\<plugin name>`, then `<AppData>\<Vendor>\<file name without .vst3>` (the
settings file says "Pro-Q 4"; the installer's folder is "FabFilter Pro-Q 4"), then the vendor
folder for vendors that keep presets flat. A plugin with **no vendor recorded finds nothing** —
guessing a vendor folder from a plugin name would capture some other vendor's work.

Each entry in `plugins.json` records `presetSource`, `presetFileCount` and `presetBytes`, so a
capture that read the wrong folder is diagnosable rather than merely disappointing. **The tier is
claimed only when at least one file was captured**: switched on and empty is not a `PRESETS` badge.
Two plugins from one vendor that resolve to the same folder store it once and each record what
their own lookup found, which makes the *estimate* an upper bound — the safe direction for a figure
printed before the user opts in.

### 4 · Tier 4 binaries — the bundle problem (Core) — [[ADR-006]], [[vst3-backs-up-as-nothing]] ✅ 2026-08-19

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

**As built.** The fixture exists on **both sides** — capture recurses the bundle and restore puts
the whole tree back, because capturing a bundle is pointless if the restore flattens it.

"Fail the tier, never quietly reduce it" is implemented as **all-or-nothing**: one plugin that
cannot be captured means tier 4 captures nothing and claims nothing, and `plugins.json` records
per plugin whether its binary was copied, so the reason is inspectable. An empty bundle directory
counts as a failure rather than a zero-byte success — that is the bug this tier exists to catch.
Two vendors shipping the same file name are disambiguated (`plugins/2-Clear.vst3`), and the
snapshot-relative root is recorded per plugin, because restore has to map it back and reversing a
name would be a guess.

**Restore is opt-in and says what it needs.** `RestoreOptions(Presets: true, PluginBinaries: false)`
is the default; the CLI opts in with `--with-plugins`. A write into `Program Files` that comes back
`UnauthorizedAccessException` is reported as *needs elevation* — distinct from an `IOException`,
because "run it again elevated" and "something else has the file" are different answers — and it
**never fails the restore**, which by then has already written the settings file.

### 5 · Restore-side resolution and version drift (Core) — [[ADR-006]], SPEC §9 ✅ 2026-08-19

On restore, for each plugin in the snapshot's `plugins.json`: does its `FilePath` resolve on this
machine? If not → missing. If it resolves but the current version differs from the recorded one →
version drift. Produce a structured result the shell can render — the restore dialog's
missing-plugin warning (`RestoreDialogModel.MissingPluginWarning`) becomes real instead of null.

**As built.** `PluginResolution` answers per plugin: on disk **or** in the live scanner cache (a
plugin the user moved is still installed, and a bundle is a directory — testing only for a file
would report every bundled plugin missing). Drift is claimed **only when both versions are known**;
either side unknown reads as unknown, never as a change, because a warning that fires on every
restore is one nobody reads by the third time.

Core writes both clauses, as `RestorePlanner` already does for the Wave Link version note. Naming
the channel — *"The Voice channel will load with that effect switched off"* — is why `plugins.json`
records channels at all. **Drift is not amber**: it joins the quiet mono line the version mismatch
uses, because a plugin that updated is not missing and nothing about the restore is un-whole. That
placement is the one thing in this section the design does not specify, so it is recorded here
rather than assumed.

### 6 · Settings and manifest extension (Core) — [[ADR-006]], technical-debt §2.4 ✅ 2026-08-19

- `BackupSettings`: add `IncludePresets = true` and `IncludePluginFiles = false`. The doc comment
  at `BackupSettings.cs:9` ("Tier toggles … arrive in phase 6") is retired.
- `SettingsSerializer`: write and tolerate the two new booleans, hand-written. **Bump
  `CurrentSchemaVersion` only if a field's meaning changes** — adding fields with defaults does not;
  the tolerant read already ignores unknown keys. Keep it reflection-free or the source-scan guard
  fails the build.
- `SnapshotManifest`: no schema change needed for the toggles themselves (they live in
  `settings.json`); the captured tiers are already expressed by `Tiers` and `Files`.

### 7 · The shell unlocks (App) — design README, Screen 3 + tier badges ✅ 2026-08-19

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

**As built.** The badges needed no code, as predicted — a snapshot carrying the tiers lights them,
and a test now pins that so a tier rename cannot silently blank the column. `plugin-manifest` gets
no badge of its own: three slots, "always three wide, so the column is scannable".

The dialog's figures come from `TierCapture.Measure`, which walks the same directories a capture
would and never reads a byte to do it. **The proportion bar recomputes when a tier is switched** —
"recompute from the enabled tiers" is only true if it recomputes at the moment the user changes
one. `Locked` changed meaning here: it used to mark the two *unbuilt* tiers and now marks the two
that have **no switch by design**, which is what the design always said, and the `NOT BUILT YET`
badge is deleted.

**One defect fell out of the wiring:** the "Your setup" row had been printing the size of Wave Link
Backup's *own* preferences file — a few hundred bytes — rather than the Wave Link settings the row
describes.

**What the shell still does not do:** restore plug-in binaries. Elevation has no designed surface
(no UAC prompt, no error screen for a declined one), so the capability lives in Core and is
reachable from the CLI. [technical-debt.md](../technical-debt.md) §4.17.

### 8 · Tier 1 is not yet what the design promises (Core) — SPEC §1, [[ADR-006]] ✅ 2026-08-19

**Found while building §2, 2026-08-19.** Tier 1 is defined by [[ADR-006]] as
*"`Settings.json` + Wave Link's own backup copies, ~470 KB"*, SPEC §1 classifies both of those
directories as **BACK UP**, the Settings dialog's first row reads *470 KB*, and
[technical-debt.md](../technical-debt.md) §3 states as settled fact that *"Wave Link's own
AutoBackups are captured but never managed"*.

**They were not captured.** A snapshot held `settings.json` and nothing else — 43 KB where four
documents say 470 KB. ✅ **Fixed 2026-08-19.**

**Why it lives in this phase** rather than a patch release: §7 unlocks the Settings rows with
*honest, recomputed* sizes, and the first row's honest size is exactly this number. Fixing the
tier and fixing the figure in the same phase is the only way they cannot disagree.

**What to capture**, per SPEC §1's table:

| Source under `LocalState` | Into the snapshot | Why |
|---|---|---|
| `Backup\AutoBackup\Settings.auto.<ts>.json` | `wavelink-backups/` | ~420 KB, roughly one per launch, ten kept. They carry history our first run will not have |
| `Backup\Settings.json.bak.<rand>.<rand>` | `wavelink-backups/` | ~217 KB, irregular atomic-save artifacts reaching back months. The highest-value forensic material |

**Rules, all of which fall out of what is already built:**

- **Each file is hashed and sized into the manifest's `Files`**, under its snapshot-relative path,
  so `SnapshotGuard` verifies them with no new code and no tier awareness — exactly as
  `plugins.json` does today.
- **Best effort per file, never a failed capture.** These are Wave Link's files in Wave Link's
  directory: one can vanish or be locked mid-copy while it rotates them. A file that cannot be read
  is left out and the snapshot still succeeds — unlike `Settings.json`, whose absence is a real
  failure. Record what was captured; do not claim what was not.
- **A cap, and an honest one.** Ten AutoBackups is the observed steady state, but nothing enforces
  it — a rig that has not been cleaned could hold far more. Take the newest N by last-write time
  (N = 10, matching what Wave Link itself keeps), and record the count.
- **Restore does not write them back.** They are evidence, not payload: restoring
  `Backup\AutoBackup\` would overwrite Wave Link's own rotation with a stale set. They are
  captured so that a person can read them, and the restore flow ignores them
  ([technical-debt.md](../technical-debt.md) §3 — *captured but never managed*).
- **No schema bump.** Additional `Files` entries and a tier name are additive; the tolerant read
  already ignores what it does not know.
- **Dedup is unaffected.** `settingsSha256` stays the dedup key. Wave Link rotating its own backups
  is not a configuration change, and letting it trigger a snapshot would defeat §6's whole point.

**Size check before building:** ~470 KB per snapshot against 43 KB today is 11×, and the store's
premise is "keep them forever". At one distinct configuration a day that is 170 MB a year rather
than 16 MB. That is still small, and it is what the design already tells the user it costs — but
the prune count and the Settings dialog's figures should be re-read with the real number in front
of them, not the old one.

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
| Tier 1 captures Wave Link's own AutoBackup and `.bak` files, hashed into `Files` | §8, SPEC §1 |
| A locked or vanished AutoBackup leaves the snapshot successful and shorter | Wave Link's directory, not ours |
| Restore writes back `Settings.json` only, never the captured AutoBackups | Evidence, not payload |
| The captured set is capped at the newest N and the count is recorded | No unbounded capture |

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
