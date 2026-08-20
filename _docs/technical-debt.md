---
title: "Technical Debt"
status: published
created: 2026-08-16
updated: 2026-08-19
tags: [meta, technical-debt]
---

# Technical Debt

What is built and not right, what has never run, and what is known-wrong deliberately.
Distinct from [dev-phases/](dev-phases/README.md), which is for things not built yet.

**As of 2026-08-16 there was no application code**, so this document began unusual: nothing had
been *incurred*. What it held instead was debt we had agreed to take on, and assumptions the
project rests on that nobody had checked. Both were worth writing down now, because the moment
the fork landed the first list would become real, and the second list is the set of things that
will look obvious in hindsight.

**That has since changed.** Phases 1–5 have shipped real code — `Core`, `Cli`, and a WPF shell
that is now the process — so §1 and §7 entries have been resolved against it. As of phase 5 plan
8, §4.8 item 4 (the settings placeholder) and §4.9 (the dormant restore-outcome strip) are both
closed; §4.10 (the not-found first-run variant) is the one deferred minor still open from the app.
The unverified-assumption list in §2 is unchanged in kind: the code made some of those assumptions
load-bearing rather than hypothetical.

Be blunt. A debt list that flatters the project is useless.

---

## 1 · Inherited on fork — real the moment `voltybat/WaveLinkSettingsUtility` lands

Five defects, read directly off upstream source at `main` (pushed 2026-07-19). Full findings
in the [audit](audits/2026-08-15-voltybat-wavelinksettingsutility.md).

### 1.1 ~~Backups are written inside `LocalState`~~ — **FIXED 2026-08-16 (phase 2)**

> Snapshots now live outside `LocalState`, in a user-chosen store defaulting to
> `%LOCALAPPDATA%\WaveLinkBackup`. Identity moved from the filename to `manifest.json`, and
> `SnapshotGuard` replaced `ValidateManagedPath` — all three entangled pieces changed
> together, as required.
>
> **Pinned by** `SnapshotStoreTests.A_snapshot_survives_deleting_the_entire_LocalState_directory`,
> which deletes the whole directory and then verifies the snapshot still restores.
>
> The original text is kept below because it explains why the store is shaped the way it is,
> and that reasoning is still load-bearing.

#### Original entry

`NewBackupPath` writes `Settings.json.backup-<ts>` beside `Settings.json`, and
`ManagedBackups` only enumerates that directory. Resetting or uninstalling the MSIX package
deletes `LocalState` wholesale — every backup with it. A backup tool whose backups are
destroyed by the exact event you would want to recover from.

**Severity:** ~~critical~~ — **resolved**. This was the single change the fork existed to make.
**Entangled with:** `ValidateManagedPath`, which enforced that location, and the filename
regex defining what counted as a managed backup. Fixing one without the others would have left
restore refusing its own files — so all three were replaced in one change.
**Resolution:** [[ADR-003]], shipped in phase 2. The replacement guard also catches
post-write corruption, which the filename check never could.

### 1.2 ~~`Serialize` uses the default JSON encoder~~ — **NOT DEBT. Withdrawn 2026-08-16.**

Measured against the live file: **Wave Link writes with the default encoder**, and upstream's
call reproduces its output byte for byte (43,052 → 43,052, identical). Setting
`UnsafeRelaxedJsonEscaping` as previously planned would have shrunk the file by 1,411 bytes
and made every snapshot differ from the app's own output — causing the churn this entry
existed to prevent.

**No action. Do not "fix" the encoder.** Kept here, struck through rather than deleted, so the
idea is not re-derived from the spec body and re-adopted.
**See:** [[every-snapshot-differs-with-no-real-change]] and the audit's withdrawn finding 2.

### 1.3 ~~No duplicate-key detection~~ — **CLOSED in our port (phase 1); confirmed 2026-08-19**

> `Analysis/DuplicateKeyScanner` is the `JsonDocument` tree walk this entry asked for, and
> `SettingsAnalysis` runs it on every read — the result reaches the manifest as
> `HasDuplicateKeys` and the UI as the SUSPECT badge. `DuplicateKeyScannerTests` covers both
> shapes of duplicate. §2.1 unblocked it the day it was answered; the entry stayed open because
> nothing came back to close it.
>
> **Still worth offering upstream**, where the defect is unfixed. Original entry below.

#### Original entry

`Validate()` only asserts that `MixerConfiguration.InputSettings` is an object. The defect
that motivated this entire project — case-insensitively duplicated keys, which Wave Link
rejects outright — passes upstream validation unnoticed.

**Severity:** high. It is the original incident.
**Fix:** add a `JsonDocument` tree walk grouping property names case-insensitively. Due in
phase 1. **Blocked on** the unverified assumption in §2.1 below.
**See:** [[file-parses-but-wave-link-resets]].

### 1.4 ~~It is a manual tool, not a safety net~~ — **CLOSED 2026-08-16 (phase 3)**

> The watcher, debounce, rate limit, dedup and retention all ship. Upstream's gap — backups
> happening only when invoked — is the reason this project exists, and it is now filled.
> `AutoBackupPolicy` is pure, so the whole suite still runs in about a second.
>
> **Nothing calls it in production yet**, because the host is a shell and there is no shell.
> Phase 4's `watch` verb is the first real caller. Tracked there, not here — this is not debt,
> it is an unwired feature.
>
> Original entry retained below.

#### Original entry

Backups happen only when invoked. No watcher, no schedule, no dedup — so repeated runs write
identical copies.

**Severity:** not a defect upstream; it is a different product. **This is the gap this app
exists to fill**, and the reason forking rather than just using it is justified.
**Resolution:** [[ADR-007]]. Due in phase 3.

### 1.6 `WavelinkSEService` is never closed — **NEW, found at intake 2026-08-16**

`ProcessControl.FindGuiProcess` only ever looks for `Elgato.WaveLink`. `WavelinkSEService` is
never enumerated, closed or verified — so upstream's "verified exited" assertion can pass with
half of Wave Link still running, and a write can still race the service's flush. `SPEC.md` §4
is explicit that both must close.

**Severity:** moderate, and it undermines the one guarantee that shutdown sequence exists to
give.
**Status in our port:** **fixed.** `WaveLinkProcess.ProcessNames` covers both, and
`WaveLinkStillRunning` reports which are up. Recorded as audit finding 6; **worth offering
upstream.**

### 1.5 ~~Runtime dependency~~ — **RESOLVED 2026-08-16. The finding was incomplete.**

The csproj does set `PublishSingleFile` with `SelfContained=false` — the audit read it
correctly. Upstream's README also claims the tool needs no installed runtime. **Both are
true:** `.github/workflows/release.yml` passes `--self-contained true`, overriding the csproj
at publish time. The audit had simply never read the release workflow.

**The real issue, which is smaller:** the csproj and the release pipeline disagree, so a local
`dotnet publish` produces a *different artifact* from CI's — framework-dependent rather than
self-contained. A hidden dependency on a CI flag.

**Our position:** `WaveLinkBackup.Cli` sets `SelfContained=true` in the csproj, so the two
cannot disagree. The NativeAOT option remains open and unforeclosed ([[ADR-004]]); §2.4 still
gates it. **No debt carried forward.**

---

## 2 · Unverified assumptions

Things the design rests on that **nobody has checked**. Each one, if wrong, invalidates real
work — so each has a cheap check attached and an owner phase.

### 2.1 ~~Whether `JsonNode.Parse` collapses duplicate keys~~ — **ANSWERED 2026-08-16**

The question was mis-framed; neither offered answer was right. It depends on the kind of
duplicate:

| Input | `JsonDocument` | `JsonNode.Parse` |
|---|---|---|
| `{"A":1,"a":2}` — case-insensitive, *the actual defect* | preserves both | **preserves both**, round-trips intact |
| `{"A":1,"A":2}` — exact duplicate | preserves both; `GetProperty` returns the **last** | **throws `ArgumentException`** |

**No silent data loss** — the feared outcome is not real. §1.3's fix proceeds unchanged.

