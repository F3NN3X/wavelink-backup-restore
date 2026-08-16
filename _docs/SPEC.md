---
title: "Wave Link Backup — Build Specification"
status: published
created: 2026-08-15
updated: 2026-08-15
tags: [spec, architecture]
---

# Wave Link Backup — Build Specification

A Windows utility that automatically backs up and restores Elgato Wave Link settings,
including optional capture of VST3 plugin presets and binaries.

**Status of this document:** paths, sizes, versions and schema keys were verified by
inspection on a real machine on 2026-08-15. Where something is a *recommendation*
rather than a *finding*, it says so. Provenance notes are at the bottom — read them
before treating any number as a constant.

> ## ⚠ Corrections — measured 2026-08-16
>
> The spec body below is left **unedited**, as the record of what was believed on 2026-08-15.
> These three points were tested against the live `Settings.json` a day later and did not
> survive. Where the body and this block disagree, **this block is right.**
>
> **1 · §5 and §7·2 — the `UnsafeRelaxedJsonEscaping` recommendation is inverted. Do not
> apply it.** Wave Link writes its own file with `System.Text.Json`'s **default** encoder and
> `WriteIndented`; a default round-trip reproduces it byte for byte (43,052 → 43,052,
> identical). The relaxed encoder *un-escapes* what Wave Link deliberately wrote, shrinking
> the file to 41,641 bytes and making every snapshot differ from the app's own output —
> causing the exact diff churn the recommendation was meant to prevent. Upstream's default
> encoder is therefore **correct as written**, and audit finding 2 is downgraded to a
> non-issue. The default escapes only `+` (to its six-character JSON escape for U+002B); it
> does **not** escape `/`, contrary to the body.
> See [[every-snapshot-differs-with-no-real-change]].
>
> **2 · §7·3 — the `JsonNode` duplicate-key question is answered, and it was mis-framed.**
> Neither option was right. `JsonNode.Parse` **preserves** case-insensitive duplicates
> (`{"A":1,"a":2}` → both survive, round-tripping intact) and **throws `ArgumentException`**
> on exact duplicates (`{"A":1,"A":2}`). `JsonDocument` preserves both forms, with
> `GetProperty` returning the last. So there is no silent data loss — but any edit path needs
> to catch that exception rather than let a dictionary error propagate.
> See [[file-parses-but-wave-link-resets]].
>
> **3 · Missing from §1 and §4 — `Settings.json` is locked while Wave Link runs.**
> `File.ReadAllBytes` fails with "being used by another process" on every capture taken while
> the app is open, which is most of them. Reads must specify
> `FileShare.ReadWrite | FileShare.Delete`. Not a transient write window — it is the app's
> steady state. See [[capture-fails-while-wave-link-is-running]].
>
> The live file was re-checked at the same time and remains clean: 5 inputs, no duplicate
> keys, 43,052 bytes.

| | |
|---|---|
| **Package family** | `Elgato.WaveLink_g54w8ztgkx496` |
| **Version observed** | 3.3.0.4108 (beta channel; release users are on 3.2.9) |
| **Processes** | `Elgato.WaveLink`, `WavelinkSEService` |
| **Payload that matters** | 43 KB — one file |
| **Language / UI** | C# / .NET 10, WPF |
| **Basis** | Fork of `voltybat/WaveLinkSettingsUtility` (MIT) |
| **VST3 in use vs installed** | 39.8 MB of 4,887 MB |

---

## 1. Where the settings actually live

Wave Link is an MSIX/Store package, so writes are redirected into the package's
`LocalState`. This is the only location that matters.

```
%LOCALAPPDATA%\Packages\Elgato.WaveLink_g54w8ztgkx496\LocalState
```

> **DECOY — do not read or write here.**
> `%APPDATA%\Elgato\WaveLink` exists, looks authoritative, and is dead. Its newest
> file dates to 2025-11-17 — nine months stale. A backup tool that finds this folder
> by name will silently protect nothing. Resolve by package family name, never by
> vendor folder.

### Contents, classified

