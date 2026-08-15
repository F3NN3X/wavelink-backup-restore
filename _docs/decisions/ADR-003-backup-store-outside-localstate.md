---
title: "ADR-003: The backup store lives outside LocalState, identified by manifest"
status: accepted
created: 2026-08-16
updated: 2026-08-16
tags: [decision, storage, architecture]
---

# ADR-003: The backup store lives outside `LocalState`, identified by manifest

**Status:** Accepted
**Date:** 2026-08-16

## Context

Upstream writes its backups as `Settings.json.backup-<ts>` **beside** `Settings.json`, inside
the MSIX package's `LocalState`. It enumerates them by scanning that one directory, and it
refuses to restore anything whose filename does not match
`^Settings\.json\.backup-\d{8}-\d{9}$`.

That filename regex is a genuinely good guard — it stops a mistyped path writing arbitrary
bytes into a config file. But the location it enforces is fatal:

> **Resetting or uninstalling the MSIX package deletes `LocalState` wholesale — every backup
> with it.** The tool's backups are destroyed by precisely the event you would most want to
> recover from.

The location is not the only problem. Three requirements collide with upstream's model at
once, and they all trace to the same root — **identity by filename**:

| Requirement | Blocked by |
|---|---|
| User-chosen store location | `ValidateManagedPath` enforces "beside `Settings.json`" |
| User-typed backup names | The filename regex has no room for them |
| Survives a package reset | The mandated location is the volatile one |

Names are the sharpest case. Put the display name in the folder name and you inherit every
problem of user text in a path: collisions, sanitising, length limits, and someone eventually
typing `Mic chain 3/4"`.

## Decision

The backup store is **user-chosen**, defaulting to `%LOCALAPPDATA%\WaveLinkBackup`, and lives
**outside `LocalState`**. Each snapshot is one directory with a machine-generated name:

```
<store>/
  2026-08-15T2307-a3f81c/
    manifest.json        ← name, notes, timestamps, hashes, app version
    settings.json
    plugins.json         ← tier 2 manifest
    presets/             (tier 3, optional)
    plugins/             (tier 4, optional)
```

**Identity moves from the filename to `manifest.json`.** The display name lives there and
nowhere else, so renaming is a metadata write — no file moves, no collisions, no sanitising,
no breakage from a slash in a name.

The safety guard is kept in spirit and rebuilt on the new identity: restore asserts *"this
directory contains a `manifest.json` we wrote, whose recorded hashes match its contents"*
rather than *"this filename matches a regex"*. Same protection against writing arbitrary
bytes into a config file, with no constraint on naming or location.

### manifest.json

| Field | Purpose |
|---|---|
| `displayName`, `notes` | What the user typed. Freely editable. |
| `createdUtc`, `trigger` | `manual` / `watcher` / `preRestore` — pre-restore snapshots render distinctly |
| `settingsSha256` | The dedup key ([[ADR-007]]) |
| `waveLinkVersion` | The first question when a restore fails ([[newest-backup-is-the-broken-one]]) |
| `inputCount`, `inputNames` | The health fingerprint, surfaced in the list |
| `hasDuplicateKeys` | Result of the `JsonDocument` walk; marks the entry suspect |
| `tiers` | Which of tiers 1–4 this snapshot actually contains |

The manifest is also, not incidentally, the entire restore UI. Every column in the main
window's list reads from it — nothing needs opening a snapshot to render the row.

## Alternatives considered

| Option | Why not |
|---|---|
| **Keep upstream's location, add a second store** | Two sources of truth for "what backups exist", and the fragile one still looks authoritative. |
| **Filename-encoded metadata** (`2026-08-15_2307_before-3-3-beta`) | Readable in Explorer, and that is the whole argument for it. Against it: rename becomes a move, user text becomes a path, and every field you later want to add becomes another delimiter. The filename in the UI's expanded row (`2026-08-11_2136_before-3-3-beta.wlbk`) already gives the Explorer-legibility benefit without carrying identity. |
| **SQLite index alongside the store** | Fast, and wrong for this scale. Four backups. It also introduces a file that can disagree with the directory it describes — a self-describing directory cannot. |
| **Single-file archive per snapshot** (`.wlbk` zip) | Tidier in Explorer, and it makes partial reads (just the manifest, for the list) and tier-3/4 selective restore harder. Reconsider only if the store ever holds thousands of entries. |

## Consequences

**This enables:** free-text names, a user-chosen location, snapshots that survive a package
reset or reinstall, and adding manifest fields later without touching anything on disk.

**This rules out:**

- Upstream's `ValidateManagedPath` as written. It must be rewritten, not adapted — and it is
  entangled with `NewBackupPath` and `ManagedBackups`, so all three change together. Fixing
  one alone leaves restore refusing its own files.
- Identifying a snapshot from its path alone. Any code that wants to know what a snapshot *is*
  reads its manifest. That is a per-snapshot file read on list load — trivial at this scale,
  and the reason the manifest carries every field the list needs.

**This creates an obligation:** the store now outlives the application that wrote it, in a
location the user chose and may back up, sync or move. `manifest.json` is a compatibility
surface from day one — version it.

**Revisit if:** the store routinely holds thousands of snapshots, at which point the
per-snapshot manifest read stops being free and an index earns its keep.

## References

- `SPEC.md` §7·1, §10
- [technical-debt.md](../technical-debt.md) §1.1
- [[ADR-002]] · [[ADR-007]]
