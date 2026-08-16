---
title: "Phase 2 — Snapshot store"
status: published
created: 2026-08-16
updated: 2026-08-16
related_adrs: [ADR-003]
tags: [dev-phase]
---

# Phase 2 — Snapshot store

**Status:** Not started — **next**.
**Entry criteria:** phase 1 complete. ✅ 2026-08-16.
**Exit criteria:** a snapshot can be written, listed, renamed, restored and deleted; every
restore takes a pre-restore snapshot first, automatically; and the manifest-hash guard refuses
a directory we did not write. All testable through `Core.Tests` with no GUI.

## Why this phase exists

**It is where the critical inherited defect is fixed.** Upstream writes backups *inside*
`LocalState`, which an MSIX package reset deletes wholesale — the backups are destroyed by
exactly the event you would want to recover from
([technical-debt.md](../technical-debt.md) §1.1). Everything else in this phase follows from
moving them out.

It is also the phase that makes the app usable in principle: after it, a person could back up
and restore from a CLI. Phases 3–5 make that pleasant and automatic.

## Scope

### In

- The store layout and `manifest.json` ([[ADR-003]]).
- Write, list, rename, delete a snapshot.
- **The assembled restore sequence** — the one phase 1 deliberately did not build.
- The automatic pre-restore snapshot.
- The manifest-hash guard replacing upstream's filename regex.
- `IClock`, introduced here because snapshot timestamps are the first real need for it.

### Out — and where it went instead

- Watcher, debounce, dedup-on-write, retention → **phase 3**. This phase writes a snapshot
  when *told* to; deciding when is phase 3's problem. `settingsSha256` is recorded now so
  dedup has something to compare, but nothing consults it yet.
- CLI verbs → **phase 4**. Tests drive the store directly.
- Tier 2–4 capture → **phase 6**. `manifest.tiers` records `["settings"]` and the shape allows
  more.
- Repairing a settings file → still not scoped.

## Work

Grouped by what each piece has to get right.

### 1 · `IClock`, and only now

Upstream carries `Func<DateTime> clock` and uses it for exactly one thing: naming backup
files. Phase 1 deferred it because no test would have exercised it. Snapshot `createdUtc` and
directory names need it, and a fake clock is what makes "two snapshots one second apart" a
test rather than a race.

### 2 · Store layout

```
<store>/                                  ← default %LOCALAPPDATA%\WaveLinkBackup
  2026-08-15T2307-a3f81c/
    manifest.json
    settings.json
    plugins.json      (phase 6)
    presets/          (phase 6)
    plugins/          (phase 6)
```

Directory names are **machine-generated** — timestamp plus a short hash. The display name
lives in the manifest and nowhere else, so renaming is a metadata write: no file moves, no
collisions, no sanitising user text into a path, and no breakage when someone types
`Mic chain 3/4"`.

**Store outside `LocalState`, always.** The default is `%LOCALAPPDATA%\WaveLinkBackup`,
resolved through `Environment.GetFolderPath`.

### 3 · `manifest.json`

Fields per [[ADR-003]]: `displayName`, `notes`, `createdUtc`, `trigger`, `settingsSha256`,
`waveLinkVersion`, `inputCount`, `inputNames`, `hasDuplicateKeys`, `tiers`.

Two constraints carried from phase 1:

- **Written with `Utf8JsonWriter`, read with `JsonDocument`.** No reflection-based
  `JsonSerializer` — the source-scan guard will fail the build, and that guard exists to keep
  NativeAOT open ([technical-debt.md](../technical-debt.md) §2.4).
- **`manifest.json` is a compatibility surface from day one.** The store outlives the
  application that wrote it, in a location the user chose and may sync or move. Version it
  now, not when it first hurts.

Most fields come straight from `SettingsAnalysisResult`, which phase 1 already produces.
`waveLinkVersion` is new and must be read from the package — the first question when a restore
fails is whether the config is bad or the validator changed.

### 4 · The manifest-hash guard

Replaces upstream's `ValidateManagedPath` filename regex. The assertion becomes *"this
directory contains a `manifest.json` we wrote, whose recorded hashes match its contents"*
rather than *"this filename matches a pattern"*.

Same protection against writing arbitrary bytes into a config file, with no constraint on
naming or location — which is the whole point, since the regex is what blocks custom names and
custom locations at once.

### 5 · The assembled restore sequence

Phase 1 built the primitives and proved each. This assembles them in the order from
[[restore-a-settings-file-safely]]:

1. Validate the source snapshot.
2. Compare its fingerprint with the live one — this is what the restore dialog will render.
3. **Take the pre-restore snapshot**, `trigger: preRestore`, named `Before restore`.
4. `CloseAndVerifyExited` — both processes.
5. `SettingsWriter.Write` — which re-checks the precondition itself.
6. `LaunchByAppId`, unless `CanRelaunch` is false.
7. Verify from the newest log.

**The pre-restore snapshot is automatic and unconditional**, never a checkbox. It is what makes
the destructive button safe to press, and a user in a hurry is exactly the user who would skip
it.

### 6 · Store operations

Write, list, rename, delete. Listing reads every manifest — trivial at this scale, and the
reason the manifest carries every field the eventual list view needs, so nothing has to open a
snapshot to render a row.

## Testing

Same discipline as phase 1: strict TDD, pure logic separated from IO, `FakeFileSystem`
extended rather than replaced.

**Tests that must exist**, because each pins something that has already gone wrong somewhere:

| Test | Pins |
|---|---|
| A snapshot survives deleting the entire `LocalState` directory | The critical defect, §1.1 |
| Renaming touches only `manifest.json` — no directory is moved | [[ADR-003]] |
| A display name containing `/`, `"` and a trailing space round-trips | Why names are not paths |
| Restore refuses a directory with no manifest, and one whose hash does not match | The guard |
| A restore takes a pre-restore snapshot **before** closing Wave Link | Ordering |
| A restore that fails at the write still leaves the pre-restore snapshot | Recovery |
| Two snapshots in the same second get distinct directories | Fake clock |
| A manifest from a future version is rejected with a clear message | Compatibility |

**Coverage target:** ≥80%, weighted toward the store and restore logic rather than adapters —
as in phase 1, chase the risk rather than the number.

## Risks

| Risk | Early signal | Response |
|---|---|---|
| Restore orchestration drifts back into `SettingsWriter` | `SettingsWriter` gaining a process-closing call | The precondition pattern stays; orchestration is a separate type ([[preconditions-inside-the-operation]]) |
| Manifest schema churn after the first real snapshot | A field added without a version bump | Version from the first write |
| The store grows a second source of truth | An index or cache file appearing | A self-describing directory cannot disagree with itself; an index can |
| Phase 2 absorbs dedup because the hash is right there | Retention logic in the store writer | `settingsSha256` is *recorded* here and *consulted* in phase 3 |
| Pre-restore becomes an option | A boolean parameter on restore | It is unconditional. If a caller can skip it, someone will |

## References

- [[ADR-003]] — the store shape and why the fork's model had to go
- [Phase 2 design](../plans/2026-08-16-phase-2-store-design.md) — the detailed design
- `SPEC.md` §4, §7·1, §10 · [technical-debt.md](../technical-debt.md) §1.1
- [[restore-a-settings-file-safely]] · [[newest-backup-is-the-broken-one]]