| Path under LocalState | Size | Verdict | Why |
|---|---|---|---|
| `Settings.json` | 43 KB | **BACK UP** | The entire configuration. This is the product. |
| `Backup\AutoBackup\Settings.auto.<ts>.json` | 420 KB | **BACK UP** | Wave Link's own rolling copies. Cheap, and they carry history yours won't have on first run. |
| `Backup\Settings.json.bak.<rand>.<rand>` | 217 KB | **BACK UP** | Atomic-save artifacts, irregular but reach back to March. Highest-value forensic material. |
| `Logs\` | 9.9 MB | Newest only | Needed to verify a restore. Keep the newest; the other 29 are noise. |
| `ws-info.json` | 21 B | Skip | Just `{"port": 11465}`. Regenerated on launch. |
| `AudioPluginCache\` | 136 KB | Skip as payload | Rebuilt by scanning. But **read** it — see §9. |
| `EBWebView\` | ~100 MB | **NEVER** | Embedded WebView2 browser profile — shader caches, IndexedDB, code caches. Copying it turns a 470 KB backup into a 100 MB one, and restoring it can wedge the UI. |

**Whole backup set: ~470 KB.** Small enough to snapshot on every change and keep months
of history — which is the entire reason to build this.

---

## 2. Why the built-in backup isn't enough

Wave Link already writes its own backups. Their retention is the gap this tool fills.

Measured: **10 files in `Backup\AutoBackup\`, oldest 3 days old.** Roughly one per
launch, rolling. If a bad config survives a long weekend unnoticed, every good copy has
already aged out.

The irregular `Settings.json.bak.*` files reach back to March, but they are written by
atomic-save, not on a schedule — you cannot rely on one existing when you need it.

**Design target:** keep one snapshot per distinct content hash, indefinitely. At 43 KB a
copy, a year of daily changes is under 16 MB.

---

## 3. What's inside Settings.json

### Top-level keys

| Key | Holds |
|---|---|
| `MixerConfiguration` | The one that matters — see below |
| `General` | 14 app-wide preferences |
| `Icons` | Per-channel icon assignments |
| `PluginHostConfiguration` | VST host state |
| `Blacklist` | Excluded plugins/devices |
| `WindowPlacement` | Window geometry |
| `Update`, `Suggestions` | Channel and nag state |

`MixerConfiguration` contains `MixSettings`, `InputSettings`, `DeviceParameterSettings`,
`MainOutputDeviceSettings`, `DefaultDeviceSettings`, `DuckingSettings`,
`ChannelBoostSettings`.

### The fingerprint that tells you a config is intact

`InputSettings` is keyed by audio device ID, each with a friendly `InputName`. A
healthy example — five inputs:

```
BS33J1A05009\\PCM_IN_01_C_00_SD1  =>  Wave Mic 1
PCM_OUT_00_V_14_SD8               =>  Voice
PCM_OUT_00_V_04_SD3               =>  Browser
PCM_OUT_00_V_00_SD1               =>  Music
PCM_OUT_00_V_12_SD7               =>  System
```

A reset config collapses to two inputs (`Elgato Wave:3`, `System`) and about 11 KB.
**Input count plus file size is a reliable health check** — cheap enough to run on every
snapshot and label the entry good or suspect. See §11 for why this must be *relative*,
not an absolute threshold.

### Fields inside each input entry

| Field | Type | Meaning |
|---|---|---|
| `InputName` | string | Friendly name shown in the mixer |
| `IsHiddenFromMixes` | bool | Hidden channel still occupying a slot |
| `AudioPluginConfigurations` | array | The EQ/VST chain — see §9 |
| `DeviceSettings.DeviceType` | string | `HardwareInputDevice` marks a physical input |
| `DeviceSettings.DeviceName` | string | Windows-reported device name |
| `DeviceSettings.DeviceId` | string | Core Audio endpoint ID |

> **Device IDs are foreign keys, not just labels.**
> The `InputSettings` key *is* the device ID, and that ID is referenced elsewhere in the
> document — both as a bare string and as a composite `<deviceId>|<suffix>`. Any tool
> that rewrites an ID must walk the entire tree and rewrite both forms, and handle the
> case where the destination key already exists.
>
> Irrelevant for pure backup/restore — you move whole files. It matters the moment you
> add "repair a dead input by pointing it at a new device", so do not model the config
> as a flat list of channels.

---

## 4. Restore — the sequence that actually works

Copying a file back is the part that looks obvious and fails. Order matters at every step.

1. **Validate before touching anything** (§5). Restoring a file the app will reject looks
   identical to the backup being broken.
2. **Close both processes** — `Elgato.WaveLink` and `WavelinkSEService`.
3. **Snapshot the current file first**, even though it's the bad one. Rollback and evidence.
4. **Write atomically.**
5. **Relaunch via the shell AppID** — MSIX apps cannot be started from their `.exe` path.
6. **Verify from the new log**, not the UI.

```powershell
$pfn = 'Elgato.WaveLink_g54w8ztgkx496'
$ls  = "$env:LOCALAPPDATA\Packages\$pfn\LocalState"

