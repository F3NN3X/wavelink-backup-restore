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

### 1.1 Backups are written inside `LocalState` — **must fix before anything else**

`NewBackupPath` writes `Settings.json.backup-<ts>` beside `Settings.json`, and
`ManagedBackups` only enumerates that directory. Resetting or uninstalling the MSIX package
deletes `LocalState` wholesale — every backup with it. A backup tool whose backups are
destroyed by the exact event you would want to recover from.

**Severity:** critical. This is the single change the fork must make.
**Entangled with:** `ValidateManagedPath`, which currently *enforces* that location, and the
filename regex that defines what counts as a managed backup. Fixing one without the others
leaves restore refusing its own files.
**Resolution:** [[ADR-003]]. Due in phase 2.

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

### 1.4 It is a manual tool, not a safety net

Backups happen only when invoked. No watcher, no schedule, no dedup — so repeated runs write
identical copies.

**Severity:** not a defect upstream; it is a different product. **This is the gap this app
exists to fill**, and the reason forking rather than just using it is justified.
**Resolution:** [[ADR-007]]. Due in phase 3.

### 1.5 Runtime dependency

The csproj sets `PublishSingleFile` with `SelfContained=false`, so the .NET 10 runtime must be
installed despite the single-file output. A user who downloads one `.exe` and double-clicks it
gets an error, not an app.

**Severity:** moderate — it is a first-run experience problem, which is the worst kind to have.
**Decision owed, not yet made:** self-contained (~70 MB), framework-dependent (small, requires
runtime), or NativeAOT (~10–15 MB, CLI only — WPF does not support it, and `[ComImport]`
interop needs verification under AOT). Due in phase 7; the NativeAOT option is preserved by
[[ADR-004]] and must not be foreclosed earlier.

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
**Mitigation regardless:** discovery failure must offer a manual `--settings-path` /
"Choose the settings file…" escape rather than dead-ending. The empty state already reserves
the place for this message; **the amber not-found variant is not designed** — see
[design-handoff.md](operations/design/design-handoff.md) *Gaps*.
**Phase:** 1 for the escape hatch, 5 for its UI.

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

## 4 · Design gaps carried into the build

Screens and states the design handoff explicitly does not cover. These are not debt yet, but
each becomes an improvised UI the moment it is hit, and improvised UI is how a coherent design
erodes. Listed here so phase 5 budgets for them rather than discovering them.

1. Delete confirmation dialog.
2. Backup-in-progress and restore-in-progress states.
3. Error states: Wave Link not installed, settings file missing, backup folder unwritable,
   disk full, corrupt snapshot on restore.
4. Search results and no-results state.
5. Keyboard map, screen-reader labels beyond the basics, Windows high-contrast mode.
6. Tray behaviour, autostart, update mechanics.

Full list and context: [design-handoff.md](operations/design/design-handoff.md) *Gaps*.

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
