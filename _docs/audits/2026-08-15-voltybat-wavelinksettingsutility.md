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
**Method:** source read directly, **not** inferred from the README. Nothing was reproduced at
runtime — every finding was a reading of code.

> **Partially re-verified 2026-08-16.** Findings 2 and 3 were tested against a live
> `Settings.json`. **Finding 2 was wrong and is withdrawn**; its proposed fix would have caused
> the problem it described. Finding 3's open question is answered, and was mis-framed. One
> claim in finding 5 is now in doubt — upstream's README contradicts it. The unreproduced
> findings are the ones that failed, which is the whole argument for the provenance labels.

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

### 2 · ~~`Serialize` uses the default JSON encoder~~ — **WITHDRAWN 2026-08-16**

> **This finding was wrong, and the fix it proposed would have caused the problem it
> described.** Retained rather than deleted, because the reasoning is a trap a future reader
> will fall into independently.

**What was claimed.** Upstream writes:

```csharp
JsonSerializer.SerializeToUtf8Bytes(root, new JsonSerializerOptions { WriteIndented = true });
```

No `Encoder` is set, so the default escapes `+` in the base64 `ParameterState` values inside
`AudioPluginConfigurations`. The audit concluded that every save therefore rewrites bytes it
never intended to touch, making snapshot diffs useless, and recommended
`JavaScriptEncoder.UnsafeRelaxedJsonEscaping`.

**What is actually true**, measured against the live `Settings.json` on 2026-08-16:

```
original file                          43,052 bytes
round-trip, default encoder            43,052 bytes   identical = True
round-trip, UnsafeRelaxedJsonEscaping   41,641 bytes   identical = False
```

**Wave Link itself writes with the default encoder.** Its file already contains those escapes.
Upstream's call reproduces Wave Link's output byte for byte — including indentation — and is
**correct as written**. Applying the recommended fix would have un-escaped 13 sequences,
shrunk the file by 1,411 bytes, and made every snapshot differ from the app's own output:
precisely the churn the finding was worried about.

Two secondary errors in the original claim: the default escapes only `+` (not `/`), and
"everywhere" overstates it — 13 occurrences in a 43 KB file with an 11-effect chain.

**Severity:** none. No action. **Do not offer this upstream.**
**Where this leaves the real rule:** capture copies bytes and never re-serializes at all, which
was always the correct design and is now better supported.
See [[every-snapshot-differs-with-no-real-change]].

**Why the audit got it wrong:** the finding was read off source and reasoned about from the
`System.Text.Json` documentation, never executed against a real file — and the missing step
was checking what the *other* program's serializer does. A claim about round-trip fidelity is
a claim about two serializers agreeing, and only one of them was examined.

### 3 · No duplicate-key detection

`Validate()` asserts only that `MixerConfiguration.InputSettings` is an object. **The defect
that motivated this entire project passes unnoticed** — case-insensitively duplicated keys,
which Wave Link's own `SettingsJsonNormalizer` rejects outright, resetting to defaults.

**Severity:** high. It is the original incident ([[file-parses-but-wave-link-resets]]).

> **~~Open question~~ — ANSWERED 2026-08-16, and it was mis-framed.** The question was whether
> `JsonNode.Parse` collapses duplicates. Neither offered answer was right: **it depends on
> which kind of duplicate.**
>
> | Input | `JsonDocument` | `JsonNode.Parse` |
> |---|---|---|
> | `{"A":1,"a":2}` — case-insensitive, *the actual defect* | preserves both | **preserves both**, round-trips intact |
> | `{"A":1,"A":2}` — exact duplicate | preserves both; `GetProperty` returns the **last** | **throws `ArgumentException`** |
>
> **No silent data loss either way** — which was the feared outcome, and it is not real. But
> upstream's edit path will **hard-crash** on a file with exact duplicates, surfacing a
> dictionary's `ArgumentException` rather than "this settings file is malformed". That is a
> new, smaller finding this question uncovered.

**Fix, unchanged by that answer:** add the `JsonDocument` tree walk, since `JsonNode`'s
preservation of case-insensitive duplicates means they reach the written file and Wave Link
rejects it. **Additionally** catch `ArgumentException` around any `JsonNode.Parse` of an
untrusted settings file. **Phase 1.**