Get-Process -Name 'Elgato.WaveLink','WavelinkSEService' -EA SilentlyContinue |
  Stop-Process -Force
Start-Sleep -Milliseconds 1500

Copy-Item "$ls\Settings.json" "$scratch\Settings.pre-restore.json" -Force
[System.IO.File]::WriteAllBytes("$ls\Settings.json", $restoreBytes)

Start-Process "shell:AppsFolder\$pfn!App"
```

> **The invariant is exit, not kill method.**
> An earlier draft of this spec said *force-kill, never a graceful quit*. That was an
> overstatement. The upstream repo closes gracefully with a 10-second timeout,
> force-kills only on timeout, then asserts the process is gone before writing — that
> sequence is correct, and better than an unconditional kill because it lets the app
> checkpoint cleanly.
>
> **The real rule:** the app must be *fully exited and verified exited* before you write.
> A graceful exit flushes in-memory config on the way out; harmless if the flush happens
> before your write, fatal if it races it. Wait for exit, re-check `IsRunning`, then write.

> **Prefer `File.Replace` over `WriteAllBytes`.**
> In the real app, write to a temp file in the same directory then call
> `File.Replace(temp, target, backupPath)`. Atomic on NTFS, and it produces the rollback
> copy in the same operation, so there is no window where the target is half-written.
> The `WriteAllBytes` above is fine for a one-off script, not for a utility.

### Verify from the log

```powershell
$log = Get-ChildItem "$ls\Logs" -File |
       Sort-Object LastWriteTime -Desc | Select-Object -First 1
