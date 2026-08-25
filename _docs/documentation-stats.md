---
title: "Documentation Stats"
status: published
created: 2026-08-16
updated: 2026-08-25
tags: [meta, stats]
---

# Documentation Stats

The living tally, the doc-ecosystem delta log, and the topical cross-reference index.

Update this file **in the same commit** as the document it counts. See
[README.md](README.md) → *Updating documentation stats* for the trigger table.

> This is the **doc-ecosystem** changelog. `CHANGELOG.md` at the repo root is the
> **engineering** changelog. Same commit is fine; different voices.

---

## Tally

*As of v0.7.5 plus unreleased work on `feat/update-available-notification` (2026-08-25).*

| Artifact | Count |
|---|---|
| ADRs | 18 |
| Gotchas | 33 |
| Patterns | 5 |
| Recipes | 2 |
| Runbooks | 1 |
| Audits | 3 (6 + 15 + 4 findings, one open question) |
| Sessions | 28 |
| Plans | 14 |
| Dev-phase documents | 11 (of 8 phases; phases 6 and 7 detailed, plus the index, spec-coverage and post-1.0) |
| **Tests** | **1,668 passing** — Core 522 · CLI 100 · App 1,046 |

> The tally sat at *"as of 0.5.1"* through the whole of 0.6.0 and was corrected on 2026-08-19.
> The trigger table in [README.md](README.md) says to update this file in the same commit as the
> document it counts; that is what did not happen, and it is the failure mode a running total in
> a separate file has.

**Patterns went 0 → 4** when the first production code shipped, which was the trigger recorded
in [README.md](README.md). Each names its real callers and the test holding it down; none was
written before the code it describes.

**Runbooks went 0 → 1** on 2026-08-20. Its trigger, recorded in [README.md](README.md), was
*"there is a running system to operate — realistically, the first release"*; building the release
pipeline and the in-app updater is that trigger firing. **Two rows of that table remain**
(`operations/diagrams/`), and the mechanism has now fired twice, which is the argument for leaving
them.

**Tests.** Upstream carries 40 against ~48 KB of source. Phase 1 ships 93 against a smaller
Core — the ratio the seam interfaces were inherited for ([[ADR-004]]).

---

## Recent additions

### The app says an update exists, without being asked (2026-08-25)

**A gap that every setting denied.** The design says *"check for updates on its own — weekly, on by
default"*; the code checked weekly and the setting was on. But the check ran from the Settings
dialog's `Loaded` handler, so the real cadence was *"weekly, the next time you happen to open
Settings"* — and Settings is a place people visit once, to pick a folder. Nothing looked wrong
anywhere: the gap was entirely in *where the check was attached*.

- **One ADR.** [[ADR-018]] — the check moves to startup, and an available update is said on the
  status strip, in the tray menu, and once per version as a notification. Its *Alternatives
  considered* carries six, including the two that look cheapest: leaving the check where it was
  (cosmetic — the segment would stay blank) and checking on every launch (a network call per launch
  for a figure the design set deliberately).

- **It spends the design's "exactly two notifications" budget, and says so.** That rule is worth
  reading precisely: *"A successful backup NEVER notifies. A safety net that congratulates itself
  weekly gets muted."* It is about the app talking about itself doing its job — routine, repeating.
  An update notice is rare, is about a version rather than a run, and fires once per version. The
  guard the rule protects is untouched and still enforced by the type, with a test that says so.

- **Two gotchas, and neither was findable until the one before it was fixed.**
  [[every-update-fails-its-checksum]] — the feed paired the app's archive with the CLI's digest,
  and had since 0.7.2 split the CLI into its own artifact. Its "how to avoid it" is the useful
  half: every payload in `UpdateFeedTests` carried one archive and one `.sha256`, and with one of
  each, "take any asset ending .sha256" and "take the right one" are the same test.
  [[the-update-installs-nothing-and-says-nothing]] — one attempt at the directory swap, and a
  failure path with thorough error handling and nowhere to report to. The handling was not
  missing; the destination was.

- **One session.**
  [the-update-path-meets-a-real-release](sessions/2026-08-25-the-update-path-meets-a-real-release.md)
  — three separate update bugs, each hidden behind the one before it. Its *What did not work*
  carries the verification script that reported a false mismatch, and why reading the code was
  never going to find the swap bug.

- **Help gained a standard About section**, composed from `AboutDialogModel` rather than restating
  it. A test builds Help from an invented about model and asserts the section moves with it, so a
  revert to hard-coded copy fails rather than rots.

- **The interval moves from a week to a day**, and one existing test had to be rewritten rather than
  deleted: it asserted a check is *not* due a day after the last one, which was correct for weekly
  and is now the opposite of the shipped behaviour. The boundary is still the thing worth pinning,
  so it moved to the hour either side of the new one.

- **Tests: 1,621 → 1,668.** The notice, its cadence and the daily interval; the checksum pairing
  against a real release's asset list; the swap breadcrumb; the About section's seam; one for the
  menu line shipping collapsed. Two existing guards moved: the tray-menu order test gained the
  new item, and the template test stopped counting menu items — a hardcoded 10 had turned "no
  template failed to apply" into an assertion about arithmetic.


### The debt list gets down to two, and the tools that get it there (2026-08-25)

**Three of the four open entries were worked in one branch,
`debt/close-remaining`.** §2.4 closed outright; §7.6 and §8.2 are now blocked on nothing but the
half only a human can do, because the mechanical half is scripted.

- **§2.4 closed, and the answer was no.** Porting `WindowsAudioEndpointInspector` is what let the
  question be asked at all. Classic `[ComImport]` fails trimming two different ways — IL2072 for
  upstream's `Type.GetTypeFromCLSID` activation, IL2050 for built-in COM marshalling — and
  `[GeneratedComInterface]` works: clean AOT publish, and 96 real endpoints enumerated by the
  native binary. Enumeration only; the editing half stays post-1.0. The evidence table is in
  [the archive](archive/technical-debt-closed.md).

- **Two developer tools, and a guard over them.**
  [`tools/plugin-resolution-experiment.ps1`](../tools/plugin-resolution-experiment.ps1) runs §7.6's
  reversible file surgery and records the verdict where it survives the shell;
  [`tools/seed-fixture-store.ps1`](../tools/seed-fixture-store.ps1) writes the five rigs §8.2's
  checklist item 5 needs, so the sitting starts at the looking. `ToolScriptGuardTests` extends the
  share-mode rule to `tools/*.ps1` — the first script repeated the exact mistake
  `SourceGuardTests` has caught in C# since phase 1, because that guard only ever scanned `*.cs`.

- **The design export is recorded as absent.** `.git/info/exclude` carries
  `_docs/operations/design/`, so the ~40 documents linking into it resolve on one machine and
  nowhere else. [README.md](README.md) now says so, and names the risk that leaves: two files in
  there are authored in this repo and protected only by a provenance banner.

- **One ADR.** [[ADR-017]] — COM interop is source-generated, and `Core` gets `AllowUnsafeBlocks`.
  Its *Alternatives considered* is the useful half: dropping `IsAotCompatible`, suppressing IL2050,
  hand-rolling vtable calls through function pointers, and putting the inspector in the shells —
  each with the reason it lost. It also reverses a refusal `RecycleBin` documented deliberately,
  and says why that refusal still stands where it was written.

- **One gotcha.** [[com-interop-stops-compiling-the-moment-the-project-is-aot-compatible]] — the
  same code that builds in a console app is two different build errors in an AOT-compatible
  library. Its "plausible explanation" section names the expensive wrong turn: suppressing the
  warning, which silently revokes NativeAOT for the whole solution and converts a build error into
  a crash on a user's machine.

- **One session.**
  [splitting-the-debt-list-and-closing-what-was-left](sessions/2026-08-25-splitting-the-debt-list-and-closing-what-was-left.md)
  — its *What did not work* section carries four dead ends, including the guard that fired on the
  next script written after it, and the tier list that disagreed with the checklist it pointed at.

- **One more gotcha, from a real high-contrast scheme.**
  [[dialogs-are-see-through-in-high-contrast]] — every dialog is a layered window, high contrast
  made both its fills transparent, and the result was a hole with a border round it. Nothing in the
  suite could assert it and nothing did: the dialog laid out, bound and closed correctly. Found by
  a person looking at a screen, which is item 5 of the by-eye checklist's whole argument.

- **The debt list is empty of work.** §7.6 and §8.2 both closed on 2026-08-25, on the reference rig
  rather than on paper. The experiment answered §7.6 — Wave Link resolves by `PluginId` and repairs
  `FilePath` — and item 5 of the by-eye checklist closed §8.2. What remains in `technical-debt.md`
  is §3 (known-wrong deliberately) and §5 (the hazard table), and neither is owed a commit. Both
  runs found bugs in the tools that ran them, which is recorded in the entries rather than tidied
  away.

- **A review round, and a CI flake that was not this branch's.**
  `SettingsDialogViewTests.The_two_plain_language_notes_render_their_lead_clauses` failed on one CI
  run and passed on another at the identical commit — the flake its own comment already described
  from v0.7.2. The mitigation had inverted the priority argument: `Dispatcher.Invoke` at priority P
  returns once everything *higher* than P has run, so moving from `Background` to `Input` drained
  less rather than more. `Wpf.Drain` pushes a frame at `SystemIdle` and keeps pumping, so work the
  binding engine queues mid-drain does not escape it.

- **Tests: 1,598 → 1,621**, in Core and App. Eight for the endpoint inspector and the redaction rules
  over its diagnostics section, five for the tool-script guard, four for the fixture seeder.


### The debt list is split: what is owed, and what was paid (2026-08-25)

**`technical-debt.md` went from 1,646 lines to 257.** Thirty-six of its thirty-nine numbered entries were closed,
withdrawn or paid, and reading the file meant scrolling past all of them to find the four that are
not. The closures moved to
[archive/technical-debt-closed.md](archive/technical-debt-closed.md) verbatim — not summarised,
because the reasoning that closed an entry is the part worth having later, and several read as the
record of *why* a thing is the way it is rather than as a ticked box.

- **What stayed is four entries and one table.** §7.6 (where a restored plug-in goes when its
  folder is unwritable — one experiment on a live rig), §8.2 (the by-eye sitting the §8.6 verdict
  and matrix are still owed), §2.4 (a re-open trigger for when `WindowsAudioEndpointInspector`
  lands), and §3 (known-wrong deliberately, which is permanent by design and is not owed work).
  §5's table of numbers-that-look-like-constants stayed as a standing hazard list; the audit that
  closed it went to the archive.

- **Section numbers are unchanged in both files.** Twenty-odd ADRs, audits and session notes cite
  "§4.18" or "§8.5" in prose, and none of them uses an anchor link, so preserving the numbering
  across the split keeps every one of those references resolving.

- **The tier list was rewritten, and the original kept.** Tier 1 — closeable by a commit — is
  empty, so the live file carries only the human-with-eyes sitting and the two Tier 3 items waiting
  on facts from outside the repo. The original three-tier ordering is the archive's appendix,
  because it records how the 2026-08-22 pass was sequenced and why each item sat where it did.