### 4 · It is a manual tool, not a safety net

Backups happen only when invoked. No watcher, no schedule, no dedup — so repeated runs write
identical copies of a file Wave Link rewrites on every launch.

**Not a defect upstream.** It is a different product, doing what it says. **This is the gap
this app exists to fill**, and the substantive justification for forking rather than simply
using it. Resolution: [[ADR-007]]. **Phase 3.**

### 5 · ~~Runtime dependency~~ — **RESOLVED 2026-08-16. The finding was incomplete, not wrong.**

The csproj does set `PublishSingleFile` with `SelfContained=false` — read correctly. But
`.github/workflows/release.yml` publishes with `--self-contained true`, overriding it. **The
shipped artifact needs no runtime; the csproj alone would suggest otherwise.** The README and
the csproj never actually contradicted each other; the audit had only read one of them.

**Method failure worth naming:** the finding was about what *users receive*, and it was
answered by reading the project file rather than the release pipeline. Build configuration is
not a single file.

**Residual issue, much smaller:** csproj and pipeline disagree, so a local `dotnet publish`
yields a different artifact from CI's. **Our position:** `WaveLinkBackup.Cli` sets
`SelfContained=true` in the csproj so they cannot. **No action owed upstream** beyond
optionally suggesting the same.

### 6 · `WavelinkSEService` is never closed — **NEW, found at intake 2026-08-16**

`ProcessControl.FindGuiProcess`:

```csharp
Process.GetProcessesByName("Elgato.WaveLink").FirstOrDefault()
```

Only the GUI process. `WavelinkSEService` is never enumerated, closed, or checked — so the
`if (process.IsRunning) throw` assertion repeated at five call sites can pass **with half of
Wave Link still running**, and a write can still race the service's flush. `SPEC.md` §4 is
explicit that both must close.

`FirstOrDefault()` is a second, smaller gap: multiple GUI processes would leave the rest alive.

**Severity:** moderate. It undermines the one guarantee the shutdown sequence exists to give,
and it is invisible — the sequence looks correct and reports success.
**Fixed here:** `WaveLinkProcess` covers both names and returns `WaveLinkStillRunning` naming
whichever survived. Covered by `WaveLinkProcessTests` and `SettingsWriterTests`.
**Worth offering upstream** — small, self-contained, and strictly a correctness fix.

---

## Summary

| # | Finding | Severity | Resolution | Phase |
|---|---|---|---|---|
| 1 | Backups inside `LocalState` | **Critical** | [[ADR-003]] | 2 |
| 2 | ~~Default JSON encoder mangles base64~~ | ~~Low~~ **Withdrawn** | None — upstream is correct | — |
| 3 | No duplicate-key detection | **High** | `JsonDocument` walk | 1 |
| 3b | `JsonNode.Parse` throws on exact duplicates | Low | Catch, report as malformed | 1 |
| 4 | Manual only — no watcher or dedup | By design | [[ADR-007]] | 3 |
| 5 | ~~Runtime dependency~~ | ~~Moderate~~ **Resolved** | Release workflow overrides the csproj | — |
| 6 | `WavelinkSEService` never closed | Moderate | **Fixed in phase 1**; offer upstream | 1 ✅ |

**Scorecard after phase 1.** Of six findings: one critical and still open (finding 1, which is
phase 2's whole reason for existing), one withdrawn as wrong, one resolved as incomplete, two
fixed, one by-design. **Two of the five original findings did not survive contact with a
running system** — both of them the ones marked *read, not reproduced*.

Ongoing status lives in [technical-debt.md](../technical-debt.md) §1. When a finding is
resolved, record the resolution **here as well** — this audit is the reconciliation point if
the two codebases are ever compared.

## Obligations

MIT requires attribution. Preserve the licence and copyright notice, and name the upstream in
the root `README.md`. **Done** — `LICENSE` carries their copyright line verbatim.

**Offer finding 3 back** as a patch: it is small, self-contained, and improves upstream without
changing what it is. **Do not offer finding 2** — it was withdrawn, and the patch would have
introduced a bug.

## References

- `SPEC.md` §7, §8
- [[ADR-002]] · [[ADR-003]] · [[ADR-004]] · [[ADR-007]]