Select-String $log.FullName -Pattern 'Failed to parse|Created a new backup file|Applied saved'
```

Success is the **absence** of `Failed to parse settings file` plus the presence of
`Applied saved friendly name 'Wave Mic 1'`. A UI that looks correct can still be a
freshly generated default.

---

## 5. Validation: the traps

### Duplicate keys — invisible to PowerShell, fatal to the app

The original incident: an older build wrote case-insensitively duplicated keys. Wave
Link's `SettingsJsonNormalizer.HasCaseInsensitiveDuplicateProperties` rejects the file
and resets to defaults.

> **`ConvertFrom-Json` cannot see this.** It silently collapses duplicates, so the file
> "parses fine" while the app refuses it. Use `System.Text.Json.JsonDocument`, which
> preserves them, and walk the tree grouping property names case-insensitively.

The live file was clean when checked, so a validator flagging duplicates should treat
them as an anomaly worth surfacing, not a routine condition.

### Never round-trip through `ConvertFrom-Json | ConvertTo-Json`

It truncates at `-Depth` (default 2 — warns, then silently drops the rest) and rewrites
number and string formatting. To repair a file, stream element-by-element with
`Utf8JsonWriter` so every value is copied verbatim. Set the encoder to
`UnsafeRelaxedJsonEscaping`, or `+` and `/` inside base64 plugin state get rewritten to
`\uXXXX` — valid JSON, but pointless churn in a file you want to diff.

### Newest is usually not best

After a reset, the newest backup is the one written seconds *after* it — the defaults.
Rank candidates by content (input count, size), never by timestamp:

```
21:39  11819 b  inputs=2  [Elgato Wave:3, System]            <- post-reset, newest
21:36  40224 b  inputs=5  [Wave Mic 1, Voice, Browser, ...]  <- last good
20:30  41805 b  inputs=5  [Wave Mic 1, Voice, Browser, ...]
```

### Beta channels ship new validators

3.3.0.4108 Beta rejected a file that 3.2.9 accepted. **Record the app version in every
snapshot's metadata** — when a restore fails, the first question is whether the config is
bad or the validator changed.

---

## 6. Worth building in

- **Hash-dedup snapshots.** Wave Link rewrites `Settings.json` on every launch with
  near-identical bytes. Store only on content change or you accumulate thousands of
  identical copies.
- **Snapshot metadata:** timestamp, SHA-256, app version, input count, input names,
  duplicate-key flag. That's the whole restore UI.
- **Watch, don't poll.** A `FileSystemWatcher` on `LocalState` filtered to `Settings.json`.
- **Snapshot on shutdown too** — the original incident happened during an update, while
  the app was restarting.
- **Store outside `LocalState`.** Uninstalling or resetting the MSIX package deletes that
  directory wholesale, backups included.

### Adjacent Elgato configs, if scope widens later

Same `%APPDATA%\Elgato\` parent, all currently live: `StreamDeck`, `CameraHub`,
`Volume Controller`, `Audio Plugins`, `VSTs`, `DiscordPlugin`. These are conventional
Win32 apps, so unlike Wave Link the `%APPDATA%` path is the real one for them — do not
carry the MSIX assumption across.

---

## 7. Prior art: voltybat/WaveLinkSettingsUtility

C# / .NET 10, MIT, ~60 KB of source, last pushed 2026-07-19. Small, clean, well-tested —
and it already solves the parts that are tedious to get right.

### Take these outright

| Component | Why it's worth having |
|---|---|
| `SettingsDiscovery` | Globs `Elgato.WaveLink_*` under `Packages` and requires `Settings.json` to exist — never touches the stale vendor folder. Also handles **multiple installed packages**, which it refuses to guess between and demands `--settings-path`. |
| `WindowsAudioEndpointInspector` | ~80 lines of hand-declared `[ComImport]` Core Audio interfaces (`IMMDeviceEnumerator`, `IMMDevice`, `IPropertyStore`) to enumerate live endpoints. This is how you tell "input is dead" from "input is fine". |
| Shutdown sequence | Graceful close → 10 s timeout → kill tree → *assert not running* → write. |
| Atomic write | Temp file then `File.Replace(temp, path, backupPath)`. |
| `ValidateManagedPath` | Restore refused unless the source matches `^Settings\.json\.backup-\d{8}-\d{9}$` beside the target package. Stops a mistyped path writing arbitrary bytes into config. |
| Seam interfaces | `IFileOperations`, `IWaveLinkProcess`, `Func<DateTime> clock` — the reason it has ~30 KB of tests against 60 KB of code. Keep this shape. |

### Fix these before shipping on top of it

**1 · Backups live inside LocalState.** `NewBackupPath` writes
`Settings.json.backup-<ts>` *beside* `Settings.json`, and `ManagedBackups` only
enumerates that directory. Resetting or uninstalling the MSIX package deletes
`LocalState` wholesale — **every backup with it**. This is the single change your version
must make, and it interacts with `ValidateManagedPath`, which currently enforces that
location.

**2 · `Serialize` uses the default JSON encoder.**
`JsonSerializer.SerializeToUtf8Bytes(root, new JsonSerializerOptions { WriteIndented = true })`
— no `Encoder` set, so the default escapes `+` and `/` to `\uXXXX`. Those characters are
everywhere in the base64 plugin state inside `AudioPluginConfigurations`. Output stays
valid and Wave Link accepts it, but every save rewrites bytes it never intended to touch
and diffs between snapshots become useless. Set `JavaScriptEncoder.UnsafeRelaxedJsonEscaping`.

**3 · No duplicate-key detection.** `Validate()` only asserts
`MixerConfiguration.InputSettings` is an object. The defect from §5 would pass unnoticed.
Confirm empirically whether `JsonNode.Parse` collapses duplicates on the way in — if it
does, a round-trip silently drops data; if it doesn't, they survive into the written file.
Either way, add the `JsonDocument` walk.

**4 · It is a manual tool, not a safety net.** Backups happen only when invoked — no
watcher, no schedule, no dedup, so repeated runs write identical copies. **This is the gap
your app exists to fill**, and the reason forking rather than just using it is justified.

**5 · Runtime dependency.** The csproj sets `PublishSingleFile` with
`SelfContained=false`, so the .NET 10 runtime must be installed despite the single-file
output. Decide this deliberately.

---

## 8. Language: C#, and it isn't close

Rust is the more interesting answer and the wrong one here. The deciding factor is not
performance or safety — nothing in this workload is hot or memory-unsafe — it's that
every hard part is a Windows API, and one of them is already written.

| Requirement | C# / .NET | Rust |
|---|---|---|
| Core Audio COM enumeration | Hand-declared `[ComImport]`, verbose but done — copyable | `windows-rs` generates bindings; arguably *less* code |
| Lossless JSON with duplicate keys | **Decisive.** `JsonDocument` preserves duplicates, `JsonNode` edits, `Utf8JsonWriter` controls bytes exactly | `serde_json`'s map collapses duplicates by design; needs `json-syntax` or a custom parser to detect the defect that motivated this project |
| File watching, MSIX paths, shell activation | First-party | Reachable, more assembly required |
| Tray app / small GUI | WinUI 3, WPF, WinForms, Avalonia — all mature | egui or Tauri; none as native-feeling |
| Standalone binary | ~70 MB self-contained, or NativeAOT ~10–15 MB | **Rust wins:** 2–5 MB static |
| Reuse of existing MIT code | Fork and go | Full rewrite |

**Recommendation:** fork `voltybat/WaveLinkSettingsUtility` (MIT, attribution required),
keep discovery / endpoint inspection / shutdown / atomic write, add the snapshot store,
watcher, dedup and duplicate-key validation. For a dependency-free executable, publish
NativeAOT — but verify COM interop still resolves, since AOT and `[ComImport]` need care.

---

## 9. VST3: back up what's referenced, not what's installed

Wave Link records the absolute path of every third-party plugin in use. That one fact
turns a 4.9 GB problem into a 40 MB one.

Each entry in `AudioPluginConfigurations` carries:

| Field | Example |
|---|---|
| `Id` | `PCM_OUT_00_V_16_SD9_ElgatoSampleRecorder` |
| `Name` | `Pro-Q 4` |
| `PluginId` | composite `<deviceId>_<name>` |
| `Category` | `Fx` |
| `Vendor` | `FabFilter` |
| `FilePath` | **empty for Elgato built-ins; absolute path for third-party** |
| `ParameterState` | base64 plugin state |
| `BypassState` | bool |
| `CustomName` | user override |

Measured — one mic chain:

```
Clear.vst3                 Supertone     24.1 MB   C:\Program Files\Common Files\VST3\
FabFilter Pro-DS.vst3      FabFilter      2.4 MB   C:\Program Files\Common Files\VST3\FabFilter\
FabFilter Pro-C 2.vst3     FabFilter      2.7 MB
FabFilter Pro-Q 4.vst3     FabFilter      4.4 MB
FabFilter Saturn 2.vst3    FabFilter      3.4 MB
FabFilter Pro-L 2.vst3     FabFilter      2.8 MB
                                       ---------