### v0.7.4 — a restore puts the service back, and the trash row finally refreshes (2026-08-24)

**The version that ships the two features and one fix above.** A restore now brings the Wave Link
service back before it relaunches the app ([[ADR-016]]), emptying the trash reports its progress as
it goes, and the settings dialog's trash row no longer shows a stale count after emptying.

- **One ADR.** [[ADR-016]] — a restore brings the service back before it relaunches. The new
  `IWaveLinkService` seam sits beside `IWaveLinkProcess`, the orchestrator owns the "service, then
  app" ordering, and a failed start is reported rather than fatal because the settings are already
  written by that point. Its *Alternatives considered* is the useful half: starting it from the
  relaunch step, making failure fatal, prompting the user, putting the call in the shells, and
  starting it before the close — each with the reason it lost.

- **One gotcha.** [[the-row-shows-stale-data-after-you-update-it]] — a WPF view-model property that
  is an auto-property never raises `PropertyChanged`, so re-assigning it updates the field and not
  the screen. The trash row's first bind coincided with the window opening, which is why the value
  looked right at first and wrong after every later write. Its "plausible explanation" section names
  the trap that costs time: the data in memory was always correct, so chasing the count leads away
  from the one-line declaration that is the real defect.

- **One session.** [service-autostart-and-trash-progress](sessions/2026-08-24-service-autostart-and-trash-progress.md) —
  what happened, including the progress test that used `System.Threading.Progress<T>` directly and
  never received its reports on a bare test thread (no synchronization context to pump), fixed by a
  synchronous fake that records the exact `(Done, Total)` sequence.

**Counts moved:** ADRs 15 → 16 · gotchas 28 → 29 · sessions 25 → 26 · tests 1,587 → 1,598 (Core
494 → 504 — three `RestoreOrchestratorTests` for the service seam and two `TrashTests` for the
progress callback; App 993 → 994 — the trash-row refresh test that asserts the *rendered* text
changes, which is what an auto-property silently breaks).

### v0.7.3 — the startup crash is fixed, and releases carry their notes (2026-08-23)

**The version that ships the fix above.** The crash recorded in
[the-app-dies-before-the-window-with-a-culture-error.md](knowledge-base/gotchas/the-app-dies-before-the-window-with-a-culture-error.md)
is resolved — `InvariantGlobalization` is gone from the app's csproj, replaced by
`SatelliteResourceLanguages=en` — and the release pipeline now leads the GitHub release page with a
*What's new* section pulled from [CHANGELOG.md](../../CHANGELOG.md) for the tagged version, so a
release says what changed rather than only where to download. The updater is untouched: it still
reads only the `*app-win-x64.zip` asset and its `.sha256`, never the body.

**Counts moved:** none. No new ADR, pattern or gotcha beyond the one this version fixes; the test
tally is unchanged at 1,587 (the fix is a build-config change with no new surface to assert
against — a crash that fires before the window exists has nothing in the suite to hold it down, and
the crash report in `%LOCALAPPDATA%\WaveLinkBackup\crash-report.txt` is what makes a future
recurrence legible).

### The startup crash that named its own cause: invariant globalization was not how you trim satellites (2026-08-23)

**A new gotcha, [the-app-dies-before-the-window-with-a-culture-error.md](knowledge-base/gotchas/the-app-dies-before-the-window-with-a-culture-error.md),** records a crash that
reproduced twice on the dev machine and whose event-log entry pointed at WPF's font cache as if
it were a font problem. It was not. `<InvariantGlobalization>true</InvariantGlobalization>` in
`WaveLinkBackup.App.csproj` — added to shed WPF's 13 culture satellite assemblies (~9 MB) from an
English-only UI — puts the *entire process* in invariant mode, where `CultureInfo("en")` throws.
WPF's font cache constructs that culture in a static constructor on the first `TextBlock` measure,
which is the first thing `Window.Show()` does: the app dies inside layout, before any of our code
after `ShowMainWindow()` runs.

The fix swaps the process-wide switch for the targeted one — `<SatelliteResourceLanguages>en</SatelliteResourceLanguages>`
trims the *resources* while leaving full globalization (and therefore working text rendering)
intact. Same apparent goal, one of them breaks the app. Verified end-to-end: republished exe runs
clean, no `CultureNotFoundException` in the event log, full suite green at 1,587.

**Counts moved:** gotchas 27 → 28. No test change — the guard is the absence of the flag, and a
crash that fires before the window exists has nothing in the suite to assert against; the crash
report in `%LOCALAPPDATA%\WaveLinkBackup\crash-report.txt` (the §8.1/§8.1a mechanism this entry
leans on) is what makes a future recurrence legible instead of a mystery.

### The debt list's last two code items closed: a crash report that names its machine, and the INPUTS verdict (2026-08-22)

**§8.1a and §8.6 of [technical-debt.md](technical-debt.md) close in one commit**, with Tier 2's
original four looks ticked against [the by-eye checklist](operations/design/screen-1-by-eye-checklist.md)
and a fifth look opened for the surfaces this same commit shipped.

- **§8.1a — what an unexpected fault can still say.** The design answer: no thirteenth error in XAML
  (that is what [[ADR-004]] exists to prevent). The crash's *surface* is the redacted report itself,
  plus a pointer to it from the one place the app can still speak after an unexpected fault — the
  restore-failure strip. `CrashReportWriter` now carries an environment block (app version, OS,
  culture, runtime) and passes the stack through Core's `Redaction.Text` before writing, so a report
  that lands in a bug report has already had its serials and username stripped; redaction failing
  marks the text as unredacted rather than dropping the report. The pointer is a pure helper —
  `AppErrorMapper.CrashReportPointer` — which appends the path only for failures no designed error
  surface owns, so a known failure never gets a second explanation. Copy-to-clipboard and
  post-crash behaviour stay out of scope by the same rule.
- **§8.6 — the INPUTS strip reads cramped past five cells.** The design answer was already in the
  package (variation 2B); this is the commit it scoped. The row's INPUTS cell becomes a verdict —
  one glyph, one word, a mono count line (`5 INPUTS · ALL NAMED`) — and the details dialog gains the
  "WHERE EACH INPUT IS HEARD" matrix: one column per mix, one row per channel, a dot where that
  channel feeds that mix. `ChannelRow` carries `MixMembership` (one bool per mix, paired with the
  headers by position); the routing line stays as the sentence, the board is the picture. No manifest
  change: both halves read data the view models already carried.

**Counts moved:** tests 1,574 → 1,587 (App 980 → 993 — five `CrashReportWriterTests` grown from
exception-only to environment-and-redaction, plus five new `CrashReportPointer` cases in
`ErrorCatalogTests`). No new ADR, gotcha or pattern; the checklist's item 5 is the record of what
is still owed a human.

### §4.15 closed: the dialog frosting renders, verified by eye (2026-08-22)

**The last open item in [technical-debt.md](technical-debt.md)'s §4 is closed.** §4.15 — 0.5.1's
dialog frosting had never been seen, and nothing in the suite can assert that a blur rendered — was
the one look only a human could do. Opening a dialog shows the window behind it blurred, not merely
dimmed by `WlScrim`, so the `SetWindowCompositionAttribute` call is doing its job on this build:
the frost stays and the item closes. Ticked on [the by-eye checklist](operations/design/screen-1-by-eye-checklist.md)
with a record-of-sitting line; §4 now reads **ALL CLOSED** and the open list drops from six to five.

**Counts moved:** none — no code, no ADR, no new document (the checklist already exists). This is a
closure by observation, which is exactly what Tier 2 of the closing order is for.

### Tier 1 of the debt list closed: a crash now leaves a report, and the by-eye checklist exists (2026-08-22)

**The commit-tier of [technical-debt.md](technical-debt.md)'s closing order is done.** Two items
closed in code and one as a watch:

- **§8.1 (cheap half)** — `App` now installs a `DispatcherUnhandledException` handler and an
  `AppDomain.UnhandledException` backstop, both writing through one `CrashReportWriter` that appends
  the exception's full `ToString()` to `crash-report.txt` beside `shell.json` in
  `%LOCALAPPDATA%\WaveLinkBackup`, on the way down. The writer never throws — a crash handler that
  threw on a crash path would put us back at the original incident. Guarded by five tests in
  `CrashReportWriterTests`. The expensive half (the thirteenth error surface in XAML) re-opened as
  §8.1a, because it is a design question, not a diff — and the file write unblocks that pass by
  giving it real exception shapes to look at instead of guesses.
- **§8.3** — closed as a *watch*, not a fix. There was never code to write; the debt was the risk of
  an unmeasured font fallback in `InputSlots.CharacterWidth`, and the guard test already held both
  directions. The rule that keeps it closed — nothing ships a change to the mono face, the 9.5px
  label size or the .06em tracking without re-running the measurement in the same commit — now lives
  in [[ADR-014]]'s Consequences as an explicit **Watch rule**, where the decision it guards lives.
- **§8.2's enabler** — `operations/design/screen-1-by-eye-checklist.md` written. Every Tier 2 item
  pointed at a file that was not in the repo; without it, each look is ad hoc and none of them leave
  a record that they happened. It lists the four looks with a box, machine and date each, in the
  order the sitting should go, plus a record-of-sittings table so the checklist's own history stays
  in the repo.

**Counts moved:** tests 1,569 → 1,574 (App 975 → 980, the five `CrashReportWriterTests`). No new ADR,
gotcha or pattern — the checklist lives in the vendored `operations/design/` folder, which is exempt
from the tally, and §8.3 owed a rule, not a document.

### The release shrank to 7.6 MB: framework-dependent, two artifacts (2026-08-22)

**A shape decision that closed [technical-debt.md](technical-debt.md) §8.5 with a before/after,
and rewrote the runbook's contract around it.** The app publishes **framework-dependent** — it
requires the .NET 10 Desktop Runtime rather than carrying it — and the CLI is its own release
artifact instead of riding inside the app's archive. Measured locally, exactly as CI runs it:

| | v0.7.0 (self-contained, one archive) | v0.7.2 (framework-dependent, two archives) |
|---|---|---|
| App archive | `WaveLinkBackup-0.7.0-win-x64.zip` — **101.2 MB** | `WaveLinkBackup-0.7.2-app-win-x64.zip` — **7.62 MB** (12 files, 26.8 MB raw) |
| CLI archive | Inside the app's archive (`wlbackup.exe`, 70.4 MB of it) | `WaveLinkBackup-CLI-0.7.2-win-x64.zip` — **0.22 MB** (3 files, 0.48 MB raw) |
| .NET runtime in the download | Twice (the app's loose copy + the CLI's bundled copy) | **Nowhere** — both resolve it from the machine's installed .NET 10 Desktop Runtime |

Three changes together: a satellite-locale trim in the app's csproj (originally
`InvariantGlobalization=true`, replaced by `SatelliteResourceLanguages=en` in v0.7.3 — see
[[the-app-dies-before-the-window-with-a-culture-error]]); the CLI's `PublishSelfContained` flipped
to `false`, keeping `PublishSingleFile`; and `release.yml` publishing two artifacts into separate
directories. The updater's contract
widened with it — `UpdateSource.AssetSuffix` defaults to `app-win-x64.zip`, so a release carrying
both assets resolves to the app, pinned by `A_release_with_both_app_and_cli_assets_picks_the_app`.

