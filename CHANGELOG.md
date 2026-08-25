# Changelog

All notable changes to Wave Link Backup are recorded here.

Format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/); versioning follows
[Semantic Versioning](https://semver.org/spec/v2.0.0.html).

**Pre-1.0, so the minor number carries breaking changes.** One MINOR release per completed phase
of [the roadmap](_docs/dev-phases/README.md): `0.1.0` is phase 1, `0.2.0` phase 2, and so on.
A **patch** release is fixes to the phase already shipped, cut when they are worth having before
the next phase closes, `0.5.1` is the first. `1.0.0` is the first public release, which is gated
on the privacy work rather than on feature completeness. See the release checklist at the bottom.

The version in `Directory.Build.props` is the source of truth and matches the newest release
heading here.

> **This is the engineering changelog**, what shipped, per version, broad enough to become
> release notes. The documentation ecosystem has its own delta log in
> [`_docs/documentation-stats.md`](_docs/documentation-stats.md) → *Recent additions*. Same
> commit is fine; don't write the same entry in both.

---

## [Unreleased]

### Changed

- **README was rewritten again, this time for its language rather than its punctuation.** The bold label on nearly every feature bullet is gone from about a third of them, so the emphasis marks the lines that earn it instead of every line equally. "Trash, not delete" and "There is no setting that would create an upload, because none exists" were clipped or circular and now say the thing plainly. `framework-dependent` appeared six times, once as the same sentence twice over, and now appears where it is load-bearing. The privacy paragraph's line wrapping was repaired after an earlier edit ran two lines together.

### Fixed

- **README's `#privacy` link now goes somewhere.** The heading had grown a subtitle, so the slug no longer matched the anchor in the nav bar and the link silently did nothing.

- **README no longer describes an update check that has not existed since 0.7.6.** It advertised a *weekly* check that *only looks*; the check runs daily and now says when it finds something, and both halves of that sentence had been false since the release that changed them. The test count, the ADR count and the gotcha count were also stale, and the status line still read v0.7.4.

- **A garbled sentence in README's privacy section.** "includes the settings file itself never, redacted or otherwise" now reads "never includes the settings file itself, redacted or otherwise". The meaning was right and the word order was not.

---

## [0.7.6] - 2026-08-25

**The update path works, and the app tells you when there is one.** This release exists because the in-app update had never actually been run, and running it turned up three separate failures, each hidden behind the one before it. The app never mentioned an update existed, every update then failed its checksum, and the install after that silently did nothing. All three are fixed, and an available update is now said on the status strip, in the tray menu, and once per version as a notification.

**If you are on 0.7.5 or earlier, updating from inside the app will still fail.** Those builds carry the broken checksum pairing, and the fix only takes effect in the build doing the updating. Download this one from the releases page; updates from 0.7.6 onward work.

### Added

- **Help opens with an About section.** The app's name, version, licence and the not-affiliated line, at the top of *How this app works*, so someone opening Help to find out what the app even is does not have to close it and open About instead. Composed from the same model the About dialog uses rather than restated, because two copies of a version number are two copies that can disagree.

- **An "update available" notice, in three places.** A fourth segment on the status strip (`· UPDATE 0.7.5 AVAILABLE`), a line at the top of the tray menu that opens Settings, and a tray notification that fires **once per version**, not once per check, and not once per launch. All three read one field, so they cannot disagree with each other. Nothing appears when you are up to date: the strip never says "UP TO DATE", and the menu line ships collapsed, because a line that is always there stops being read. See [ADR-018](_docs/decisions/ADR-018-a-third-notification-and-an-update-notice-on-the-strip.md).

### Fixed

- **A failed update check now backs off like a successful one.** The automatic check moved onto the timer tick, which runs every 15 seconds, and the timestamp that makes it wait a day was written only after the feed answered. A machine that was offline, behind a blocking proxy, or rate-limited by GitHub therefore recorded no attempt and re-tried on every tick, roughly 5,700 times a day. The attempt is now recorded on the way out whether it succeeded or not, which is what `BackupSettings` has always said this field is for.

- **An update that downloaded and verified could still fail to install, silently.** The final step renames the install directory aside and moves the new version into place, and it made exactly one attempt. A process exiting is not the same as Windows finishing with its files, an image section for the just-closed app, a shell extension, or a virus scanner reading eight megabytes of freshly-extracted DLLs will each hold the folder for a moment. When the rename lost that race the old version came back and **nothing anywhere said why**: the swap runs in the staged process, after the window the user was looking at has already gone. The swap is now patient (ten attempts over two and a half seconds), and a failure that survives that leaves a note beside `settings.json` which the next launch reads once, on the status strip as `UPDATE DIDN'T INSTALL`, and as a notification. Your backups are never involved either way.

- **Updating from inside the app failed its checksum every time, and has since 0.7.2.** The release feed took *any* asset whose name ended `.sha256` as the checksum for the archive it was downloading, keeping the last one it saw. That was correct while a release carried one archive, and silently wrong from 0.7.2, when the CLI was split into its own artifact. A release has carried two archives and two checksums ever since, so the app downloaded `…app-win-x64.zip` and verified it against `…CLI-…zip.sha256`. It failed as a checksum error, which reads exactly like a corrupted download, so the only symptom pointed away from the cause. The download and its digest are now matched **by name**, and the order GitHub returns assets in no longer decides the answer. A checksum that belongs to something else is treated as no checksum at all rather than used anyway.

- **The automatic update check now actually runs on its own.** It was wired to the Settings dialog's `Loaded` handler, so "check for updates on its own — weekly, on by default" meant "weekly, the next time you happen to open Settings". The setting said on, the interval was seven days, and the code matched both, the gap was entirely in where the check was attached. It now runs at startup **and every 24 hours while the app is running**, off the UI thread, with a guard so a slow feed cannot stack a request on every tick. A feed that is down or rate-limiting stays silent rather than showing an alarming strip.

- **A check run from the Settings dialog now lights the strip and the tray too.** Pressing *Check now*, being told an update existed, closing the dialog and finding the rest of the app silent was the old behaviour. Every check, timer, startup, dialog auto-check, or *Check now*, records its outcome in one place.

---

## [0.7.5] - 2026-08-25

**The debt list is empty of work, and the app can see the machine's audio devices.** `wlbackup diagnostics` now reports how many capture and playback endpoints exist and what state each is in, the fact that separates "this input is gone" from "this input is fine", which nothing in a settings file can answer. Dialogs no longer render see-through in a high-contrast scheme. And the two questions that could only be answered on a real rig were answered on one: Wave Link resolves a plug-in by identity rather than by path, and the last by-eye look is done.

### Added

- **The audio endpoint inspector.** `IAudioEndpointInspector` and `WindowsAudioEndpointInspector` enumerate the machine's capture and playback endpoints through Core Audio, with each one's state, active, disabled, not present, unplugged. Read-only: pointing a dead channel at a working device is an editing feature and stays out of 1.0, because rewriting a device id means walking the whole settings tree and rewriting both the bare and `<deviceId>|<suffix>` forms (SPEC §3). See [ADR-017](_docs/decisions/ADR-017-source-generated-com-and-unsafe-on-core.md).

- **`wlbackup diagnostics` reports endpoint counts.** A new *Audio endpoints* section, grouped by direction and state. **Counts only, never an id or a device name**, an endpoint id embeds a hardware serial and a friendly name is the hardware someone owns, and this report exists to be safe to paste into a public issue tracker. Two tests assert neither ever reaches it.

- **`tools/plugin-resolution-experiment.ps1`** runs the reversible experiment that answers whether Wave Link resolves a plug-in by `PluginId` or by `FilePath` ([technical-debt.md](_docs/technical-debt.md) §7.6). The state lives in a journal outside the repository, so `-Undo` works from a fresh shell, after a reboot, or a week later; the original file is renamed rather than deleted, and the copy is made before the rename so a failure part-way leaves the install untouched.

- **`tools/seed-fixture-store.ps1`** writes a throwaway snapshot store holding the five rigs the by-eye checklist needs, five named inputs, a collapsed two-input rig, nine channels, twelve, and one with long effect chains. It refuses to write inside the real store. The snapshots are for looking at, not restoring: the endpoint ids are invented.

### Changed

- **COM interop in `Core` is source-generated, and `Core` now builds with `AllowUnsafeBlocks`.** Classic `[ComImport]` does not survive trim analysis, upstream's `Type.GetTypeFromCLSID` activation is IL2072, and built-in COM marshalling is IL2050, both build errors under the `IsAotCompatible` setting the CLI's NativeAOT option depends on. `[GeneratedComInterface]` does survive, and requires the unsafe flag. This reverses a refusal `RecycleBin` documented deliberately; the reasoning for both is in [ADR-017](_docs/decisions/ADR-017-source-generated-com-and-unsafe-on-core.md) and [the gotcha](_docs/knowledge-base/gotchas/com-interop-stops-compiling-the-moment-the-project-is-aot-compatible.md).

- **The share-mode source guard covers `tools/*.ps1`.** `SourceGuardTests` has enforced reading Wave Link's files with `FileShare.ReadWrite | FileShare.Delete` since phase 1 and only ever scanned `*.cs`; the first PowerShell tool written against a live install repeated the exact mistake and failed on its first run. `ToolScriptGuardTests` extends the rule, in both directions, the banned spelling, and the requirement that any script *reading* `Settings.json` names a share mode.

- **`technical-debt.md` went from 1,646 lines to 249.** Thirty-six of its thirty-nine numbered entries were closed, and moved verbatim to [an archive](_docs/archive/technical-debt-closed.md) with section numbering preserved so existing references still resolve. §2.4 closed with this release's interop work; what remains is §7.6, §8.2 and the standing known-wrong list.

### Answered

- **The last by-eye look is done, and `technical-debt.md` now holds no work at all.** Item 5 of the by-eye checklist, the INPUTS verdict at two, five, nine and twelve inputs, and the details dialog's routing matrix, was worked against rigs written by `tools/seed-fixture-store.ps1`. All three read as specified, and the nine-plus cell is no longer crowded: the verdict prints no name per channel, so there is nothing left to crowd. That closes §8.2, and with §2.4 and §7.6 the debt list is down to two permanent sections, neither of which is owed a commit.

- **Wave Link resolves a channel's plug-in by `PluginId`, not by `FilePath`, measured, not assumed.** The experiment behind [technical-debt.md](_docs/technical-debt.md) §7.6 ran on the reference rig: a plug-in was moved to the user-level VST3 folder and the shared copy renamed, and after a restart Wave Link found it, kept the channel intact, and **rewrote `FilePath`** to the new location. An untouched plug-in on the same channel was not rewritten, so this was the moved file being repaired rather than a blanket refresh. The user-level folder is therefore a *viable* fallback destination for tier 4, and the standing recommendation not to build one is unchanged, because §7.5 already removed the prompt in the common case and viable is not the same as worth building. It also retires the audit's §2.4 caveat that the user-level folder could not be observed being scanned: it is scanned.

### Fixed

- **An audio endpoint whose id cannot be read is dropped rather than reported blank.** `IMMDevice.GetId` failing produced an `AudioEndpoint` with an empty `Id`, which reads as a real device to every caller and matches nothing, the id is the only field a channel key matches on. The same path returned early without freeing the COM-allocated buffer, so a driver that allocated and then failed leaked it on every enumeration; the pointer is now freed unconditionally. `EndpointState`'s documentation was also wrong for two of its five values: `NotPresent` is a missing adapter and `Unplugged` is a present adapter with an empty socket, which are different things to tell a user diagnosing a lost channel.

- **Dialogs are no longer see-through in a high-contrast scheme.** Every dialog is a layered window, `AllowsTransparency="True"`, `Background="Transparent"`, carrying a `WlScrim` fill with a `WlCard` card on top. High contrast set *both* to transparent: the scrim because a dialog is separated by a border rather than by dimming, and the card because of the theme's governing rule that every fill goes transparent. Together they left the window without a single opaque pixel, so the desktop showed through the dialog behind its own text. `WlCard` now resolves to `SystemColors.WindowColorKey` and is the one documented exception to that rule. Inside the main window this is visually identical to what it replaced; on a layered window it is the difference between a dialog and a hole. See [the gotcha](_docs/knowledge-base/gotchas/dialogs-are-see-through-in-high-contrast.md).

- **`_docs/README.md` no longer describes the design export as if it were in the repository.** The folder is listed in `.git/info/exclude`, so the roughly 40 documents linking into it resolve only on a machine holding the export. Recorded rather than rewritten, those links cite the authority, and a citation to a document the reader may not hold is honest in a way a removed link is not.

---

## [0.7.4] - 2026-08-24

**A restore now puts the service back before it relaunches, and emptying the trash shows where it is.** A restore that closes Wave Link's processes used to leave its background service down, so the relaunched app came up against a missing `WavelinkSEService` and showed its own "Start Service / Exit App" box. That no longer happens on the elevated path where most restores run, and the trash-empty action, which could freeze the window while a full store was cleared, now reports its progress as it goes.

### Added

- **A restore brings the Wave Link service back before it relaunches the app.** A new `IWaveLinkService` seam in Core (`Exists`, `IsRunning`, `EnsureStarted()`) sits beside `IWaveLinkProcess`; the real implementation goes through the Service Control Manager with a 15-second start timeout. The restore orchestrator calls it immediately before relaunching, so the app comes up against a running service instead of Wave Link's "Start Service / Exit App" box. A failed start is reported, never fatal, the settings are already written by that point, and an unelevated caller simply falls back to Wave Link's own prompt, exactly as before. See [ADR-016](_docs/decisions/ADR-016-a-restore-brings-the-service-back-before-it-relaunches.md).

- **Emptying the trash shows live progress.** The settings dialog's trash row now fills a determinate bar and counts "Removing N of M…" as items are cleared, instead of looking frozen while a full store is emptied. `SnapshotStore.EmptyTrash` gained an optional `IProgress<(int Done, int Total)>` callback that reports after each successful removal; the existing CLI and app callers are untouched because it defaults to null.

### Fixed

- **The trash row no longer shows a stale count and size after emptying.** The row's view-model property was an auto-property, so re-assigning it never raised `PropertyChanged` and the screen kept its first value. It is now a notifying property; all three write sites (initial open, folder change, post-empty refresh) update the UI at once with no XAML changes. See [the gotcha](_docs/knowledge-base/gotchas/the-row-shows-stale-data-after-you-update-it.md).

---

## [0.7.3] - 2026-08-23

**The app that would not start is fixed, and releases now carry their own notes.** A crash that
killed the tray app on launch, before any window or tray icon appeared, is resolved, and the
release page now shows what changed in each version rather than only the download links.

### Fixed

- **The app no longer dies at startup with a culture error.** The publish had set
  `InvariantGlobalization=true` to shed WPF's satellite locale assemblies from the English-only
  build, but that puts the whole process in invariant mode where `CultureInfo("en")` throws. WPF's
  font cache constructs that culture in a static constructor on the first text measure, so the app
  died inside `Window.Show()` layout before any of our code ran, no window, no tray icon, nothing.
  The fix swaps it for `SatelliteResourceLanguages=en`, which trims the non-English satellite
  resources while keeping full globalization (real cultures, working text rendering). Verified: the
  republished app runs clean and the event log is free of the `CultureNotFoundException` that had
  been reproducing on every launch. See
  [the gotcha](_docs/knowledge-base/gotchas/the-app-dies-before-the-window-with-a-culture-error.md).

### Changed

- **The GitHub release page now carries the version's notes.** The release body, which previously
  held only the download links and checksums, now leads with a *What's new* section pulled from
  this file for the tagged version, so a person landing on the release reads what changed before
  they decide to download. The updater is untouched: it still reads only the `*app-win-x64.zip`
  asset and its `.sha256`, never the body.

---

## [0.7.2] - 2026-08-22

### Added

- **Help and About dialogs.** The tray menu gains two entries, and the window's caption bar
  gains a "?" beside the Settings gear. *Help* says what the app does in the user's words -
  what gets backed up, how snapshots are kept, how restoring works, what the tray icon is for -
  with a link to the documentation when the build was given one. *About* states the facts about
  this build: name, version (read from the same source the updater compares against, so it can
  never drift), licence, and the not-affiliated line. Both are static content behind a view
  model - no logic in either - and both open modally over the window when one is open,
  standalone otherwise.

### Changed

- **The release is now two small archives instead of one large one.** The app publishes
  **framework-dependent** (it requires the [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0),
  which a fresh machine will not have - the README names the prerequisite) and the CLI is its own
  release artifact rather than riding inside the app's archive. Measured, exactly as CI runs it:
  the app archive drops from **101 MB to 7.6 MB**, the CLI is **0.22 MB** on its own, and the .NET
  runtime ships nowhere at all - both resolve it from the machine. The satellite locale folders are
  gone too (`InvariantGlobalization`). The updater's asset match widened with it: it now looks for
  `*app-win-x64.zip`, so a release carrying both assets resolves to the app, and a checksum is
  still published beside each archive. A machine without the runtime fails at native load before
  managed code runs, so there is no in-app prompt - that is the accepted trade for ~94 MB off every
  download. See [technical-debt.md](_docs/technical-debt.md) §8.5 (closed) and
  [the runbook](_docs/operations/runbooks/releasing-and-updating.md).

---

## [0.7.1] - 2026-08-22

**Three fixes to the phase already shipped.** The daily backup that silently never ran, the list
that did not refresh after a capture, and the click that selected the wrong row after scrolling.

### Fixed

- **The daily backup now actually fires.** The 0.7.0 wording, "if an ordinary automatic backup
  already happened after today's set time, the day is covered and the daily one is skipped", was a
  bug: on any machine where Wave Link writes settings during the day (which is most of them), the
  schedule silently never ran, because the change-driven capture that morning had already "covered"
  the day. Only today's own copy of this one now covers the day; a change-driven backup before or
  after the set time no longer cancels it. Dedup keeps it free when nothing has changed since the
  last copy. The Settings row says so plainly now too.
- **The snapshot list updates after an automatic capture.** A new automatic backup used to appear
  only after a restart; the list now refreshes in place when the tick that took it reports success.

- **Clicking a row selects the row you clicked, not one at the bottom of the screen.** After
  scrolling the list to the end, a click highlighted a *different* (lower) row. The list had two
  scroll owners: an outer scroll view did the real scrolling while the list's own was switched off
  but still held the virtualising panel, which tracks only the offset of the scroll view that owns
  it, so the realized rows stayed anchored to the top while the pixels showed the last ones, and a
  click landed on a stale row. The outer scroll view is gone; the list now scrolls itself. With the
  rows grouped, its scrolling also has to be by pixel rather than by item, or the list's own extent
  collapses to nothing and it cannot see how tall the content really is.

---

## [0.7.0], 2026-08-21

**The release phase, and a rig bigger than the design drew.** Phase 7's work, the privacy gate,
the two tray notifications, the in-app updater and the release pipeline, landed early as a
debt-clearing pass. This version packages it, and adds what a nine-channel setup turned out to
need: every channel visible in the row, a view of what a backup actually holds, and a theme you
can choose.

**It is not signed.** Windows will warn on first run, and
[phase-7-release.md](_docs/dev-phases/phase-7-release.md) still owns that. Everything else in the
exit criterion holds: one self-contained archive, no SDK, no installer.

### Added

- **The app can update itself.** A new `UPDATES` section in Settings: what version you have, when
  it last looked, and a weekly check you can switch off. An available update shows its version,
  release date and size, with *What changed* and *Install and restart*. Nothing installs without
  you pressing something, and an available update is never a notification or a badge, the one
  exception is a backup made by a newer version, whose *Get the update* now opens this section
  with a check already running instead of landing nowhere.

  Updates are **verified against a checksum the release publishes**, and an update without one is
  refused rather than installed hopefully. The new version is staged beside the install and
  swapped in by rename, so an interruption at any point leaves a working app, the previous
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
  report describes structure, how many inputs, what they are called, which tiers each backup
  holds, and **never includes the settings file itself**, redacted or otherwise. Nothing is ever
  uploaded, and there is no setting that would make it. Your channel names are kept on purpose:
  they are what nearly every support question is actually about.

- **The two tray notifications the design specifies, and no others.** *Nothing has been backed up
  for 9 days*, which fires once rather than daily and re-arms only after a backup actually
  happens; and *Wave Link reset your settings*, after a rejected restore. A successful backup
  never notifies.

- **A "backing up" state.** The strip that shows a restore's four stages now has its other half:
  *Backing up your setup…*, the size, and a progress bar whose numbers are real, bytes on disk
  against a total known before the first write.

- **Every channel shows in the row, not just the first five.** The INPUTS strip was five cells
  wide because that is the rig the design was drawn for; a nine-channel setup lost its last four
  channels off the end of every row with nothing to say they were missing. The strip is now as wide
  as the biggest configuration in your store, never narrower than five, so a collapse still reads
  as a gap, and the labels are what give way as it widens: nine characters at five channels, four
  at nine, and past about a dozen the cells keep their solid and dashed rules and drop their words.
  Hovering names them all, and so does the new details view.

- **What's in this backup.** A new view off any row, `Ctrl+I`, the row's `···` menu, or a
  double-click, listing **every channel, the effects on it in the order they run**, and what each
  mix plays out of. Effects show their vendor, their category, whether they are switched off, and
  whether they ship with Wave Link or are a VST3 that would have to be installed on a new machine.
  Channels say which mixes they are heard in, **and say so when they are heard in none**, which
  nothing else in the app would tell you.

  It reads the backup's own settings file rather than anything recorded at capture time, so it
  works on **every backup you already have**, including ones taken before this existed. A damaged
  backup opens too, and says why it cannot be described, which is exactly when someone wants to
  know what was in it. See [ADR-015](_docs/decisions/ADR-015-the-details-view-reads-the-backup-itself.md).

- **You can pick the theme.** A `HOW IT LOOKS` section in Settings: *Auto*, *Dark*, *Light* or
  *High contrast*. Auto follows Windows, which is what the app did before there was a choice, and
  it stays the default. The choice applies the moment you press it, window, dialogs, tray menu and
  tray icon, and is remembered in `shell.json` beside the window position, not in `settings.json`,
  which describes itself on that same screen as the folder, the automatic-backup switch, how many
  to keep and which Wave Link you picked.

  **Turning on a high-contrast theme in Windows still overrides it**, because that is Windows
  saying the palette is no longer ours.

- **When Windows starts.** Two switches in Settings: start with Windows and sit in the tray, and
  whether closing the window hides it there. If Task Manager has disabled the startup entry, the
  switch reads off and says why rather than fighting it.

- **A first run with no Wave Link now says so, and offers a way in.** *Wave Link not found*, where
  it looked, and a *Choose the settings file…* button, the only route into the app for an
  installation discovery cannot find.

- **A rejected restore can be acted on.** It was the app's worst moment and it offered nothing: a
  bar stating that Wave Link had reset your settings, with no way to act and no way to close it.
  It now carries *Show the log* and *Restore "Before restore"*, and selects that backup in the
  list so the button and the row are visibly the same thing.

- **Pointing the backup folder somewhere that is not a backup folder now says so**, in place,
  with *Choose another…* and *Keep the current folder*, instead of silently pointing at your
  Recordings folder and showing an empty list.

- **The real icon set.** Every glyph is now a Lucide path rather than a hand-drawn approximation
  of one. See [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

- **A release pipeline.** Pushing a `v*` tag builds, tests, packages and publishes the release the
  updater looks for, with its checksum. The version comes from the tag.

- **You can set how often automatic backups happen.** *At most one an hour* was a constant, and the
  Settings dialog said so as though it were a fact about the world. It is a stepper now, 15 min,
  30 min, 1 h, 2 h, 4 h, 12 h, 24 h, and the row's title is the value read back as a sentence, so
  the label and the control cannot drift. It remains a **cap on change-driven backups, not a
  timer**: nothing is written when nothing changes, so a shorter interval does not make the app
  busier on a quiet machine.
- **And a daily backup at a time you choose.** Off by default; on, it starts at 03:00 and steps in
  half hours, wrapping at midnight. It takes a backup whether or not anything changed, dedup means
  an unchanged one stores nothing, and it does not fight the interval cap: if an ordinary automatic
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
  preset and every plug-in binary at once, about 40 MB on a typical rig, and unbounded if you run
  a large sampler on a channel. Files are now streamed, so the memory cost is a buffer.

- **Unchanged plug-ins are not re-read on every backup.** Their fingerprint is reused when the
  path, size and modification time all match, which on an automatic backup is the common case.

- **Arrow keys move through the whole list**, rather than stopping at each date. The list was one
  control per date, which is also what made three backups look selected at once; it is now one
  control, and the workaround that used to hold that together is gone.

- **Keyboard and screen-reader support to Windows conventions**, not just the four keys the design
  names: `Alt` shortcuts on dialog buttons, `Shift+F10` and the Menu key opening a row's actions,
  `Delete` on a selected row, and labels that read as sentences.

- **The main window's minimum width is 1152px**, up from 980. Below that the last column was being
  clipped with no way to scroll to it.

- **A failed manual backup reports inline** rather than in a message box.

- **The tray icon follows the DPI of the screen holding the taskbar**, and re-renders when
  monitors change. It was a fixed size, correct at 100% and 150% scaling and soft above.

- **Preset paths inside a snapshot name their root.** `presets/appdata/…` and
  `presets/documents/…`. Required rather than cosmetic: once presets can come from two places, a
  path that does not say which one cannot be restored to the right one.
- **`plugins.json` is schema 2**: `presetSource` (one string) became `presetSources` (an array,
  at most one folder per root). **Snapshots already on disk are unaffected**, a preset path with
  no root segment reads as `%APPDATA%`, which is the only place those files came from, and the
  schema-1 `presetSource` key is still read.
- `TierCapture` and `TierRestore` take a Documents path alongside the AppData one, so a test can
  redirect both.

---

### Fixed

- **The backup list scrolls with the mouse wheel.** It never had: the scroll bar, Page Up/Down and
  the arrow keys all worked, and the wheel did nothing. The list's own scrolling is switched off so
  the column header and the rows share one scroll position, and a switched-off scroll view still
  swallows the wheel rather than passing it on.

- **A backup taken before you added a channel is not marked suspect any more.** Adding channels in
  Wave Link turned the input strip amber on every backup you already had: "fewer inputs than the
  most any backup has" was being read as "Wave Link reset your settings", so a rig that grew
  repainted its own history. A backup is now judged against the one taken before it, which is what
  the amber is for, and what it now means again.

- **The bottom bar counts your backups as soon as the list loads.** It read `0 BACKUPS · 0 B` for
  the first fifteen seconds of every launch, under a window full of backups: the figures come from
  the list and nothing re-read them until a selection or the next tick.

- **"Back up now" in the window no longer kills the app.** The capture moved off the UI thread so
  the new backing-up bar could animate, and the refresh that follows a capture - the tray icon, its
  tooltip, its menu - ran on that thread too. Writing a window's own objects from another thread
  throws, and there is nothing above a button handler to catch it, so the process ended: window,
  tray and all. The backup itself was always written first; what was lost was the app, not the
  backup. Both refreshes now marshal themselves.

- **The CONTENTS column no longer clips `PLUGINS` mid-word.** Three tier badges measure 224px at
  the design's own type role and padding, in a column the design gives 200. Its own reference
  render draws them at about 224 too, so the column is 248 now and the window's minimum width goes
  from 1124 to 1152 with it.

- **The CONTENTS column shows what each backup holds again.** Every row drew three blank pills
  where `SETTINGS`, `PRESETS` and `PLUGINS` belong: the badges picked the right treatment - filled
  for present, a dashed ghost for absent - and then rendered their label against nothing.

- **Restoring plug-in files no longer asks for administrator rights unless it needs them.** The
  app assumed it always would, because plug-ins usually live in a folder every account shares. On
  many machines they don't need it, several plug-in installers make that folder writable so their
  own updates run without a prompt, so the app now checks first, and says which kind of restore
  this will be before you confirm.

- **A backup's recorded size is what was written**, not what was measured beforehand. The two are
  normally identical; they differ when a file changes while a backup is being taken, and the
  recorded figure was the stale one.

- **Settings' drive line prints all three figures** the design gives it, how many backups, how
  much they use, how much is free, instead of free space alone.

- **In high contrast, the "what goes in a backup" bar is labelled.** Its colour segments carry no
  meaning there, and nothing replaced them.

- **Nothing in the Settings dialog took effect until the next launch.** Every control there commits
  as you change it, and the commit reached the settings file but never the running app, so
  switching a tier on, or automatic backups off, appeared to work and did nothing until you
  restarted. Both are live now. §4.20.
- **The keep-count stepper's − and + buttons did nothing.** They were declared, the number beside
  them was bound, and no handler was ever wired. Found while adding two steppers next to them; a
  view test now presses the `+` of every stepper in the dialog and asserts the value moved. §4.20.
- **Tier 3 was capturing 2% of your presets.** Preset discovery only ever looked in
  `%APPDATA%\<Vendor>\`, and running it against a real rig for the first time
  ([technical-debt.md](_docs/technical-debt.md) §4.18) showed what that misses: for FabFilter
  Pro-Q 4 it found three files, an interface default, a MIDI map and a cache, while the 172
  `.ffp` presets the Settings dialog promises as *"your EQ curves, your gate thresholds"* sat in
  `Documents\FabFilter\Presets\Pro-Q 4\`. Discovery now reads **both roots**. On the reference
  rig a snapshot went from **61 preset files to 491**.
- **Crash reports were being backed up as presets.** `%APPDATA%\Supertone\Clear` holds crash
  dumps and nothing else, and tier 3 captured two of them and told the user it had saved two
  presets. `Reports`, `Logs`, `Crashes` and `Diagnostics` directories are now skipped at any
  depth. Clear reports its folder with a count of **zero**, which is the honest answer and a state
  `presetFileCount` was designed to show.
- **Documents is resolved through `GetFolderPath`, never composed from `%USERPROFILE%`.** The
  reference rig has it redirected to another drive entirely, a composed path would look in an
  empty folder and quietly report that the user has no presets. The same trap
  [technical-debt.md](_docs/technical-debt.md) §5 already records for `%LOCALAPPDATA%`, and the
  test constants now sit on a different drive so it cannot come back.

## [0.6.0], 2026-08-19

**Plugin tiers.** A backup stops being one 43 KB file. It now carries what the settings *reference*,
the plug-ins, their versions, the presets you saved inside them, and optionally the `.vst3` files
themselves, and a restore puts them back. [[ADR-006]]'s four tiers, all of them, capturing and
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
- **Tier 3 · Effect presets.** `%APPDATA%\<Vendor>\<Plugin>\` for every referenced plug-in, on by
  default. Discovery looks in the narrowest place first (`<Vendor>\<Plugin>`, then the plug-in's
  file name, then the vendor folder) and **records which folder it read**, because a heuristic
  whose result cannot be inspected is one nobody can improve.
- **Tier 4 · Plug-in binaries.** The `.vst3` at each `FilePath`, off by default. **A `.vst3` may
  be a directory**: the VST3 bundle case is checked first, recursed in full, and covered by a
  synthetic fixture, because all six plug-ins on the author's machine are single files and that
  path would otherwise ship untested ([[vst3-backs-up-as-nothing]]).
- **Tier 1 is finally what four documents already said it was.** Wave Link's own backup copies,
  the rolling `AutoBackup` files and the irregular `.bak` atomic-save artifacts, now travel with
  `Settings.json`. That is the ~470 KB [[ADR-006]] describes and the Settings dialog prints; until
  now a snapshot held 43 KB.
- **The restore-side check.** For every plug-in a snapshot recorded: is it here, and at which
  version? Missing plug-ins fill the restore dialog's amber block by name, *"FabFilter Pro-Q 4
  isn't installed on this computer. The Voice channel will load with that effect switched off."*,
  and version drift joins the quiet line instead, because a plug-in that updated is not missing.
- **Tiers restore, not just capture.** Presets go back to `%APPDATA%` on an ordinary account.
  Plug-in binaries are opt-in (`wlbackup restore <id> --with-plugins`) because
  `C:\Program Files\Common Files\VST3` is the one location that can need administrator rights,
  and when it does, the CLI says so plainly rather than surfacing an access-denied trace.
- `--with-plugins` on the CLI, and the missing-plug-in warning in `wlbackup restore`'s plan.

### Changed

- **The Settings dialog's two locked rows are real controls.** *Effect presets* and *The effect
  plug-ins themselves* switch, commit immediately like every other control on that screen, and the
  **NOT BUILT YET badge is gone.** Nothing on that screen is unbuilt any more.
- **Every size in WHAT GOES IN A BACKUP is measured**, not the design mock's 470 KB / 4 KB / 10 MB
  / 40 MB. The proportion bar recomputes live when a tier is switched, which is what
  "recompute from the enabled tiers" was always supposed to mean.
- `BackupSettings` gained `IncludePresets` (on) and `IncludePluginFiles` (off), **with no schema
  bump**, a field whose absence means its default is exactly what the tolerant read already
  handles.
- `IFileSystem` gained `GetFileSize`, so "how big would a backup be?" can be answered without
  reading 40 MB of plug-in binaries to find out.
- Manifest file sizes are read as 64-bit. Tier 4 puts real binaries in the manifest, and a file
  size read as an `int` is a trap that springs once, on someone else's machine.

### Fixed

- **The "Your setup" row was showing the wrong file's size.** Wave Link Backup's own preferences
  file (a few hundred bytes) rather than the Wave Link settings the row describes. Found while
  wiring the measured sizes.

### Notes

- **Nothing in tiers 1-extra, 3 or 4 can fail a capture.** A locked AutoBackup, an unreadable
  preset, a plug-in that was uninstalled: each drops out of the snapshot and the snapshot still
  gets written. The settings file is the product.
- **A tier is claimed only when it is actually in there.** Tier 3 claims `presets` only if a file
  was captured; tier 4 is all-or-nothing, because a snapshot that can restore five plug-ins out of
  six cannot do what its `PLUGINS` badge promises.
- The shell restores presets and never plug-in binaries, elevation has no designed surface yet.
  The capability is in Core and reachable from the CLI. Recorded as
  [technical-debt.md](_docs/technical-debt.md) §4.17.

---

## [0.5.1], 2026-08-19

**The design audit.** An independent pass over the shipped phase-5 shell against
[the design reference](_docs/operations/design/README.md), plus the four defects the user reported
against it. Five of them made a feature unusable rather than merely wrong-looking, and none was
visible to the test suite: **every one lived in a view no test had ever constructed.**

No behaviour was redesigned. Everything here is the shell doing what phase 5 already said it did.

**1,050 tests passing** (Core 318, CLI 91, App 641), up from 964, the eight new App test classes
are almost entirely view tests, which is the gap this release exists to close. Release build clean,
zero warnings.

### Fixed

- **The restore dialog threw on construction and could never be opened.** It applied a
  `TargetType="TextBlock"` style to a `TrackedText`, which WPF rejects while the window is being
  built, so the app's one irreversible action was unreachable. No test had ever instantiated
  that window; two now do, and a source scan catches the whole class.
- **The settings dialog showed `{Binding WhatGoesIn.NoteOneLead}` and two more binding
  expressions as literal text.** A markup extension only evaluates in attribute syntax; written
  as a property element the braces are just characters.
- **Every dialog rendered a large black rectangle behind its card.** They were borderless
  windows with `Background="Transparent"` and `AllowsTransparency` left false, and a
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
- Buttons were rendered at 12.5px, the secondary/description role, where the type table gives
  13.5px, with 6px corners instead of 8px, and the primary and danger fills were missing their
  Medium weight.
- The settings section labels, the tier badges and the `NOT BUILT YET` badge rendered untracked,
  losing the letter-spacing that separates the design's micro-caps from shouted text.
- **Selection was per date group.** Clicking a backup from today, then one from yesterday, left
  both highlighted, the shared `SelectedItem` binding every group used cannot express one
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

## [0.5.0], 2026-08-19

**Phase 5: the WPF shell.** The app gets a window, and, per the design's own framing, is a
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
  strip wired to `RestoreOrchestrator`, four outcomes (succeeded-confirmed, succeeded-unconfirmed,
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
  against any hard-coded colour in `HighContrast.xaml`, and every phase 5, 8 surface swept in both
  HC schemes.
- **Four Core changes** underpinning the shell ([technical-debt.md](_docs/technical-debt.md) §7):
  two-stage delete via `.trash` and an `IRecycleBin` seam, lazy verify-only-the-condemned pruning,
  a watcher that clears its pending write and surfaces the error on failure instead of queuing,
  and Windows-convention keyboard/focus/screen-reader support throughout the shell.

### Closed

- [technical-debt.md](_docs/technical-debt.md) §4.8 item 4 (the `Settings…` placeholder) and §4.9
  (the dormant restore-outcome strip), both real UI now, nothing left unwired.

---

## [0.4.0], 2026-08-16

**Phase 4: the CLI.** The first release you can actually run. `wlbackup` reaches every
capability the previous three phases built, which until now had no caller outside a test.

**308 tests passing.** Core 84.8% line / 81.6% branch; CLI 83.6% / 81.6%.

### Added

- **`wlbackup`.** Eight verbs: `backup`, `list`, `restore`, `rename`, `delete`, `verify`,
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
  trim warnings, and it runs correctly against a real Wave Link install.
  [ADR-001](_docs/decisions/ADR-001-csharp-over-rust.md) estimated 10, 15 MB and treated small
  binaries as the one thing Rust would have done better; that gap is now roughly closed, and
  the ADR records the measurement.
- The published single-file build discovers the package, writes a real backup, and lists it.

### Changed

- `SettingsInspector(IFileSystem)` **removed.** It resolved `%LOCALAPPDATA%` from the real
  environment, so tests wired against a fake filesystem quietly consulted the developer's
  machine, the same bug, twice. Replaced with `SettingsInspector.For(fileSystem, path)` and
  an explicit `SettingsLocator.SystemLocalAppData`.
- `BackupService` accepts an explicit settings path, so `--settings-path` reaches every verb
  rather than only `restore`.
- `SnapshotId.LooksLikeSnapshotId` **deleted**, three phases without a caller.

### Still open

- **`[ComImport]` under NativeAOT is not answered.** There is no COM interop in the codebase
  yet, so the AOT result above measures code that lacks the risky part. See
  [`technical-debt.md`](_docs/technical-debt.md) §2.4.

---

## [0.3.0], 2026-08-16

**Phase 3: it backs up on its own.** The release that makes this a different product from the
tool it was forked from. Everything before it, a person could have done by hand.

Still no user interface, and **nothing calls the watcher in production yet**: the host is a
shell, and the shell arrives in phase 4.

**235 tests passing, 84.9% line / 81.8% branch. The whole suite runs in about a second.**

### Added

- **`Automation/`.** The watcher, and the rules about when to use it.
  - `AutoBackupPolicy`, a **pure** function of three timestamps: ~60s debounce after the last
    write, at most one automatic snapshot an hour. Every timing case is an instant test.
  - `AutoBackupCoordinator`, wires watcher to policy to service. **Owns no timer**; the host
    calls `Tick()`, which is what keeps the timing tests instantaneous.
  - `BackupService`, manual and automatic capture, with dedup and its exception in one place.
  - `SnapshotRetention`, pure. Prunes automatic snapshots to a keep count, default 30.
  - `FileSystemSettingsWatcher`, filters on `LastWrite`, `CreationTime` **and** `FileName`,
    because Wave Link's atomic-save *replaces* the file rather than writing through it. A
    `LastWrite`-only filter would miss exactly the saves that matter most.
  - `BackupSettings`, store path, auto-backup toggle, keep count.
- **Capture on shutdown**, ignoring the debounce and rate limit. The original incident happened
  during an update, while the app was restarting.

### Behaviour worth knowing

- **A manual backup is never deduplicated.** You pressed a button; a new row appears. Automatic
  captures *are* deduplicated, because Wave Link rewrites its settings on every launch with
  near-identical bytes.
- **A skipped duplicate does not restart the hourly limit.** Otherwise a launch-time rewrite
  would mask a real change made moments later.
- **Backups you took yourself are never deleted**, at any keep count, nor are pre-restore
  backups.
- A dropped watcher event is a latency problem, not data loss: the next write, shutdown or
  launch reconciles by content hash.

### Verified

- The four claims in the Settings dialog's copy, *"waits a minute"*, *"at most one an hour"*,
  *"keep the last 30"*, *"backups you took yourself are never deleted"*, all match the
  implemented constants. Two are pinned directly by a test named for it.

---

## [0.2.0], 2026-08-16

**Phase 2: the snapshot store.** Backups can now be written, listed, renamed, restored and
deleted. Still no automatic capture and still no user interface, every backup has to be asked
for, in code.

**186 tests passing, 83.0% line / 81.2% branch coverage.**

### Fixed

- **Backups no longer live inside `LocalState`.** This is the defect the fork existed to fix:
  upstream writes them beside `Settings.json`, inside the MSIX package directory, which
  resetting or uninstalling Wave Link deletes wholesale, destroying every backup along with
  the thing you wanted to recover from. Pinned by a test that deletes the entire `LocalState`
  directory and then verifies the snapshot still restores.
  ([`technical-debt.md`](_docs/technical-debt.md) §1.1, audit finding 1.)

### Added

- **`Snapshots/`.** The store, outside `LocalState`, defaulting to
  `%LOCALAPPDATA%\WaveLinkBackup`.
  - `manifest.json` holds identity, so **renaming is a metadata write**, no directory moves,
    nothing sanitised, and a backup called `Mic chain 3/4"` is just a string. Upstream's
    filename regex blocked custom names, custom locations and a cheap rename at once.
  - `SnapshotGuard` replaces `ValidateManagedPath`: it asserts *"we wrote this and it still
    matches its hashes"* rather than *"this filename fits a pattern"*. Same protection against
    a mistyped path, **plus** detection of a backup corrupted after it was written, by a
    failed sync, a bad disk, or a hand edit.
  - `schemaVersion` from the first write. A manifest from a newer version is refused with a
    readable message, never partially read.
- **`Restore/`.** The assembled sequence: verify, plan, **pre-restore snapshot**, close both
  processes, write, relaunch, confirm from the log.
  - The pre-restore snapshot is **unconditional and has no parameter to skip it**. It is what
    makes the destructive button safe to press.
  - `RestorePlan` is pure and computes the restore dialog's *now vs. after* table, so phase 5
    renders rather than calculates.
- **`waveLinkVersion` in every snapshot.** Read from `Settings.json`'s
  `Update.LastUpdateVersion`, and from the log banner including the `(Beta)` channel marker.
  When a restore fails, the first question is whether the config is bad or the validator
  changed.
- **`IClock`.** The third seam, added now because snapshot timestamps are the first thing
  that genuinely needs it.

### Changed

- `IFileSystem` gains `CreateDirectory` and `DeleteDirectory`.
- `SettingsAnalysisResult` carries `WaveLinkVersion`; `RestoreVerdict` carries `Version` and
  `Channel`.

---

## [0.1.0], 2026-08-16

**Phase 1: `WaveLinkBackup.Core`.** The library everything else will call. No user-facing
application yet, the CLI and WPF shells are stubs that exist to prove the reference graph.

**93 tests passing, 81.2% line / 81.8% branch coverage.**

### Added

- **`WaveLinkBackup.Core`.** Settings discovery, validation, health fingerprinting, safe
  replacement and log verification, in a functional-core shape: everything that can be pure is
  pure, and all IO sits behind two seams.
  - `Analysis/`, pure, no IO, no constructors. Duplicate-key scanning, the health
    fingerprint, log verification. 96, 100% covered.
  - `Discovery/SettingsLocator`, globs `Elgato.WaveLink_*`, requires `Settings.json` to
    exist, refuses to guess between multiple installs, and never looks at `%APPDATA%`.
  - `Io/`, shared-mode reads, retry-once on a torn read, atomic replace with a rollback copy.
  - `Process/WaveLinkProcess`, graceful close, kill on timeout, then **verify**; covers both
    `Elgato.WaveLink` and `WavelinkSEService`.
  - `Results/`, `Result<T>` for expected failures, exceptions reserved for bugs.
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
- **`LICENSE`.** MIT, carrying upstream's copyright line verbatim alongside ours.
- Seven read-only integration tests against a real Wave Link install, skipped when absent.

### Fixed relative to upstream

- **`WavelinkSEService` is now closed too.** Upstream only ever looks for `Elgato.WaveLink`, so
  its "verified exited" check can pass with half of Wave Link running and a write can still
  race the service's flush. Audit finding 6; worth offering back.
- **Reads use `FileShare.ReadWrite | FileShare.Delete`.** `Settings.json` is locked while Wave
  Link runs, so `File.ReadAllBytes`, upstream's call, fails on most captures.
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

## [0.0.2], 2026-08-16

Documentation only. A probe against a live Wave Link install answered one open question and
invalidated two documented decisions; corrections were applied at source rather than noted and
worked around. See
[`_docs/sessions/2026-08-16-phase-1-probe.md`](_docs/sessions/2026-08-16-phase-1-probe.md).

## [0.0.1], 2026-08-16

Documentation only. The `_docs/` system, seeded from `SPEC.md` and the design handoff: 8 ADRs,
8 gotchas, a recipe, an upstream audit, an 8-phase roadmap, a glossary and a technical-debt
register. Root `README.md`, `CHANGELOG.md` and `.gitignore`.

---

## Release checklist

A gate, not a version. Before any public `1.0.0`:

- [x] **Redacting "copy diagnostics" action.** Shipped 2026-08-20, in Settings and as
      `wlbackup diagnostics`. Serial numbers, the Windows user name and snapshot display names are
      removed; the settings file is never included at all; nothing is uploaded. §6, paid.
- [x] **Packaging decided deliberately.** The app and CLI both publish framework-dependent from
      their csprojs (no self-contained flag anywhere), so a local publish and CI cannot produce
      different artifacts; the .NET 10 Desktop Runtime is a documented prerequisite, not a payload.
- [x] **MIT attribution** preserved for upstream, in `LICENSE` and `README.md`.
- [x] **Windows-only stated above the fold** in `README.md`.
- [x] **The VST3 bundle path covered by a fixture test.** Both directions, `TierCaptureTests`
      and `TierRestoreTests`. Closed in phase 6, §2.3.
- [ ] **Code signing.** Owed before the updater is trusted in anger: the checksum it verifies
      proves the bytes are the ones the release named, not that the release is ours. It is also
      what would make elevating during an update defensible, see
      [ADR-012](_docs/decisions/ADR-012-check-only-updates-with-a-staged-swap.md).
- [ ] **Run the release loop once, end to end.** The pipeline and the updater are built and
      unit-tested; the download, the swap and the relaunch have met fixtures and temp directories
      only. Record the result in
      [the runbook](_docs/operations/runbooks/releasing-and-updating.md).
      **Half of it is done:** v0.7.0 was packaged locally on 2026-08-21 with the workflow's own
      steps, and the archive, the checksum and both published binaries were verified, the runbook
      records what that settled. The half that remains needs a remote: a tag push, the release CI
      creates, and the app reading it back.
- [ ] **Set `WLBACKUP_UPDATE_OWNER` / `_REPO`** in the published build, once the repository has a
      remote. Until then the UPDATES section correctly hides itself, which also means nobody has
      exercised it.
- [ ] **Decide the pre-release rule** before a `-beta` tag exists. `1.4.0-beta.2` currently reads
      as `1.4.0` and would be offered to everyone.
- [ ] **The by-eye pass.** §4.15, the dialog frosting has never been seen, and nothing in the
      suite can assert that a blur rendered.

<!-- Add the [Unreleased] / version compare links here once the repository has a remote. -->