**New, smaller debt this uncovered — closed:** any `JsonNode.Parse` of an untrusted settings file
can throw `ArgumentException` from a dictionary insert. Unhandled, the user sees "An item with the
same key has already been added. Key: A" instead of "this settings file is malformed". It is
closed twice over: **nothing in the codebase calls `JsonNode.Parse` at all** — every parse is
`JsonDocument.Parse` — and every one of those call sites catches `ArgumentException` beside
`JsonException` and translates it. Confirmed 2026-08-19.

### 2.2 Whether non-MSIX Wave Link installs exist

Everything in `SPEC.md` assumes the Store/MSIX package. Whether older or enterprise builds
ever install as conventional Win32 is untested. If they do, discovery returns "not found" and
the app is simply useless to those users — with no diagnostic that says why.

**Check:** the release-channel installer, and whatever Elgato ships for managed deployment.
**Mitigation: DONE in phase 1.** `SettingsLocator.Locate(explicitSettingsPath)` **bypasses
discovery entirely** — unlike upstream, which requires the override to match a discovered
`Elgato.WaveLink_*` candidate and so cannot help a non-MSIX user at all. Covered by
`SettingsLocatorTests.An_explicit_path_bypasses_discovery_entirely`. `SettingsLocation.CanRelaunch`
is false for such a path, so callers can say "restored, but you will need to start Wave Link
yourself" rather than failing obscurely.
**Still open:** whether such installs exist, and **the amber not-found UI variant is not
designed** — see [README.md](operations/design/README.md) *Gaps*.
**Phase:** 5 for the UI.

### 2.3 ~~Whether the VST3 bundle path works~~ — **ANSWERED 2026-08-19 (phase 6)**

> **Both directions are covered by synthetic fixtures.** Capture tests for a directory FIRST and
> recurses the tree; restore puts the whole tree back. An empty bundle directory is treated as a
> failure rather than a zero-byte success, which is the shape the original worry took. The
> author's machine still has six single-file plugins and still cannot exercise either path, which
> is exactly why the fixtures exist. `TierCaptureTests` and `TierRestoreTests`.
>
> The original entry follows.

All six referenced plugins on the author's machine are single files. The VST3 spec defines a
*bundle* — `Plugin.vst3\Contents\x86_64-win\Plugin.vst3` — and installers increasingly ship
them that way. **This code path will never be exercised by the author's own setup**, so it
needs a deliberate test with a synthetic bundle or it ships broken.

Assuming "file" does not throw. It silently backs up **nothing** for those plugins, and the
snapshot looks fine.

**Phase:** 6. **See:** [[vst3-backs-up-as-nothing]].

### 2.4 Whether `[ComImport]` interop survives NativeAOT

`WindowsAudioEndpointInspector` is ~80 lines of hand-declared Core Audio COM interfaces. AOT
and `[ComImport]` need care. If it does not survive, the NativeAOT CLI option in §1.5
evaporates and the answer there is forced.

**Phase:** 7, but cheap to check earlier and worth doing before the §1.5 decision is framed.

> **Partially answered 2026-08-16 (phase 4) — and the part that matters is still open.**
>
> A NativeAOT publish of `wlbackup` **succeeds**, produces a **3.2 MB** binary (against 70.2 MB
> self-contained), and that binary runs correctly against a real Wave Link install. The IL
> compiler emitted **zero trim/AOT warnings**, so nothing in `Core` or `Cli` is
> AOT-incompatible today.
>
> **But this does not answer the question this entry asks.** `WindowsAudioEndpointInspector`
> has not been ported — there is no `[ComImport]` in the codebase — so the interop that
> prompted the doubt was never exercised. When endpoint inspection lands, re-run this and
> expect it to be the interesting case.
>
> **Build requirement worth knowing:** the AOT link step invokes `vswhere.exe` unqualified and
> fails with a misleading `MSB3073 ... exited with code 123` if it is not on `PATH`, even
> though the MSVC toolset is installed. Adding
> `%ProgramFiles(x86)%\Microsoft Visual Studio\Installer` to `PATH` fixes it. CI's
> `windows-latest` image has this wired already.

> **Related signal, 2026-08-16.** The phase-1 probe ran as a .NET 10 file-based app, which
> defaults to trimming-friendly settings, and reflection-based `JsonSerializer` threw
> `InvalidOperationException: Reflection-based serialization has been disabled`. That is not a
> product bug — but it is the same constraint AOT imposes. **`Core` should avoid
> reflection-based serialization regardless of the AOT decision**, using `JsonDocument`,
> `JsonNode` and `Utf8JsonWriter` (all reflection-free) rather than
> `JsonSerializer.Serialize<T>`. Doing that from the start keeps §1.5's NativeAOT option open
> at no cost; discovering it in phase 7 would mean rewriting the manifest layer.

---

## 3 · Known-wrong deliberately

Choices made with eyes open. Listed so they are not "discovered" later and fixed by someone
who does not know they were decided.

**Snapshots are not portable between machines, and we are not fixing that.** Endpoint IDs
embed device serials; plugin paths are absolute. "Export a chain" is a *different feature*
built on `AudioPluginConfigurations` alone, and it is out of scope. The UI labels snapshots
machine-local instead. See [[restored-backup-has-dead-channels]].

**Licences are not captured, and the UI says so rather than working around it.** Copying a
`.vst3` restores the code, not the authorisation — those vendors authorise via registry,
machine-bound licence files elsewhere, or an online account. Nothing licence-shaped exists in
the vendor folders we copy. Tier 4 gets a working plugin on the same machine; on a rebuild the
user still reinstalls and re-authorises. See [[restored-plugin-demands-a-licence]].

