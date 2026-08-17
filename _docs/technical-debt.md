---
title: "Technical Debt"
status: published
created: 2026-08-16
updated: 2026-08-16
tags: [meta, technical-debt]
---

# Technical Debt

What is built and not right, what has never run, and what is known-wrong deliberately.
Distinct from [dev-phases/](dev-phases/README.md), which is for things not built yet.

**As of 2026-08-16 there is no application code**, so this document is unusual: nothing here
has been *incurred*. What it holds instead is debt we have agreed to take on, and assumptions
the project rests on that nobody has checked. Both are worth writing down now, because the
moment the fork lands the first list becomes real, and the second list is the set of things
that will look obvious in hindsight.

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

## 7 · Design decisions that outdated shipped code — **NEW 2026-08-17**

The phase-5 design package (handoff part 2) closed the six gaps **and** made four behavioural
decisions that contradict code already written and tested. None of this is a mistake in either
place: the code was built to the best spec available, and the design has since decided better.
Recording it here because "the design says X, the code does Y" is exactly the kind of drift
that becomes invisible once phase 5 starts and everyone is looking at XAML.

### 7.1 Delete must go to the Recycle Bin — **conflicts with shipped code**

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

**Recommendation:** the third. It is the same shape as `IFileSystem` and `IWaveLinkProcess`,
it keeps the guard meaningful, and "deleting is a platform gesture, not a file operation" is a
true statement worth encoding. **Needs a decision before phase 5 — it is not a UI change.**

### 7.2 Damaged backups must not count toward the keep-count — **not implementable today**

Decision 6: *"A corrupted file must never push a good one out."*

`SnapshotRetention.SelectForPruning` filters on `SnapshotManifest.IsPrunable`, which is
`Trigger == Automatic` and nothing else. **Retention cannot see damage at all** — damage is
detected by `SnapshotGuard.Verify`, which reads and hashes every file, and is called at
*restore* time, not at list time (a deliberate phase-2 choice: rehashing the whole store on
every window open is not free).

So this needs either a cheap damage signal in the manifest, or retention that verifies — and
verifying during a prune reintroduces exactly the cost phase 2 avoided. **Design work, not
just code.**

### 7.3 Automatic backup must not queue while the folder is missing — **current behaviour is what the design forbids**

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

### 7.4 Keyboard behaviour is specified and unimplemented — **no conflict, just work**

Escape cancels every dialog and clears search · Enter fires the primary button **except**
Delete and Restore, where focus starts on Cancel · F5 re-reads the folder · focus ring 2px/2px
always visible, including list rows. All phase-5 work; listed so it is scheduled rather than
discovered.

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
| 5. Keyboard, focus, high-contrast | `10-decisions.md` §6 — keyboard and focus closed; **high-contrast still open** |
| 6. Tray, autostart, update | **Still out of scope.** Unchanged. |

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
