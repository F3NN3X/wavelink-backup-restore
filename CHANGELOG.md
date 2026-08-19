# Changelog

All notable changes to Wave Link Backup are recorded here.

Format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/); versioning follows
[Semantic Versioning](https://semver.org/spec/v2.0.0.html).

**Pre-1.0, so the minor number carries breaking changes.** One MINOR release per completed phase
of [the roadmap](_docs/dev-phases/README.md): `0.1.0` is phase 1, `0.2.0` phase 2, and so on.
A **patch** release is fixes to the phase already shipped, cut when they are worth having before
the next phase closes — `0.5.1` is the first. `1.0.0` is the first public release, which is gated
on the privacy work rather than on feature completeness — see the release checklist at the bottom.

The version in `Directory.Build.props` is the source of truth and matches the newest release
heading here.

> **This is the engineering changelog** — what shipped, per version, broad enough to become
> release notes. The documentation ecosystem has its own delta log in
> [`_docs/documentation-stats.md`](_docs/documentation-stats.md) → *Recent additions*. Same
> commit is fine; don't write the same entry in both.

---

## [Unreleased]

Phase 6 — **plugin tiers** — is under way. §1 has landed: the referenced-plugin set is extracted
from `AudioPluginConfigurations` and cross-referenced against Wave Link's plugin-scanner cache for
version and uniqueId. Matching is by path before name, and a plugin the cache has never seen is
still recorded with its version left unknown — the settings file's `FilePath` is the authority on
what is in use. Nothing consumes it yet; §2 (`plugins.json`) is the first caller. See
[dev-phases/phase-6-plugin-tiers.md](_docs/dev-phases/phase-6-plugin-tiers.md) and
[the session note](_docs/sessions/2026-08-19-phase-6-plugin-discovery.md).

---

## [0.5.1] — 2026-08-19

**The design audit.** An independent pass over the shipped phase-5 shell against
[the design reference](_docs/operations/design/README.md), plus the four defects the user reported
against it. Five of them made a feature unusable rather than merely wrong-looking, and none was
visible to the test suite: **every one lived in a view no test had ever constructed.**

No behaviour was redesigned. Everything here is the shell doing what phase 5 already said it did.

**1,050 tests passing** (Core 318, CLI 91, App 641), up from 964 — the eight new App test classes
are almost entirely view tests, which is the gap this release exists to close. Release build clean,
zero warnings.

### Fixed

- **The restore dialog threw on construction and could never be opened.** It applied a
  `TargetType="TextBlock"` style to a `TrackedText`, which WPF rejects while the window is being
  built — so the app's one irreversible action was unreachable. No test had ever instantiated
  that window; two now do, and a source scan catches the whole class.
- **The settings dialog showed `{Binding WhatGoesIn.NoteOneLead}` and two more binding
  expressions as literal text.** A markup extension only evaluates in attribute syntax; written
  as a property element the braces are just characters.
- **Every dialog rendered a large black rectangle behind its card.** They were borderless
  windows with `Background="Transparent"` and `AllowsTransparency` left false — and a
  non-layered WPF window cannot be transparent, so "transparent" resolved to opaque black at
  WPF's default window size. Dialogs are now layered overlays covering their owner, dimmed by
  the theme's scrim and frosted behind (`DialogOverlay`, `AcrylicDialogBackdrop`).
- **The dark theme carried the light theme's numbers** for `WlLine`, `WlLine2`, `WlHover` and
  `WlScrim`, and the wrong alphas for `WlOkSoft` and `WlWarnSoft`. The scrim was the visible
  one: 22% where the design specifies 55%.
- **Three paddings were transcribed from CSS shorthand into WPF's `Left,Top,Right,Bottom`
  order**, putting 11px on the left of the column header rather than the top. That was the
  header-vs-row misalignment the by-eye checklist had been asking design to sign off; it was a
  transcription bug, not a design call.
- Buttons were rendered at 12.5px — the secondary/description role — where the type table gives
  13.5px, with 6px corners instead of 8px, and the primary and danger fills were missing their
  Medium weight.
- The settings section labels, the tier badges and the `NOT BUILT YET` badge rendered untracked,
  losing the letter-spacing that separates the design's micro-caps from shouted text.
- **Selection was per date group.** Clicking a backup from today, then one from yesterday, left
  both highlighted — the shared `SelectedItem` binding every group used cannot express one
  selection across several Selectors, and two of them wired that way write each other's rows back
  and forth until WPF's loop detection intervenes. Selection is explicit now (`GroupSelection`).
- **The settings dialog's proportion bar had never rendered.** The fractions were computed and
  unit-tested throughout phase 5; nothing bound a width to them, so all three segments measured
  zero and the bar drew as an empty track.

### Added

- **Motion.** README's timings are built: 140ms hover across the ghost, secondary, primary,
  danger, stepper and caption buttons and the list rows, and a 220ms reveal on the selected
  row's expansion. `CubicBezierEase` implements the specified `cubic-bezier(.2,0,0,1)` exactly
  rather than substituting the nearest named WPF curve.
- **A styled scrollbar**, app-wide: 10px, no trough, no arrows, a `WlLine2` thumb stepping to
  `WlMuted`. Windows' own 17px grey trough was the loudest thing in the settings dialog.
- The restore dialog's missing-plug-in warning now reaches the view as a lead clause and a
  consequence, so the sentence naming what is missing carries the weight the design gives it.

---

## [0.5.0] — 2026-08-19

**Phase 5: the WPF shell.** The app gets a window — and, per the design's own framing, is a
*tray app with a window*, not the reverse: closing the window hides it, the process keeps
backing up, and the tray icon is the primary surface.

**964 tests passing** (Core 296, CLI 91, App 577), up from 308 at the end of phase 4. Build
clean, zero warnings.

### Added

- **The main window**: the backup list (name, date, trigger pill, five-slot health strip, tier
  badges, suspect marker, row expansion), live OS theme following, the custom caption bar, and
  the inline result strip that is the single home for restore outcomes, in-progress states and
  six of the twelve designed errors.
- **The real restore flow**: a confirmation dialog rendering `RestorePlan` (focus starts on
  Cancel, not the destructive button), a four-stage in-progress strip, and the restore-outcome
  strip wired to `RestoreOrchestrator` — four outcomes (succeeded-confirmed, succeeded-unconfirmed,
  rejected, failed), each with its own dismiss rule.
- **In-place rename**, a three-variant two-stage delete (normal, only-backup, pre-restore) that
  moves snapshots to `<store>/.trash/<id>/`, and an **Empty trash** action in Settings that hands
  the trash to the Recycle Bin.
- **The twelve designed errors** in their four placements, plus the first-run/empty state
  (found-Wave-Link variant; the not-found variant remains open, [technical-debt.md](_docs/technical-debt.md) §4.10).
- **The settings dialog**: in-place commit with no Save button, atomic persistence to
  `%LOCALAPPDATA%\WaveLinkBackup\settings.json`, a computed proportion bar, and the unbuilt
  tier rows shown present-but-disabled with a NOT BUILT YET badge.
- **The tray shell**: the app icon (tray and window), second-launch activation instead of a
  second watcher, `--tray` windowless start, hide-on-close via `OnExplicitShutdown`, the context
  menu, and an autostart toggle that reads back the Task Manager veto.
- **High contrast** as a fully verified third theme: the runtime swap pinned end to end, a guard
  against any hard-coded colour in `HighContrast.xaml`, and every phase 5–8 surface swept in both
  HC schemes.
- **Four Core changes** underpinning the shell ([technical-debt.md](_docs/technical-debt.md) §7):
  two-stage delete via `.trash` and an `IRecycleBin` seam, lazy verify-only-the-condemned pruning,
  a watcher that clears its pending write and surfaces the error on failure instead of queuing,
  and Windows-convention keyboard/focus/screen-reader support throughout the shell.

### Closed

- [technical-debt.md](_docs/technical-debt.md) §4.8 item 4 (the `Settings…` placeholder) and §4.9
  (the dormant restore-outcome strip) — both real UI now, nothing left unwired.

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
