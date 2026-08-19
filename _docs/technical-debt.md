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

### 1.3 No duplicate-key detection

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

**New, smaller debt this uncovered:** any `JsonNode.Parse` of an untrusted settings file can
throw `ArgumentException` from a dictionary insert. Unhandled, the user sees "An item with the
same key has already been added. Key: A" instead of "this settings file is malformed". Catch
and translate. **Phase:** 1.

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

### 2.3 Whether the VST3 bundle path works

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
because they carry history our first run will not have. We do not prune, rotate or write them.

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

### 7.4 Keyboard and focus — **Windows conventions, not just the design's list**

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

## 4 · ~~Design gaps carried into the build~~ — **CLOSED 2026-08-17, except item 6**

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

### 4.8 Deferred minors from the tray shell — **open, 2026-08-17**

Plans 2 and 3 shipped. Five things were deliberately left, none blocking.

| # | Minor | Why it was left, and what fixing it costs |
|---|---|---|
| 1 | **The tray icon renders at a fixed 32px.** Correct at 100% and 150% scaling, soft at 200%+ | The right size comes from the DPI of the screen holding the taskbar, and it should re-render on a DPI change. The renderer already takes `pixelSize`, so this is a caller change plus a `WM_DPICHANGED` hook |
| 2 | ~~**Mono letter-spacing is not implemented.**~~ **Closed, phase 5 plan 4, Task 2.** `Views/TrackedText.cs` is a custom `FrameworkElement` with its own `Tracking` dependency property (em-based, matching the type scale's `.18em`/`.06em` figures) and does its own glyph-run layout — `TextBlock`'s missing `CharacterSpacing` is no longer a blocker. Used throughout plan 4: the tray readout, the column headers, the status strip, and every slot label | — |
| 3 | **`Back up automatically` shows a trailing check, not a switch.** `screens/12`'s ASCII sketch writes `[toggle]` | A switch inside a menu is not something Windows draws, so the sketch was read as shorthand. One template change in `TrayMenuStyles.xaml` if the literal reading was meant |
| 4 | ~~**`Settings…` opens a placeholder `MessageBox`, from the tray and — since phase 5 plan 4, Task 10b — the main window's own gear button too.**~~ **Closed, phase 5 plan 8.** The real 680px settings dialog ships: in-place commit (no Save button), atomic persistence to `%LOCALAPPDATA%\WaveLinkBackup\settings.json`, and the two new sections. Both the tray menu item and the main window's gear button call `App.OpenSettings()`, which now builds a `SettingsViewModel` and shows the dialog instead of a `MessageBox`. | — |
| 5 | **A failed manual backup shows a raw `MessageBox`** carrying `CoreError.Message` | The twelve designed error screens (`06-errors.md`) are a later phase-5 session. Reporting it plainly beats swallowing it, but the wording is Core's log phrasing, not the design's |

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

### 4.10 First-run "Wave Link not found" variant — **open, carried forward from plan 7**

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

### 4.11 Total-size arithmetic is copy-pasted, not shared — **open, found in the phase-5 audit, 2026-08-19**

`manifest.Files.Values.Sum(f => f.SizeBytes)` (or the equivalent) is independently reimplemented
in at least five places: `DeleteDialogModel`, `SnapshotRowViewModel`, `SnapshotListViewModel`,
`HealthProbe` in `WaveLinkBackup.App`, and `SnapshotStore` itself in Core. `SnapshotManifest` has
no `TotalSizeBytes` computed property to source this from, so every caller re-derives it.

**Not a Core-logic leak** — it is trivial arithmetic, not analysis, so this is a DRY gap rather
than a violation of "no Core logic in the shell". **Fix:** add a `TotalSizeBytes` computed
property to `SnapshotManifest` in Core and point all five call sites at it. Cheap, low risk,
no schema change. **Phase:** whenever it's next touched; not blocking.

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
