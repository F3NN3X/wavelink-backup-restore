# Changelog

All notable changes to Wave Link Backup are recorded here.

Format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/); versioning follows
[Semantic Versioning](https://semver.org/spec/v2.0.0.html).

**Pre-1.0, so the minor number carries breaking changes.** One release per completed phase of
[the roadmap](_docs/dev-phases/README.md): `0.1.0` is phase 1, `0.2.0` phase 2, and so on.
`1.0.0` is the first public release, which is gated on the privacy work rather than on feature
completeness — see the release checklist at the bottom.

The version in `Directory.Build.props` is the source of truth and matches the newest release
heading here.

> **This is the engineering changelog** — what shipped, per version, broad enough to become
> release notes. The documentation ecosystem has its own delta log in
> [`_docs/documentation-stats.md`](_docs/documentation-stats.md) → *Recent additions*. Same
> commit is fine; don't write the same entry in both.

---

## [Unreleased]

Nothing yet. Phase 5 — the WPF shell — is planned in
[dev-phases/phase-5-wpf.md](_docs/dev-phases/phase-5-wpf.md).

---

## [0.4.0] — 2026-08-16

**Phase 4: the CLI.** The first release you can actually run. `wlbackup` reaches every
capability the previous three phases built — which until now had no caller outside a test.

**308 tests passing.** Core 84.8% line / 81.6% branch; CLI 83.6% / 81.6%.

### Added

- **`wlbackup`** — eight verbs: `backup`, `list`, `restore`, `rename`, `delete`, `verify`,
  `prune`, `watch`.
  - `restore` prints what would change and **asks first**. `--yes` skips the question;
    a redirected stdin never counts as an answer.
  - `watch` backs up automatically until Ctrl+C, then takes one last backup on the way out.
  - `--json` for scripts, `--store` for a custom location, `--settings-path` for a Wave Link
    we cannot find on our own.
  - Distinct exit codes per failure, mapped from Core's error types, so scripts can branch.
- **Output never includes a device ID.** They embed hardware serial numbers; the list shows
  friendly channel names, which is what a person recognises anyway.

### Verified

- **NativeAOT works: a 3.2 MB single binary**, against 70.2 MB self-contained, with zero
  trim warnings — and it runs correctly against a real Wave Link install.
  [ADR-001](_docs/decisions/ADR-001-csharp-over-rust.md) estimated 10–15 MB and treated small
  binaries as the one thing Rust would have done better; that gap is now roughly closed, and
  the ADR records the measurement.
- The published single-file build discovers the package, writes a real backup, and lists it.

### Changed

- `SettingsInspector(IFileSystem)` **removed.** It resolved `%LOCALAPPDATA%` from the real
  environment, so tests wired against a fake filesystem quietly consulted the developer's
  machine — the same bug, twice. Replaced with `SettingsInspector.For(fileSystem, path)` and
  an explicit `SettingsLocator.SystemLocalAppData`.
- `BackupService` accepts an explicit settings path, so `--settings-path` reaches every verb
  rather than only `restore`.
- `SnapshotId.LooksLikeSnapshotId` **deleted** — three phases without a caller.

### Still open

- **`[ComImport]` under NativeAOT is not answered.** There is no COM interop in the codebase
  yet, so the AOT result above measures code that lacks the risky part. See
  [`technical-debt.md`](_docs/technical-debt.md) §2.4.

---

## [0.3.0] — 2026-08-16

**Phase 3: it backs up on its own.** The release that makes this a different product from the
tool it was forked from — everything before it, a person could have done by hand.

Still no user interface, and **nothing calls the watcher in production yet**: the host is a
shell, and the shell arrives in phase 4.

**235 tests passing, 84.9% line / 81.8% branch. The whole suite runs in about a second.**

### Added

- **`Automation/`** — the watcher, and the rules about when to use it.
  - `AutoBackupPolicy` — a **pure** function of three timestamps: ~60s debounce after the last
    write, at most one automatic snapshot an hour. Every timing case is an instant test.
  - `AutoBackupCoordinator` — wires watcher to policy to service. **Owns no timer**; the host
    calls `Tick()`, which is what keeps the timing tests instantaneous.
  - `BackupService` — manual and automatic capture, with dedup and its exception in one place.
  - `SnapshotRetention` — pure. Prunes automatic snapshots to a keep count, default 30.
  - `FileSystemSettingsWatcher` — filters on `LastWrite`, `CreationTime` **and** `FileName`,
    because Wave Link's atomic-save *replaces* the file rather than writing through it. A
    `LastWrite`-only filter would miss exactly the saves that matter most.
  - `BackupSettings` — store path, auto-backup toggle, keep count.
- **Capture on shutdown**, ignoring the debounce and rate limit. The original incident happened
  during an update, while the app was restarting.

### Behaviour worth knowing

- **A manual backup is never deduplicated.** You pressed a button; a new row appears. Automatic
  captures *are* deduplicated, because Wave Link rewrites its settings on every launch with
  near-identical bytes.
- **A skipped duplicate does not restart the hourly limit.** Otherwise a launch-time rewrite
  would mask a real change made moments later.
- **Backups you took yourself are never deleted**, at any keep count — nor are pre-restore
  backups.
- A dropped watcher event is a latency problem, not data loss: the next write, shutdown or
  launch reconciles by content hash.

### Verified

- The four claims in the Settings dialog's copy — *"waits a minute"*, *"at most one an hour"*,
  *"keep the last 30"*, *"backups you took yourself are never deleted"* — all match the
  implemented constants. Two are pinned directly by a test named for it.

---

## [0.2.0] — 2026-08-16

**Phase 2: the snapshot store.** Backups can now be written, listed, renamed, restored and
deleted. Still no automatic capture and still no user interface — every backup has to be asked
for, in code.

**186 tests passing, 83.0% line / 81.2% branch coverage.**

### Fixed

- **Backups no longer live inside `LocalState`.** This is the defect the fork existed to fix:
  upstream writes them beside `Settings.json`, inside the MSIX package directory, which
  resetting or uninstalling Wave Link deletes wholesale — destroying every backup along with
  the thing you wanted to recover from. Pinned by a test that deletes the entire `LocalState`
  directory and then verifies the snapshot still restores.
  ([`technical-debt.md`](_docs/technical-debt.md) §1.1, audit finding 1.)

### Added

- **`Snapshots/`** — the store, outside `LocalState`, defaulting to
  `%LOCALAPPDATA%\WaveLinkBackup`.
  - `manifest.json` holds identity, so **renaming is a metadata write** — no directory moves,
    nothing sanitised, and a backup called `Mic chain 3/4"` is just a string. Upstream's
    filename regex blocked custom names, custom locations and a cheap rename at once.
  - `SnapshotGuard` replaces `ValidateManagedPath`: it asserts *"we wrote this and it still
    matches its hashes"* rather than *"this filename fits a pattern"*. Same protection against
    a mistyped path, **plus** detection of a backup corrupted after it was written — by a
    failed sync, a bad disk, or a hand edit.
  - `schemaVersion` from the first write. A manifest from a newer version is refused with a
    readable message, never partially read.
- **`Restore/`** — the assembled sequence: verify, plan, **pre-restore snapshot**, close both
  processes, write, relaunch, confirm from the log.
  - The pre-restore snapshot is **unconditional and has no parameter to skip it**. It is what
    makes the destructive button safe to press.
  - `RestorePlan` is pure and computes the restore dialog's *now vs. after* table, so phase 5
    renders rather than calculates.
- **`waveLinkVersion` in every snapshot** — read from `Settings.json`'s
  `Update.LastUpdateVersion`, and from the log banner including the `(Beta)` channel marker.
  When a restore fails, the first question is whether the config is bad or the validator
  changed.
- **`IClock`** — the third seam, added now because snapshot timestamps are the first thing
  that genuinely needs it.

### Changed

- `IFileSystem` gains `CreateDirectory` and `DeleteDirectory`.
- `SettingsAnalysisResult` carries `WaveLinkVersion`; `RestoreVerdict` carries `Version` and
  `Channel`.

---

## [0.1.0] — 2026-08-16

**Phase 1: `WaveLinkBackup.Core`.** The library everything else will call. No user-facing
application yet — the CLI and WPF shells are stubs that exist to prove the reference graph.

**93 tests passing, 81.2% line / 81.8% branch coverage.**

### Added

- **`WaveLinkBackup.Core`** — settings discovery, validation, health fingerprinting, safe
  replacement and log verification, in a functional-core shape: everything that can be pure is
  pure, and all IO sits behind two seams.
  - `Analysis/` — pure, no IO, no constructors. Duplicate-key scanning, the health
    fingerprint, log verification. 96–100% covered.
  - `Discovery/SettingsLocator` — globs `Elgato.WaveLink_*`, requires `Settings.json` to
    exist, refuses to guess between multiple installs, and never looks at `%APPDATA%`.
  - `Io/` — shared-mode reads, retry-once on a torn read, atomic replace with a rollback copy.
  - `Process/WaveLinkProcess` — graceful close, kill on timeout, then **verify**; covers both
    `Elgato.WaveLink` and `WavelinkSEService`.
  - `Results/` — `Result<T>` for expected failures, exceptions reserved for bugs.
- **Four-project solution** per [ADR-004](_docs/decisions/ADR-004-core-library-thin-shells.md):
  `Core`, `Cli`, `App`, `Core.Tests`. `Core` targets `net10.0` and needs nothing from the
  Windows Desktop ref pack.
- **Three guards.** An MSBuild target failing the build if `Core` resolves anything from
  `Microsoft.WindowsDesktop.App`, plus source-scan tests banning `File.ReadAllBytes` and
  reflection-based `JsonSerializer` in `Core`. Each catches a bug that surfaces far from where
  it is introduced; the file-lock one cannot be caught at runtime in CI at all.
- **CI** on `windows-latest`: restore, build, test.
- **Vendored upstream snapshot** at
  [`211a18c4`](https://github.com/voltybat/WaveLinkSettingsUtility/commit/211a18c4af4da9c05ad8d08de6e50740ccaa933f)
  in `third_party/`, verbatim and excluded from the build, for attribution and audit. Its own
  suite was verified green (40 tests) at that commit before vendoring.
- **`LICENSE`** — MIT, carrying upstream's copyright line verbatim alongside ours.
- Seven read-only integration tests against a real Wave Link install, skipped when absent.

### Fixed relative to upstream

- **`WavelinkSEService` is now closed too.** Upstream only ever looks for `Elgato.WaveLink`, so
  its "verified exited" check can pass with half of Wave Link running and a write can still
  race the service's flush. Audit finding 6; worth offering back.
- **Reads use `FileShare.ReadWrite | FileShare.Delete`.** `Settings.json` is locked while Wave
  Link runs, so `File.ReadAllBytes` — upstream's call — fails on most captures.
- **`--settings-path` bypasses discovery entirely.** Upstream requires the override to match a
  discovered `Elgato.WaveLink_*` candidate, which cannot help a user whose install is not
  found.
- **Duplicate-key detection**, absent upstream, built on `JsonDocument` because it is the only
  API that survives both duplicate forms.

### Not changed, after measurement

- **The JSON encoder.** A previous finding recommended `UnsafeRelaxedJsonEscaping`. Measured
  against the live file, Wave Link writes with the **default** encoder and a default round-trip
  reproduces its 43,052 bytes exactly. Applying the "fix" would have broken dedup. Withdrawn.

---

## [0.0.2] — 2026-08-16

Documentation only. A probe against a live Wave Link install answered one open question and
invalidated two documented decisions; corrections were applied at source rather than noted and
worked around. See
[`_docs/sessions/2026-08-16-phase-1-probe.md`](_docs/sessions/2026-08-16-phase-1-probe.md).

## [0.0.1] — 2026-08-16

Documentation only. The `_docs/` system, seeded from `SPEC.md` and the design handoff: 8 ADRs,
8 gotchas, a recipe, an upstream audit, an 8-phase roadmap, a glossary and a technical-debt
register. Root `README.md`, `CHANGELOG.md` and `.gitignore`.

---

## Release checklist

A gate, not a version. Before any public `1.0.0`:

- [ ] **Redacting "copy diagnostics" action.** Settings files contain hardware serial numbers
      and the Windows username, and users attach backups to bug reports without thinking about
      it. This gates going public rather than following it — see
      [`technical-debt.md`](_docs/technical-debt.md) §6.
- [x] **Packaging decided deliberately.** `WaveLinkBackup.Cli` sets `SelfContained=true` in the
      csproj, so a local publish and CI cannot produce different artifacts.
- [x] **MIT attribution** preserved for upstream, in `LICENSE` and `README.md`.
- [x] **Windows-only stated above the fold** in `README.md`.
- [ ] **The VST3 bundle path covered by a fixture test.** It cannot be exercised by the
      author's machine and will silently capture nothing if wrong. Phase 6.

<!-- Add the [Unreleased] / version compare links here once the repository has a remote. -->
