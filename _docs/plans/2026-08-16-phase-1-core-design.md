---
title: "Phase 1 Core — Design"
status: review
created: 2026-08-16
updated: 2026-08-16
related_adrs: [ADR-001, ADR-002, ADR-004]
tags: [plan, design, core]
---

# Phase 1 Core — Design

**Status:** awaiting review. Supersedes nothing; implements
[phase-1-core.md](../dev-phases/phase-1-core.md).

`WaveLinkBackup.Core` is the library everything else calls. The snapshot store, the watcher,
both shells and every plugin tier sit on four capabilities: **find the file**, **decide
whether it is any good**, **describe it cheaply**, and **replace it without losing anything**.

Four of the project's nine gotchas live entirely inside this phase — the decoy folder, the
duplicate keys, the file lock and the shutdown flush race. Getting it wrong is not a bug in
one feature; it is a bug in the premise.

> **This design rests on measurements taken 2026-08-16**, not on `SPEC.md`'s recommendations.
> Three of those recommendations were tested and did not survive; see the Corrections block at
> the top of [SPEC.md](../SPEC.md) and the [probe session](../sessions/2026-08-16-phase-1-probe.md).
> Where this document and the spec body disagree, this document is right.

---

## 1 · Shape

**Functional core, imperative shell.** Everything that can be pure is pure; all IO sits behind
two seams.

The reason is not aesthetic. Phase 1's work splits unevenly:

| Work | Nature |
|---|---|
| Discovery, reading, atomic write, process lifecycle | IO — needs fakes |
| Validation, duplicate-key walk, health fingerprint, log parsing | **Pure** — bytes in, records out |

The pure half is where the dangerous bugs live, and pure functions make each test two lines
with no setup. Under strict TDD that is the difference between twelve duplicate-key cases and
three.

```
WaveLinkBackup.Core/
├── Analysis/            ← PURE. static. no seams, no IO, no async.
│   ├── SettingsAnalysis.cs      static Analyse(bytes) → SettingsAnalysisResult
│   ├── DuplicateKeyScanner.cs   the JsonDocument tree walk
│   ├── HealthFingerprint.cs     record: InputCount, InputNames, SizeBytes, Sha256
│   ├── ValidationReport.cs      record: findings, not errors
│   └── LogAnalysis.cs           Verify(logText) → RestoreVerdict
├── Discovery/
│   ├── SettingsLocation.cs      record: SettingsPath, PackageFamily, LocalState, LogsDir
│   └── SettingsLocator.cs
├── Io/
│   ├── SettingsReader.cs        the ONLY shared-mode read
│   ├── SettingsWriter.cs        atomic replace, exit-verified
│   └── SettingsInspector.cs     composes locate → read → analyse, owns retry-once
├── Process/
│   ├── IWaveLinkProcess.cs
│   └── WaveLinkProcess.cs
├── Abstractions/
│   └── IFileSystem.cs
└── Results/
    ├── Result.cs
    └── CoreError.cs
```

**`Analysis/` is the load-bearing boundary.** No constructors, no dependencies, no `async`. It
takes bytes and returns records. It **cannot write a file even by accident** — which is how
"capture is a byte copy" ([[every-snapshot-differs-with-no-real-change]]) stops being a
convention someone has to remember and becomes a property of the type system.

### Two seams, not three

Upstream carries `IFileOperations`, `IWaveLinkProcess` and `Func<DateTime> clock`. We take the
first two and **defer the clock to phase 2**.

Nothing in phase 1 depends on wall-clock time. The 10-second close timeout and the
retry-once-on-torn-read are both driven through the `IWaveLinkProcess` and `IFileSystem`
fakes. Phase 2's snapshot timestamps genuinely need a clock; adding it there costs nothing,
and adding it now means a seam with no test exercising it.

`IFileSystem` exposes **`ReadSharedBytes` as a named method**, not a general `Open`. Callers
cannot get the share mode wrong because they never choose it.

---

## 2 · Error model

A hand-rolled `Result<T>` — no external dependency — over a sealed error hierarchy:

```csharp
public abstract record CoreError(string Message);

public sealed record WaveLinkNotInstalled()                       : CoreError(…);
public sealed record MultiplePackagesFound(string[] Candidates)   : CoreError(…);
public sealed record SettingsUnreadable(string Path, string Why)  : CoreError(…);
public sealed record MalformedSettings(string Detail)             : CoreError(…);
public sealed record WaveLinkStillRunning()                       : CoreError(…);
```

**Expected failures return a `Result`. Genuine faults throw.** A GUI has to render every
failure as a message; catch-and-hope at each UI boundary is how error handling rots.

### The distinction that carries the most weight

**A validation finding is not an error.**

| Situation | Result |
|---|---|
| Cannot parse at all | **failure** — `MalformedSettings` |
| Parses, but has case-insensitive duplicate keys | **success**, `ValidationReport.HasCaseInsensitiveDuplicates == true` |
| Parses, collapsed to 2 inputs | **success**, the fingerprint says so |

