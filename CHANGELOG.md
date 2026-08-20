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

**Phase 7's work landed early, as a debt-clearing pass**: the privacy gate, the two notifications
and the update mechanism are all here. What remains of
[phase-7-release.md](_docs/dev-phases/phase-7-release.md) is packaging, signing, and running the
release loop once for real.

### Added

- **The app can update itself.** A new `UPDATES` section in Settings: what version you have, when
  it last looked, and a weekly check you can switch off. An available update shows its version,
  release date and size, with *What changed* and *Install and restart*. Nothing installs without
  you pressing something, and an available update is never a notification or a badge — the one
  exception is a backup made by a newer version, whose *Get the update* now opens this section
  with a check already running instead of landing nowhere.

  Updates are **verified against a checksum the release publishes**, and an update without one is
  refused rather than installed hopefully. The new version is staged beside the install and
  swapped in by rename, so an interruption at any point leaves a working app — the previous
  install is moved aside, not deleted, and only removed once the new one is in place.

  **It never asks for administrator rights.** Where the app is installed somewhere you cannot
  write, it says so and offers the download instead. Overwriting a program's own binaries with
  files from the internet is not something to do quietly with elevated rights.
  See [ADR-012](_docs/decisions/ADR-012-check-only-updates-with-a-staged-swap.md) and
  [the runbook](_docs/operations/runbooks/releasing-and-updating.md).

- **Copy diagnostics.** In Settings, beside *where these settings live*: everything the app knows
  about itself, on the clipboard, with **hardware serial numbers and your Windows user name
  removed**. `wlbackup diagnostics` prints the same thing.

  This exists because settings files get attached to bug reports, and that file carries both. The
  report describes structure — how many inputs, what they are called, which tiers each backup
  holds — and **never includes the settings file itself**, redacted or otherwise. Nothing is ever
  uploaded, and there is no setting that would make it. Your channel names are kept on purpose:
  they are what nearly every support question is actually about.

- **The two tray notifications the design specifies, and no others.** *Nothing has been backed up
  for 9 days*, which fires once rather than daily and re-arms only after a backup actually
  happens; and *Wave Link reset your settings*, after a rejected restore. A successful backup
  never notifies.

- **A "backing up" state.** The strip that shows a restore's four stages now has its other half:
  *Backing up your setup…*, the size, and a progress bar whose numbers are real — bytes on disk
  against a total known before the first write.

- **When Windows starts.** Two switches in Settings: start with Windows and sit in the tray, and
  whether closing the window hides it there. If Task Manager has disabled the startup entry, the
  switch reads off and says why rather than fighting it.

- **A first run with no Wave Link now says so, and offers a way in.** *Wave Link not found*, where
  it looked, and a *Choose the settings file…* button — the only route into the app for an
  installation discovery cannot find.

- **A rejected restore can be acted on.** It was the app's worst moment and it offered nothing: a
  bar stating that Wave Link had reset your settings, with no way to act and no way to close it.
  It now carries *Show the log* and *Restore "Before restore"*, and selects that backup in the
  list so the button and the row are visibly the same thing.

- **Pointing the backup folder somewhere that is not a backup folder now says so**, in place,
  with *Choose another…* and *Keep the current folder* — instead of silently pointing at your
  Recordings folder and showing an empty list.