**The trade, stated rather than hidden.** A machine without the .NET 10 Desktop Runtime cannot
start the app, and because a framework-dependent WPF app fails at native load before managed code
runs, there is no in-app surface to say so — the user gets the stock .NET error dialog. The README
names the prerequisite; that is the whole mitigation.

**Documentation touched:** [releasing-and-updating.md](operations/runbooks/releasing-and-updating.md)
rewritten for the two-artifact shape, with the measurement carried into a dated block and the
symptom table's asset error corrected to `...HAS NO APP-WIN-X64.ZIP`; technical-debt.md §8.5 closed
with the before/after and its original options table retained as the reasoning that led here;
[[ADR-012]]'s Context corrected from "self-contained publish" to what it is now, with the swap
mechanism noted as indifferent to which; phase-7-release.md's self-contained recommendation marked
**superseded** rather than edited away; the README's Building section names the runtime
prerequisite and drops `--self-contained true`.

**Counts moved:** tests 1,568 → 1,569 (App 974 → 975, the dual-asset regression test). No new
documents, no new gotcha — nothing here is a mistake that happened; it is a decision made and
measured.

### Recent additions (v0.7.2 — Help and About: the shell's two information surfaces)

**Cut 2026-08-22.** The tray menu gained Help and About…, and the caption bar a "?" beside the
Settings gear. Both dialogs are static content behind a model record — `HelpDialogModel` is pure
constant copy, `AboutDialogModel` adds only the version (read from `ReleaseVersion.Current`, so
it cannot drift from the UPDATES section) and two environment-sourced links that hide themselves
when absent. No ADR: the shape follows [[ADR-004]]'s existing rule rather than choosing between
alternatives, so this block plus the plan carry it.

- **Plan:** [2026-08-22-phase-5-plan-11-help-and-about.md](plans/2026-08-22-phase-5-plan-11-help-and-about.md)
  — goal, architecture and the executed block, including the one deviation (the "?" is text in the
  mono font because the design package has no help icon).
- **Session:** [2026-08-22-help-and-about-dialogs.md](sessions/2026-08-22-help-and-about-dialogs.md)
  — what happened, including the five view-test failures from the first draft (all in the 0.5.1
  design-audit family: a view no test had ever constructed) and the Settings gear that was altered
  and then restored to its committed markup.

**Counts moved:** sessions 24 → 25 · plans 13 → 14 · tests 1,551 → 1,568 (App 957 → 974, the five
new dialog view tests). No gotcha: nothing here is a mistake that happened in production — the
test failures were caught by the suite before anything shipped.

### Recent additions (v0.7.1 — the three fixes to 0.7.0's phase)

**Cut 2026-08-22.** A patch release: three fixes to the phase 0.7.0 already shipped, none of them
new surface. The documentation delta for two of the three lives in the dated blocks below — this
block exists so a reader scanning by version finds all three under one heading, and because the
daily-backup fix never got its own block (it updated [glossary.md](glossary.md) and
[screens/14-backup-timing.md](operations/design/screens/14-backup-timing.md) in place, with no
counts moving).

- **The daily backup now actually fires.** The 0.7.0 wording — an ordinary automatic capture after
  today's set time "covers" the day and suppresses the daily copy — was a bug on any machine where
  Wave Link writes settings during the day, which is most of them. Only today's own copy of this
  one now covers the day. The glossary entry and the design screen were corrected in place; no new
  document, no count moved.

- **The snapshot list updates after an automatic capture.** No documentation delta: a behaviour
  fix inside the existing tick, with its tests in `AutoBackupCoordinatorTests`.

- **Clicking a row selects the row you clicked** — the structural fix for
  [[scrolling-the-list-selects-a-row]], written up in full in the block below. It also closed
  [technical-debt.md](technical-debt.md) §8.4 (the list that did not virtualise), which is why the
  debt list's header moved from seven open items to six.

**Counts moved:** none. The session note and the gotcha correction landed in the block below,
under their own date; this release adds no new documents.

### The scroll-click jump was two scroll owners, not recycling (2026-08-22)

**A correction to [[scrolling-the-list-selects-a-row]], made because neither of the first two fixes
held.** The 2026-08-21 session attributed the symptom — scrolling to the end, then clicking a row
selects the *bottom-most visible* row instead of the one clicked — to `IsSynchronizedWithCurrentItem`
left at its default `True`, and removed it. That was a real latent defect (the view's currency
driving `SelectedItem`) but not what the user saw. A second pass blamed
`VirtualizingPanel.VirtualizationMode="Recycling"` and set it to `Standard`; that did not hold
either — the jump persisted in the debug build, unchanged.

The true cause was structural, and neither fix touched it: **the list had two scroll owners.** An
outer `ScrollViewer` (`ListScrollViewer`) did the real scrolling (wheel events forwarded into it),
while the ListBox's own inner ScrollViewer was disabled but still carried the
`VirtualizingStackPanel`. A `VirtualizingStackPanel` tracks only the offset of the ScrollViewer
that *owns* it — the frozen inner one — so when the outer viewer moved the content, the panel never
learned. Realized containers stayed anchored to the top while the pixels showed the last rows, and
a click hit-tested to a stale container holding a *different* `SnapshotRowViewModel`, writing that
row into the TwoWay `SelectedItem` binding.

The fix removes the second owner: the outer `ScrollViewer` and its wheel-forwarding shim are gone,
and the ListBox's inner ScrollViewer is now the only one that scrolls (`VerticalScrollBarVisibility="Auto"`).
A second, independent defect had to be fixed for that to work: with a **grouped** list and
`CanContentScroll="True"` (item scrolling), WPF treats each group as one scroll unit and the inner
viewer's extent collapses to ~1px — it cannot see through the group container to the real content
height. Setting `CanContentScroll="False"` (pixel scrolling) measures the actual pixel height;
the `VirtualizingStackPanel` still virtualizes via its viewport provider.

The gotcha now names both causes, with the two-scroll-owner mismatch first and the grouped extent
collapse second, and records that recycling was a wrong hypothesis held in the tree by one commit.
A fifth test guards the single-scroll-owner invariant — after scrolling, every realized container
holds its own data item — so the App suite stands at **957** (total **1,551**; the count is down
from 962 because the wheel-forwarding shim and its two tests were deleted with the outer viewer).

Session note: [scroll-selection-jump](sessions/2026-08-22-scroll-selection-jump.md).

**Counts moved:** sessions 23 → 24 · tests 1,554 → 1,551 (App 962 → 957; the wheel-forwarding shim
and its two tests were deleted).

### Recent additions (v0.7.0 — the release phase, and a bigger rig)

**Cut 2026-08-21.** The version that packages phase 7 and answers what a nine-channel rig needed.
The documentation delta for it is the two blocks below — the elevation audit and the debt
clearance landed in the same version — plus:

- **A new debt section, §8**, which is the honest cost of this version: three surfaces built past
  the design package with no design pass behind them, no surface for an unexpected exception, a
  measured constant, and a list whose virtualisation markup is inert. **Four entries, all opened by
  the work in this release rather than inherited** — the first time §1–§7's pattern of "found, then
  closed" has been joined by a section that only grows.

- **A correction to §5**, which had claimed a rule was structural when it was true of Core and
  false of the shell. Recorded rather than edited away.

- **`releasing-and-updating.md` is still marked *the loop has never run end to end*.** This
  release does not change that: it was built locally, exactly as the runbook says a local publish
  reproduces CI, and the repository still has no remote to publish to. The first real tag push is
  what flips that provenance line.

### A theme choice, a crash, and what's in a backup (2026-08-20)

**Three ADRs, three gotchas and a new debt section**, from a session that started as a screenshot
comparison and turned into four pieces of work. The through-line is worth naming: **three of the
four defects were invisible to tests that read source and visible to tests that render it.**

- **Three ADRs.** [[ADR-013]] puts the theme preference behind the existing `ISystemTheme` seam —
  a decorator, so none of the six consumers changed and none of them can disagree. [[ADR-014]]
  widens the health strip to the rig and moves collapse from a high-water mark to a comparison with
  the previous snapshot. [[ADR-015]] reads a backup's own settings file on demand rather than
  extending `manifest.json`, which is what makes the details view work on every backup already on
  disk.

- **Three gotchas, and they share a shape.**
  [[a-chip-draws-its-box-and-not-its-label]] — a `ContentTemplate` renders against `Content` and
  the triggers around it read `DataContext`; every source-text guard passed while the column drew
  three empty pills. [[pressing-back-up-now-closes-the-whole-app]] — a `DependencyProperty` written
  from a `Task.Run`, diagnosed from the Windows event log after the source reading went the wrong
  way. [[every-older-backup-turns-amber-after-adding-a-channel]] — a verdict computed against the
  store's peak, which rewrites itself on every older row the moment the peak moves.

- **A correction, not a silent edit.** `technical-debt.md` §5 claimed the "5 inputs is one user's
  rig" rule was *"held by `HealthFingerprint` comparing against that user's own previous
  snapshot"*. That was true of `HealthFingerprint` and false of the shell, which sized its strip at
  a hard five and judged against the peak. §5 now says so, and points at the tests that hold it
  instead of the paragraph that claimed it.

- **A new debt section, §8**, for what building past the design package costs: no surface for an
  unexpected exception (§8.1), three surfaces no design pass has seen (§8.2), and a measured
  character-width constant that only a rendering test can keep honest (§8.3).

- **And one more the next day.** [[the-list-will-not-scroll-with-the-wheel]] — the main list never
  scrolled by wheel, because a disabled `ScrollViewer` still marks the event handled. Its *how to
  avoid it* is mostly about the TEST: raising the event on the ListBox proves nothing (the
  swallowing ScrollViewer is inside its template), and `RaiseEvent` raises one event where real
   input raises two. Both mistakes were made before the test failed correctly.

- **And one more on the same list, the next day.** [[scrolling-the-list-selects-a-row]] — scrolling
  the backup list to the end auto-selected a row with no click, because `GroupsHost` left
  `IsSynchronizedWithCurrentItem` at its default `True`, so the view's *currency* drove the
  `SelectedItem` binding. The wheel was exonerated by measurement (it moves neither focus nor
  currency); the fix is one attribute off, held down by four tests against the real window — the
  defect itself plus End/Home still selecting their extremes.

- **Five new glossary entries** — *mix*, *channel*, *effect chain*, *bypassed*, *collapsed*. Four
  of them are words the settings file and the UI both use and mean slightly differently, which is
  the [[glossary]]'s own criterion.

### Elevation, measured rather than assumed (2026-08-20)

**An audit that produced a fix, a gotcha and an open question** — and the open question is the
point. A later session should be able to pick it up cold.