This is what makes *"a suspect snapshot is still restorable"* fall out of the design instead of
being a rule someone has to remember. A suspect snapshot may be the only one there is
([[newest-backup-is-the-broken-one]]).

`MalformedSettings` is also where audit finding 3b is handled: `JsonNode.Parse` throws
`ArgumentException` on exact duplicate keys, and unhandled the user sees *"An item with the
same key has already been added. Key: A"*. Catch it at the boundary and translate.

---

## 3 · The read pipeline

```csharp
Result<SettingsLocation>  loc   = locator.Locate();                    // IFileSystem
Result<byte[]>            bytes = reader.Read(loc.Value.SettingsPath); // shared mode
Result<SettingsAnalysisResult> a = SettingsAnalysis.Analyse(bytes.Value); // pure
```

Naming, to avoid the obvious collision: **`SettingsAnalysis`** is the static class,
**`Analyse`** the method, **`SettingsAnalysisResult`** the record it returns — carrying the
`ValidationReport`, the `HealthFingerprint` and the SHA-256 together.

`Analyse` parses **once**. Two walks over one `JsonDocument`: the duplicate scan needs the
whole tree, the fingerprint needs `MixerConfiguration.InputSettings`. It returns a `Result`
because parsing can fail; the report inside it describes a file that parsed.

`SettingsInspector` is what real callers use — it chains these three, unwraps at each step and
short-circuits on the first error, so no caller writes the `.Value` accesses shown above.

### Discovery

Port upstream's `SettingsDiscovery` behaviour; it is right, and it is one of the main reasons
to fork ([[backup-succeeds-but-protects-nothing]]).

- Glob `Elgato.WaveLink_*` under `Packages` — never the hard-coded family name.
- Require `Settings.json` to exist. That is what disqualifies a stale package directory.
- **Zero matches** → `WaveLinkNotInstalled`. **More than one** → `MultiplePackagesFound`.
  It never picks.
- `Environment.GetFolderPath`, never a composed `%LOCALAPPDATA%` string — the path is
  redirected on some corporate and OneDrive setups.

An **explicit settings-path override** bypasses the glob entirely (still checking the file
exists). This is the escape hatch for both the multiple-package case and the untested
non-MSIX case ([technical-debt.md](../technical-debt.md) §2.2) — a user we cannot auto-locate
gets a way forward, not a dead end.

### Reading — shared mode, always

```csharp
using var fs = new FileStream(path, FileMode.Open, FileAccess.Read,
                              FileShare.ReadWrite | FileShare.Delete);
```

`Settings.json` is **locked while Wave Link runs**, which is when most captures happen.
`File.ReadAllBytes` fails with *"being used by another process"*
([[capture-fails-while-wave-link-is-running]]). `FileShare.Delete` additionally tolerates the
file being replaced underneath us, which is exactly what Wave Link's own atomic-save does.

### Retry-once, and only here

A single read is **not atomic** against Wave Link's save, so a capture can catch a torn file.
`SettingsInspector` owns the rule:

- Parse failure on the **first** read → re-read once.
- Parse failure on the **second** → `MalformedSettings`.

This is the one place a retry is the right answer. It is deliberately not a general backoff —
a retry loop around the *lock* failure would turn an immediate, clearly-worded error into a
slow one reported as a timeout.

### No re-serialization, no reflection

**Capture hashes the source bytes and writes the source bytes.** Parsing exists for validation
and the fingerprint; its output is metadata, never a file.

> **Do not set `UnsafeRelaxedJsonEscaping`.** Wave Link writes with the **default** encoder;
> a default round-trip reproduces its file byte for byte (43,052 → 43,052). The relaxed
> encoder shrinks it to 41,641 bytes and makes every snapshot differ from the app's own
> output — the exact churn it was meant to prevent. Upstream is correct as written; audit
> finding 2 is withdrawn.

**No reflection-based `JsonSerializer` in `Core`.** Use `JsonDocument`, `JsonNode` and
`Utf8JsonWriter`. This keeps NativeAOT open at zero cost
([technical-debt.md](../technical-debt.md) §2.4).

---

## 4 · The write path — primitives, not the sequence

Phase 1 delivers four pieces:

1. **`CloseAndVerifyExited()`** — graceful close, 10-second timeout, kill tree on timeout,
   then **assert not running**. Both processes: `Elgato.WaveLink` *and* `WavelinkSEService`.
2. **`WriteAtomic()`** — temp file in the same directory (`File.Replace` needs the same
   volume), then `File.Replace(temp, target, rollback)`.
3. **`LaunchByAppId()`** — `shell:AppsFolder\<packageFamilyName>!App`. An MSIX app will not
   start from its `.exe` path.
4. **`LogAnalysis.Verify(text)`** — pure. Success is the *absence* of `Failed to parse
   settings file` plus the presence of `Applied saved friendly name`. Finding the newest log
   file is the IO half.

