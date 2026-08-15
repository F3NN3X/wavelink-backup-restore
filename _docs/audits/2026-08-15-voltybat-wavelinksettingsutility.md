---
title: "Audit: voltybat/WaveLinkSettingsUtility"
status: published
created: 2026-08-16
updated: 2026-08-16
related_adrs: [ADR-002, ADR-003, ADR-004, ADR-007]
tags: [audit, upstream]
---

# Audit: `voltybat/WaveLinkSettingsUtility`

**Audited:** 2026-08-15 · **Subject:** `main`, pushed 2026-07-19 · **Licence:** MIT
**Method:** source read directly, **not** inferred from the README. Nothing below was
reproduced at runtime — every finding is a reading of code. Two are flagged where that
distinction changes what you should do.

**Verdict: fork it.** ~60 KB of source with ~30 KB of tests, solving several problems that are
tedious to get right and boring to get wrong. Five defects, one of them critical and structural.
Decision recorded in [[ADR-002]].

---

## What it is

A C# / .NET 10 console utility for backing up and restoring Wave Link settings. Small, clean,
well-tested, and deliberately minimal. It is **a manual tool, not a safety net** — which is
finding 4 below, and also the reason this project exists rather than a pull request.

---

## Take these outright

| Component | Why it is worth having |
|---|---|
| **`SettingsDiscovery`** | Globs `Elgato.WaveLink_*` under `Packages` and requires `Settings.json` to exist — so it **never touches the stale vendor folder** that catches everyone ([[backup-succeeds-but-protects-nothing]]). Also handles multiple installed packages, refusing to guess and demanding `--settings-path`. That refusal is a design decision worth preserving verbatim. |
| **`WindowsAudioEndpointInspector`** | ~80 lines of hand-declared `[ComImport]` Core Audio interfaces — `IMMDeviceEnumerator`, `IMMDevice`, `IPropertyStore` — enumerating live endpoints. This is how you tell "this input is dead" from "this input is fine", and it is precisely the code nobody wants to write twice. |
| **Shutdown sequence** | Graceful close → 10 s timeout → kill tree → **assert not running** → write. The assertion is the part that matters, and it is easy to drop when reimplementing ([[restored-settings-revert-seconds-later]]). |
| **Atomic write** | Temp file then `File.Replace(temp, path, backupPath)`. Atomic on NTFS, rollback copy produced in the same operation. |
| **`ValidateManagedPath`** | Restore refuses unless the source matches `^Settings\.json\.backup-\d{8}-\d{9}$` beside the target package. The *location* constraint has to go (finding 1), but **the instinct is right** — a mistyped path must not write arbitrary bytes into a config file. Keep the guard, rebuild it on manifest identity ([[ADR-003]]). |
| **Seam interfaces** | `IFileOperations`, `IWaveLinkProcess`, `Func<DateTime> clock`. The reason 60 KB of code carries 30 KB of tests. **Keep this shape** — it is the single most valuable thing being inherited, and the easiest to erode by accident ([[ADR-004]]). |

---

## Findings

### 1 · Backups live inside `LocalState` — critical, structural

`NewBackupPath` writes `Settings.json.backup-<ts>` beside `Settings.json`. `ManagedBackups`
enumerates only that directory. `ValidateManagedPath` **enforces** that location.

Resetting or uninstalling the MSIX package deletes `LocalState` wholesale — **every backup
with it**. The tool's backups are destroyed by exactly the event you would most want to
recover from.

**Severity:** critical. This is the single change the fork must make.

**Entanglement — the part that makes this more than a path change.** Three components encode
the same assumption, and identity is by *filename*. Changing the location alone leaves restore
refusing its own files, because the regex no longer matches. All three change together.

**Resolution:** [[ADR-003]] — store outside `LocalState`, identity moves to `manifest.json`,
the guard becomes "this directory contains a manifest we wrote whose hashes match".
**Phase 2.**