- **One audit**, [plug-in resolution and elevation](audits/2026-08-20-plugin-resolution-and-elevation.md).
  It **carries its own method as runnable commands**, deliberately: the findings contradict an
  assumption that had been in the code since phase 6, so verifying them should be cheap rather than
  a matter of trusting this file. §3 is an *unanswered question* with a reversible experiment and a
  table of what each outcome would mean.

- **One gotcha.** [[windows-asks-for-rights-the-app-already-had]] — the shared VST3 folder carries
  an `Everyone:(F)` ACE that a plug-in installer added, so the app was prompting for rights it
  had. Its "plausible explanation" section carries the *second* trap too: the fix that suggests
  itself is to read the ACL, which needs group membership, inherited denies and UAC's filtered
  token to be right.

- **A contradiction corrected, same day.** [[ADR-012]] deferred MSIX for lack of a signing
  certificate. [post-1.0.md](dev-phases/post-1.0.md) had **already refused it for a better
  reason** — a redirected `LocalState` an uninstall deletes wholesale, which is [[ADR-003]]'s
  whole subject. The ADR now defers to that. Worth recording as a delta rather than a silent edit:
  a new ADR restating a weaker version of an existing refusal is exactly what a cross-reference
  index exists to catch.

- **A row in post-1.0's *Deferred*** so the question has a home in the roadmap and not only in the
  debt list.

### The debt list, cleared (2026-08-20)

**The whole of `technical-debt.md` that a commit can close, closed** — §1, §4, §5, §6 and §7. Three
entries remain and none of them is code: §4.15 needs a human's eyes, §2.2 is a fact about the
world, §2.4 needs a component that is not ported yet.

- **One ADR.** [[ADR-012]] — update by staging beside the install and swapping, never elevated. Its
  *Alternatives considered* is the useful half: MSIX, Squirrel/Velopack, reusing [[ADR-011]]'s
  elevation, download-only, and a background poller, each with the reason it lost. **MSIX is
  rejected *for now, not forever*** and the entry says what would change that, because a future
  reader with a signing certificate should find the door marked rather than the subject closed.

- **One runbook, and the folder it created.**
  [releasing-and-updating.md](operations/runbooks/releasing-and-updating.md) — the release shape,
  the pipeline, the feed configuration, what the app does with it, why it does not elevate, and a
  symptom table. **Written as one document on purpose**: a release in the wrong shape is invisible
  to the updater, so splitting them would let each half look correct alone.

- **Three gotchas**, and the grouping is the finding. Two of the three
  ([[a-progress-report-never-arrives-in-a-test]], [[the-serializer-that-never-throws-throws]]) are
  the *test environment* being wrong while the production code was right — the direction it is
  easy to look in last. The third ([[an-accelerator-shows-as-a-literal-underscore]]) is
  [[a-settings-control-moves-and-nothing-happens]] in a new costume, which is now the third time
  that shape has cost this project a session.

- **One pattern.** [[decisions-as-pure-functions]] — extracted, not invented: four of its five
  callers predate the session, and writing it down is what made the fifth (`TrayNotifications`)
  take the shape it did. This is the folder working as intended after two phases of producing
  nothing.

- **The index's session table was stale**, listing eight of nineteen sessions and stopping at
  2026-08-17 — the same failure mode the tally note below records, in a different file. All twenty
  are listed now.

- **`THIRD-PARTY-NOTICES.md`** (repo root, new): Lucide's ISC licence, H.NotifyIcon, and an
  explicit note that upstream was read and never copied.

**Not written, deliberately.** No recipe came out of this. The update flow has one procedure —
`git tag && git push` — and the rest is reference; padding it into numbered steps would have made
a recipe out of a paragraph, which [README.md](README.md) names as the boundary.

### The two deferred items, closed — and a settable schedule (2026-08-19)

**The documentation delta here is larger than the code delta, and that is the honest shape of the
session:** one of the two items being closed was a *measurement*, and what it measured turned out
to contradict a sentence in an accepted ADR.

- **Two ADRs, and a renumbering of two that do not exist yet.** [[ADR-010]] (two preset roots and
  a rooted snapshot layout) and [[ADR-011]] (elevate by relaunching the shell) took the numbers
  `phase-7-release.md` had pencilled in for packaging and notifications. ADRs are numbered in the
  order they are **written**, so a reserved number yields to a real one — the phase-7 references
  moved to ADR-012 and ADR-013, with a note saying so. Renumbering a *plan* is not renumbering an
  ADR, which never happens.
- **[[ADR-006]] now has a correction on top of it.** Its tier 3 definition —
  *"the presets each effect saved under `%APPDATA%\<Vendor>\`"* — was checked against the
  reference rig for the first time and was wrong. Its **measurements were right and were
  misread**: `%APPDATA%\FabFilter` does hold 246 files, and they are caches and factory
  component presets. [[ADR-010]] supersedes the definition without superseding the ADR, and says
  which sentence it replaces.
- **Two gotchas, both observed.**
  [[backup-says-it-saved-your-presets-and-it-did-not]] — the §4.18 finding as a symptom, with the
  numbers and the two plausible-but-wrong diagnoses. And
  [[a-settings-control-moves-and-nothing-happens]] — a stepper wired to nothing for two phases,
  and a commit that reached disk but not the running app.
- **`technical-debt.md`: §4.17 and §4.18 closed, §4.20 opened.** The two closures are long,
  deliberately — §4.18 carries the before/after table because a measurement that changed a
  decision is worth more than the decision. §4.20 is new debt found while fixing the others.
- **Two design specs written in this repo**, `screens/13-elevation.md` and
  `screens/14-backup-timing.md`. `operations/design/` is a vendored export exempt from the
  frontmatter rule, so these two carry frontmatter and a provenance banner **to survive a
  re-export**, and [README.md](README.md) now records that exception and calls it a last resort.
- **`glossary.md` gained four terms** — *preset root*, *preset source*, *elevation*, *daily
  backup* — and its tier-3 row was corrected. Each is a word where the everyday meaning is close
  enough to mislead: "elevation" in particular means *a second headless process*, not a
  permission this one acquires.
- **The tally was stale by a whole release** and is corrected above.
- **A corpus pass fixed what had drifted**, run against the rules in [README.md](README.md)
  rather than by eye:
  - `index.md` claimed *"Phase 6 has started"*, 1,050 tests, *"eight records"* against a
    nine-row ADR table, and **listed ten of sixteen gotchas** — which is worse than listing
    none, because a partial index reads as a complete one. The gotcha table is now whole and
    grouped by where each bites.
  - **Five plans carried statuses outside the schema** — `completed`, `in-progress`, `planned`
    against `draft | review | published | archived` — and two of those were stale as well as
    off-schema, describing shipped work as not started. All five are `published`, matching
    their eight siblings.
  - Every `[[slug]]`, every relative link and every frontmatter block was checked mechanically.
    106 files, 11 ADRs contiguous from 001, nothing dangling.

Counts: ADRs 9 → **11**, gotchas 16 → **18**, sessions 17 → **19**, tests 1,146 → **1,207**.

### Recent additions (v0.6.0 — plugin tiers)

**All four tiers capture and restore.** The documentation delta is mostly corrections, which is the
interesting part:

- **`technical-debt.md` §3 was stating a falsehood as settled fact** — *"Wave Link's own AutoBackups
  are captured but never managed"* — since 2026-08-16. They were not captured at all. The
  spec-coverage table found it; phase 6 §8 made the sentence true.
- **§2.3 closed** (the VST3 bundle path) with fixtures on both sides: capture recurses a directory
  `.vst3`, restore rebuilds the tree, and an empty bundle counts as a failure rather than a
  zero-byte success.
- **Three new entries**: §4.17 (the shell cannot ask for a tier 4 restore — elevation has no
  designed surface), §4.18 (the preset heuristic has never met a real vendor folder), §4.19 (tier 4
  reads whole binaries into memory). None gates 1.0.
- **`spec-coverage.md` earned its keep on its first day** by finding the §8 gap, and now marks
  `SPEC.md` §9's tier table complete.
- [Session note](sessions/2026-08-19-phase-6-tiers-complete.md), and phase 6's file carries an
  "as built" paragraph per section — including the two decisions the design does not specify
  (where plug-in version drift renders, and what `Locked` means in the Settings dialog).

### The rest of the road, written down (2026-08-19)

Phase 7 and everything after it are now planned rather than sketched, in three new documents plus
one new section:

- **[phase-7-release.md](dev-phases/phase-7-release.md)** — nine work items, a test table, a risk
  register, and a **1.0 gate table** stating for every open debt whether it blocks a release.
  Two ADRs are named as deliverables of the phase rather than assumed: **ADR-010** (packaging and
  distribution) and **ADR-011** (how notifications are delivered).
- **[spec-coverage.md](dev-phases/spec-coverage.md)** — every requirement in `SPEC.md` §1–§11 plus
  the corrections block, marked built / planned / refused with the code or decision that settles it.
  It also lists what was built that the spec never asked for, so the table is not read as the whole
  product.
- **[post-1.0.md](dev-phases/post-1.0.md)** — split into *refused* (reopening needs a new argument)
  and *deferred* (each names its promotion signal), so the two phases cannot quietly absorb work
  that was decided against.
- **phase 6 §8** — a gap the coverage pass found: [[ADR-006]], `SPEC.md` §1, the Settings dialog
  and `technical-debt.md` §3 all describe tier 1 as `Settings.json` **plus Wave Link's own backup
  copies**, ~470 KB. Only the 43 KB settings file is captured, and `technical-debt.md` states the
  opposite as settled fact. It lands in phase 6 because §7's "honest, recomputed sizes" and that
  figure are the same number.

Three further findings, all of the same shape — **modelled, persisted, and with no control bound
to them**: the `WHEN WINDOWS STARTS` settings section (so autostart cannot be switched on from
anywhere in the app, though `ShellViewModel.ToggleAutostart` exists and is tested), the `UPDATES`
section, and the close-to-tray preference.

### Recent additions (v0.5.1 — the design audit)

**Four gotchas**, all from one audit of the shipped shell and all with the same shape: a view
defect that no test could see, because **no test had ever constructed the view**.

| Gotcha | What it costs you if you do not know it |
|---|---|
| [[a-dialog-opens-as-a-black-rectangle]] | `Background="Transparent"` is not transparency; a WPF window needs `AllowsTransparency` to have an alpha channel at all |
| [[the-window-never-opens-and-nothing-says-why]] | Three separate throws during window construction, all presenting as "nothing happened" — including one that looks exactly like a layout hang |
| [[a-binding-expression-appears-on-screen]] | A markup extension is evaluated in attribute syntax only; in a property element it is literal text the user reads |
| [[three-backups-look-selected-at-once]] | One `SelectedItem` shared across several Selectors cannot express one selection, and two-way it ping-pongs |

**Two sessions**: [the design audit](sessions/2026-08-19-design-audit-and-ui-fixes.md) and
[phase 6 §1](sessions/2026-08-19-phase-6-plugin-discovery.md).