referenced set                            39.8 MB
entire VST3 tree                       4,887.0 MB   <- 123x larger, and pointless
```

### Four tiers, independently switchable

| Tier | What | Size | Default |
|---|---|---|---|
| 1 · Settings | `Settings.json` + the app's own backups | ~470 KB | Always |
| 2 · Plugin manifest | Name, vendor, version, uniqueId, path, SHA-256 per referenced plugin | ~4 KB | Always |
| 3 · Plugin presets | `%APPDATA%\<Vendor>\<Plugin>\` for referenced vendors | ~10 MB | Opt-in |
| 4 · Plugin binaries | The `.vst3` at each `FilePath` | ~40 MB | Opt-in |

> **Tier 2 earns its keep.** 4 KB that converts "my effects are gone and I don't know why"
> into "install FabFilter Pro-Q 4 v4.x, it's missing". Build it from `FilePath`
> cross-referenced against `AudioPluginCache\AvailablePlugins.cache` — a JUCE
> `<KNOWNPLUGINS>` XML with `name`, `manufacturer`, `version`, `file`, `uniqueId` per
> plugin. On restore, check each resolves and flag version drift.

### Three things that will bite

> **Licences are not backup-able — do not imply otherwise.**
> Copying a `.vst3` restores the code, not the authorisation. Nothing licence-shaped
> exists in these vendor folders: `%APPDATA%\FabFilter` is 246 files of *presets* only,
> and `%APPDATA%\Supertone\Clear` holds nothing but crash reports. Those vendors
> authorise via registry, machine-bound licence files elsewhere, or an online account.
> Tier 4 gets a working plugin on the same machine; on a rebuild the user is still
> reinstalling and re-authorising. Say so in the UI.

> **A `.vst3` may be a directory.** All six observed are single files, but the VST3 spec
> defines a *bundle* — `Plugin.vst3\Contents\x86_64-win\Plugin.vst3` — and installers
> increasingly ship them that way. Handle both: test for directory and recurse.
> Assuming "file" will silently back up nothing for some plugins.

> **Restoring binaries needs elevation; restoring settings does not.**
> `C:\Program Files\Common Files\VST3` is not user-writable. Keep tiers 1–3 admin-free —
> that is most of the value — and prompt for elevation only when tier 4 restore is
> actually requested.

`ParameterState` is written by a specific plugin version. Restoring an older settings file
against a newer plugin normally works, because plugins version their own state — but it
isn't guaranteed, which is why tier 2 records the version it was captured against.

---

## 10. Backup store and GUI

### Why the fork's model has to go

It identifies backups by filename regex — `^Settings\.json\.backup-\d{8}-\d{9}$` — and
requires them beside `Settings.json`. That blocks all three requirements at once: custom
names break the regex, custom locations break the path check, and the mandated location
is the volatile one from §7.

### The shape that supports all three

```
<store>/                                 <- user-chosen, default %LOCALAPPDATA%\WaveLinkBackup
  2026-08-15T2307-a3f81c/
    manifest.json                        <- name, notes, timestamps, hashes, app version
    settings.json
    plugins.json                         <- tier 2 manifest
    presets/     (tier 3, optional)
    plugins/     (tier 4, optional)
