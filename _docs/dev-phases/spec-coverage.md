---
title: "Spec coverage"
status: review
created: 2026-08-19
updated: 2026-08-19
tags: [dev-phase, index, spec]
---

# Spec coverage

Every requirement in [`SPEC.md`](../SPEC.md), and where it stands. Written so that "we built the
spec" is a claim someone can check line by line rather than a feeling.

**Legend.** ✅ built and tested · 🔨 planned, with the section that owns it · ⛔ refused, with the
decision that refused it · ❓ unanswerable here.

Read alongside [README.md](README.md) (the phase order) and [post-1.0.md](post-1.0.md) (what is
deliberately not in 1.0).

---

## The corrections block (measured 2026-08-16)

| Correction | Status | Where |
|---|---|---|
| Do **not** apply `UnsafeRelaxedJsonEscaping`; Wave Link's default encoder is correct | ✅ | Moot by construction — capture is a byte copy, never a re-serialize. [technical-debt.md](../technical-debt.md) §1.2 withdrawn |
| `JsonNode.Parse` preserves case-insensitive duplicates and throws on exact ones | ✅ | `SettingsAnalysis` catches `ArgumentException` as `MalformedSettings`; §2.1 answered |
| `Settings.json` is locked while Wave Link runs; reads need `FileShare.ReadWrite \| Delete` | ✅ | `IFileSystem.ReadSharedBytes`, enforced by a source-scan guard that fails the build |

## §1 · Where the settings live