**`IsRunning` is an internal precondition of `WriteAtomic`, not a caller's duty.** It returns
`WaveLinkStillRunning` rather than trusting that step 1 happened. Enforced at the boundary,
the flush race ([[restored-settings-revert-seconds-later]]) cannot be reintroduced by a future
caller — and a fixed `Sleep` cannot be substituted for the assertion, because there is no
sleep to substitute for.

**The restore *sequence* is phase 2.** Validate → compare fingerprints → take the pre-restore
snapshot → close → write → relaunch → verify needs the snapshot store. Phase 1 builds the
parts and proves each one independently; [[restore-a-settings-file-safely]] is the order they
are assembled in.

---

## 5 · Testing

**Strict TDD: red, green, refactor.** Every behaviour gets a failing test first.

**Pure components first** — they need no fakes, so roughly 60% of the risk is covered before
any test infrastructure exists.

| # | Component | First failing test |
|---|---|---|
| 1 | `DuplicateKeyScanner` | `{"A":1,"a":2}` detected · `{"A":1,"A":2}` detected without throwing · clean file → none · nested objects and arrays |
| 2 | `HealthFingerprint` | 5 named inputs · the 2-input collapsed case · SHA matches a known vector |
| 3 | `LogAnalysis` | parse-failure line → fail · `Applied saved friendly name` → pass · both present |
| 4 | `Result` / `CoreError` | — |
| 5 | `SettingsLocator` | **a populated `%APPDATA%\Elgato\WaveLink` fixture must be ignored** · multi-package refusal · not-found · explicit override |
| 6 | `SettingsReader` | the fake asserts `FileShare.ReadWrite \| Delete` was requested · torn-file retry-once |
| 7 | `SettingsWriter` | fake reports `IsRunning == true` → write refuses · atomic replace produces the rollback |

Test 5 is the guard against a well-meaning "add a fallback location" change later, which is
exactly how the decoy comes back.

**Test framework:** whatever upstream uses, adopted at intake. Their tests must pass unchanged
first (phase 0's exit criterion), then be adapted to this shape — that adaptation is real work
and is scoped into this phase, not assumed free.

### Real-machine verification

Read-only integration tests against the live install — discovery, reading, validation,
fingerprinting — **tagged to skip when Wave Link is absent**, so CI stays green while the
development machine gets the stronger evidence.

**Anything that writes, kills a process, or restores runs only against temp-directory
fixtures.** No test touches the live configuration.

### Three CI guards

Each catches a bug that only surfaces far from where it was introduced:

| Guard | Why CI cannot catch it any other way |
|---|---|
| `Core` references neither `PresentationFramework` nor `System.Console` | Invisible until AOT or the test host fails, phases later |
| No `File.ReadAllBytes` / `File.ReadAllText` in `Core` | **CI has no Wave Link running**, so the lock bug cannot appear at runtime there |
| No reflection-based `JsonSerializer.Serialize<T>` in `Core` | Only fails under AOT, in phase 7 |

The second is a crude source scan and that is fine — it catches the reintroduction, which is
the realistic failure.

---

## 6 · Out of scope

Named explicitly, because a phase quietly absorbing the next one is the failure mode here.

| Out | Where it goes |
|---|---|
| Snapshot store, `manifest.json`, dedup | Phase 2 |
| The assembled restore sequence | Phase 2 |
| Watcher, debounce, retention | Phase 3 |
| CLI, GUI | Phases 4, 5 |
| `AudioPluginConfigurations` / tier 2–4 capture | Phase 6 |
| **Repairing** a settings file | Not scoped at all — needs the foreign-key handling from [[restored-backup-has-dead-channels]] and is a separate feature. Validation **reports**; it never modifies. |

---

## 7 · Exit criteria

Testable statements, not intentions:

1. A settings file can be **located** — with the decoy ignored, multiple packages refused, and
   an explicit override honoured.
2. It can be **read while Wave Link is running**, with one retry on a torn read.
3. It can be **validated** — both duplicate kinds detected, exact duplicates not throwing past
   the boundary — and **fingerprinted**, relative to a previous fingerprint rather than a
   constant.
4. It can be **atomically replaced**, with Wave Link's exit verified *inside* the write.
5. A restore can be **verified from the log**.
6. **No code path in `Core` re-serializes a settings file it is only storing**, enforced by
   test and by CI guard.
7. All three CI guards pass; upstream's adapted tests pass.

## References

- [phase-1-core.md](../dev-phases/phase-1-core.md) · [SPEC.md](../SPEC.md) §1, §3, §4, §5
- [[ADR-001]] · [[ADR-002]] · [[ADR-004]]
- [[backup-succeeds-but-protects-nothing]] · [[file-parses-but-wave-link-resets]] ·
  [[capture-fails-while-wave-link-is-running]] ·
  [[every-snapshot-differs-with-no-real-change]] · [[restored-settings-revert-seconds-later]]
- [Audit](../audits/2026-08-15-voltybat-wavelinksettingsutility.md) findings 1, 3, 3b
- [technical-debt.md](../technical-debt.md) §2.1, §2.2, §2.4