**technical-debt.md §4.12 and §4.13 closed** — motion is built (a real `cubic-bezier(.2,0,0,1)`
easing rather than the nearest named WPF curve), and the missing-plug-in warning reaches the view
as two clauses. §4.12 records the two things that stayed undone and why, which is the part worth
reading: a hover cannot animate a `Background` between theme resources, and the row's selection
fill was left instant rather than destabilising a test-pinned trigger order.

**Tests 964 → 1,050.** Eight new App test classes, almost all of them view tests. That ratio is
the finding, not a statistic: the model layer was covered thoroughly and the view layer was not
covered at all, and every defect this release fixes lived in the gap.


### Phase 6 detailed: plugin tiers (2026-08-19)

Phase 6 — **plugin tiers** — gets its own detailed file, [phase-6-plugin-tiers.md](dev-phases/phase-6-plugin-tiers.md),
per the "current or next phase" rule. Phase 5 closed on 2026-08-19 with 964 tests green, so phase 6
is now *the* next phase and its sketch in the index is joined by a full plan: entry/exit criteria,
scope (in/out), seven work items grouped Core-first then shell, a test table, and a risk register.

The plan is grounded in what already exists rather than assumed: `SettingsAnalysis` already reads
`AudioPluginConfigurations` for the effect count (so tier 2 extends it, not replaces it),
`SnapshotRowViewModel.TierOrder` already lists all three tiers (so the badges should render once
snapshots carry them — no badge code expected), and the Settings dialog's two locked rows are the
surfaces this phase unlocks. The defining risk is the **bundle problem** — a `.vst3` may be a
directory, and the author's machine will never exercise that path ([[vst3-backs-up-as-nothing]]) —
so the synthetic bundle fixture is written into the exit criteria as mandatory, not optional.

Doc-only commit; no code, so the test count stands at **964**. Status `review`, awaiting go-ahead
before any implementation.

**Counts moved:** dev-phase docs 7 → 8 (phase 6 detailed; only phase 7 remains sketched).

---

### Corpus audit: reconcile the tally after phase 5 closed (2026-08-19)

A pass over `_docs/` against its own README found the **tally had drifted** behind the work it
counts. Phase 5 shipped five more plans, two gotchas, a recipe and two sessions since the last
stats update, and none of those commits touched this file — exactly the failure mode the
"update in the same commit" rule exists to prevent. The audit also caught two stale **status**
claims that the counts alone would not have surfaced:

- **[dev-phases/README.md](dev-phases/README.md)** still showed phase 5 as *Next* and phase 6 as
  *Not started*. Phase 5 is complete (all ten plans landed, 964 tests green); the table now says
  so and marks **phase 6 — plugin tiers** as *Next*.
- **[plan 5](plans/2026-08-18-phase-5-plan-5-the-restore-flow.md)** still read `status: planned` with
  22 of its 28 boxes unticked, although it shipped long ago. It is now `completed` with every box
  ticked and a short note saying the checkboxes record completion rather than pending work.