- **The real icon set.** Every glyph is now a Lucide path rather than a hand-drawn approximation
  of one. See [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

- **A release pipeline.** Pushing a `v*` tag builds, tests, packages and publishes the release the
  updater looks for, with its checksum. The version comes from the tag.

- **You can set how often automatic backups happen.** *At most one an hour* was a constant, and the
  Settings dialog said so as though it were a fact about the world. It is a stepper now — 15 min,
  30 min, 1 h, 2 h, 4 h, 12 h, 24 h — and the row's title is the value read back as a sentence, so
  the label and the control cannot drift. It remains a **cap on change-driven backups, not a
  timer**: nothing is written when nothing changes, so a shorter interval does not make the app
  busier on a quiet machine.
- **And a daily backup at a time you choose.** Off by default; on, it starts at 03:00 and steps in
  half hours, wrapping at midnight. It takes a backup whether or not anything changed — dedup means
  an unchanged one stores nothing — and it does not fight the interval cap: if an ordinary automatic
  backup already happened after today's set time, the day is covered and the daily one is skipped.
  A machine that was asleep at 03:00 captures when it wakes rather than losing the day. The row says
  plainly that your computer has to be awake and the app running; this is not a scheduled task.
- **The plug-in files can be restored from the app**, not only from the CLI. A row in the restore
  dialog, off every time, absent when the backup holds no plug-in binaries. Confirming with it on
  hands the restore to an elevated copy of the app and Windows shows its own consent dialog.
  Declining leaves everything as it was and says so, and the settings and presets restore either
  way. [technical-debt.md](_docs/technical-debt.md) §4.17, closed.

### Changed

- **Backups no longer read plug-in files into memory to copy them.** A capture used to hold every
  preset and every plug-in binary at once — about 40 MB on a typical rig, and unbounded if you run
  a large sampler on a channel. Files are now streamed, so the memory cost is a buffer.

- **Unchanged plug-ins are not re-read on every backup.** Their fingerprint is reused when the
  path, size and modification time all match, which on an automatic backup is the common case.

- **Arrow keys move through the whole list**, rather than stopping at each date. The list was one
  control per date, which is also what made three backups look selected at once; it is now one
  control, and the workaround that used to hold that together is gone.

- **Keyboard and screen-reader support to Windows conventions**, not just the four keys the design
  names: `Alt` shortcuts on dialog buttons, `Shift+F10` and the Menu key opening a row's actions,
  `Delete` on a selected row, and labels that read as sentences.

- **The main window's minimum width is 1124px**, up from 980. Below that the last column was being
  clipped with no way to scroll to it.

- **A failed manual backup reports inline** rather than in a message box.

- **The tray icon follows the DPI of the screen holding the taskbar**, and re-renders when
  monitors change. It was a fixed size, correct at 100% and 150% scaling and soft above.

- **Preset paths inside a snapshot name their root** — `presets/appdata/…` and
  `presets/documents/…`. Required rather than cosmetic: once presets can come from two places, a
  path that does not say which one cannot be restored to the right one.
- **`plugins.json` is schema 2**: `presetSource` (one string) became `presetSources` (an array,
  at most one folder per root). **Snapshots already on disk are unaffected** — a preset path with
  no root segment reads as `%APPDATA%`, which is the only place those files came from, and the
  schema-1 `presetSource` key is still read.
- `TierCapture` and `TierRestore` take a Documents path alongside the AppData one, so a test can
  redirect both.

---

### Fixed

- **Restoring plug-in files no longer asks for administrator rights unless it needs them.** The
  app assumed it always would, because plug-ins usually live in a folder every account shares. On
  many machines they don't need it — several plug-in installers make that folder writable so their
  own updates run without a prompt — so the app now checks first, and says which kind of restore
  this will be before you confirm.

- **A backup's recorded size is what was written**, not what was measured beforehand. The two are
  normally identical; they differ when a file changes while a backup is being taken, and the
  recorded figure was the stale one.

- **Settings' drive line prints all three figures** the design gives it — how many backups, how
  much they use, how much is free — instead of free space alone.

- **In high contrast, the "what goes in a backup" bar is labelled.** Its colour segments carry no
  meaning there, and nothing replaced them.

- **Nothing in the Settings dialog took effect until the next launch.** Every control there commits
  as you change it, and the commit reached the settings file but never the running app — so
  switching a tier on, or automatic backups off, appeared to work and did nothing until you
  restarted. Both are live now. §4.20.
- **The keep-count stepper's − and + buttons did nothing.** They were declared, the number beside
  them was bound, and no handler was ever wired. Found while adding two steppers next to them; a
  view test now presses the `+` of every stepper in the dialog and asserts the value moved. §4.20.
- **Tier 3 was capturing 2% of your presets.** Preset discovery only ever looked in
  `%APPDATA%\<Vendor>\`, and running it against a real rig for the first time
  ([technical-debt.md](_docs/technical-debt.md) §4.18) showed what that misses: for FabFilter
  Pro-Q 4 it found three files — an interface default, a MIDI map and a cache — while the 172
  `.ffp` presets the Settings dialog promises as *"your EQ curves, your gate thresholds"* sat in
  `Documents\FabFilter\Presets\Pro-Q 4\`. Discovery now reads **both roots**. On the reference
  rig a snapshot went from **61 preset files to 491**.
- **Crash reports were being backed up as presets.** `%APPDATA%\Supertone\Clear` holds crash
  dumps and nothing else, and tier 3 captured two of them and told the user it had saved two
  presets. `Reports`, `Logs`, `Crashes` and `Diagnostics` directories are now skipped at any
  depth. Clear reports its folder with a count of **zero**, which is the honest answer and a state
  `presetFileCount` was designed to show.
- **Documents is resolved through `GetFolderPath`, never composed from `%USERPROFILE%`.** The
  reference rig has it redirected to another drive entirely — a composed path would look in an
  empty folder and quietly report that the user has no presets. The same trap
  [technical-debt.md](_docs/technical-debt.md) §5 already records for `%LOCALAPPDATA%`, and the
  test constants now sit on a different drive so it cannot come back.

## [0.6.0] — 2026-08-19

**Plugin tiers.** A backup stops being one 43 KB file. It now carries what the settings *reference*
— the plug-ins, their versions, the presets you saved inside them, and optionally the `.vst3` files
themselves — and a restore puts them back. [[ADR-006]]'s four tiers, all of them, capturing and
restoring end to end.

The failure this exists to prevent: restore a settings file onto a machine without FabFilter
Pro-Q 4 and the channel loads with that effect **switched off**, looking exactly like an incomplete
backup. The restore dialog now names the plug-in before you press the button.

**1,146 tests passing** (Core 399, CLI 97, App 650), up from 1,050. Release build clean, zero
warnings.

### Added

- **Tier 2 · `plugins.json`** in every snapshot: name, vendor, version, uniqueId, path, the
  SHA-256 of the binary at capture time, the channels the plug-in sits on, and what tiers 3 and 4
  found for it. Hand-written serializer, no reflection, like `manifest.json`.
- **Tier 3 · Effect presets** — `%APPDATA%\<Vendor>\<Plugin>\` for every referenced plug-in, on by
  default. Discovery looks in the narrowest place first (`<Vendor>\<Plugin>`, then the plug-in's
  file name, then the vendor folder) and **records which folder it read**, because a heuristic
  whose result cannot be inspected is one nobody can improve.
- **Tier 4 · Plug-in binaries** — the `.vst3` at each `FilePath`, off by default. **A `.vst3` may
  be a directory**: the VST3 bundle case is checked first, recursed in full, and covered by a
  synthetic fixture, because all six plug-ins on the author's machine are single files and that
  path would otherwise ship untested ([[vst3-backs-up-as-nothing]]).
- **Tier 1 is finally what four documents already said it was.** Wave Link's own backup copies —
  the rolling `AutoBackup` files and the irregular `.bak` atomic-save artifacts — now travel with
  `Settings.json`. That is the ~470 KB [[ADR-006]] describes and the Settings dialog prints; until
  now a snapshot held 43 KB.
- **The restore-side check.** For every plug-in a snapshot recorded: is it here, and at which
  version? Missing plug-ins fill the restore dialog's amber block by name — *"FabFilter Pro-Q 4
  isn't installed on this computer. The Voice channel will load with that effect switched off."* —
  and version drift joins the quiet line instead, because a plug-in that updated is not missing.
- **Tiers restore, not just capture.** Presets go back to `%APPDATA%` on an ordinary account.
  Plug-in binaries are opt-in (`wlbackup restore <id> --with-plugins`) because
  `C:\Program Files\Common Files\VST3` is the one location that can need administrator rights —
  and when it does, the CLI says so plainly rather than surfacing an access-denied trace.
- `--with-plugins` on the CLI, and the missing-plug-in warning in `wlbackup restore`'s plan.

### Changed

- **The Settings dialog's two locked rows are real controls.** *Effect presets* and *The effect
  plug-ins themselves* switch, commit immediately like every other control on that screen, and the
  **NOT BUILT YET badge is gone** — nothing on that screen is unbuilt any more.
- **Every size in WHAT GOES IN A BACKUP is measured**, not the design mock's 470 KB / 4 KB / 10 MB
  / 40 MB. The proportion bar recomputes live when a tier is switched, which is what
  "recompute from the enabled tiers" was always supposed to mean.
- `BackupSettings` gained `IncludePresets` (on) and `IncludePluginFiles` (off), **with no schema
  bump** — a field whose absence means its default is exactly what the tolerant read already
  handles.
- `IFileSystem` gained `GetFileSize`, so "how big would a backup be?" can be answered without
  reading 40 MB of plug-in binaries to find out.
- Manifest file sizes are read as 64-bit. Tier 4 puts real binaries in the manifest, and a file
  size read as an `int` is a trap that springs once, on someone else's machine.

### Fixed

- **The "Your setup" row was showing the wrong file's size** — Wave Link Backup's own preferences
  file (a few hundred bytes) rather than the Wave Link settings the row describes. Found while
  wiring the measured sizes.

### Notes

- **Nothing in tiers 1-extra, 3 or 4 can fail a capture.** A locked AutoBackup, an unreadable
  preset, a plug-in that was uninstalled: each drops out of the snapshot and the snapshot still
  gets written. The settings file is the product.
- **A tier is claimed only when it is actually in there.** Tier 3 claims `presets` only if a file
  was captured; tier 4 is all-or-nothing, because a snapshot that can restore five plug-ins out of
  six cannot do what its `PLUGINS` badge promises.
- The shell restores presets and never plug-in binaries — elevation has no designed surface yet.
  The capability is in Core and reachable from the CLI. Recorded as
  [technical-debt.md](_docs/technical-debt.md) §4.17.

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

- [x] **Redacting "copy diagnostics" action.** Shipped 2026-08-20, in Settings and as
      `wlbackup diagnostics`. Serial numbers, the Windows user name and snapshot display names are
      removed; the settings file is never included at all; nothing is uploaded. §6, paid.
- [x] **Packaging decided deliberately.** `WaveLinkBackup.Cli` sets `SelfContained=true` in the
      csproj, so a local publish and CI cannot produce different artifacts.
- [x] **MIT attribution** preserved for upstream, in `LICENSE` and `README.md`.
- [x] **Windows-only stated above the fold** in `README.md`.
- [x] **The VST3 bundle path covered by a fixture test.** Both directions, `TierCaptureTests`
      and `TierRestoreTests`. Closed in phase 6 — §2.3.
- [ ] **Code signing.** Owed before the updater is trusted in anger: the checksum it verifies
      proves the bytes are the ones the release named, not that the release is ours. It is also
      what would make elevating during an update defensible — see
      [ADR-012](_docs/decisions/ADR-012-check-only-updates-with-a-staged-swap.md).
- [ ] **Run the release loop once, end to end.** The pipeline and the updater are built and
      unit-tested; the download, the swap and the relaunch have met fixtures and temp directories
      only. Record the result in
      [the runbook](_docs/operations/runbooks/releasing-and-updating.md).
- [ ] **Set `WLBACKUP_UPDATE_OWNER` / `_REPO`** in the published build, once the repository has a
      remote. Until then the UPDATES section correctly hides itself, which also means nobody has
      exercised it.
- [ ] **Decide the pre-release rule** before a `-beta` tag exists. `1.4.0-beta.2` currently reads
      as `1.4.0` and would be offered to everyone.
- [ ] **The by-eye pass.** §4.15 — the dialog frosting has never been seen, and nothing in the
      suite can assert that a blur rendered.

<!-- Add the [Unreleased] / version compare links here once the repository has a remote. -->