| Requirement | Status | Where |
|---|---|---|
| Resolve by package family, never the `%APPDATA%\Elgato\WaveLink` decoy | ✅ | `SettingsLocator` globs `Elgato.WaveLink_*` and requires `Settings.json` |
| Multiple packages are refused, never guessed | ✅ | `MultiplePackagesFound` + the Settings dialog's `WHICH WAVE LINK` section |
| `Settings.json` captured | ✅ | Tier 1, phase 2 |
| `Backup\AutoBackup\*` and `Backup\Settings.json.bak.*` captured | ✅ | Phase 6 §8 — newest ten of each, best effort per file, never written back on restore |
| Newest log read to verify a restore | ✅ | `RestoreOrchestrator` step 6 — the log, never the UI |
| `ws-info.json`, `AudioPluginCache\` skipped as payload | ✅ | Neither is written into a snapshot; the cache is read for tier 2 |
| `EBWebView\` never captured | ⛔ | [technical-debt.md](../technical-debt.md) §3 — ~100 MB of WebView2 profile |

## §2 · Why the built-in backup isn't enough

| Requirement | Status | Where |
|---|---|---|
| One snapshot per distinct content hash, kept indefinitely | ✅ | `settingsSha256` dedup; manual and pre-restore snapshots are never pruned |

## §3 · What's inside Settings.json

| Requirement | Status | Where |
|---|---|---|
| Health fingerprint: input count + names + size | ✅ | `HealthFingerprint`, surfaced in every list row |
| Do not model the config as a flat list of channels | ✅ | Nothing rewrites the tree; capture and restore move whole files |
| Device IDs are foreign keys in both bare and `<id>\|<suffix>` forms | 🔨 | Only matters where the tree is rewritten — **phase 7 §1** (redaction) is the first place that happens |

## §4 · The restore sequence

| Requirement | Status | Where |
|---|---|---|
| Validate before touching anything | ✅ | `SnapshotGuard.Verify` before any write |
| Close **both** `Elgato.WaveLink` and `WavelinkSEService`, verify exited | ✅ | `WaveLinkProcess.ProcessNames` — upstream's §1.6 defect, fixed |
| Snapshot the current file first, unconditionally | ✅ | `SnapshotTrigger.PreRestore`, with no parameter to skip it |
| Write atomically via `File.Replace` | ✅ | `SettingsWriter`, re-checking the precondition itself |
| Relaunch via shell AppID | ✅ | `LaunchByAppId`; `CanRelaunch` is false for an explicit non-package path |
| Verify from the new log, not the UI | ✅ | `LogAnalysis` |

## §5 · Validation traps

| Requirement | Status | Where |
|---|---|---|
| Duplicate-key detection with `JsonDocument` | ✅ | `DuplicateKeyScanner`; `hasDuplicateKeys` marks a snapshot suspect without blocking restore |
| Never round-trip through a serializer | ✅ | Byte copy end to end, asserted by test |
| Newest is not best — rank by content | ✅ | Every row shows its fingerprint; pre-restore rows are visually distinct |
| Record the app version in every snapshot | ✅ | `waveLinkVersion` from `Update.LastUpdateVersion` |

## §6 · Worth building in

| Requirement | Status | Where |
|---|---|---|
| Hash-dedup | ✅ | `BackupService.CaptureAutomatic`; manual captures are never deduped, deliberately |
| Snapshot metadata | ✅ | `manifest.json` carries all seven fields |
| Watch, don't poll | ✅ | `FileSystemSettingsWatcher` + ~60s debounce. The hourly cap is now the user's to set (15 min – 24 h), alongside an optional daily backup ([14-backup-timing.md](../operations/design/screens/14-backup-timing.md)) |
| Snapshot on shutdown | ✅ | `CaptureOnShutdown` |
| Store outside `LocalState` | ✅ | [[ADR-003]] — the critical inherited defect |
| Adjacent Elgato configs | ⛔ | Scope widening; not planned |

## §7 · Prior art

| Requirement | Status | Where |
|---|---|---|
| Take `SettingsDiscovery` | ✅ | Ported and extended — an explicit path bypasses discovery entirely |
| Take `WindowsAudioEndpointInspector` | ⛔ 1.0 | Nothing in the design needs live endpoint enumeration. [post-1.0.md](post-1.0.md) — it arrives with "repair a dead input" |
| Take the shutdown sequence | ✅ | Plus the `WavelinkSEService` fix upstream is missing |
| Take the atomic write | ✅ | `SettingsWriter` |
| Take `ValidateManagedPath`'s *intent* | ✅ | `SnapshotGuard` asserts contents, not a filename — which also catches corruption a regex never could |
| Keep the seam interfaces | ✅ | `IFileSystem`, `IClock`, `IWaveLinkProcess`, `IRecycleBin` |
| Fix 1 — backups inside `LocalState` | ✅ | Phase 2 |
| Fix 2 — the JSON encoder | ⛔ | Withdrawn: the recommendation was inverted. See the corrections block |
| Fix 3 — no duplicate-key detection | ✅ | Phase 1 |
| Fix 4 — manual tool, not a safety net | ✅ | Phase 3 |
| Fix 5 — the runtime dependency | 🔨 | **Phase 7 §4** — ADR-010. CLI is settled (NativeAOT, 3.2 MB); the WPF app is not |

## §8 · Language

| Requirement | Status | Where |
|---|---|---|
| C# / .NET 10, core library + thin shells, NativeAOT kept open for the CLI | ✅ | [[ADR-001]], [[ADR-004]]; a source-scan guard fails the build on reflection-based JSON |

## §9 · VST3 tiers

| Tier | Status | Where |
|---|---|---|
| 1 · Settings + Wave Link's own backups (~470 KB) | ✅ | Phase 6 §8 |
| 2 · Plugin manifest (~4 KB) | ✅ | `plugins.json`, phase 6 §1–2 |
| 3 · Plugin presets (~10 MB, opt-in, on) | ✅ | Phase 6 §3 — captured and restored from **both** `%APPDATA%` and Documents. The heuristic was measured against the reference rig and was wrong; the fix took a snapshot from 61 preset files to 491 ([technical-debt.md](../technical-debt.md) §4.18) |
| 4 · Plugin binaries (~40 MB, opt-in, off) | ✅ | Phase 6 §4 — and restorable from the shell since the elevation surface was designed ([13-elevation.md](../operations/design/screens/13-elevation.md), §4.17) |
| A `.vst3` may be a **directory** — synthetic bundle fixture | ✅ | Fixtures on both sides: capture recurses, restore rebuilds the tree. [[vst3-backs-up-as-nothing]] |
| Elevation for tier 4 restore only; tiers 1–3 admin-free | ✅ | Reported as *needs elevation* rather than an access-denied trace, and it never fails the restore. The shell asks for it through an opt-in row and a real UAC prompt; declining is error 13, neutral, and costs nothing |
| Version drift flagged on restore | ✅ | Phase 6 §5 — only when both versions are known |
| Licences are never captured, and the UI says so | ✅ | The Settings dialog's first plain-language note |

## §10 · Store and GUI

| Requirement | Status | Where |
|---|---|---|
| Store layout, machine-generated directory names | ✅ | `<store>/2026-08-15T2307-a3f81c/` |
| Display name in `manifest.json`, never in a path | ✅ | Rename is a metadata write |
| A guard asserting "we wrote this and it still matches" | ✅ | `SnapshotGuard` |
| `presets/` and `plugins/` subdirectories | ✅ | Plus `wavelink-backups/` for tier 1's other half |
| WPF, list-as-the-app, settings pane | ✅ | Phase 5, four screens and thirteen state groups |
| Always snapshot before restoring | ✅ | Unconditional, with no opt-out |

## §11 · Shipping publicly

| Requirement | Status | Where |
|---|---|---|
| Health check relative to the user's own history, never an absolute threshold | ✅ | Every row compares against that user's snapshots |
| Glob the package family; never hard-code the store identity | ✅ | `SettingsLocator` |
| Resolve plugins from `FilePath`; standard dirs are fallback only | ✅ | `PluginReferences`; phase 6 §4 keeps the rule for capture |
| `Environment.GetFolderPath` for **Documents** too, never `%USERPROFILE%\Documents` | ✅ | `TierCapture.SystemDocuments`. The reference rig has it redirected to another drive; a composed path finds an empty folder and reports the user has no presets |
| Never gate on a Wave Link version; record it and warn on mismatch | ✅ | Recorded per snapshot |
| `Environment.GetFolderPath`, never a composed `%LOCALAPPDATA%` | ✅ | `SnapshotStore.DefaultStorePath` |
| Backups labelled machine-local | ✅ | The Settings dialog's second plain-language note |
| **Privacy: "copy diagnostics" with redaction, nothing auto-uploaded** | 🔨 | **Phase 7 §1 — the 1.0 gate** |
| Non-MSIX installs: a diagnostic and a way through | 🔨 | Escape hatch ✅ (`--settings-path`); the UI door is **phase 7 §5** |
| macOS: scope the repo Windows-only in the README | 🔨 | Phase 7 §7, [[ADR-008]] |

---

## What the spec does not cover, and we built anyway

Recorded because a coverage table read on its own would suggest these were undesigned.

- **The trash** — a two-stage delete, because permanent deletion of a backup on one click is a
  failure mode the spec never considered. [technical-debt.md](../technical-debt.md) §7.1
- **Damaged backups are immortal to the pruner** — a corrupt snapshot must never push a good one
  out. §7.2
- **The watcher never queues** — §7.3
- **The whole visual design** — thirteen state groups, both themes, high contrast. The spec says
  "WPF" and stops there.