Nothing here changed code, so the test count stands at **964**. The two new gotchas are both
tray-shell incidents from plan 9 — [[the-tray-icon-refuses-every-image-you-draw]] (a
`<ApplicationIcon>`-only asset is not a valid `Window.Icon`, dotnet/wpf#209) and
[[tray-menu-keeps-the-theme-it-started-with]] (a context menu built once does not re-theme); the
new recipe is [[publish-the-native-aot-binary]]. The frontmatter sweep was clean: every `.md`
carries frontmatter, the single exception being `archive/README-temp.md`, which is a consumed
template whose `status: archived` line is part of its record.

**Counts moved:** gotchas 10 → 12 · recipes 1 → 2 · sessions 12 → 14 · plans 8 → 13 · dev-phase
docs "7 (1 sketched)" → "7 (2 sketched)".

---

### Phase 5, plan 10: high contrast — the phase's last surface (2026-08-19)

**964 tests green** (296 Core, 91 CLI, **577 App**) — up from 959. Build clean, zero warnings.
This was a verification + gap-filling pass over a third theme that was already ~90% built and
tested, not a build: `HighContrast.xaml` matches spec key for key (every fill Transparent, every
text/line role a `SystemColors.*ColorKey`, no literal hex), the row template encodes health in
shape plus a verdict word, and the focus ring, buttons and tray PAUSED glyph were already pinned.

**Two real gaps closed.** The runtime-swap chain — *turn Windows high contrast on and watch the
app swap without a restart* — was exercised in pieces but never end to end; `UiSettingsTheme` now
exposes its preference handler as an internal seam (with a same-thread fast path so the test is
deterministic) and `UiSettingsThemeTests` pins it: colour change fires once, non-colour change
fires not at all, dispose stops firing. And the "no hard-coded hex in HighContrast.xaml" rule had
no guard; a new `ThemeTests` case reads the file as source and fails the build if a literal colour
ever appears or a brush resolves to anything but a `SystemColors` key or Transparent.

**The HC contract is now a Definition-of-done line in plans 5–8.** Every surface those plans land
must ship with its HC shape/word encoding and a pinning test, plan 10 as the source of truth — no
new surface ships without it. The final sweep found **no gaps**: every plans 5–8 surface already
encodes meaning by glyph shape or verdict word (the restore strip's "FAILED" is a word plus a
dotted rule, not a red fill; the settings proportion bar labels every segment), so in both HC
schemes the tints go transparent and nothing authored against a background luminance breaks.

Session note — [phase 5 high contrast](sessions/2026-08-19-phase-5-high-contrast.md). Plan 10 is
complete, and with it **phase 5 is complete** — every surface built and verified in both themes and
high contrast.

**Counts moved:** sessions 11 → 12 · tests 959 → 964.

---

### Phase 5, plan 9: the tray shell (2026-08-19)

**959 tests green** (296 Core, 91 CLI, **572 App**) — up from 939. Build clean, zero warnings.
The app is now a *tray app with a window* end to end: the shield-check mark appears in the taskbar
button, Alt-Tab and the Start list as well as the notification area; a second launch activates the
first instance instead of starting a watcher twice; autostart is surfaced in Settings with the Task
Manager veto; and the tray icon tracks the live host on every tick.

**One asset, two jobs — but not the way the plan said.** The shield-check mark is authored once
from the same geometry `TrayIconRenderer` already draws, so the static asset and the four live
states read as one object. It is the exe's `<ApplicationIcon>` (file properties, taskbar, Alt-Tab),
but **not** `Window.Icon`: a WPF resource-pack URI for an `<ApplicationIcon>`-only asset fails at
runtime (dotnet/wpf#209), so the window's caption glyph is rendered from geometry in code
(`AppCaptionGlyph`). The exe icon via the linker works fine; only the WPF resource pipeline chokes.

**The hide branch of `OnClosing` is manual-verify-only, and it is documented as such.** It needs a
real `App` installed as `Application.Current`, but WPF allows exactly one `Application` per
AppDomain and the test harness's shared bare `Application` occupies the slot — `new App()` throws
`InvalidOperationException`. The exit branch is exercised by the existing crash-regression test;
the hide-vs-exit distinction is a look-at-it item, same class of exclusion as the DWM interop and
unshown-window geometry already documented in `MainWindowGeometryTests`.

**The context menu is pinned item-for-item.** Beyond order and checkability (already tested), the
two load-bearing labels are now asserted: **Quit — stops backing up** (the consequence rides on the
label, not a confirmation dialog) and **Pause for an hour** (the designed starting label;
`RefreshTray` rewrites it to "Resume" while paused).

Session note — [phase 5 tray shell](sessions/2026-08-19-phase-5-tray-shell.md). Plan 9 is complete;
plan 10 (high contrast) is the last surface in the phase.

**Counts moved:** sessions 10 → 11 · tests 939 → 959.

---

### Phase 5, plan 8: the settings dialog (2026-08-19)

**939 tests green** (296 Core, 91 CLI, **552 App**) — up from 764 as plans 5–8 landed their
surfaces. Build clean, zero warnings. The settings dialog ships in full: the real 680px modal
replaces the placeholder `MessageBox`, every control commits on change (there is no Save button),
and settings persist atomically to `%LOCALAPPDATA%\WaveLinkBackup\settings.json` on change, never
on exit — a command-line flag overrides the file for one run and is never written back.

**The proportion bar is computed, not hard-coded.** Enabling or disabling a tier recomputes the
stacked widths from what is actually included; the locked rows (Your setup, A list of your
effects) cannot be moved at all, and a programmatic set on them is rejected by the view model.

**Unbuilt tiers stay on screen — present but disabled.** PRESETS and PLUGINS render with the NOT
BUILT YET badge and a footnote explaining why they are not hidden. The Task 7 keyboard/SR pass
made the locked toggles *present-but-disabled* rather than collapsed, so a screen reader announces
them as off/unavailable switches instead of dropping them from the tree; focus also returns to the
list when the dialog closes, reusing the same seam every other dialog uses.

**Two debts closed in the same commit.** [technical-debt.md](technical-debt.md) §4.8 item 4 (the
settings placeholder) and §4.9 (the dormant restore-outcome strip — plan 5 wired it to
`RestoreOrchestrator`) are both struck through with their reasoning kept. Session note —
[phase 5 settings dialog](sessions/2026-08-19-phase-5-settings-dialog.md).

**Counts moved:** sessions 9 → 10 · tests 764 → 939 (cumulative across plans 5–8).

---

### Phase 5: the last two plans — tray shell and high contrast (2026-08-18)

The phase is now **fully planned end to end.** The two surfaces that remained after plans 5–8
each got a dated plan under [`plans/`](plans/):

| Plan | Surface | Design source |
|---|---|---|
| [plan-9](plans/2026-08-18-phase-5-plan-9-the-tray-shell.md) | The tray shell: the app icon (tray **and** window — no `.ico` existed, so this authors one and sets `<ApplicationIcon>` + `Window.Icon`), second-launch activation, the autostart toggle with its Task Manager veto, live-host icon states on every tick, hide-on-close + context menu verification | `screens/12-tray-autostart-update.md` |
| [plan-10](plans/2026-08-18-phase-5-plan-10-high-contrast.md) | High contrast: a verification pass over the already-built third theme, pinning the runtime swap end to end, a guard that `HighContrast.xaml` carries no hard-coded colour, the HC contract plans 5–8 must sign before their surfaces are done, and a both-schemes sweep | `screens/11-high-contrast.md` |

**Both plans are shaped by what already exists, and that is the point of reading the code
before planning it.** Plan 9 *extends* rather than rebuilds: single-instance, hide-on-close,
the context menu, the four icon states and autostart are all implemented — the new work is the
shared app icon asset, second-launch activation, the Settings toggle and pinning the live-host
state changes. Plan 10 found high contrast **~90% built and tested** (`HighContrast.xaml`
matches spec key for key; shape-encoded health, verdict words, focus ring and the PAUSED tray
glyph are all pinned), so it is a gap-filling pass: the runtime-swap chain was never pinned
end to end, the no-hard-coded-colour rule had no guard, and nothing enforced that plans 5–8's
new surfaces arrive HC-complete.

The status line in [phase-5-wpf.md](dev-phases/phase-5-wpf.md) now says what it has not said
since 2026-08-17: every surface in the phase is planned; what remains is execution.

Doc-only commit; no code, so the test count is unchanged at 764.

**Counts moved:** plans 6 → 8.

---

### Phase 5: execution plans for every remaining surface (2026-08-18)

The backup list (part 4) and the restore-outcome strip are shipped. The rest of the phase is now
broken into **four dated execution plans** under [`plans/`](plans/), each following part 4's task
format (pure model → tests → view → wiring → keyboard/SR → guards + full verification):

| Plan | Surface | Design source |
|---|---|---|
| [plan-5](plans/2026-08-18-phase-5-plan-5-the-restore-flow.md) | Real restore flow: confirmation dialog, four-stage in-progress strip, wire `RestoreOrchestrator`, feed the outcome strip | `screens/04-in-progress.md`, `09` |
| [plan-6](plans/2026-08-18-phase-5-plan-6-delete-rename-trash.md) | In-place rename, three-variant two-stage delete, empty-trash row + per-volume detection | `screens/05-delete-dialogs.md`, `08` |
| [plan-7](plans/2026-08-18-phase-5-plan-7-errors-and-first-run.md) | The twelve errors in their four placements (weight rule), error 9/12 full screen, first-run/empty state | `screens/06-errors.md`, `08`, README Screen 4 |
| [plan-8](plans/2026-08-18-phase-5-plan-8-settings-dialog.md) | Settings dialog: in-place commit (no Save button), atomic persistence, WHICH WAVE LINK + WHERE THESE SETTINGS LIVE, unbuilt tiers | README Screen 3, `screens/08` |

The phase's status line in [phase-5-wpf.md](dev-phases/phase-5-wpf.md) was refreshed from
"Not started — next" to "In progress", with a plan table and an explicit note that the **tray
shell** (icon states, context menu, hide-on-close, single-instance, autostart) and **high
contrast** are still not broken into a dated plan — that is the next planning step once plans
5–8 have landed.

Doc-only commit; no code, so the test count is unchanged at 764.

**Counts moved:** plans 2 → 6.

---

### Phase 5: the restore-outcome strip (2026-08-18)

**764 tests green** (296 Core, 91 CLI, 377 App). The App project's first WPF test surface for a
shell-level view model lands here: `RestoreOutcomeStripTests` pins the four designed outcomes —
succeeded-and-confirmed (quiet, auto-dismiss), succeeded-unconfirmed (neutral, "Check again"),
rejected (amber, not dismissible until acted on), and failed (danger, dismissible) — plus the
dismiss rules and the 6-second auto-dismiss constant.

**One Core test added from a list I nearly skipped.** `RestoreOutcome.Confirmed` is a computed
projection over `Verdict.Succeeded`, and its null-verdict branch (log unreadable) was never
asserted directly — only through `outcome.Confirmed == false`. The new test pins that the
unreadable-log path returns `Confirmed == false` without a `NullReferenceException`, which is
exactly the branch the strip's `Show(RestoreOutcome)` maps to *succeeded-unconfirmed*.

**A brush added, and the guard test caught it.** `WlDangerSoft` (the failed-outcome fill) was
added to all three theme dictionaries. The existing `ThemeTests.Every_theme_declares_every_brush`
guard would have failed on the missing key in any one of them — so the three-theme check is done
by the suite, not by eye. High contrast gets a transparent tint per `11-high-contrast.md`.

**Documented, and tracked as dormant.** Session note —
[phase 5 restore-outcome strip](sessions/2026-08-18-phase-5-restore-outcome-strip.md). The strip
is fully built but nothing feeds it yet (the restore button still shows the placeholder), so it is
recorded in [technical-debt.md](technical-debt.md) §4.9 as a *dormant seam*, not a bug — the same
shape as the `Settings…` placeholder in §4.8 item 4. The debt register's opening paragraph was
also refreshed: "no application code" is no longer true, and §1/§7 entries have since been
resolved against shipped code.

**Counts moved:** sessions 8 → 9 · tests 746 → 764.

---

### Phase 5, part 1: the four Core changes + design v5 (2026-08-17)

**351 tests green** (266 Core, 85 CLI). Core 85.7% line / 82.3% branch, CLI 84.1% / 82.0%.
NativeAOT still 3.2 MB despite new shell interop.

**Shipped:** technical-debt §7.1, §7.2 and §7.3. Only §7.4 (keyboard) remains, and it is WPF
work that arrives with the shell.

**Design v5 integrated — and the amendment is upstream now, not just in this repo.**
`screens/05` specifies the two-stage delete; `screens/08` specifies the Empty trash row. The
code and the design no longer disagree.

**The designer solved the sentence I flagged as possibly unsolvable**, and rejected the
fallback I offered:

> *"After that it is gone" is exactly true on a network share and slightly **pessimistic** on a
> local disk… Pessimism is the safe direction in a destructive dialog, and it is the one
> sentence that never breaks on any volume.*

Worth recording because the brief explicitly invited a "no", and the answer was better than
either option in it.

**One divergence found by reading the spec rather than assuming:** `screens/08` says Empty
trash takes **no confirmation on a local drive** — *"a dialog guarding a reversible action is
the noise that teaches people to click through the ones that matter."* My CLI confirmed
unconditionally. Now it confirms only where the Recycle Bin cannot catch it.

**Five tests added from a list I nearly skipped.** `screens/05` closes with *".trash must be
invisible to the list, the search, every count and size readout, and the keep-count."* Those
passed first time — the implementation already satisfied them — but "it already works" is not
the same as "it is pinned", and each is a place where a trashed backup leaking back would look
like a bug in deletion rather than in counting.

---

### Phase 5 scope split (2026-08-17)

The tray design looked like it doubled phase 5. Examined rather than accepted: **the framing is
free, the Windows integrations are not.**

`AutoBackupCoordinator` already owns no timer and waits for a host to call `Tick()` — the CLI's
`watch` verb is one today — so "tray app with a window" is what Core was built for, and
`ShutdownMode` is one line. What actually costs is that **WPF provides none of the three
integrations the design assumes**: tray icon, toast notifications, autostart registry.

**Split accordingly.** Phase 5 keeps the tray shell, hide-on-close, single-instance, `--tray`,
autostart, and high contrast. Phase 7 takes the two notifications and the update mechanism —
both are *"something has been wrong for a while"* cases, and the tray's `NEEDS YOU` icon
carries the same information passively until then. Nothing else in the design depends on them.

The framing stays because dropping it would be wrong, not because it is cheap: **if closing the
window stops backups, the app fails its own promise** and becomes upstream's tool with extra
steps.

---

### Design handoff v4 + four decisions (2026-08-17) — no version, no code

**Integrated:** `11-high-contrast.md` and `12-tray-autostart-update.md` with three PNGs.
**All six design gaps in §4 are now closed** — nothing in the UI is undesigned.

`12` changes what the app is: *"it lives in the tray and the window is the exception."* That is
a tray app with a window, not a window app with a tray, and it lands scope phase 5 did not
carry — four icon states, a context menu as the primary interface, exactly two notifications,
`HKCU\...\Run` autostart that **Task Manager can veto**, and an update section whose *UI* is
phase 5 while its *mechanism* stays phase 7.

**The four §7 conflicts are decided**, and one of them improved on my recommendation:

- **Delete → two-stage.** Move to `<store>/.trash/`, *Empty trash* forwards to the Recycle Bin.
  Better than the direct `SHFileOperation` I proposed, for a reason I had missed: **the store is
  user-chosen, and the Recycle Bin does not exist on network shares** — so the design's promise
  was one the app could not keep there. A directory move behaves identically on every volume,
  and interop leaves the delete path entirely. **Amends design decision 3.**
- **Damaged vs keep-count → verify lazily, only the condemned.** Hashes one or two snapshots per
  prune instead of the whole store, so it does not reintroduce the cost phase 2 avoided.
- **Watcher → clear the pending write on failure and carry the error.** The error is what feeds
  the tray's `NEEDS YOU` state; without it the tray has a state it cannot enter.
- **Keyboard → Windows conventions generally**, and screen-reader labels are part of it rather
  than a follow-up.

---

### Design handoff part 2 (2026-08-17) — no version, no code

An updated design package landed and was integrated into `operations/design/`. Doc-only, but
it changes what phase 5 is.

**Integrated**

- **11 state-group specs** in `screens/` with 12 PNGs, `MANIFEST.md`, and `CHANGES-SINCE-V1.md`.
- Regenerated prototype (1.24 MB) and canvas (235 KB).
- **Tokens and brand assets are hash-verified byte-identical** to what was already here — the
  token-drift risk flagged before the export turned out to be zero.

**Structural**

- `design-handoff.md` reverted to the export's own `README.md`, and **the whole folder is now
  exempt from the frontmatter rule** — stated in `README.md` with the reason. It is a vendored
  drop-in export; patching frontmatter on every re-export would guarantee the repo copy drifts
  from the design tool's, which is the one thing a handoff must not do. Same exemption as
  `third_party/`, same reason. 13 files repointed; the two references left in session notes are
  deliberately historical.

**Closed**

- **[technical-debt.md](technical-debt.md) §4** — five of the six design gaps. Only Windows
  high-contrast and tray/autostart/update remain.

**Opened — and this is the substantive part**

- **§7, four decisions that outdated shipped code.** Delete must go to the Recycle Bin
  (`SnapshotStore.Delete` is permanent, and `SHFileOperation` is Win32 interop against a
  library deliberately targeting `net10.0`); damaged backups must not count toward the
  keep-count (retention cannot see damage at all); automatic backup must not queue when the
  folder is missing (it currently retries every 15s, silently, forever). None is a mistake in
  either place — the code was built to the best spec available and the design has since decided
  better — but "the design says X, the code does Y" goes invisible once everyone is looking at
  XAML.

**Also worth recording:** the first handoff specified the SUSPECT badge in red inside an amber
row — the forbidden second red, by its own rules. The design caught it. Nothing had been built
against it, so the correction cost nothing.

---

### v0.4.0 — Phase 4: the CLI (2026-08-16)

**Added**

- **[[ADR-009]]** — hand-rolled command-line parsing. The first ADR since the scaffold, and it
  exists because a reader seeing a hand-written parser will reasonably ask why no library.
- **Session note** — [phase 4 CLI build](sessions/2026-08-16-phase-4-cli-build.md).
- **`dev-phases/phase-5-wpf.md`** — including an explicit list of what the GUI needs that
  **Core does not have yet** (search, settings persistence, disk-free, a hosted watcher),
  because those are the items that will feel like "just UI work" and are not.

**Corrected by measurement**

- **[[ADR-001]]** — NativeAOT produces a **3.2 MB** binary, not the 10–15 MB estimated. The
  table credited Rust with 2–5 MB as the one row it won; that row is now roughly a tie. The
  decision is unchanged (it turned on lossless JSON, not size) and the estimate is corrected
  rather than left standing. Third time a measurement has overturned something written down.

**Partially resolved, and said so**

- **[technical-debt.md](technical-debt.md) §2.4** — AOT compiles clean with zero trim
  warnings, **but there is no `[ComImport]` in the codebase**, so the interop that prompted
  the doubt was never exercised. Recorded as *partially answered*; claiming closure would have
  been the more satisfying lie.

**Counts moved:** ADRs 8 → 9 · sessions 5 → 6 · dev-phase docs 6 → 7 · tests 235 → 308.

---

### v0.3.0 — Phase 3: automation (2026-08-16)

**Added**

- **Session note** — [phase 3 automation build](sessions/2026-08-16-phase-3-automation-build.md).
- **`dev-phases/phase-4-cli.md`** — phase 4 detailed.

**Resolved**

- **[technical-debt.md](technical-debt.md) §1.4** — upstream being a manual tool rather than a
  safety net. Struck through, original retained. This was never a *defect* upstream; it was the
  gap this project exists to fill, and it is now filled.

**A documented exemption withdrawn**

- `FileSystemSettingsWatcher` was briefly left untested on the reasoning that excuses
  `WaveLinkProcess` at 5% coverage. That reasoning does not transfer — closing a user's Wave
  Link to test a shutdown is unacceptable, but *watching a temp directory is harmless*. The
  session note records it as "laziness wearing a principle's clothes", because the distinction
  is worth keeping sharp: an exemption is only legitimate while the thing it protects is real.
  One of the resulting tests found that a `LastWrite`-only filter would have been a bug.

**Still no new patterns.** Phases 2 and 3 both applied the four from phase 1. The set has
stopped growing, which is the expected shape — patterns come from novelty, and the last two
phases were composition.

**Counts moved:** sessions 4 → 5 · dev-phase docs 5 → 6 · tests 186 → 235.

**Corpus audit (same day, after the release)**

A pass over `_docs/` against its own README turned up three stale claims, all created by
`patterns/` coming into existence and nothing updating the document that said it had not:

- the directory-structure block omitted `patterns/`;
- *Folders deliberately absent* still listed it;
- the `patterns/` folder guide still opened with "Not yet created".

Fixed, and the absent-folders entry is kept as a **note that its trigger fired** rather than
deleted — a mechanism that demonstrably worked is worth more as evidence than as a blank space,
and the two remaining rows are the same bet.

Also added: three topics to the cross-reference index (**the snapshot store**, **automatic
capture**, **keeping the corpus honest**), which had not moved since v0.0.1 despite three
phases of work; and a *Words the code uses precisely* section to the glossary covering the
vocabulary phases 1–3 introduced — expected failure, finding, pure, seam, guard, tick,
debounce, rate limit, prunable, schema version, as built.

---

### v0.2.0 — Phase 2: the snapshot store (2026-08-16)

The release that closes the project's founding defect. The doc delta is small and mostly
consists of striking things through — which is the point.

**Added**

- **Session note** — [phase 2 store build](sessions/2026-08-16-phase-2-store-build.md).
- **`dev-phases/phase-3-automation.md`** — phase 3 detailed, per the "current or next phase"
  rule.

**Resolved**

- **[technical-debt.md](technical-debt.md) §1.1 and audit finding 1 — the critical defect.**
  Struck through, original text retained, because the reasoning still explains why the store is
  shaped the way it is.
- **The audit now has nothing open.** Six findings: three fixed, one withdrawn as wrong, one
  resolved as incomplete, one answered by building a different product.

**Corrected by measurement, again**

- The phase 2 design said `waveLinkVersion` "needs reading from the package manifest". Probing
  first showed `C:\Program Files\WindowsApps` is unreadable without elevation, and that the
  version is already in `Settings.json`. Recorded as an *as built* delta rather than silently
  implemented differently.

**No new patterns.** Phase 2 applied the four from phase 1 rather than producing new ones,
which is what a pattern set is for. Writing a fifth to have a fifth would be documenting an
intention.

**Counts moved:** sessions 3 → 4 · dev-phase docs 4 → 5 · tests 93 → 186. Audit open findings
1 → 0.

---

### v0.1.0 — Phase 1: Core (2026-08-16)

The first release with code in it. The documentation delta is mostly *promotion*: claims that
were read became claims that are tested.

**Added**

- **`knowledge-base/patterns/` — created, with 4 patterns.** The trigger in `README.md` was
  "the first line of production code ships", and it did. [[pure-analysis-core]],
  [[named-method-seams]], [[preconditions-inside-the-operation]], [[guards-that-can-fail]].
- **Session note** — [phase 1 Core build](sessions/2026-08-16-phase-1-core-build.md).
- **`plans/` gained a second document** — the [phase 2 design](plans/2026-08-16-phase-2-store-design.md).
- **`dev-phases/phase-2-store.md`** — phase 2 detailed, per the "current or next phase" rule.
- **`third_party/WaveLinkSettingsUtility/VENDOR.md`** — the vendored snapshot's record: SHA,
  baseline, what was ported, and seven deliberate divergences.

**Resolved**

- **Audit finding 5** — not wrong, *incomplete*. The release workflow overrides the csproj, so
  the README and the project file never contradicted each other. Method failure named in the
  audit: a claim about what users receive was answered from one build file.
- **`technical-debt.md` §1.5** — closed, no debt carried forward.
- **§2.2 mitigated** — `--settings-path` now bypasses discovery entirely, unlike upstream's.

**Added to the debt register**

- **§1.6 / audit finding 6** — upstream never closes `WavelinkSEService`, so its
  "verified exited" check can pass with half of Wave Link running. Fixed in our port; worth
  offering back.

**Promoted from claim to test**

- [[capture-fails-while-wave-link-is-running]] — now pinned by
  `RealInstallTests.The_naive_read_fails_while_Wave_Link_is_running`, which asserts the naive
  call throws against the live file.

**Counts moved:** patterns 0 → 4 · sessions 2 → 3 · plans 1 → 2 · dev-phase docs 3 → 4 ·
tests 0 → 93. Audit findings 5 → 6, of which 2 did not survive contact with a running system.

---

### v0.0.2 — Probe corrections (2026-08-16)

A ten-minute probe run before designing phase 1 answered one open question and **invalidated
two documented decisions**. The doc-ecosystem effect is mostly *subtractive*, which is unusual
enough to note.

**Added**

- Gotcha 9 — [[capture-fails-while-wave-link-is-running]]. `Settings.json` is locked while
  Wave Link runs; `File.ReadAllBytes` fails on most captures. Not in `SPEC.md` at all.
- Session note — [phase-1 probe](sessions/2026-08-16-phase-1-probe.md).
- `LICENSE` at the repo root (MIT, upstream's copyright line verbatim).
- A **Corrections block** at the top of `SPEC.md`. The body is left unedited on purpose: it is
  the record of what was believed on 2026-08-15, and rewriting it would destroy the thing that
  makes the corrections legible.

**Withdrawn**

- **Audit finding 2 (JSON encoder)** — struck through, not deleted, in the audit,
  `technical-debt.md` §1.2 and `SPEC.md`. Wave Link writes with the *default* encoder;
  the recommended `UnsafeRelaxedJsonEscaping` would have caused the churn it was meant to
  prevent. A wrong recommendation that merely disappears gets re-derived by the next reader.

**Resolved**

- `technical-debt.md` §2.1 (`JsonNode.Parse` duplicates) — answered, and the question was
  mis-framed. New sub-finding 3b recorded instead.

**Rewritten**

- [[every-snapshot-differs-with-no-real-change]] — same symptom, opposite cause. The
  superseded version's `Provenance: read, not reproduced` line is what made this catchable.

**Counts moved:** gotchas 8 → 9 · sessions 1 → 2. Audit findings: 5 → 4 actionable, plus one
new sub-finding and one disputed.

---

### v0.0.1 — Documentation scaffold (2026-08-16)

The documentation system, seeded from `SPEC.md` and the design handoff. No application code.

**Added**

- The docs system itself: `README.md`, `index.md`, `templates.md`, `glossary.md`,
  `technical-debt.md`, this file.
- **8 ADRs**, `ADR-001` … `ADR-008` — every structural decision `SPEC.md` had already made
  but never recorded as a decision with alternatives and consequences attached.
- **8 gotchas**, each carrying a `Provenance` line: 3 observed, 4 read-not-reproduced,
  1 spec-derived. That split is itself the most useful thing in the set.
- **1 recipe** — the restore sequence, where the order is load-bearing at every step.
- **1 audit** — the read of `voltybat/WaveLinkSettingsUtility` at `main`.
- **3 dev-phase documents** — the 8-phase roadmap index plus detail for phases 0 and 1.
- **1 session note**.

**Moved**

- `design_handoff_wave_link_backup/` → `_docs/operations/design/`, its `README.md` renamed
  `README.md` so it does not read as a folder readme.
- `_docs/README-temp.md` → `_docs/archive/README-temp.md`, consumed.

**Counts moved:** ADRs 0 → 8 · gotchas 0 → 8 · recipes 0 → 1 · audits 0 → 1 · sessions 0 → 1.

---

## Related documentation

Topics spanning several artifacts. A single-file topic is discoverable by search and does not
belong here.

### Updating the app, and three ways it did nothing

The first real update this project attempted failed three times over, each failure hidden behind
the one before it. Read together before touching the update path.

| Artifact | Contribution |
|---|---|
| [[ADR-012]] | Check-only updates with a staged swap — the design the rest of this rests on |
| [[ADR-018]] | Where the app says an update exists, and the check cadence that makes it true |
| [[every-update-fails-its-checksum]] | The archive verified against another artifact's digest, and why CI could not catch it |
| [[the-update-installs-nothing-and-says-nothing]] | A swap with one attempt, and careful error handling with nowhere to report to |
| [the-update-path-meets-a-real-release](sessions/2026-08-25-the-update-path-meets-a-real-release.md) | The order they were found in, and why that order was forced |

---

### Interop under NativeAOT, and the guards that only cover one language

The first COM in this codebase, and two lessons that arrived with it: what survives trim analysis,
and what happens when a rule is enforced in one language and the second one shows up. Read together
before adding interop or a tool in a new language.

| Artifact | Contribution |
|---|---|
| [[ADR-017]] | The decision, its four rejected alternatives, and why `AllowUnsafeBlocks` on `Core` is a different trade from the one `RecycleBin` refused |
| [[com-interop-stops-compiling-the-moment-the-project-is-aot-compatible]] | IL2072 and IL2050, why neither is suppressible, and the three things that make `[GeneratedComInterface]` work |
| [technical-debt.md](technical-debt.md) §2.4, in [the archive](archive/technical-debt-closed.md) | The question, open since phase 4, and the evidence table that closed it |
| [[capture-fails-while-wave-link-is-running]] | The rule `SourceGuardTests` has enforced in C# since phase 1 — and that `ToolScriptGuardTests` had to extend to PowerShell after a tool repeated it exactly |
| [splitting-the-debt-list…](sessions/2026-08-25-splitting-the-debt-list-and-closing-what-was-left.md) | The two failed porting attempts in order, and why the second looks like the fix for the first |

---

### WPF views, and why they need their own tests

Four defects in one audit, one root cause: a view fails by not existing, and a model test cannot
see that. Read together before writing the next window.

| Artifact | Contribution |
|---|---|
| [[the-window-never-opens-and-nothing-says-why]] | Three construction-time throws, and why "show the window" is the assertion |
| [[a-binding-expression-appears-on-screen]] | The XAML rule with no compiler warning behind it |
| [[a-dialog-opens-as-a-black-rectangle]] | Layered windows, scrims, and what `Transparent` does not mean |
| [[three-backups-look-selected-at-once]] | Selection across several Selectors, and the test that passed while the bug shipped |
| [operations/design/README.md](operations/design/README.md) | The values every one of these was audited against |
| [2026-08-19-design-audit-and-ui-fixes.md](sessions/2026-08-19-design-audit-and-ui-fixes.md) | The narrative, including three wrong diagnoses |

### A rig bigger than the design drew

Spans two ADRs, a gotcha and a Core type. The design specifies a five-channel rig throughout, in
the piece it calls the core information design; a nine-channel rig breaks the row and the details
view is where the rest of the answer went. Read all four before changing either surface — the strip
and the dialog are two halves of one question.

| Artifact | Contribution |
|---|---|
| [[ADR-014]] | Why the strip widens, why the labels are what yield, and why collapse is a drop rather than a threshold |
| [[ADR-015]] | Why the details view reads the backup rather than the manifest, and what that rules out |
| [[every-older-backup-turns-amber-after-adding-a-channel]] | The reported symptom, and the two wrong fixes |
| [operations/design/README.md](operations/design/README.md) §Screen 1 | The five-slot strip as drawn, which is what both decisions depart from |
| `technical-debt.md` §5, §8.2 | The note that predicted this, and the by-eye pass now owed |

### Shipping a release, and the app finding it

Spans a decision, an operational procedure and a designed surface. Read all three before changing
any of them — the release shape and the updater are **one contract**, so a change to either half
that does not look at the other will break the loop silently.

| Artifact | Contribution |
|---|---|
| [[ADR-012]] | Why check-only with a staged swap, what it rules out, and why MSIX is deferred rather than dismissed |
| [releasing-and-updating.md](operations/runbooks/releasing-and-updating.md) | The shape a release must have, how to cut one, and the symptom table when it fails |
| [[ADR-011]] | The elevation path the updater deliberately does not reuse, and the difference between the two operations |
| [operations/design/screens/12-tray-autostart-update.md](operations/design/screens/12-tray-autostart-update.md) | The designed section, and the rule that an available update is never a notification |
| [technical-debt.md](technical-debt.md) §4.21 item 5, §5 | The debt closed, and why the feed is configuration rather than a constant |

### Deciding from a path instead of from the thing itself

**Three times now**, each costing a phase or a session, which is what earns an index entry. The
shape: code decides what is true by pattern-matching a location, and is right on the machine it
was written on.

| Artifact | Contribution |
|---|---|
| [[backup-says-it-saved-your-presets-and-it-did-not]] | Where preset files *are* — one root guessed, 98% of them missed |
| [[windows-asks-for-rights-the-app-already-had]] | What may be *done* to a folder — `Program Files` inferred to need administrator, when its ACL said otherwise |
| [technical-debt.md](technical-debt.md) §5 | The same instinct as a rule, now four source guards |
| [technical-debt.md](technical-debt.md) §7.6 | The **open** instance: whether a plug-in reference resolves by path or by identity — deliberately not guessed |
| [audits/2026-08-20-plugin-resolution-and-elevation.md](audits/2026-08-20-plugin-resolution-and-elevation.md) | The method for answering it, and why the current data cannot |

### A control that looks wired, and is not

**Three sessions have now lost time to this**, in three costumes, which is why it earns an index
entry rather than three unrelated gotchas. The shape: a view-model property that is implemented,
correct and unit-tested, with nothing rendering or reaching it — so every test passes and the
feature does not exist.

| Artifact | Contribution |
|---|---|
| [[a-settings-control-moves-and-nothing-happens]] | The original: a stepper with a tested clamp and no `Click` handler, for two phases |
| [[an-accelerator-shows-as-a-literal-underscore]] | The same, one layer down — `RecognizesAccessKey` defaults false, so every templated button silently refused access keys |
| [technical-debt.md](technical-debt.md) §4.20, §4.21 | Eight designed surfaces with finished view models behind them and no markup |
| [2026-08-20-clearing-the-technical-debt.md](sessions/2026-08-20-clearing-the-technical-debt.md) | What it costs to find eight at once, and the view-test rule that came out of it |

### Tier 3, and a heuristic that was wrong for two phases

The most instructive failure in the project so far: every test passed, the output looked correct,
and the tier captured 2% of what it promised. Read together before writing anything that guesses
at a path.

| Artifact | Contribution |
|---|---|
| [[ADR-006]] | The four tiers, and the tier-3 sentence that turned out to be wrong |
| [[ADR-010]] | Two roots, the rooted snapshot layout, and the four alternatives weighed |
| [[backup-says-it-saved-your-presets-and-it-did-not]] | The symptom, the numbers, and why the obvious diagnosis is wrong |
| [technical-debt.md](technical-debt.md) §4.18 | The entry that specified the check, and the before/after it produced |
| [2026-08-19-preset-roots-elevation-and-timing.md](sessions/2026-08-19-preset-roots-elevation-and-timing.md) | Why "verify it" became "change the snapshot format" |

### Administrator rights, and the one thing that needs them

Tiers 1–3 restore on an ordinary account. Tier 4 does not, and the gap between "the code can do
it" and "the app can ask for it" was a whole phase.

| Artifact | Contribution |
|---|---|
| [[ADR-006]] | Why only tier 4 touches a folder the user does not own |
| [[ADR-011]] | Relaunch the shell headless; what that rules out, including the mutex and the confirmed verdict |
| [`screens/13-elevation.md`](operations/design/screens/13-elevation.md) | The designed surface — the row, the prompt, and error 13 |
| [technical-debt.md](technical-debt.md) §4.17 | The entry, and why the blocker was design rather than code |

### Where the settings live, and where they don't

The decoy folder is the first thing that goes wrong and the easiest to get wrong silently.

| Artifact | Contribution |
|---|---|
| `SPEC.md` §1 | The paths, the sizes, the classification of every file under `LocalState` |
| [[ADR-003]] | Why the store is outside `LocalState` |
| [[backup-succeeds-but-protects-nothing]] | The symptom when discovery finds the decoy |
| [glossary.md](glossary.md) | `LocalState`, the decoy, package family name, backup store |
| [[phase-1-core]] | Where discovery is built |

### Validating a settings file

Three separate traps, one of which is the incident that started the project.

| Artifact | Contribution |
|---|---|
| `SPEC.md` §5 | Duplicate keys, round-trip loss, ranking by content |
| [[file-parses-but-wave-link-resets]] | Duplicate keys — the original incident |
| [[newest-backup-is-the-broken-one]] | Why timestamp ranking picks the reset config |
| [[every-snapshot-differs-with-no-real-change]] | The encoder mangling base64 state |
| [technical-debt.md](technical-debt.md) §1.3, §2.1 | The upstream gap and the unverified assumption blocking its fix |
| [Audit: voltybat](audits/2026-08-15-voltybat-wavelinksettingsutility.md) | Upstream `Validate()` and what it misses |

### Restoring safely

The part that looks obvious and fails.

| Artifact | Contribution |
|---|---|
| `SPEC.md` §4 | The sequence, and verification from the log |
| [[restore-a-settings-file-safely]] | The recipe, with the reason attached to each ordering constraint |
| [[restored-settings-revert-seconds-later]] | The flush race |
| [[preconditions-inside-the-operation]] | Why the write refuses rather than trusting the caller |
| `Restore/RestoreOrchestrator.cs` | The assembled sequence (phase 2) |
| [README.md](operations/design/README.md) Screen 2 | The confirmation dialog, and the automatic pre-restore snapshot |
| [glossary.md](glossary.md) | Verified exited, atomic write, shell AppID, pre-restore snapshot |

### The snapshot store

Where backups live, and why not where upstream put them.

| Artifact | Contribution |
|---|---|
| [[ADR-003]] | The decision: outside `LocalState`, identity in `manifest.json` |
| [Phase 2 design](plans/2026-08-16-phase-2-store-design.md) | Layout, manifest schema, the guard |
| [technical-debt.md](technical-debt.md) §1.1 | The inherited defect, struck through with its reasoning kept |
| [Audit](audits/2026-08-15-voltybat-wavelinksettingsutility.md) finding 1 | What upstream does and why it cannot be kept |
| [[newest-backup-is-the-broken-one]] | Why the list ranks by content, not by date |
| [glossary.md](glossary.md) | Snapshot, managed backup, trigger, dedup key, backup store |

### Automatic capture

The phase that made this a different product from the tool it was forked from.

| Artifact | Contribution |
|---|---|
| [[ADR-007]] | Watch don't poll; dedup by hash; never prune what the user named |
| [phase-3-automation.md](dev-phases/phase-3-automation.md) | The plan, and the no-real-time constraint |
| `SPEC.md` §2, §6 | Retention measurements and the design target |
| [README.md](operations/design/README.md) Screen 3 | **Copy that is a specification** — the debounce and rate limit are quoted to users |
| [technical-debt.md](technical-debt.md) §1.4 | The gap this filled, struck through |
| [[capture-fails-while-wave-link-is-running]] | Why the watcher's reads must be shared-mode |

### Keeping the corpus honest

This project's most distinctive practice, and it spans nearly everything.

| Artifact | Contribution |
|---|---|
| [README.md](README.md) | The `Provenance` rule for gotchas, and the "state provenance" best practice |
| `SPEC.md` Provenance + Corrections | The example the rule is modelled on, and three claims it later caught |
| [[guards-that-can-fail]] | The same idea in code: a guard nobody has watched reject something is a guess |
| [Audit](audits/2026-08-15-voltybat-wavelinksettingsutility.md) | Two of five findings did not survive a running system — both marked *read, not reproduced* |
| [[every-snapshot-differs-with-no-real-change]] | A gotcha rewritten when its cause turned out to be inverted |
| [Probe session](sessions/2026-08-16-phase-1-probe.md) | Where the discipline paid for itself |

### VST3 capture

Four tiers, three ways it bites.

| Artifact | Contribution |
|---|---|
| `SPEC.md` §9 | The tiering, the measurements, the three warnings |
| [[ADR-006]] | The decision, and what it rules out |
| [[restored-plugin-demands-a-licence]] | Licences do not travel |
| [[vst3-backs-up-as-nothing]] | Bundles are directories |
| [technical-debt.md](technical-debt.md) §2.3 | The untested path, and why the author's machine will never catch it |

### Shipping publicly

| Artifact | Contribution |
|---|---|
| `SPEC.md` §11 | Numbers that are not constants, privacy, open questions |
| [[ADR-008]] | Windows-only, stated rather than implied |
| [[restored-backup-has-dead-channels]] | Machine-local snapshots |
| [technical-debt.md](technical-debt.md) §5, §6 | The constants list and the privacy debt that gates going public |
| `.gitignore` | Refuses real settings files, VST3 binaries and the backup store |
