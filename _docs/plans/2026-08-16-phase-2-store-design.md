---
title: "Phase 2 Snapshot Store — Design"
status: review
created: 2026-08-16
updated: 2026-08-16
related_adrs: [ADR-003, ADR-004]
tags: [plan, design, store]
---

# Phase 2 Snapshot Store — Design

**Status:** awaiting review. Implements
[dev-phases/phase-2-store.md](../dev-phases/phase-2-store.md).

Phase 1 built the primitives: find the file, decide whether it is any good, describe it,
replace it safely. Phase 2 gives them somewhere to put things, and assembles the one sequence
phase 1 deliberately left unassembled.

---

## 1 · Shape

Extends the phase-1 layout rather than reorganising it. The functional-core rule holds: manifest
serialisation and snapshot ranking are pure; only the store itself touches disk.

```
WaveLinkBackup.Core/
├── Analysis/            (phase 1, unchanged)
├── Discovery/           (phase 1, unchanged)
├── Io/                  (phase 1, unchanged)
├── Process/             (phase 1, unchanged)
├── Snapshots/                    ← new
│   ├── SnapshotManifest.cs       record — the compatibility surface
│   ├── ManifestSerializer.cs     PURE. Utf8JsonWriter out, JsonDocument in.
│   ├── SnapshotId.cs             machine-generated directory name
│   ├── Snapshot.cs               record — manifest + resolved paths
│   ├── SnapshotStore.cs          write / list / rename / delete
│   └── SnapshotGuard.cs          "did we write this, and does it still match?"
├── Restore/                      ← new
│   ├── RestorePlan.cs            PURE. what would change, for the dialog.
│   └── RestoreOrchestrator.cs    the assembled sequence
└── Abstractions/
    └── IClock.cs                 ← new, and only now
```

**`RestoreOrchestrator` is a separate type from `SettingsWriter`, deliberately.** The
precondition pattern says the write refuses to run while Wave Link is up
([[preconditions-inside-the-operation]]); it does not say the write should start closing
things. Keeping orchestration outside preserves the property that `SettingsWriter.Write` is
safe to call from anywhere.

### `IClock`, introduced here

Phase 1 deferred it because no test would have exercised it. Snapshot timestamps are the first
real need, and a fake clock is what turns "two snapshots in the same second" from a race into
a test.

```csharp
public interface IClock { DateTimeOffset UtcNow { get; } }
```

---

## 2 · Store layout and naming

```
<store>/                                  ← default %LOCALAPPDATA%\WaveLinkBackup
  2026-08-15T2307-a3f81c/
    manifest.json
    settings.json
```

`SnapshotId` = `{createdUtc:yyyy-MM-ddTHHmm}-{first 6 of settingsSha256}`.

**The display name is never in the path.** Renaming is a metadata write — no file moves, no
collisions, no sanitising, and no breakage from `Mic chain 3/4"`. The user's text is data;
the directory name is an implementation detail.

**Collisions.** Two snapshots in the same minute with the same content hash are the *same
snapshot*, so the collision is meaningful rather than accidental — phase 3's dedup will skip
the write entirely. Until then, a `-2` suffix, and a test that asserts both survive.

---

## 3 · `manifest.json`

```jsonc
{
  "schemaVersion": 1,
  "displayName": "Before 3.3 beta",
  "notes": "",
  "createdUtc": "2026-08-15T23:07:11Z",
  "trigger": "manual",              // manual | automatic | preRestore
  "settingsSha256": "a3f81c…",
  "waveLinkVersion": "3.3.0.4108",
  "inputCount": 5,
  "inputNames": ["Wave Mic 1", "Voice", "Browser", "Music", "System"],
  "effectCount": 17,
  "effectChannelCount": 4,
  "hasDuplicateKeys": false,
  "tiers": ["settings"],
  "files": { "settings.json": { "sha256": "a3f81c…", "sizeBytes": 43052 } }
}
```

Most of it comes straight from `SettingsAnalysisResult`, which phase 1 already produces.

**`schemaVersion` from the first write.** The store outlives the app that wrote it, in a
location the user chose and may sync, move or restore from a backup of their own. A manifest
with a higher version than we understand is **rejected with a clear message**, never
partially read.

**`files` is what makes the guard possible** — see §4.

**`waveLinkVersion` is the one genuinely new field** and needs reading from the package
manifest. When a restore fails, the first question is whether the config is bad or the
validator changed: 3.3.0.4108 Beta rejected a file 3.2.9 accepted.

**Serialisation is pure and reflection-free.** `ManifestSerializer.Write(manifest)` returns
bytes via `Utf8JsonWriter`; `Read(bytes)` returns `Result<SnapshotManifest>` via
`JsonDocument`. The source-scan guard from phase 1 will fail the build otherwise, which is
the guard doing its job.