### 2 · `Serialize` uses the default JSON encoder

```csharp
JsonSerializer.SerializeToUtf8Bytes(root, new JsonSerializerOptions { WriteIndented = true });
```

No `Encoder` set, so the default escapes `+` and `/` to `\uXXXX`. Those characters are
everywhere in the base64 `ParameterState` inside `AudioPluginConfigurations`.

Output stays valid and Wave Link accepts it — which is why this survives unnoticed. The cost
is that every save rewrites bytes it never intended to touch, and diffs between snapshots
become useless.

**Severity:** low impact, two-line fix, high annoyance if it ships.
**Fix:** `Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping`. **Phase 1.**
**Worth offering upstream** — it is small, self-contained and benefits them equally.
See [[every-snapshot-differs-with-no-real-change]].

### 3 · No duplicate-key detection

`Validate()` asserts only that `MixerConfiguration.InputSettings` is an object. **The defect
that motivated this entire project passes unnoticed** — case-insensitively duplicated keys,
which Wave Link's own `SettingsJsonNormalizer` rejects outright, resetting to defaults.

**Severity:** high. It is the original incident ([[file-parses-but-wave-link-resets]]).

> **Open question, and it must be settled empirically before relying on either answer.**
> Upstream's edit path uses `JsonNode.Parse`. Does it collapse duplicates?
>
> - **If it collapses them:** a round-trip **silently drops data**.
> - **If it does not:** duplicates **survive into the written file** and the app rejects it.
>
> Opposite failures, both bad, and the code reads the same either way. Ten-minute check with a
> `{"A":1,"a":2}` fixture. Tracked in [technical-debt.md](../technical-debt.md) §2.1.

**Fix regardless of that answer:** add the `JsonDocument` tree walk. **Phase 1.**

### 4 · It is a manual tool, not a safety net

Backups happen only when invoked. No watcher, no schedule, no dedup — so repeated runs write
identical copies of a file Wave Link rewrites on every launch.

**Not a defect upstream.** It is a different product, doing what it says. **This is the gap
this app exists to fill**, and the substantive justification for forking rather than simply
using it. Resolution: [[ADR-007]]. **Phase 3.**

### 5 · Runtime dependency

The csproj sets `PublishSingleFile` with `SelfContained=false`, so the .NET 10 runtime must be
installed despite the single-file output. A user who downloads one `.exe` and double-clicks it
gets an error rather than an app.

**Severity:** moderate — a first-run experience problem, which is the worst kind.
**Decision owed, not made:** self-contained (~70 MB) / framework-dependent / NativeAOT
(~10–15 MB, CLI only — WPF does not support AOT, and `[ComImport]` interop under AOT needs
verification). [[ADR-004]] preserves the AOT option by keeping the CLI in its own project.
**Phase 7.**

---

## Summary

| # | Finding | Severity | Resolution | Phase |
|---|---|---|---|---|
| 1 | Backups inside `LocalState` | **Critical** | [[ADR-003]] | 2 |
| 2 | Default JSON encoder mangles base64 | Low | Set `UnsafeRelaxedJsonEscaping` | 1 |
| 3 | No duplicate-key detection | **High** | `JsonDocument` walk; §2.1 first | 1 |
| 4 | Manual only — no watcher or dedup | By design | [[ADR-007]] | 3 |
| 5 | Runtime dependency despite single-file | Moderate | Open | 7 |

Ongoing status lives in [technical-debt.md](../technical-debt.md) §1. When a finding is
resolved, record the resolution **here as well** — this audit is the reconciliation point if
the two codebases are ever compared.

## Obligations

MIT requires attribution. Preserve the licence and copyright notice, name the upstream in the
root `README.md`, and offer findings 2 and 3 back as patches — both are small, self-contained
and improve upstream without changing what it is.

## References

- `SPEC.md` §7, §8
- [[ADR-002]] · [[ADR-003]] · [[ADR-004]] · [[ADR-007]]
