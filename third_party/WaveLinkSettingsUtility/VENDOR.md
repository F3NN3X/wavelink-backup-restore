# Vendored snapshot — voltybat/WaveLinkSettingsUtility

**Source:** https://github.com/voltybat/WaveLinkSettingsUtility
**Commit:** `211a18c4af4da9c05ad8d08de6e50740ccaa933f`
**Committed:** 2026-07-18
**Vendored:** 2026-08-16
**Licence:** MIT — see `LICENSE` beside this file, reproduced verbatim.

## What this is, and what it is not

A **verbatim snapshot**, taken for the record and for attribution. It is **not part of the
build** — no project here appears in `WaveLinkBackup.slnx`, and nothing in `src/` references
it.

`WaveLinkBackup.Core` was written by **porting behaviour** from this code into the shape
described in [[ADR-004]]. Keeping
the original beside the port is what makes the port auditable: anyone can diff the behaviour
claims against the source they came from, and the MIT attribution points at something concrete
rather than a URL that may move.

Intake was chosen as a vendored snapshot with **no shared git history**; see
[[ADR-002]]. Future upstream fixes are hand-ported.

## Baseline

Upstream's own test suite was run at this commit before vendoring:

```
Passed!  -  Failed: 0, Passed: 40, Skipped: 0, Total: 40  (net10.0)
```

That is the phase-0 exit criterion — *upstream's tests pass unchanged* — satisfied at the
snapshot SHA. Note the criterion could not be met the way phase 0 originally worded it: with
the snapshot excluded from our solution, there is no upstream csproj in our build to run.
Verifying at the SHA before vendoring is the honest equivalent, and phase 0 records the
change.

## What was taken

| Upstream | Ported to | Notes |
|---|---|---|
| `SettingsDiscovery` (`Infrastructure.cs`) | `Discovery/SettingsLocator.cs` | Glob, `Settings.json`-must-exist, refuse-to-guess. **Behaviour changed:** see below. |
| `IFileOperations` | `Abstractions/IFileSystem.cs` | Reshaped; `ReadSharedBytes` is a named method so callers cannot choose the wrong share mode. |
| `IWaveLinkProcess`, `ProcessControl` | `Process/` | **Extended** to cover `WavelinkSEService`. |
| `AppActivator` | `Process/WaveLinkProcess.LaunchByAppId` | `shell:AppsFolder\<family>!App` via `explorer.exe`. |
| `ReplaceBytesSafely` (`CleanerApplication.cs`) | `Io/SettingsWriter.cs` | Temp file in same directory → validate the temp → `File.Replace`. The validate-before-replace step is upstream's idea and worth keeping. |
| `JsonCleaner.Validate` / `GetInputs` | `Analysis/SettingsAnalysis.cs` | Rewritten as a pure function over bytes. |
| Seam-interface discipline | throughout | The reason upstream has 40 tests against ~48 KB of code. |

**Not ported (yet):** `AudioEndpoints.cs` (Core Audio COM interop) — needed for "is this input
dead", which is a repair feature, not phase 1. `JsonCleaner.RelinkHardwareChannel` and
`RewriteReferences` are the real prior art for foreign-key rewriting and stay here until a
repair feature exists to need them.

## Deliberate divergences

Each of these is a place the port does **not** match upstream, recorded so the difference reads
as a decision rather than an accident.

1. **Backup location.** Upstream writes `Settings.json.backup-<ts>` beside `Settings.json`,
   inside `LocalState`, which an MSIX package reset deletes wholesale. Ours goes outside. See
   [[ADR-003]]. *(Phase 2 — not yet built.)*
2. **Identity.** Upstream identifies backups with the filename regex
   `^Settings\.json\.backup-\d{8}-\d{9}$`. Ours uses `manifest.json`. *(Phase 2.)*
3. **`--settings-path` semantics.** Upstream requires the override to *match a discovered
   candidate* — it must still be `Settings.json` inside an `Elgato.WaveLink_*` package. Ours
   **bypasses discovery entirely**, because that is the only thing that helps a user whose
   install we cannot find ([technical-debt.md](../../_docs/technical-debt.md) §2.2).
4. **Both processes.** Upstream's `FindGuiProcess` only ever looks for `Elgato.WaveLink`;
   `WavelinkSEService` is never closed. Recorded as audit finding 6.
5. **Shared-mode reads.** Upstream calls `File.ReadAllBytes`, which fails while Wave Link is
   running ([[capture-fails-while-wave-link-is-running]]). Ours uses
   `FileShare.ReadWrite | FileShare.Delete`.
6. **Errors.** Upstream throws `InvalidOperationException` / `SettingsFormatException`. Ours
   returns `Result<T>` for expected failures and reserves exceptions for bugs.
7. **Encoder.** Upstream's default encoder is **correct** and is kept — audit finding 2 was
   withdrawn after measurement. See [[every-snapshot-differs-with-no-real-change]].

## Obligations

MIT attribution is satisfied by `LICENSE` here, the dual copyright line in the repo-root
`LICENSE`, and the credit in the root `README.md`.

**Offer upstream:** duplicate-key detection (audit finding 3) and the two-process shutdown
(finding 6). Both are small and improve their tool without changing what it is.
**Do not offer** the encoder change — it was withdrawn, and the patch would introduce a bug.
