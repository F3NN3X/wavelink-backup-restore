---
title: "After 1.0"
status: review
created: 2026-08-19
updated: 2026-08-19
tags: [dev-phase, index]
---

# After 1.0

What is deliberately **not** in phases 6 and 7, so that neither phase absorbs it by accident.

Two kinds of thing live here, and they are worth keeping apart:

- **Refused** — decided against, with a reason. Reopening one needs a new argument, not a
  reminder.
- **Deferred** — wanted, not gating. Each names the signal that would promote it.

Anything not listed in [phase-6-plugin-tiers.md](phase-6-plugin-tiers.md), [phase-7-release.md](phase-7-release.md)
or here is unplanned, which is a fine thing for it to be — but it should be noticed rather than
assumed.

---

## Refused

| Thing | Why not, and where it is written down |
|---|---|
| **Portable backups / restoring onto another machine** | `InputSettings` is keyed by Core Audio endpoint IDs that embed device serials, and plugin paths are absolute. A cross-machine restore produces dead channels, not a shared preset. The UI labels backups machine-local instead. SPEC §11, [technical-debt.md](../technical-debt.md) §3, [[restored-backup-has-dead-channels]] |
| **Capturing licence material** | Nothing licence-shaped exists in the folders tier 3 and 4 copy — checked. Those vendors authorise via registry, machine-bound files elsewhere, or an online account. [[ADR-006]], [[restored-plugin-demands-a-licence]] |
| **Capturing `EBWebView\`** | ~100 MB of WebView2 profile inside `LocalState`. It turns a 470 KB snapshot into a 100 MB one and restoring it can wedge the UI. SPEC §1 |
| **Managing Wave Link's own AutoBackups** | We copy them as payload because they carry history our first run will not have. We do not prune, rotate or write them — that is Wave Link's directory and its business. [technical-debt.md](../technical-debt.md) §3 |
| **macOS** | Wave Link ships there; this app does not. Scoped Windows-only in the README rather than left ambiguous. [[ADR-008]] |
| **Telemetry, crash reporting, auto-upload** | Nothing leaves the machine unless the user pastes it. SPEC §11 |
| **A Microsoft Store / MSIX package of this app** | It would buy signing and updates at the cost of writing into a redirected `LocalState` — the exact defect the store exists to avoid. [[ADR-003]] |

## Deferred — wanted, with the signal that promotes it

### Features

**Export a chain.** SPEC §11 names it explicitly as a *different feature* built on
`AudioPluginConfigurations` alone: the effect chain on one channel, without device IDs, importable
onto another machine. It is the honest answer to the thing people will actually ask for when they
discover backups are machine-local.
**Signal:** someone asking to move a mic chain between machines, more than once.

**Repair a dead input.** Point a channel whose device has gone at a device that is there. This is
what `WindowsAudioEndpointInspector` exists for upstream (~80 lines of hand-declared Core Audio
`[ComImport]`), and SPEC §7 recommends taking it. It is out of 1.0 because it is an *editing*
feature and everything shipped so far moves whole files — SPEC §3 warns that rewriting a device ID
means walking the whole tree and rewriting both the bare and `<deviceId>|<suffix>` forms, and
handling a destination key that already exists.
**Signal:** a user with a dead channel and a working device, which the health strip can already
show them.
**Note:** porting it is also what finally answers [technical-debt.md](../technical-debt.md) §2.4 —
`[ComImport]` under NativeAOT — because there is no COM interop in the codebase today.

**Restoring a plug-in somewhere other than where it came from.** Blocked on one measurement,
not on appetite: whether Wave Link resolves a channel's plug-in by `PluginId` or by `FilePath`
([technical-debt.md](../technical-debt.md) §7.6, and
[the audit](../audits/2026-08-20-plugin-resolution-and-elevation.md) for the experiment). If it is
`PluginId`, an unwritable plug-in folder could fall back to the user-level VST3 location and never
need administrator rights — and *"the plug-in moved"* becomes a state tier 2 can describe rather
than one indistinguishable from *"the plug-in is gone"*. It would also remove one of the two
reasons portable backups are refused above; the other, device serials in endpoint IDs, would
remain.
**Signal:** the experiment being run, plus a user who cannot write their own VST3 folder. The
current recommendation is *do not build it* — one UAC prompt on an explicit opt-in is what UAC is
for, and a fallback destination costs a promise tier 4 currently keeps.

**A diff between two backups.** The store already keys on content hashes, so "what changed between
these two" is cheap to compute and would answer "why did my mixer change" directly.
**Signal:** the restore dialog's fingerprint line not being enough to choose between two backups.

### Debts that may ship open

Each is recorded in full in [technical-debt.md](../technical-debt.md); this is only the
disposition.

| # | What | Promote when |
|---|---|---|
| §4.11 | Total-size arithmetic reimplemented in five places | Next time one of the five is touched — add `SnapshotManifest.TotalSizeBytes` and point them at it |
| §4.14 | One `ListBox` per date group, so arrow keys stop at a group boundary | Cross-group keyboard movement is asked for. The fix is a flat list with `CollectionViewSource` grouping and deletes `GroupSelection` entirely |
| §4.16 | Tier 2 rehashes every referenced plugin binary on every capture (~40 MB on the reference rig) | An automatic capture visibly lags, or the watcher's debounce window is missed. The fix is a size + last-write-time skip, which is a cache with an invalidation rule — measure first |
| §4.8 (1) | The tray icon renders at a fixed 32px, soft above 150% scaling | A high-DPI user notices. The renderer already takes `pixelSize`; it needs the taskbar's DPI and a `WM_DPICHANGED` hook |
| §4.8 (3) | `Back up automatically` shows a trailing check, not a switch | Someone confirms `screens/12`'s `[toggle]` was literal rather than shorthand |
| §4.8 (5) | A failed manual backup shows a raw `MessageBox` with Core's wording | It is seen in the wild; the twelve designed error screens exist to be used |
| §2.2 | Whether non-MSIX Wave Link installs exist | A user reports "not found" with Wave Link plainly installed. The `--settings-path` escape hatch already serves them; phase 7 §5 gives it a door in the UI |

### Documentation hygiene

- [technical-debt.md](../technical-debt.md) §1.3 ("No duplicate-key detection") still reads as
  open with a fix "due in phase 1". `DuplicateKeyScanner` shipped in phase 1 and
  `SnapshotManifest.HasDuplicateKeys` records it per snapshot. **Strike it through with the other
  closed inherited findings** next time that file is edited.
- §2.4 should be closed as *not applicable* rather than left open, unless "repair a dead input"
  above is promoted — that is the only thing that would bring `[ComImport]` into the codebase.

## References

- `SPEC.md` §3 (device IDs as foreign keys), §7 (what to take from upstream), §11 (shipping)
- [[ADR-003]] · [[ADR-006]] · [[ADR-008]]
- [technical-debt.md](../technical-debt.md) §2, §3, §4