```

**The display name lives in `manifest.json`, never in the folder name.** Renaming is then
a metadata write — no file moves, no collisions, no sanitising user text into a path, no
breakage when someone types `Mic chain 3/4"`. Folder names stay machine-generated.

Keep a guard in the spirit of `ValidateManagedPath`, but assert *"this directory contains
a manifest.json we wrote whose hashes match"* rather than matching a filename. Same
safety, no constraint on naming or location.

### manifest.json

| Field | Purpose |
|---|---|
| `displayName`, `notes` | What the user typed. Freely editable. |
| `createdUtc`, `trigger` | `manual` / `watcher` / `preRestore` — pre-restore rollbacks should be visually distinct |
| `settingsSha256` | Dedup key. Skip the write when it matches the newest entry. |
| `waveLinkVersion` | §5: the first question when a restore fails |
| `inputCount`, `inputNames` | Health fingerprint from §3 — surface it in the list |
| `hasDuplicateKeys` | Result of the `JsonDocument` walk. Mark the entry suspect. |
| `tiers` | Which of 1–4 this backup actually contains |

### GUI: WPF

Mature, first-party, no packaging requirement, single-file-publishes cleanly. WinUI 3
drags in the Windows App SDK for no benefit at this scale; Avalonia buys cross-platform
never used; WinForms is fine but you will fight it on a list view that needs to look
pleasant.

