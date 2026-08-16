---
title: "Glossary"
status: published
created: 2026-08-16
updated: 2026-08-16
tags: [meta, glossary]
---

# Glossary

Words this project uses **precisely**, where the everyday meaning is close enough to
mislead. Not a dictionary of obvious terms.

This project has an unusual number of these, for one reason: it is a backup tool for a
backup-capable app, built on a fork that also says "backup", storing things it calls
backups. Four different meanings, one word. That ambiguity has already caused one
misreading of the spec, so the vocabulary below is enforced.

---

## The four things called "backup"

Never use the bare word. Use one of these.

**Snapshot** — what *this app* writes. One directory in the backup store, containing a
`manifest.json` plus the captured tiers. Identified by its manifest, never by its filename.
This is the only sense in which we say the app "backs up".

**AutoBackup** — what *Wave Link itself* writes, unprompted, into
`LocalState\Backup\AutoBackup\`. Roughly one per launch, rolling, about ten kept, oldest
around three days old. Their retention is the gap this project exists to fill. We capture
them as payload; we do not manage them.

**Atomic-save artifact** — Wave Link's `LocalState\Backup\Settings.json.bak.<rand>.<rand>`
files. Written by the atomic-save path, not on a schedule, so one may or may not exist when
you need it. They reach further back than the AutoBackups, which makes them the highest-value
forensic material and the least reliable.

**Managed backup** — the *fork's* term for `Settings.json.backup-<ts>` files it writes beside
`Settings.json`. Appears throughout upstream source. We do not adopt the concept; see
[[ADR-003]].

---

## Locations

**LocalState** — `%LOCALAPPDATA%\Packages\Elgato.WaveLink_g54w8ztgkx496\LocalState`. Where
Wave Link's writes are redirected because it is an MSIX package. **The only location that
matters**, and the only one the app reads or writes to. Resolve it by package family glob,
never by composing a string.

**The decoy** — `%APPDATA%\Elgato\WaveLink`. Exists, looks authoritative, is dead. Nine
months stale as of 2026-08-15. Named as "the decoy" throughout the docs so nobody has to
re-derive why it is being avoided. See
[[backup-succeeds-but-protects-nothing]].

**Package family name** — `Elgato.WaveLink_g54w8ztgkx496`. Stable per Store identity but
never assumed: discovery globs `Elgato.WaveLink_*`, and refuses to guess when more than one
package matches.

**The backup store** — where snapshots live. User-chosen, defaulting to
`%LOCALAPPDATA%\WaveLinkBackup`. Deliberately **outside** `LocalState`, because resetting or
uninstalling the MSIX package deletes `LocalState` wholesale. See [[ADR-003]].

---

## Capture

**Tier** — one independently switchable class of content in a snapshot. Four exist:

| Tier | Content | Size | Default |
|---|---|---|---|
| 1 · Settings | `Settings.json` plus Wave Link's own backup copies | ~470 KB | Always on, not switchable |
| 2 · Plugin manifest | Name, vendor, version, uniqueId, path and SHA-256 per referenced plugin | ~4 KB | Always on, not switchable |
| 3 · Plugin presets | `%APPDATA%\<Vendor>\<Plugin>\` for referenced vendors | ~10 MB | Opt-in, on by default |
| 4 · Plugin binaries | The `.vst3` at each `FilePath` | ~40 MB | Opt-in, off by default |

Sizes are one machine's measurements, not constants. See [[ADR-006]].

**Referenced, not installed** — the rule that makes tier 4 tractable. Wave Link records the
absolute path of every plugin actually in use; backing those up is 39.8 MB against 4,887 MB
for the whole VST3 tree. Always resolve from `FilePath`; standard directories are a fallback
only.

**Trigger** — why a snapshot was taken: `Manual`, `Automatic` (the watcher) or `PreRestore`.
Surfaced in the UI as `MANUAL` / `AUTOMATIC` / `PRE-RESTORE` and rendered distinctly, because
a pre-restore snapshot means "this is what you were escaping from".

**Pre-restore snapshot** — taken automatically before every restore, never as a checkbox,
always named `Before restore`. The cheapest possible safety net and the thing that makes the
destructive button safe to press.

**Dedup key** — `settingsSha256` in the manifest. Wave Link rewrites `Settings.json` on every
launch with near-identical bytes; without this you accumulate thousands of identical copies.
See [[ADR-007]].

---

## Health

**Health fingerprint** — input count plus file size plus input names, recorded in every
manifest. Cheap enough to compute on every snapshot, and enough to tell a real configuration
from a reset one.

**Collapsed** — a configuration that has reset to defaults: two inputs (`Elgato Wave:3`,
`System`) and about 11 KB, against a healthy five inputs and ~40 KB. The word describes the
*shape* of the failure, not its cause.

**Suspect** — a snapshot flagged by validation, at write time and re-checked on load. Drives
the amber row tint, the amber left edge and the `SUSPECT` pill. A suspect snapshot can still
be restored — the flag informs, it does not block.

**Relative, not absolute** — the rule governing every health check. Five inputs and 43 KB is
*one user's rig*. Compare a snapshot against **that user's previous snapshot**; an absolute
threshold is a bug waiting for the first user with three inputs.

---

## Configuration internals

**Endpoint ID** — a Core Audio device identifier, e.g.
`BS33J1A05009\PCM_IN_01_C_00_SD1`. Used as the key of each `InputSettings` entry. Two things
follow: it embeds a hardware **serial number** (privacy), and it is a **foreign key**
referenced elsewhere in the document both bare and as `<deviceId>|<suffix>` — so the config
is never modelled as a flat list of channels.

**`AudioPluginConfigurations`** — the per-input effect chain. The array that makes tiers 2–4
possible: each entry carries the plugin's name, vendor, absolute `FilePath` (empty for Elgato
built-ins) and its `ParameterState`.

**`ParameterState`** — base64 plugin state, written by one specific plugin version. Full of
`+` and `/`, which is why the JSON encoder choice matters. See
[[every-snapshot-differs-with-no-real-change]].

**Duplicate keys** — case-insensitively duplicated property names in `Settings.json`. Wave
Link's own `SettingsJsonNormalizer.HasCaseInsensitiveDuplicateProperties` rejects the file and
resets to defaults. Invisible to `ConvertFrom-Json`, which silently collapses them. See
[[file-parses-but-wave-link-resets]].

**Machine-local** — the property that every snapshot has and that users will assume it does
not: endpoint IDs embed device serials and plugin paths are absolute, so a snapshot restored
on another machine produces dead channels rather than a shared preset. Labelled as such in
the UI. See [[restored-backup-has-dead-channels]].

---

## Mechanics

**Shell AppID** — `shell:AppsFolder\<packageFamilyName>!App`. The only way to launch an MSIX
app; its `.exe` path will not start it.

**Atomic write** — write to a temp file in the same directory, then `File.Replace(temp,
target, backupPath)`. Atomic on NTFS, and it produces the rollback copy in the same
operation, so there is no window where the target is half-written. Not `WriteAllBytes`.

**Verified exited** — the precondition for any write to `Settings.json`. Not "kill sent", not
"close requested": exited, then re-checked. A graceful exit flushes in-memory config on the
way out, which is harmless before your write and fatal racing it. See
[[restored-settings-revert-seconds-later]].

**Seam interface** — `IFileOperations`, `IWaveLinkProcess`, `Func<DateTime> clock`. The
upstream's testability shape, inherited deliberately: ~30 KB of tests against 60 KB of code
is only possible because of them.

**Bundle** — a `.vst3` that is a *directory*
(`Plugin.vst3\Contents\x86_64-win\Plugin.vst3`) rather than a file. Permitted by the VST3
spec and increasingly shipped that way. Test for directory and recurse. See
[[vst3-backs-up-as-nothing]].

---

## Words the code uses precisely

Added as the codebase acquired them. Each is a term where the ordinary programming meaning is
close enough to mislead.

**Expected failure** — something that can go wrong in normal operation and must be *rendered*:
Wave Link not installed, a malformed file, a snapshot that no longer matches its hashes. These
return a `Result`. Distinct from a **bug**, which throws. The split exists because a GUI has to
show every expected failure as a message, and catch-and-hope at each UI boundary is how error
handling rots. See [[preconditions-inside-the-operation]].

**Finding** — something validation *noticed*, not something that failed. A settings file with
duplicate keys analyses **successfully** and reports a finding; only a file that cannot be
understood at all is an error. This is what makes "a suspect snapshot is still restorable" a
property of the design rather than a rule to remember.

**Pure**, in this codebase, means more than "no side effects": no constructor, no injected
dependency, no `async`, and no reference to a seam. `Analysis/` and the automation policy are
pure in this sense, which is why they *cannot* write a file and why their tests need no setup.
See [[pure-analysis-core]].

**Seam** — an interface that exists so a test can substitute reality. There are three, and the
count is deliberate: `IFileSystem`, `IWaveLinkProcess`, `IClock`. `IClock` was **deferred to
phase 2** because phase 1 had no test that would have exercised it — a seam with no test is
decoration.

**Guard** — a rule enforced by the build rather than by intention. Three exist: one MSBuild
target (Core must not resolve the Windows Desktop ref pack) and two source scans (no
`File.ReadAllBytes`, no reflection-based `JsonSerializer`). Each was *verified to fail* before
being trusted. See [[guards-that-can-fail]].

**Tick** — one evaluation of whether an automatic snapshot is due. Cheap: it compares three
timestamps and usually returns immediately. `AutoBackupCoordinator` **owns no timer** — the
host calls `Tick()`, which is what keeps every timing test instantaneous.

**Debounce** — the ~60s wait after the *last* write before capturing. A burst of writes
restarts it, so touching five faders is one snapshot rather than five.

**Rate limit** — at most one *automatic* snapshot per hour. A **manual** backup is never rate
limited and never deduplicated: the user pressed a button, and the new row appearing is the
only confirmation the design gives them.

**Prunable** — an automatic snapshot, and only an automatic snapshot. Manual and pre-restore
snapshots are never pruned at any keep count, including zero. The rule lives in
`SnapshotManifest.IsPrunable` and is consulted rather than re-derived.

**Schema version** — `manifest.json`'s compatibility marker. A manifest from a *newer* version
is refused with a readable message, never partially read: understanding some fields of a format
you do not know is how a store gets quietly corrupted by an older build. Older versions are
accepted — rejection is forward-only.

**As built** — a section appended to an executed design recording where the code diverged from
it. Both shipped designs have one. It exists so a plan can stay accurate without being rewritten
into a description of what happened, which would destroy the record of what was intended.