**`EBWebView\` is never captured**, despite being inside `LocalState`. ~100 MB of WebView2
browser profile — shader caches, IndexedDB, code caches. Capturing it turns a 470 KB snapshot
into a 100 MB one, and restoring it can wedge the UI.

**Wave Link's own AutoBackups are captured but never managed.** We copy them as payload
because they carry history our first run will not have. We do not prune, rotate or write them, and
a restore never puts them back — they are evidence, not payload. *(True since 0.6.0. It was stated
here as settled fact from 2026-08-16, and the code did not do it until phase 6 §8 — found by the
spec-coverage pass, which is the argument for having written one.)*

---

## 7 · Design decisions that outdated shipped code — **three shipped, one is phase-5 UI work**

> **Status 2026-08-17:** 7.1, 7.2 and 7.3 are **implemented and tested** (351 tests green).
> 7.4 is keyboard and focus, which is WPF work and arrives with the shell.
>
> The design package was amended to match (v5): `screens/05` now specifies the two-stage
> delete, `screens/08` the Empty trash row. **The amendment is upstream, not just in this
> repo** — the two no longer disagree.

The phase-5 design package (handoff part 2) closed the six gaps **and** made four behavioural
decisions that contradict code already written and tested. None of this is a mistake in either
place: the code was built to the best spec available, and the design has since decided better.
Recording it here because "the design says X, the code does Y" is exactly the kind of drift
that becomes invisible once phase 5 starts and everyone is looking at XAML.

### 7.1 ~~Delete must not be permanent~~ — **SHIPPED 2026-08-17**

> `SnapshotStore.Delete` moves to `<store>/.trash/<id>/`; `EmptyTrash` forwards via
> `IRecycleBin`. The CLI gained an `empty-trash` verb. Smoke-tested end to end on the AOT
> binary, which is still 3.2 MB.
>
> **Two deviations from the recommendation below, both deliberate:**
>
> 1. **`RecycleBin` lives in Core**, not per-shell. The recommendation assumed interop needs
>    the Windows Desktop ref pack; it does not — P/Invoke works from plain `net10.0`, and
>    `GuardNoDesktopFramework` guards the *ref pack*. Per-shell would duplicate the interop,
>    and a fourth project for one class is worse than either. Core already contains
>    Windows-specific behaviour for the same reason (`LaunchByAppId` shells to `explorer.exe`).
> 2. **`DllImport`, not `LibraryImport`.** The generator requires `AllowUnsafeBlocks` for the
>    whole project; granting unsafe to a conservative library for one call that runs when
>    someone clicks *Empty trash* is a poor trade. Equally AOT-compatible — verified.
>
> **`SHFileOperation` is tested against real temp directories**, including a sibling-directory
> case: `pFrom` is a double-null-terminated *list*, and a single terminator reads past the end
> and can take neighbours with it. That is not a bug to discover in production.

#### Original decision

`SnapshotStore.Delete` → `IFileSystem.DeleteDirectory` → `Directory.Delete(path, recursive: true)`.
Permanent. The design (decision 3) requires `SHFileOperation` with `FOF_ALLOWUNDO`, and changed
the dialog copy from "gone for good" to naming the Recycle Bin — because the old sentence
**became untrue** the moment the decision was made.

**This is the expensive one, and not for the obvious reason.** `SHFileOperation` is Win32
interop. `WaveLinkBackup.Core` deliberately targets **`net10.0`, not `net10.0-windows`**
([phase 1](plans/2026-08-16-phase-1-core-design.md) *as built*, enforced by the
`GuardNoDesktopFramework` MSBuild guard). Adding shell interop to `Core` either changes that
target or needs the call to live in a shell.

**Options, none of them free:**

| Option | Cost |
|---|---|
| Move `Core` to `net10.0-windows` | Undoes a phase-1 decision and re-admits the desktop ref pack the guard exists to reject |
| `[LibraryImport]` against `shell32` from `net10.0` | Works — P/Invoke needs no Windows TFM — but puts platform interop in the "headless" library, and `[SupportedOSPlatform]` warnings arrive with `TreatWarningsAsErrors` on |
| An `IRecycleBin` seam, implemented per shell | Keeps `Core` clean and honest; costs a seam and means the CLI must implement it too |

**DECIDED 2026-08-17 — two-stage delete, and it is better than what I recommended.**

Delete **moves the snapshot directory to `<store>/.trash/<id>/`** — a plain directory move, no
interop, no rename. An **Empty trash** action then hands the contents to the Recycle Bin.

Three reasons this beats the direct `SHFileOperation` I proposed:

1. **The Recycle Bin does not exist everywhere.** The store is user-chosen (`--store`), and on
   a network share or many removable volumes `SHFileOperation` either permanently deletes or
   prompts. The design's promise — *"goes to the Recycle Bin"* — is one the app **cannot keep**
   on those volumes. A `.trash/` directory behaves identically on all of them.
2. **Interop leaves the delete path entirely.** `Core` stays `net10.0`, the
   `GuardNoDesktopFramework` guard stays meaningful, and the one interop call lives behind an
   `IRecycleBin` seam used only by *Empty trash* — where failing to reach the Recycle Bin can
   degrade to an honest "deleted permanently" rather than corrupting the main flow.
3. **Undo becomes a move, not an API.**

**The naming mess this seemed to risk does not exist.** Snapshot directories are already
machine-generated (`2026-08-15T2307-a3f81c`) with identity in `manifest.json` — that is
[[ADR-003]]'s whole point. A move into `.trash/` preserves the id, the manifest and the guard's
ability to verify it. Collisions are impossible because ids carry a timestamp *and* a content
hash.

> **This amends design decision 3**, which currently reads *"Deleted backups go to the Windows
> Recycle Bin… No in-app trash view, no undo toast."* The amendment needed:
> deleted backups go to `<store>/.trash/`, and **Empty trash** — a single Settings row with a
> size readout, not a list — sends them onward. Still no trash *view*, still no undo toast; the
> tray's existing "Open the backup folder" is how anyone recovers one by hand.
> **The dialog copy must name `.trash`, not the Recycle Bin**, or it repeats the same untrue
> sentence for a new reason.

### 7.2 ~~Damaged backups must not count toward the keep-count~~ — **SHIPPED 2026-08-17**

> `BackupService.Prune` verifies each candidate through `SnapshotStore.Verify` and refuses any
> that fail. A test asserts **exactly one** `settings.json` read while pruning one of five —
> which is the whole argument that this does not reintroduce phase 2's cost.
>
> `TrashInvisibilityTests` additionally pins that trashed backups do not count toward the
> keep-count, so deleting one cannot silently make the next capture prune a good one.

#### Original decision

Decision 6: *"A corrupted file must never push a good one out."*

`SnapshotRetention.SelectForPruning` filters on `SnapshotManifest.IsPrunable`, which is
`Trigger == Automatic` and nothing else. **Retention cannot see damage at all** — damage is
detected by `SnapshotGuard.Verify`, which reads and hashes every file, and is called at
*restore* time, not at list time (a deliberate phase-2 choice: rehashing the whole store on
every window open is not free).

**DECIDED 2026-08-17 — verify lazily, only the condemned.**

`SelectForPruning` returns candidates; the pruner then verifies **only those**, and skips
deleting any that fail. Pruning a 30-deep store removes one or two snapshots, so this hashes
one or two — not the thirty that a verify-on-list would.

This is why it does **not** reintroduce phase 2's cost: the expensive thing was verifying
everything on every window open, and this verifies almost nothing, almost never.

No manifest change, no new field to keep in sync, and it is correct by construction rather than
by remembering to update a cached flag. A damaged snapshot simply never gets deleted — which is
exactly the rule: *"a corrupted file must never push a good one out."*

**Consequence to accept:** a damaged snapshot is now immortal until the user deletes it by
hand. That is the right trade — the alternative is a program that quietly destroys the evidence
of its own corruption — but the UI must therefore make damaged backups deletable, which
`05-delete-dialogs.md` already allows.

### 7.3 ~~Automatic backup must not queue~~ — **SHIPPED 2026-08-17**

> `Tick` clears `lastWriteAt` on failure and `TickResult` carries the `CoreError`.
> `WatcherFailureTests` pins the **absence** of retrying — two ticks after a failure reach Core
> once — rather than only the presence of the error, which is the assertion that would have
> passed while the bug remained.

#### Original problem

Decision 6: *"does nothing at all, and the status strip says so. It must not fail silently
every hour and it must not queue."*

`AutoBackupCoordinator.Tick` line ~85:

```csharp
var result = service.CaptureAutomatic();
if (!result.IsSuccess) return new TickResult(decision, null);   // lastWriteAt NOT cleared
```

A failure leaves the pending write set, so the next tick re-evaluates as `Capture` and tries
again — **every 15 seconds, silently, forever**. That is both halves of what the decision
forbids, and worse than the "every hour" it names.

Also needed: `TickResult` currently discards the error, so the shell has nothing to put in the
status strip. It needs to carry the `CoreError`.

**DECIDED 2026-08-17 — clear the pending write on failure, and surface the error.**

```csharp
var result = service.CaptureAutomatic();
if (!result.IsSuccess)
{
    lock (gate) lastWriteAt = null;          // stop queuing
    return new TickResult(decision, null, result.Error);
}
```

Clearing `lastWriteAt` is what stops the queue: the failure is reported once and the coordinator
returns to `NothingPending` until Wave Link writes again. The next real write re-arms it, so
nothing is lost — this is the same reconciliation-by-hash argument that makes a dropped watcher
event a latency problem rather than data loss.

`TickResult` gains the `CoreError`, which is what feeds the tray's **NEEDS YOU** state and its
tooltip (*"the backup folder can't be used"*), plus the nine-day notification in
`12-tray-autostart-update.md`. Without it the tray has a state it cannot enter.

**Needs a test that pins the absence of retrying**, not just the presence of the error — two
consecutive ticks after a failure must produce exactly one `CaptureAutomatic` call.

### 7.4 ~~Keyboard and focus~~ — **CLOSED 2026-08-20**

> Every item on the list, and most of them by making the structure right rather than by handling a
> key:
>
> - **Arrow keys with `Home`/`End`** — §4.14's flat list. `Home`/`End` had hand-written
>   code-behind because neither could reach past its own group's Selector; both are WPF's own now,
>   and a guard test pins that the code-behind is gone rather than merely unused.
> - **`Shift+F10` and the Menu key open the row's overflow** — the row had a decorative `···` and
>   no menu anywhere. There is a real one now, and it is on the CONTAINER rather than on the glyph,
>   which is the whole difference: WPF opens a control's `ContextMenu` for both keys and for a
>   right-click, with no code. Its three items reuse the same `RoutedUICommand`s as the bottom bar,
>   so it greys itself on a damaged row without knowing what damaged means.
> - **Alt-accelerators on dialog buttons** — and the trap underneath them, which is §4.20's lesson
>   again: a bare `ContentPresenter` has `RecognizesAccessKey` **false**, so an underscore renders
>   as a literal underscore and no accelerator exists. Every button template sets it, and a test
>   counts presenters against recognisers. The destructive buttons get one too, and a separate test
>   pins that this did NOT weaken the focus rule — focus still starts on Cancel and `IsDefault`
>   stays off everywhere.
> - **`Ctrl+F`, `F2`, `F5`, `Delete`, `Enter`, `Escape`** — already in `ShellCommands`; now pinned
>   gesture by gesture rather than assumed.
> - **`Space` activating the focused control**, and full keyboard reachability — WPF's, and
>   `FocusRingTests` already holds the visible focus ring.
> - **Screen-reader labels** — `SnapshotRowViewModel.AutomationName` and `SlotsAutomationName` were
>   already built to read as sentences, including the five-slot health strip this entry names. Every
>   surface added in this session carries `AutomationProperties.Name` for the same reason.
>
> Original entry below.

#### Original entry

The design closes Escape / Enter / F5 / focus-ring. **Decided 2026-08-17:** implement to
Windows conventions generally, not only the four keys named.

That means at minimum: full keyboard reachability with a visible focus ring everywhere;
`Alt`-accelerators on dialog buttons; `Space` activating the focused control; arrow keys moving
list selection with `Home`/`End`; `Shift+F10` and the Menu key opening the row's overflow;
`Ctrl+F` reaching the search field; and `Delete` on a selected row opening the delete dialog —
which must still land focus on Cancel, per the design.

**Screen-reader labels are part of this**, not a follow-up: the five-slot health strip is
meaningless to a screen reader as five unlabelled cells, and needs an `AutomationProperties`
name that reads as a sentence — *"5 inputs, all named: Wave Mic 1, Voice, Browser, Music,
System"*.


---

## 4 · ~~Design gaps carried into the build~~ — **the original six CLOSED 2026-08-17, except item 6**

> **The heading is about items 1–6 only.** Everything from 4.7 down arrived later, as the build
> found its own gaps, and several are open — check each item's own status line rather than this
> one. Open as of 0.6.3: **4.7** (no icon set) and **4.15** (0.5.1's frosting unseen — it needs a
> human, not a commit).

All six were undesigned. Five are now specified in
[operations/design/screens/](operations/design/screens/00-index.md), which also designed
several states nobody had listed:

| Was | Now |
|---|---|
| 1. Delete confirmation | `05-delete-dialogs.md` — three variants: normal, only-backup, pre-restore |
| 2. In-progress states | `04-in-progress.md` — determinate hairline for backup, four named stages for restore |
| 3. Error states | `06-errors.md` — **all twelve**, with a placement rule and a weight rule |
| 4. Search + no-results | `07-search.md` |
| 5. Keyboard, focus, high-contrast | `10-decisions.md` §6 + `11-high-contrast.md` |
| 6. Tray, autostart, update | `12-tray-autostart-update.md` |

**All six are now closed** (v4 of the package, 2026-08-17). Nothing in the UI is undesigned.

**Also designed, and not on anyone's list:** SUSPECT vs DAMAGED as distinct states (`02`), four
restore outcomes (`03`), persisted settings and the missing-folder screen (`08`), the
version-mismatch note (`09`), and a **WHICH WAVE LINK** settings row — which exists because
error 2 asks the user to choose an installation and nothing was storing the answer, so the app
would have asked on every launch.

**One correction to the first handoff worth knowing:** the SUSPECT badge was specified in red
(`--wl-accent-soft` fill, `--wl-accent` text) sitting inside an amber row — *the forbidden
second red*, and it made a health state look like an action. It is now amber. The design caught
its own rule violation; nothing had been built against it, so the correction cost nothing.

**Not closed:** Windows high-contrast mode, and item 6. See §7 for the four decisions that
outdated shipped code.

### 4.7 There is no icon set — **open, 2026-08-17**

`README.md` §icons says the prototype's glyphs are *"hand-drawn monoline SVG stand-ins in the
Lucide idiom (1.75px stroke, 24px grid). Substitute the codebase's real icon set at the same
weight and size."*

**There is no real icon set.** `operations/design/assets/` holds two F3NN3X marks and nothing
else, and the design names eleven Lucide glyphs it expects to exist — `shield-check`,
`download`, `rotate-ccw`, `pencil`, `trash-2`, `search`, `settings`, `folder`,
`alert-triangle`, `check-circle`, `chevron-down`.

This surfaces first in the tray, whose four states are shield + check / down arrow /
exclamation / slash, and which cannot ship as four `.ico` files because `11-high-contrast.md`
requires the icon to follow the *system* contrast. Plan 2 therefore draws the shield to the
24px grid and renders it at runtime.

**The deferred part is the asset, not the mechanism.** The renderer takes geometry and a
colour; substituting real Lucide path data is a data change. Recorded because a hand-drawn
stand-in that works is exactly the kind of thing that quietly becomes permanent.

**Status 2026-08-17:** the mechanism shipped. `TrayIconRenderer` draws all four states to the
24px grid and renders them at runtime. The four glyph constants in that file are the
substitution point.

### 4.8 ~~Deferred minors from the tray shell~~ — **CLOSED 2026-08-20**

> All five are resolved: 2 and 4 in phase 5, 1 and 5 in this session, and 3 by settling the
> question rather than flipping the control — see its row.

Plans 2 and 3 shipped. Five things were deliberately left, none blocking.

| # | Minor | Why it was left, and what fixing it costs |
|---|---|---|
| 1 | ~~**The tray icon renders at a fixed 32px.**~~ **Closed 2026-08-20.** `TrayIconRenderer.PixelSizeFor` derives the size from 16px × the DPI of the screen holding the taskbar, snapped to the sizes an `.ico` is normally cut at (16/20/24/32/48/64) — the shell rescales whatever it is given, and 38px scaled to 40 is blurrier than 48 scaled down. The DPI is asked of `Shell_TrayWnd` directly rather than inferred from a screen rectangle, because that window IS the taskbar. `SystemEvents.DisplaySettingsChanged` re-renders it, which covers the taskbar moving to a differently-scaled monitor. An unreadable DPI falls back to 32 — the size it always drew | — |
| 2 | ~~**Mono letter-spacing is not implemented.**~~ **Closed, phase 5 plan 4, Task 2.** `Views/TrackedText.cs` is a custom `FrameworkElement` with its own `Tracking` dependency property (em-based, matching the type scale's `.18em`/`.06em` figures) and does its own glyph-run layout — `TextBlock`'s missing `CharacterSpacing` is no longer a blocker. Used throughout plan 4: the tray readout, the column headers, the status strip, and every slot label | — |
| 3 | **`Back up automatically` shows a trailing check, not a switch.** `screens/12`'s ASCII sketch writes `[toggle]` | **Settled 2026-08-20: the check stays, and this is now a decision rather than an open question.** Windows draws no switch in a native context menu; a hand-drawn one would be the only control in the app that ignores the platform it sits in, in the one surface the user reaches most often. `screens/12`'s own sentence — "Nothing here opens a submenu" — reads as a menu that behaves like a menu, and the sketch's `[toggle]` is shorthand for "this is the on/off control", which a trailing check already is. Reopen only if the design says the literal reading was meant |
| 4 | ~~**`Settings…` opens a placeholder `MessageBox`, from the tray and — since phase 5 plan 4, Task 10b — the main window's own gear button too.**~~ **Closed, phase 5 plan 8.** The real 680px settings dialog ships: in-place commit (no Save button), atomic persistence to `%LOCALAPPDATA%\WaveLinkBackup\settings.json`, and the two new sections. Both the tray menu item and the main window's gear button call `App.OpenSettings()`, which now builds a `SettingsViewModel` and shows the dialog instead of a `MessageBox`. | — |
| 5 | ~~**A failed manual backup shows a raw `MessageBox`** carrying `CoreError.Message`~~ **Closed 2026-08-20.** The designed errors landed in phase 5, and this call site kept the box for everything they did not cover. It now goes to the danger strip via `Strip.ShowFailure`, exactly as a failed restore does — inline, where 06 places a consequence of a press, instead of a modal in Core's log phrasing | — |

Two related items are **not** debt and are recorded elsewhere because they are traps rather than
shortfalls: [the tray menu keeping its startup
theme](knowledge-base/gotchas/tray-menu-keeps-the-theme-it-started-with.md) and [the tray icon
refusing generated images](knowledge-base/gotchas/the-tray-icon-refuses-every-image-you-draw.md).

### 4.9 The restore-outcome strip is built but nothing feeds it — **closed, phase 5 plan 5 (2026-08-19)**

`RestoreOutcomeStrip` shipped fully in the restore-outcome-strip session: the four
`03-restore-outcomes.md` states, per-state chrome (left edge, amber status warm-up, auto-dismiss,
the *Rejected* state that refuses to clear until acknowledged), the XAML DataTriggers, and the new
`WlDangerSoft` brush in all three themes. Eighteen App tests plus a Core test pinning the
null-verdict branch hold it down.

**It was dormant by design** — no production code called `Strip.Show` or `Strip.ShowFailure`, so
the restore button ran `ShowRestorePlaceholder()` and the strip could not light up from a user
gesture. A tested seam waiting for its caller, not a bug.

**Closed by phase 5 plan 5.** The real restore flow now runs `RestoreOrchestrator` and feeds its
result into the strip: `MainWindow.xaml.cs` calls `shell.Strip.ShowResult(view.Result)` on success
and `shell.Strip.ShowFailure(...)` / `ShowError(...)` on failure, so every designed outcome is
reachable from a user gesture. The placeholder path is gone.

**One sub-item carried forward rather than closed here:** the *Failed* state's `WlDangerSoft` is
Transparent in HighContrast per `11-high-contrast.md`, and nobody has watched that read as
"failed" in a real high-contrast theme. The rule is applied; the pixels are not yet checked. That
is now part of plan 10's high-contrast verification pass, not an open debt of its own.

### 4.10 ~~First-run "Wave Link not found" variant~~ — **CLOSED 2026-08-20**

> The amendment below was right that only the markup was missing. It is drawn now: the amber dot,
> the `WAVE LINK NOT FOUND · NO SETTINGS FILE IN THE USUAL PLACE` line, the mono `LOOKED IN …`
> second line at 80%, and the secondary *Choose the settings file…* 10px below it — bound to
> `FirstRunError1Label` and `FirstRunLookedInLabel`, which had been correct and referenced by
> nothing since plan 7.
>
> **The button is the point.** `App.ChooseSettingsFile` persists an explicit path and re-points the
> capture path at it, which is the only route a **non-MSIX install** has into this app at all
> (§2.2). Before it, such a user saw an empty gap where the found-line goes and had no way in.
>
> Original entry below.

#### Original entry

Phase 5 plan 7 shipped the first-run / empty state (Screen 4) with its **found** variant: the
green dot, *"Found Wave Link's settings — 5 inputs · C:\Users\…\LocalState\Settings.json"*, and
the amber "Wave Link not found" status-strip line for error 1. The **not-found first-run variant**
is deliberately not built yet: when Wave Link is absent on the very first run, Screen 4 should
render without the green found-line (or with a neutral *"No Wave Link installation found"* note)
and still offer *Back up now* / *Choose where to keep them*, because an explicit settings path
(§2.2's `SettingsLocator.Locate(explicitSettingsPath)`) can make the app useful to non-MSIX users
even with no discoverable install.

**Why it was left:** plan 7's scope was the found-path first run plus the twelve error screens;
the not-found-first-run combination is a distinct state that needs its own design pass in
`06-errors.md` (it is the one screen where an *error* and the *empty state* overlap) before code.
The status-strip amber line for error 1 already exists, so nothing regresses — the app simply
does not yet special-case first-run-when-not-found beyond that strip.

**What closes this:** a designed variant in `06-errors.md`, then a Screen 4 branch keyed on
`WaveLinkInputs == null` (the harness already models it) that suppresses the found-line and shows
the neutral note. **Phase:** 5, after plan 7. **See:** §2.2 (non-MSIX installs) and
[[file-parses-but-wave-link-resets]].

> **Amended 2026-08-19, by the design-conformance audit.** Two thirds of "what closes this" turn
> out to be done already. `06-errors.md` **does** specify the variant — *"First-run variant (the
> amber status line the empty state reserves space for)"*, with the centred amber dot, the
> `LOOKED IN …` mono second line and the *"Choose the settings file…"* button — and
> `ShellViewModel.FirstRunError1Label` / `FirstRunLookedInLabel` implement it, correctly and with
> tests. What is missing is only the markup that binds them: nothing in the app references either
> property. This is smaller than the entry above describes, and it is no longer blocked on design.
> See [audits/2026-08-19-design-conformance.md](audits/2026-08-19-design-conformance.md) §2.3.

### 4.22 ~~The audit's three small ones~~ — **CLOSED 2026-08-20**

> [audits/2026-08-19-design-conformance.md](audits/2026-08-19-design-conformance.md) §2.9 a, b
> and c. Recorded here because the audit is a snapshot and this list is the ledger.
>
> **a · The stats line printed free space only.** The design's line is
> `N BACKUPS · X MB USED · Y GB FREE ON THIS DRIVE`; the count and the used bytes live on the
> shell and were never plumbed through, which the code's own comment said. All three now come from
> the same store read that fills the trash row, and each omits itself when it is not known.
>
> **b · The proportion bar kept its colours in high contrast and gained no labels.**
> `11-high-contrast.md`: "the proportion bar loses its colour segments; label the segments
> instead." In high contrast every fill is transparent, so the four bands were one
> undifferentiated track with nothing carrying the encoding. `ProportionSegment.Label` and a
> legend below the bar, shown only in high contrast.
>
> **c · The row grid was clipped below ~1124px.** The window minimum goes 980 → 1124. The design's
> own six columns need 1084 in a window it allows to be 980 wide, so it does this to itself;
> raising the floor is the answer that invents no visuals and adds no interaction the design never
> specified. Decided with the user, 2026-08-20.

### 4.11 ~~Total-size arithmetic is copy-pasted, not shared~~ — **FIXED 2026-08-19**

> `SnapshotManifest.TotalSizeBytes` is the one place it lives, and all five call sites —
> `DeleteDialogModel`, `SnapshotRowViewModel`, `SnapshotListViewModel`, `HealthProbe` and
> `SnapshotStore.TrashSize` — read it. The two remaining `Sum(f => f.SizeBytes)` in the codebase
> are deliberately different sums over FILTERED sets (`RestoreOrchestrator`'s per-tier figure,
> `SettingsViewModel`'s per-row one) and are left alone. Two tests in `ManifestFieldTests`.
>
> Original entry below.

#### Original entry

`manifest.Files.Values.Sum(f => f.SizeBytes)` (or the equivalent) is independently reimplemented
in at least five places: `DeleteDialogModel`, `SnapshotRowViewModel`, `SnapshotListViewModel`,
`HealthProbe` in `WaveLinkBackup.App`, and `SnapshotStore` itself in Core. `SnapshotManifest` has
no `TotalSizeBytes` computed property to source this from, so every caller re-derives it.

**Not a Core-logic leak** — it is trivial arithmetic, not analysis, so this is a DRY gap rather
than a violation of "no Core logic in the shell". **Fix:** add a `TotalSizeBytes` computed
property to `SnapshotManifest` in Core and point all five call sites at it. Cheap, low risk,
no schema change. **Phase:** whenever it's next touched; not blocking.

### 4.12 Motion is specified and entirely unbuilt — **closed, 2026-08-19**

README's "Space, shape, motion" gives three timings — 140ms hover, 220ms state change and theme
change, easing `cubic-bezier(.2,0,0,1)`, with "no bounce, no slide" and "hover changes colour
**or** position, never both" — and the row-expansion note repeats it ("a height/opacity change at
220ms, no slide"). **Nothing in the app animates.** Every hover, selection, row expansion and
theme swap is an instant property change, because each is a plain `Trigger`/`DataTrigger`
`Setter` rather than an `EnterActions` storyboard.

This is the largest single remaining gap against the reference and the one an eye notices without
measuring anything: the design reads as a surface that responds, the build reads as one that
switches. It was not fixed in the audit because it is not a value correction — it needs
`ColorAnimation`/`DoubleAnimation` storyboards on `WlGhostButton`, `WlSecondaryButton`,
`WlPrimaryButton`, `WlDangerButton`, `WlRowTemplate` and the caption buttons, each of which
changes how those templates are structured, and the row expansion additionally needs a height
animation over a `Collapsed`/`Visible` swap that does not animate at all today.

**Built 2026-08-19.** `Views/Motion.xaml` holds one shared easing — `CubicBezierEase`, a real
`EasingFunctionBase` solving cubic-bezier(.2,0,0,1) rather than the nearest named WPF curve, with
its solver unit-tested against hand-computed points and against the identity bezier. Storyboards
run on all six templates plus the row: hover at 140ms on the ghost, secondary, stepper and caption
buttons and on the row; the primary and danger fills dip their own opacity at 140ms; the selected
row's expansion reveals over 220ms on MaxHeight and Opacity.

**Two things the build had to work around, both worth knowing before touching this again:**

- **A hover is a LAYER, not a `Background` swap.** WPF cannot animate a `Background` between two
  brushes held as theme resources — a resource brush is frozen, and animating its `Color` throws.
  Every hover is therefore a separate `Border` fading its `Opacity`, which has the useful side
  effect of surviving a live theme swap with no special case.
- **The row's selection FILL is still instant, deliberately.** Animating it means moving
  `RowSurface`'s background onto a layer, and that background is painted by a trigger graph whose
  ORDER is load-bearing and pinned by `RowTemplateTests` (health outranks selection; selected +
  high contrast must be last). Hover and the expansion animate without touching that graph at all;
  the fill cannot. Left as a deliberate exception rather than destabilising a tested invariant.

**Still not animated:** the theme swap itself (220ms per README). A resource swap has no
intermediate state to interpolate — it needs a snapshot layer cross-faded over the window, which is
a different piece of work from anything here. Not carried as its own item because it is the same
sentence in the same README row; noted here so it is not mistaken for an oversight.

### 4.13 The restore dialog's missing-plug-in warning has no bold lead — **closed, 2026-08-19**

README Screen 2 item 4 splits that warning in two: **"FabFilter Pro-Q 3 isn't installed on this
computer."** in `--wl-strong`, then the consequence in body colour. `RestoreDialogModel` exposes
it as one `MissingPluginWarning` string and the view renders one `Run`, so the sentence that
names the missing plug-in carries no more weight than the rest of the paragraph — which is the
opposite of the point.

**Built 2026-08-19.** `RestoreDialogModel` now carries `MissingPluginLead` and
`MissingPluginRest`, with `MissingPluginWarning` kept as a computed join — the view still binds its
visibility to that one value, and a screen reader still reads the warning as one announcement
rather than two fragments. The view renders two `Run`s, lead in `WlStrong`.

Phase 6 §5 still owns making it *appear*: both properties are null until there is a plug-in
manifest to compare against, so the block renders nothing today. What changed is that §5 now has
somewhere to put each half.

### 4.14 ~~The list is several Selectors, and arrow keys stop at a group boundary~~ — **FIXED 2026-08-20**

> Built exactly as the entry proposed, and it deleted everything it said it would.
>
> One flat `ListBox` over a `ListCollectionView` grouped on each row's own `GroupHeader`. The
> header comes from a `GroupStyle`, which matters more than it looks: a group container is not a
> `ListBoxItem`, so `↓` does not stop on a date on its way between two backups.
>
> **`GroupSelection` is gone** — file deleted, not merely unused. So is the `Home`/`End`
> code-behind, and the `SelectionChanged` routing. `SelectedItem` is an ordinary TwoWay binding
> again, which is what a single Selector allows and what several Selectors made actively harmful.
>
> `MainWindowSelectionTests` was rewritten rather than repaired: it existed to pin the workaround.
> It now pins the property that actually has to hold — one selection across dates, the Selector and
> the model agreeing, and rows under different dates in one continuous Items collection. Five
> source-guard tests in `MainWindowTemplateTests` were inverted for the same reason, each saying in
> place why the old assertion was pinning the defect.
>
> Original entry below.

#### Original entry

One `ListBox` per date group is what gives native row selection at all (Task 10b), and it is why
`↑`/`↓` move within a group and stop at its edge. `Home`/`End` are the exception — they were given
explicit code-behind handling to reach the true first and last row of the whole list.

That was an accepted limitation. **0.5.1 made it a debt**, because the same structure turned out to
be the cause of a real defect: a selection cannot span several Selectors, and the shared
`SelectedItem` binding built to make it do so both failed and ping-ponged
([[three-backups-look-selected-at-once]]). `GroupSelection` now carries that rule in explicit code.

**Fix:** one flat `ListBox` with a `CollectionViewSource` and `GroupStyle`. It would delete
`GroupSelection` entirely and fix arrow-key movement in the same stroke, because a single Selector
is single-select and continuous by construction. It is a real change: `SnapshotListViewModel`
pre-groups into `DateGroup`s, so the grouping would have to be re-derived through a
`CollectionView`, and the row template's host changes with it.

**Deliberately not done as a bug fix** — the defect had a smaller correct fix, and rebuilding the
list's structure to close it would have been a refactor wearing a bug fix's clothes. **Phase:** its
own task, whenever cross-group keyboard movement is wanted.

### 4.15 0.5.1's dialog frosting has never been seen — **open, needs a human, 2026-08-19**

`AcrylicDialogBackdrop` calls `SetWindowCompositionAttribute`, which is undocumented and the only
route that blurs the *window behind* rather than the desktop material (`DwmSetWindowAttribute`'s
system backdrops composite the wallpaper). Nothing in the suite can assert that a blur rendered.

It is structured to fail safely: nothing throws, the return is advisory, two accent states are
tried newest-first, and the dialog's own `WlScrim` fill guarantees a dimmed owner regardless. So
the risk is not a crash — it is that the frost silently does nothing on some builds and nobody
notices, because the fallback looks deliberate.

**Check by eye**, alongside the rest of 0.5.1's visual work — the motion timings, the scrollbar,
and the restored letter-spacing are all in the same category. **Fix if absent:** the fallback is
already correct; the question is only whether to keep the call.

### 4.16 ~~Tier 2 rehashes every plugin binary on every capture~~ — **FIXED 2026-08-19**

> Built as the entry describes, with the measurement it asked for replaced by the cheaper move of
> making the skip conservative enough that being wrong costs a hash rather than a stale value.
>
> **The rule:** `PluginManifestEntry.BinaryMatches` says yes only when the entry has a hash, a
> recorded size AND a recorded write time, and both figures equal what the binary measures now.
> Any of those missing or different means hash it. plugins.json goes to **schema 3** to carry
> `binarySizeBytes` and `binaryLastWriteUtc`; the addition is purely additive, and a schema-2
> entry reads back as one that always needs rehashing.
>
> `TierCapture.Gather` takes the previous manifest as an optional third argument — null is always
> correct and always safe. Both shells pass the newest snapshot's plugins.json; the CLI wiring has
> its own test, per §4.20's lesson that a tested rule is no evidence anything reaches it.
>
> **The second read is gone too.** §4.19's streaming copy means tier 4 no longer re-reads the same
> binary to copy it, so an unchanged plug-in set now costs one `stat` per plug-in on a capture that
> claims tiers 1–3, and one streamed pass when it claims tier 4.
>
> Original entry below.

#### Original entry

`PluginManifestBuilder` reads each referenced `.vst3` in full to hash it, and it runs on every
capture — including the automatic ones the watcher fires. On the reference rig that is ~40 MB per
snapshot ([[ADR-006]]: the referenced set is 39.8 MB against a 4,887 MB VST3 tree), read from disk
for a value that changes only when the user updates a plugin.

Not a correctness problem, and not yet measured against a capture the user can feel. The obvious
fix is to skip the hash when the path, size and last-write time match the newest snapshot's entry
— `IFileSystem` already exposes `GetLastWriteTimeUtc` and would need a size — which is a cache
with an invalidation rule, so it is worth having a measurement before writing one.

**Signal it matters:** an automatic capture visibly lagging, or the watcher's debounce window
being missed on a rig with a large plugin set. **Worse since 0.6.0**, where tier 4 can read the
same 40 MB a second time to copy it.

### 4.17 ~~The shell cannot restore plug-in binaries~~ — **CLOSED 2026-08-19**

> The blocker was never the code — tier 4 restore has been built and tested since phase 6. It was
> that elevation had no designed surface, and inventing one in XAML is the thing [[ADR-004]] and the
> design package exist to prevent. So the surface was designed first:
> [13-elevation.md](operations/design/screens/13-elevation.md), in 06-errors.md's own shape and
> under its own rules.
>
> **What was designed.** One row in the restore dialog — *"Also put the plug-in files back"*, the
> Settings dialog's row shape, off every time, **absent** rather than disabled when the snapshot
> holds no binaries. And a thirteenth error for the declined prompt: an inline result strip,
> **neutral**, because declining changed nothing — the settings and presets went back, the plug-ins
> on this machine are as they were, and the backup still holds them.
>
> **How it elevates.** The shell starts a second copy of ITSELF with `--restore <id>
> --with-plugins` and waits; Windows draws its own consent dialog. We never paint an administrator
> prompt — a program that draws its own is teaching people to trust a thing they should not. The
> elevated copy takes the pre-restore snapshot itself, so at the moment Windows asks, nothing has
> been touched and a decline costs exactly nothing.
>
> **The one non-obvious part** is that the elevated copy must skip the single-instance mutex. It is
> `Local\` and per-user, so the elevated copy runs as the *same* user, would find the mutex held by
> the window that started it, conclude it is a second launch and exit without restoring anything.
> It is not a second instance — it is one operation, and the race the mutex prevents is two watchers
> over one settings file. `ShellArguments.IsHeadlessRestore` is that distinction, with the reasoning
> on it.
>
> **Pinned by** `ElevatedRestoreTests` (13 tests: the flags, what the elevated copy does and
> refuses to do without the flag, the pre-restore snapshot, the exit codes against the CLI's, the
> row's presence rule and its measured size) and two `RestoreDialogViewTests` that render the row in
> all three themes — the guard that exists because this dialog once could not open at all.
>
> **Left deliberately undone:** a "don't ask again" (Windows will not remember it and neither
> should the row), and elevating the whole app at launch.

### 4.18 ~~Tier 3's preset heuristic has never met a real vendor folder~~ — **MEASURED AND FIXED 2026-08-19**

> Run against the reference rig at last, and it was wrong in exactly the way this entry feared —
> **capturing the wrong thing quietly.** Both halves of the fix and the evidence are below.
>
> **What one capture found.** `%APPDATA%\FabFilter\Pro-Q 4` exists, so the heuristic read it and
> reported three saved presets. The three files are `InterfaceDefaults.ffd`,
> `MidiControllerMap.ffm` and `PresetCache.dat`. The user's actual presets — the `.ffp` files the
> Settings dialog promises as *"your EQ curves, your gate thresholds"* — were 172 files in
> `Documents\FabFilter\Presets\Pro-Q 4\`, in a folder tier 3 never looked at.
>
> [[ADR-006]]'s two measurements were both correct and both misread: `%APPDATA%\FabFilter` does
> hold 246 files, and they are caches and factory component presets; `%APPDATA%\Supertone\Clear`
> does hold crash reports only — and tier 3 captured two of them and counted them as presets.
>
> | Plug-in | Captured before | Captured now |
> |---|---|---|
> | FabFilter Pro-Q 4 | 3 | 175 |
> | FabFilter Pro-C 2 | 2 | 111 |
> | FabFilter Saturn 2 | 53 (factory `Component Presets`) | 131 |
> | FabFilter Pro-L 2 | 2 | 62 |
> | FabFilter Pro-DS | 1 | 12 |
> | Supertone Clear | 2 crash reports | 0, with the folder still recorded |
> | **Snapshot** | 61 preset files | **491 preset files, 4.4 MB** |
>
> **The fix, in three parts.**
>
> 1. **Two roots, not one.** `PresetFiles` reads `%APPDATA%` and Documents, and takes at most one
>    folder from each. Additive rather than first-wins, because FabFilter keeps the MIDI map in one
>    and the presets in the other, and choosing would mean losing half the user's work.
> 2. **The roots have different fallbacks, deliberately.** `%APPDATA%\<Vendor>` ends the AppData
>    candidates because a vendor folder there is config-sized whatever it holds.
>    `Documents\<Vendor>` does **not** end the Documents candidates — a vendor folder in Documents
>    is as likely to be a project library as a preset folder, and that fallback would turn a
>    ten-megabyte tier into a hundred-gigabyte one on somebody's machine. Documents stops at
>    `<Vendor>\Presets`.
> 3. **Some files are never presets.** A `Reports`, `Logs`, `Crashes` or `Diagnostics` directory is
>    skipped at any depth. Clear now reports its folder with a count of zero, which is the state
>    `PresetFileCount` was designed to show: *we looked here and there was nothing worth keeping.*
>
> **The snapshot layout changed with it,** because it had to: a preset stored at
> `presets/<Vendor>/…` cannot be restored to the right place once there are two places it could
> have come from. Preset paths now name their root — `presets/appdata/…`, `presets/documents/…` —
> and `plugins.json` is **schema 2**, with `presetSources` as an array. Snapshots already on disk
> are unaffected: a path with no root segment reads as AppData, which is the only place those files
> came from, and the schema-1 `presetSource` string is still read.
>
> **Pinned by** `TierCaptureTests.Presets_in_Documents_are_captured_as_well_as_the_ones_in_AppData`,
> `A_vendor_folder_in_Documents_is_never_taken_whole`,
> `Crash_reports_are_not_presets_and_are_never_captured`, and
> `TierRestoreTests.A_preset_from_Documents_goes_back_to_Documents_and_not_to_AppData` and
> `A_snapshot_written_before_the_roots_existed_still_restores_into_AppData`.
>
> **One number for §5.** The reference rig has Documents redirected to `G:\win_user-folders\`.
> A composed `%USERPROFILE%\Documents` would have found an empty folder and reported that the user
> has no presets — the same trap as `%LOCALAPPDATA%`, failing more quietly. Both roots resolve
> through `Environment.GetFolderPath`, and the test constants sit on a different drive so the trap
> cannot be reintroduced by a test that passes.
>
> **Still a heuristic.** Two vendors were checked, not twenty. The original entry is kept below
> because its reasoning is what caught this.

#### Original entry

`PresetFiles` tries `<Vendor>\<Plugin>`, then `<Vendor>\<file name>`, then the vendor folder. Every
test uses a synthetic tree. [[ADR-006]] measured `%APPDATA%\FabFilter` at 246 preset files and
`%APPDATA%\Supertone\Clear` as crash reports only — but nobody has run the capture against either.

**The risk is not a crash**; it is capturing the wrong thing quietly, or nothing at all. Which is
why every plugin records `presetSource` and its file count in `plugins.json`: the check is one
capture and one look at that file.

**Check by eye**, alongside 0.5.1's visual items (§4.15). **Fix if wrong:** the order of the
candidates, or a per-vendor exception list — the heuristic is deliberately in one class.

### 4.19 ~~Tier 4 reads whole binaries into memory~~ — **FIXED 2026-08-19**

> `IFileSystem.CopyFile` is the seam the entry asked for: 1 MiB at a time through an
> `IncrementalHash`, returning the SHA-256 and the length of what it wrote. Peak memory is the
> buffer.
>
> **It was worse than this entry described.** The peak was not one plug-in — `CapturedFile`
> carried a `byte[]`, and `TierCapture` built the whole list before the store wrote any of it, so
> a capture held the ENTIRE preset and binary set at once (~40 MB on the reference rig, unbounded
> with a sample-library instrument on a channel). `CapturedFile` now names a source path and a
> size; the store copies from it.
>
> **Two things fell out.** The manifest now records what the copy actually wrote rather than what
> the capture measured beforehand — pinned by a test where the two differ, which the old shape
> could not express. And the readability check that decided tier 4's all-or-nothing fate moved to
> `IFileSystem.CanReadShared`, which opens and closes and reads nothing.
>
> `TierRestore` streams too. Five tests in `FileSystemTests` against the real filesystem, one
> deliberately larger than the buffer.
>
> Original entry below.

#### Original entry

Capture and restore both go `ReadSharedBytes` → `WriteBytes`, so a 24 MB `Clear.vst3` is a 24 MB
array. One file at a time, so the peak is one plugin rather than the set — acceptable, and the
reason `IFileSystem` has no streaming copy today.

**Signal it matters:** a plugin large enough to be felt (sample-library instruments run to
hundreds of megabytes, and nothing stops one being on a channel). **Fix:** a `CopyFile` on the
seam, which also lets §4.16's hash-skip reuse it.

### 4.20 ~~The Settings dialog committed to a file nothing re-read, and one stepper did nothing at all~~ — **FIXED 2026-08-19**

> Found while adding the two backup-timing controls, because both would have landed in the same
> trap. Two defects, one cause: the dialog's "changes apply as you make them" was only half true.
>
> **The save callback wrote the file and stopped.** `App.BuildSettingsViewModel` passed
> `s => repo.Save(s).IsSuccess`, so a committed change reached disk and never reached the running
> app. `App.settings` — the record `GatherPayload` closes over — stayed stale, which means the tier
> toggles shipped in 0.6.0 took effect on the **next launch**, not the next capture, despite a
> comment in `Compose` saying the closure existed precisely so they would not. The automatic-backup
> switch had the same problem, more quietly. `App.ApplySettings` is now the one place a settings
> change becomes true: written, held, and re-applied to the host.
>
> **The keep-count stepper's buttons had no handler.** `DecrementKeepCountButton` and
> `IncrementKeepCountButton` were declared in XAML with the readout bound and the view model's clamp
> unit-tested — and nothing ever wired a `Click`. Pressing either did nothing, for the whole of
> phases 5 and 6. Nothing caught it because the model was tested and the wiring was not.
>
> **Pinned by** `SettingsDialogViewTests.Every_stepper_button_is_wired_to_something`, which presses
> the `+` of every stepper in the dialog and asserts each value MOVED — only the `+` halves, so an
> unwired handler cannot hide behind its opposite cancelling out. Verified to fail when the wiring
> is removed.
>
> **The lesson worth keeping:** a view-model property with a commit path is not evidence that a
> control reaches it. These two suites now overlap on purpose.

### 4.21 ~~Eight designed surfaces are specified and undrawn~~ — **ALL EIGHT CLOSED 2026-08-20**

Full detail, with what exists behind each one:
[audits/2026-08-19-design-conformance.md](audits/2026-08-19-design-conformance.md) §2. Summarised
here because that is where a debt belongs, and because §4.20's lesson repeats itself in almost
every line of it — a view model with a tested property is not evidence that anything renders it.

| | Surface | Design | State |
|---|---|---|---|
| 1 | The rejected restore has no action and cannot be dismissed | `03` §3 | **CLOSED 2026-08-20.** Headline, body, mono meta, ghost *Show the log*, primary *Restore "Before restore"*, and that row rendered selected below. `AcknowledgeReject` is called by the primary action — the only exit the design allows. A rejection with no pre-restore copy says so and lets itself be cleared, rather than being permanent for a second reason |
| 2 | Backing-up in-progress state | `04` | **CLOSED 2026-08-20.** `BackupProgressModel` + the strip: hollow circle, *Backing up your setup…*, `470 KB · WRITING`, and a 2px determinate bar. Determinate on **real** bytes — `SnapshotWriteProgress` reports what is on disk against a total the payload knew before the first write. 04 bans a spinner because it "implies uncertainty that does not exist here", which makes an invented percentage the worse version of the same claim |
| 3 | First run, Wave Link not found | `06` | **CLOSED 2026-08-20** — see §4.10 |
| 4 | Settings `WHEN WINDOWS STARTS` | `12` | **CLOSED 2026-08-20.** Both toggles, the Task-Manager-veto note, and the Run-key line. `StartupSeam` carries the two seams in; the section hides itself entirely when nothing is behind it, rather than drawing controls that write nowhere |
| 5 | Settings `UPDATES` | `12` | **CLOSED 2026-08-20.** The three rows, the failed-update block, and a real updater behind them — feed, checksum-verified download, staged install, relaunch. Error 8's *"Get the update"* deep-links here and opens with a check already running. The design's restraint rule is structural: `UpdateViewModel` takes no success as an input, so it cannot produce a congratulatory anything, and the only unprompted act is the weekly look. **Where to look is read from the environment, not compiled in** — this repo has no remote, and a hard-coded owner/repo would be §5's exact mistake; unset hides the section. `.github/workflows/release.yml` produces the shape the updater looks for |
| 6 | The two tray notifications | `12` | **CLOSED 2026-08-20.** `TrayNotifications` decides both, as a pure function. The nine-day notice fires once per EPISODE — it re-arms when a backup happens, so a machine that recovers and falls behind again is told twice, which is two real problems rather than a nag. Nothing here can produce a success notice, because nothing here takes a success as an input. **One documented difference from the design:** each notice's action is the whole notification rather than a labelled button. A classic balloon has no buttons and Windows renders one as a toast that drops them; real toast buttons need an AppUserModelID and a Start-menu shortcut, which is an installer concern this app does not have yet. The label is stated in the body and clicking anywhere does the thing |
| 7 | Error 2's chooser rows | `06` §2 | **CLOSED 2026-08-20.** Version, `RUNNING` chip, ellipsised path, `SETTINGS SAVED … · N INPUTS · N KB`, the selected-row fill and 3px accent edge, and *Remember this one*. Each candidate is inspected on its own — the dialog exists because discovery could NOT choose between them, so nothing may lean on a "current" one. **The `RUNNING` chip is an approximation and the model says so**: Windows offers no mapping from a running MSIX process back to its package, so it goes to whichever candidate's settings file was written most recently, and only while Wave Link is up |
| 8 | Error 9, in Settings after *Change folder…* | `06` §9 | **CLOSED 2026-08-20.** Rendered in place under *Change folder…*, with *Choose another…* and *Keep the current folder*. 06's placement table files error 9 under Dialogs; §9's own text says "appears in Settings, in place", which is the more specific instruction and is what shipped. Changing the folder to somewhere holding files but no snapshot now raises it instead of silently pointing the store at a Recordings folder |

**Every one of these was the §4.20 lesson repeating**: a view model with a tested property is not
evidence that anything renders it. Each now has a view test that walks the real tree or reads the
real markup, not just a model test.

---

## 5 · Numbers that are not constants

Measured on one machine on 2026-08-15. Each becomes a bug if hard-coded. Reproduced from
`SPEC.md` §11 because this is the list most likely to be violated by someone moving fast.

| Looks like a constant | Actually | Do this instead |
|---|---|---|
| `Elgato.WaveLink_g54w8ztgkx496` | Stable per Store identity, but never assume | Glob `Elgato.WaveLink_*` |
| 5 inputs / 43 KB | One user's rig | Compare against *that user's* previous snapshot |
| `C:\Program Files\Common Files\VST3` | Default, and overridable | Resolve from `FilePath`; standard dirs are fallback only |
| 3.3.0.4108 | A beta; release users are on 3.2.9 | Record it, warn on mismatch, never gate on it |
| `%LOCALAPPDATA%` | Redirected on some corporate/OneDrive setups | `Environment.GetFolderPath`, never a composed string |

---

## 6 · Privacy — a debt the moment the repo is public

`Settings.json` contains hardware serial numbers inside device IDs, and absolute paths
including the Windows username. **Users will attach snapshots to bug reports.** They will not
think about it, and by then it is in a public issue tracker.

**Owed:** a "copy diagnostics" action that redacts both. Nothing is ever auto-uploaded.
**Phase:** 7, and it gates going public rather than following it. The `.gitignore` already
refuses real settings files; that protects the repo, not the issue tracker.