> **One structural decision worth making on day one.** Split the fork's console app into
> a **core class library** plus a thin WPF shell, and keep a CLI shell alongside. The
> library stays headless and testable — preserving the seam interfaces that give the fork
> its test coverage — and unattended/scheduled operation comes free. It also leaves
> NativeAOT open for the CLI, which WPF does not support.

The list is the whole app: name, date, health fingerprint, tier badges, suspect marker.
Restore, rename and delete hang off each row. Everything else — location picker, tier
toggles, watcher on/off — is a settings pane opened twice a year.

> **Always snapshot before restoring.** Write a `trigger: preRestore` backup of the
> current state before every restore, automatically and without asking. Cheapest possible
> safety net, and what makes the destructive button safe to press.

---

## 11. Shipping this publicly

Most numbers above were measured on one machine. Several are properties of *that* machine
and become bugs if treated as constants.

| Looks like a constant | Actually | What to do instead |
|---|---|---|
| `Elgato.WaveLink_g54w8ztgkx496` | Stable per Store identity, but never assume | Glob `Elgato.WaveLink_*`, as the fork does |
| 5 inputs / 43 KB | One user's rig | Health check must be **relative** — compare against that user's previous snapshot, never an absolute threshold |
| `BS33J1A05009\PCM_IN_01…` | Contains a hardware **serial number** | See privacy note |
| `C:\Program Files\Common Files\VST3` | Default, and overridable | Always resolve from `FilePath`; standard dirs are fallback only |
| 3.3.0.4108 | A beta. Release users are on 3.2.9 | Never gate on one version; record it and warn on mismatch |
| `%LOCALAPPDATA%` | Redirected on some corporate/OneDrive setups | Use `Environment.GetFolderPath`, never a composed string |

> **Backups are not portable between machines.**
> `InputSettings` is keyed by Core Audio endpoint IDs embedding device serials, and plugin
> `FilePath`s are absolute. Restoring one person's backup onto another machine produces
> dead channels, not a shared preset. If "export/import a chain" is ever wanted, that is a
> *different feature* built on `AudioPluginConfigurations` alone. Label backups as
> machine-local in the UI.

> **Privacy — matters once strangers file issues.**
> `Settings.json` contains hardware serial numbers in device IDs, and absolute paths
> including the Windows username. Users *will* attach backups to bug reports. Offer a
> "copy diagnostics" action that redacts both, and never auto-upload anything.

> **Open questions to resolve before v1.**
> **Non-MSIX installs:** everything here assumes the Store/MSIX package. Whether older or
> enterprise builds ever install as conventional Win32 is untested — if they do, discovery
> returns "not found" and the app is useless to those users. **macOS:** Wave Link ships
> there too; scope the repo as Windows-only in the README rather than leave it ambiguous.

---

## Provenance

- **§1–6, §9** — paths, sizes, versions, schema keys, plugin paths, vendor folders and
  the absence of licence material are measured by inspection on one machine, 2026-08-15.
  Retention figures and the reset fingerprint come from a recorded 2026-08-11 recovery.
- **§7–8** — from reading `voltybat/WaveLinkSettingsUtility` at `main` (pushed
  2026-07-19), source read directly rather than inferred from its README. The encoder and
  backup-location findings are read off code and **not** reproduced at runtime; the
  duplicate-key question in 7·3 needs an empirical check before relying on either answer.
- **The bundle-vs-file warning in §9** is from the VST3 specification, not observed — all
  six referenced plugins are single files, so that code path needs a deliberate test
  rather than being exercised by the author's own setup.
- **§9 tiering, §10 store layout and GUI choice** are design recommendations, not findings.

Sizes will drift; the package family name and structure will not, short of a major
version change.
