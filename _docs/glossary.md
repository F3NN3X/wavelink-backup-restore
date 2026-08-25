---
title: "Glossary"
status: published
created: 2026-08-16
updated: 2026-08-20
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

**Snapshot.** What *this app* writes. One directory in the backup store, containing a
`manifest.json` plus the captured tiers. Identified by its manifest, never by its filename.
This is the only sense in which we say the app "backs up".

**AutoBackup.** What *Wave Link itself* writes, unprompted, into
`LocalState\Backup\AutoBackup\`. Roughly one per launch, rolling, about ten kept, oldest
around three days old. Their retention is the gap this project exists to fill. We capture
them as payload; we do not manage them.

**Atomic-save artifact.** Wave Link's `LocalState\Backup\Settings.json.bak.<rand>.<rand>`
files. Written by the atomic-save path, not on a schedule, so one may or may not exist when
you need it. They reach further back than the AutoBackups, which makes them the highest-value
forensic material and the least reliable.

**Managed backup.** The *fork's* term for `Settings.json.backup-<ts>` files it writes beside
`Settings.json`. Appears throughout upstream source. We do not adopt the concept; see
[[ADR-003]].

---

## Locations

**LocalState.** `%LOCALAPPDATA%\Packages\Elgato.WaveLink_g54w8ztgkx496\LocalState`. Where
Wave Link's writes are redirected because it is an MSIX package. **The only location that
matters**, and the only one the app reads or writes to. Resolve it by package family glob,
never by composing a string.

**The decoy.** `%APPDATA%\Elgato\WaveLink`. Exists, looks authoritative, is dead. Nine
months stale as of 2026-08-15. Named as "the decoy" throughout the docs so nobody has to
re-derive why it is being avoided. See
[[backup-succeeds-but-protects-nothing]].

**Package family name.** `Elgato.WaveLink_g54w8ztgkx496`. Stable per Store identity but
never assumed: discovery globs `Elgato.WaveLink_*`, and refuses to guess when more than one
package matches.

**The backup store.** Where snapshots live. User-chosen, defaulting to
`%LOCALAPPDATA%\WaveLinkBackup`. Deliberately **outside** `LocalState`, because resetting or
uninstalling the MSIX package deletes `LocalState` wholesale. See [[ADR-003]].

---

## Capture

**Tier.** One independently switchable class of content in a snapshot. Four exist:

| Tier | Content | Size | Default |
|---|---|---|---|
| 1 · Settings | `Settings.json` plus Wave Link's own backup copies | ~470 KB | Always on, not switchable |
| 2 · Plugin manifest | Name, vendor, version, uniqueId, path and SHA-256 per referenced plugin | ~4 KB | Always on, not switchable |
| 3 · Plugin presets | Both **preset roots** for referenced vendors | ~10 MB | Opt-in, on by default |
| 4 · Plugin binaries | The `.vst3` at each `FilePath` | ~40 MB | Opt-in, off by default |

Sizes are one machine's measurements, not constants. See [[ADR-006]].

**Preset root.** One of the two places a vendor may keep a user's presets: **`%APPDATA%`** or
**Documents**. Both are read, and a snapshot records which root each captured file came from
(`presets/appdata/…`, `presets/documents/…`), because restore cannot put a file back without
knowing. The two are not interchangeable and do not get the same fallbacks, `%APPDATA%\<Vendor>`
is config-sized whatever it holds, while `Documents\<Vendor>` is as likely to be a project
library, so the Documents lookup stops at `<Vendor>\Presets`. Both resolve through
`Environment.GetFolderPath`; the reference rig has Documents redirected to another drive. See
[[ADR-010]] and [[backup-says-it-saved-your-presets-and-it-did-not]].

**Preset source.** The folder tier 3 actually read for one plug-in, recorded per plug-in in
`plugins.json`. Plural (`presetSources`), at most one per root. It exists so a heuristic's result
can be inspected: a count of 3 beside `%APPDATA%\FabFilter\Pro-Q 4` is what made §4.18
diagnosable in ten minutes. A source with a count of **zero** is meaningful, *we looked here and
there was nothing worth keeping*, and is not the same as no source at all.

**Referenced, not installed.** The rule that makes tier 4 tractable. Wave Link records the
absolute path of every plugin actually in use; backing those up is 39.8 MB against 4,887 MB
for the whole VST3 tree. Always resolve from `FilePath`; standard directories are a fallback
only.

**Trigger.** Why a snapshot was taken: `Manual`, `Automatic` (the watcher) or `PreRestore`.
Surfaced in the UI as `MANUAL` / `AUTOMATIC` / `PRE-RESTORE` and rendered distinctly, because
a pre-restore snapshot means "this is what you were escaping from".

**Pre-restore snapshot.** Taken automatically before every restore, never as a checkbox,
always named `Before restore`. The cheapest possible safety net and the thing that makes the
destructive button safe to press.

**Dedup key.** `settingsSha256` in the manifest. Wave Link rewrites `Settings.json` on every
launch with near-identical bytes; without this you accumulate thousands of identical copies.
See [[ADR-007]].

---

## Health

**Health fingerprint.** Input count plus file size plus input names, recorded in every
manifest. Cheap enough to compute on every snapshot, and enough to tell a real configuration
from a reset one.

**Collapsed.** A configuration that has reset to defaults: two inputs (`Elgato Wave:3`,
`System`) and about 11 KB, against a healthy five inputs and ~40 KB. The word describes the
*shape* of the failure, not its cause.

**Suspect.** A snapshot flagged by validation, at write time and re-checked on load. Drives
the amber row tint, the amber left edge and the `SUSPECT` pill. A suspect snapshot can still
be restored, the flag informs. It does not block.

**Relative, not absolute.** The rule governing every health check. Five inputs and 43 KB is
*one user's rig*. Compare a snapshot against **that user's previous snapshot**; an absolute
threshold is a bug waiting for the first user with three inputs.

---

## Configuration internals

**Endpoint ID.** A Core Audio device identifier, e.g.
`BS33J1A05009\PCM_IN_01_C_00_SD1`. Used as the key of each `InputSettings` entry. Two things
follow: it embeds a hardware **serial number** (privacy), and it is a **foreign key**
referenced elsewhere in the document both bare and as `<deviceId>|<suffix>`, so the config
is never modelled as a flat list of channels.

**`AudioPluginConfigurations`.** The per-input effect chain. The array that makes tiers 2, 4
possible: each entry carries the plugin's name, vendor, absolute `FilePath` (empty for Elgato
built-ins) and its `ParameterState`.

**`ParameterState`.** Base64 plugin state, written by one specific plugin version. Full of
`+` and `/`, which is why the JSON encoder choice matters. See
[[every-snapshot-differs-with-no-real-change]].

**Duplicate keys.** Case-insensitively duplicated property names in `Settings.json`. Wave
Link's own `SettingsJsonNormalizer.HasCaseInsensitiveDuplicateProperties` rejects the file and
resets to defaults. Invisible to `ConvertFrom-Json`, which silently collapses them. See
[[file-parses-but-wave-link-resets]].

**Mix.** One of Wave Link's output busses, in `MixerConfiguration.MixSettings`. Keyed by a mix
id (`PCM_IN_01_V_04_SD3`) and named by the user, *Headphones*, *Stream Mix*, *Record Mix*. A
channel's `MixerIds` name the mixes it feeds; a mix's `OutputDevices` name what it plays out of,
and **an empty list is normal**, on a stock rig only the monitor mix carries a hardware output
and the rest are read by the stream software over the virtual device. Shown in the details
dialog ([[ADR-015]]).

**Channel.** One entry of `InputSettings`, and what the UI calls an INPUT. The two words are the
same thing seen from two sides: *input* is how the mixer takes it in and is what the list's column
is headed; *channel* is what an effect chain sits on and is what the details dialog lists. Wave
Link uses both, so this project does too, and neither is a synonym for a physical device, a
channel may be an application, a virtual bus, or nothing at all.

**Effect chain.** The ordered contents of one channel's `AudioPluginConfigurations`. **The order
is part of the configuration**, not a display detail: an EQ before a compressor is a different
sound from the same two the other way round, which is why the details dialog numbers them and
never sorts them.

**Bypassed.** `BypassState` on one entry of a chain. The effect is in the chain and switched off;
it is restored that way, so it is shown rather than hidden.

**Collapsed.** A configuration with fewer inputs than the snapshot before it: Wave Link fell back
to device-derived names, which is what a reset looks like. **Judged against the previous snapshot,
never against a threshold or a high-water mark**, a rig that grows has not collapsed
([[ADR-014]], [[every-older-backup-turns-amber-after-adding-a-channel]]).

**Machine-local.** The property that every snapshot has and that users will assume it does
not: endpoint IDs embed device serials and plugin paths are absolute, so a snapshot restored
on another machine produces dead channels rather than a shared preset. Labelled as such in
the UI. See [[restored-backup-has-dead-channels]].

---

## Mechanics

**Shell AppID.** `shell:AppsFolder\<packageFamilyName>!App`. The only way to launch an MSIX
app; its `.exe` path will not start it.

**Atomic write.** Write to a temp file in the same directory, then `File.Replace(temp,
target, backupPath)`. Atomic on NTFS, and it produces the rollback copy in the same
operation, so there is no window where the target is half-written. Not `WriteAllBytes`.

**Verified exited.** The precondition for any write to `Settings.json`. Not "kill sent", not
"close requested": exited, then re-checked. A graceful exit flushes in-memory config on the
way out, which is harmless before your write and fatal racing it. See
[[restored-settings-revert-seconds-later]].

**Seam interface.** `IFileOperations`, `IWaveLinkProcess`, `Func<DateTime> clock`. The
upstream's testability shape, inherited deliberately: ~30 KB of tests against 60 KB of code
is only possible because of them.

**Elevation.** Asking Windows for administrator rights, which this program does for exactly one
thing: putting tier 4's `.vst3` files back into `C:\Program Files\Common Files\VST3`. It is
never acquired in place (Windows grants rights at process creation only), so it means a second,
**headless** copy of the app: no window, no tray, no watcher, and **no single-instance mutex**,
that mutex is per-user and the elevated copy runs as the same user, so it would see itself as a
second launch and exit. Tiers 1, 3 never need it. See [[ADR-011]].

**Staged install.** How an update is applied. A process cannot overwrite its own executable
while running, so the new version is expanded to `<install>.staged`, that copy is started with
`--apply-update`, and *it* does the swap from outside the directory being replaced. The previous
install is **moved** to `<install>.previous` and deleted only once the new one is in place, so
there is no instant at which the user has no app. Deliberately **not** elevated, unlike
**elevation** above: that writes files the user chose from their own disk, this writes the
program's own binaries fetched from the network. See [[ADR-012]].

**Release feed.** Where the app looks for a newer version: a GitHub `releases/latest` for an
owner and repository read from the environment, never compiled in. **Unset means the whole
UPDATES section hides**, because a *Check now* that cannot reach anything is worse than no button.
See [releasing-and-updating.md](operations/runbooks/releasing-and-updating.md).

**Redaction.** Removing the two things in this app's data that identify a *person* or their
*hardware*: the serial number leading a Core Audio endpoint ID, and the Windows user name inside
any absolute path. It **fails closed**, an ID whose shape it does not recognise is masked
wholesale rather than passed through, because a redactor that lets an unknown shape through is
worse than none: it teaches the user the output is safe. Channel names are kept on purpose. See
[[technical-debt]] §6.

**Diagnostics.** The redacted self-description offered by Settings' *Copy diagnostics* and by
`wlbackup diagnostics`. It describes **structure**, counts, channel names, versions, which tiers
each snapshot holds, and never includes the settings file, redacted or otherwise: a redacted copy
of a file is still a copy of a file. Nothing is ever uploaded.

**Daily backup.** An optional capture at a wall-clock time each day, distinct from the
**interval cap**. The cap is a ceiling on change-driven captures ("at most one an hour"); the
daily backup is an instruction with a time on it and the cap does not suppress it. Only today's
own copy of this one covers the day, an ordinary automatic capture, before or after the set
time, never cancels it (dedup keeps it free when nothing has changed).

**Bundle.** A `.vst3` that is a *directory*
(`Plugin.vst3\Contents\x86_64-win\Plugin.vst3`) rather than a file. Permitted by the VST3
spec and increasingly shipped that way. Test for directory and recurse. See
[[vst3-backs-up-as-nothing]].

---

## Words the code uses precisely

Added as the codebase acquired them. Each is a term where the ordinary programming meaning is
close enough to mislead.

**Expected failure.** Something that can go wrong in normal operation and must be *rendered*:
Wave Link not installed, a malformed file, a snapshot that no longer matches its hashes. These
return a `Result`. Distinct from a **bug**, which throws. The split exists because a GUI has to
show every expected failure as a message, and catch-and-hope at each UI boundary is how error
handling rots. See [[preconditions-inside-the-operation]].

**Finding.** Something validation *noticed*, not something that failed. A settings file with
duplicate keys analyses **successfully** and reports a finding; only a file that cannot be
understood at all is an error. This is what makes "a suspect snapshot is still restorable" a
property of the design rather than a rule to remember.

**Pure**, in this codebase, means more than "no side effects": no constructor, no injected
dependency, no `async`, and no reference to a seam. `Analysis/` and the automation policy are
pure in this sense, which is why they *cannot* write a file and why their tests need no setup.
See [[pure-analysis-core]].

**Seam.** An interface that exists so a test can substitute reality. There are three, and the
count is deliberate: `IFileSystem`, `IWaveLinkProcess`, `IClock`. `IClock` was **deferred to
phase 2** because phase 1 had no test that would have exercised it, a seam with no test is
decoration.

**Guard.** A rule enforced by the build rather than by intention. Three exist: one MSBuild
target (Core must not resolve the Windows Desktop ref pack) and two source scans (no
`File.ReadAllBytes`, no reflection-based `JsonSerializer`). Each was *verified to fail* before
being trusted. See [[guards-that-can-fail]].

**Tick.** One evaluation of whether an automatic snapshot is due. Cheap: it compares three
timestamps and usually returns immediately. `AutoBackupCoordinator` **owns no timer**, the
host calls `Tick()`, which is what keeps every timing test instantaneous.

**Debounce.** The ~60s wait after the *last* write before capturing. A burst of writes
restarts it, so touching five faders is one snapshot rather than five.

**Rate limit.** At most one *automatic* snapshot per hour. A **manual** backup is never rate
limited and never deduplicated: the user pressed a button, and the new row appearing is the
only confirmation the design gives them.

**Prunable.** An automatic snapshot, and only an automatic snapshot. Manual and pre-restore
snapshots are never pruned at any keep count, including zero. The rule lives in
`SnapshotManifest.IsPrunable` and is consulted rather than re-derived.

**Schema version.** `manifest.json`'s compatibility marker. A manifest from a *newer* version
is refused with a readable message, never partially read: understanding some fields of a format
you do not know is how a store gets quietly corrupted by an older build. Older versions are
accepted, rejection is forward-only.

**As built.** A section appended to an executed design recording where the code diverged from
it. Both shipped designs have one. It exists so a plan can stay accurate without being rewritten
into a description of what happened, which would destroy the record of what was intended.