---

## 4 · The guard

Upstream refuses to restore anything not matching
`^Settings\.json\.backup-\d{8}-\d{9}$` *beside* `Settings.json`. That regex is what blocks
custom names and custom locations simultaneously, so it cannot be kept — but the instinct
behind it is right: a mistyped path must never write arbitrary bytes into a config file.

`SnapshotGuard.Verify(directory)` asserts:

1. `manifest.json` exists and parses at a `schemaVersion` we understand;
2. every entry in `files` exists;
3. every recorded `sha256` **matches the bytes on disk**.

Same protection, no constraint on naming or location. It also catches something the regex
never could: a snapshot corrupted *after* it was written — by a failed sync, a bad disk, or a
user editing it.

---

## 5 · The assembled restore sequence

`RestoreOrchestrator.Restore(snapshot)`, in the order from
[[restore-a-settings-file-safely]] — each step justified there, so only the ordering
constraints are repeated:

| # | Step | Why here |
|---|---|---|
| 1 | `SnapshotGuard.Verify` | Restoring a file the app will reject looks identical to the snapshot being broken |
| 2 | Build a `RestorePlan` | Needs the live config, so it must happen **before** anything closes |
| 3 | **Pre-restore snapshot** | Wants the current state, and wants it even though it is the bad one |
| 4 | `CloseAndVerifyExited` | Both processes |
| 5 | `SettingsWriter.Write` | Re-checks the precondition itself |
| 6 | `LaunchByAppId` | Skipped when `CanRelaunch` is false — an explicit-path user is told to start it themselves |
| 7 | Verify from the newest log | A UI that looks correct can be a freshly generated default |

**Step 3 is unconditional and has no parameter.** If a caller can skip it, someone will — and
it is the entire reason the destructive button is safe.

**`RestorePlan` is pure**: two `HealthFingerprint`s in, a description of what changes out. It
is exactly what the restore dialog's *now vs. after* table renders, so building it here means
phase 5 has nothing to compute.

**Failure leaves the pre-restore snapshot.** A restore that dies at step 5 has already written
step 3, and `File.Replace` is atomic, so the config is either old or new — never half.

---

## 6 · Testing

Strict TDD. `FakeFileSystem` and `FakeWaveLinkProcess` extend; `FakeClock` is new.

| Test | Pins |
|---|---|
| A snapshot survives deleting all of `LocalState` | The critical defect, §1.1 |
| Rename touches only `manifest.json`; no directory moves | [[ADR-003]] |
| `Mic chain 3/4"` round-trips as a display name | Why names are not paths |
| Restore refuses a directory with no manifest | The guard |
| Restore refuses a manifest whose hash no longer matches | Post-write corruption |
| Restore refuses `schemaVersion` from the future, with a readable message | Compatibility |
| The pre-restore snapshot exists **before** the close is attempted | Ordering |
| A write failure still leaves the pre-restore snapshot | Recovery |
| Two snapshots in the same second get distinct directories | `FakeClock` |
| A restore with `CanRelaunch == false` succeeds and reports no relaunch | The non-MSIX path |

**Coverage ≥80%**, weighted toward store and restore logic. As in phase 1, chase the risk: the
number came out right there only after covering `FingerprintComparison`, which was the actual
hole.

---

## 7 · Out of scope

| Out | Where |
|---|---|
| Watcher, debounce, dedup-on-write, retention | Phase 3. `settingsSha256` is *recorded* here and *consulted* there. |
| CLI verbs | Phase 4 |
| Any UI | Phase 5. `RestorePlan` is built here so the dialog computes nothing. |
| Tier 2–4 capture | Phase 6. `tiers` is `["settings"]` and the shape allows more. |
| Repair | Still unscoped |

## 8 · Exit criteria

1. Write, list, rename and delete a snapshot; renaming moves no files.
2. A snapshot survives destruction of `LocalState`.
3. `SnapshotGuard` refuses a directory we did not write, one that no longer matches its
   hashes, and one from a future schema.
4. The full restore sequence works end to end, taking a pre-restore snapshot **first**, always.
5. A failed restore leaves a recoverable machine and a usable pre-restore snapshot.
6. ≥80% coverage; all phase-1 guards still pass.

## References

- [[ADR-003]] · [[ADR-004]] · [[ADR-007]]
- `SPEC.md` §4, §7·1, §10 — and the Corrections block at its top
- [[restore-a-settings-file-safely]] · [[preconditions-inside-the-operation]] ·
  [[pure-analysis-core]]
- [technical-debt.md](../technical-debt.md) §1.1, §2.4
