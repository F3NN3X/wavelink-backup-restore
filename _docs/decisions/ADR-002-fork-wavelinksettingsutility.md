---
title: "ADR-002: Fork voltybat/WaveLinkSettingsUtility"
status: accepted
created: 2026-08-16
updated: 2026-08-16
tags: [decision, architecture, upstream]
---

# ADR-002: Fork `voltybat/WaveLinkSettingsUtility`

**Status:** Accepted
**Date:** 2026-08-16

## Context

`voltybat/WaveLinkSettingsUtility` is a C# / .NET 10 console utility, MIT-licensed, roughly
60 KB of source with 30 KB of tests, last pushed 2026-07-19. It already solves the parts of
this problem that are tedious to get right and boring to get wrong:

- **`SettingsDiscovery`** globs `Elgato.WaveLink_*` under `Packages` and requires
  `Settings.json` to exist, so it never touches the stale vendor folder that catches everyone
  ([[backup-succeeds-but-protects-nothing]]). It also handles multiple installed packages,
  which it refuses to guess between and demands `--settings-path` instead.
- **`WindowsAudioEndpointInspector`** is ~80 lines of hand-declared `[ComImport]` Core Audio
  interfaces. This is how you tell "this input is dead" from "this input is fine", and it is
  exactly the kind of code nobody enjoys writing twice.
- **The shutdown sequence.** Graceful close, 10-second timeout, kill tree on timeout, then
  *assert not running* before writing.
- **Atomic write.** Temp file, then `File.Replace(temp, path, backupPath)`.
- **Seam interfaces.** `IFileOperations`, `IWaveLinkProcess`, `Func<DateTime> clock`. The
  reason it has 30 KB of tests against 60 KB of code.

It is not, however, the same product. It is a manual tool: backups happen only when invoked,
with no watcher, no schedule and no dedup, so repeated runs write identical copies. **That gap
is the entire reason this project exists**, and it is also why forking is honest rather than
parasitic. We are not repackaging someone's work; we are building a different thing on its
foundations.

## Decision

**Fork it**, under MIT with attribution preserved. Keep discovery, endpoint inspection, the
shutdown sequence, atomic write and the seam interfaces. Replace the backup model entirely.

## Alternatives considered

| Option | Why not |
|---|---|
| **Depend on it as a library** | It is a console application, not a published package. There is no versioned artifact to depend on, and the changes we need are structural, [[ADR-003]] rewrites where backups live, which reaches into `NewBackupPath`, `ManagedBackups` and `ValidateManagedPath` at once. |
| **Contribute upstream instead** | Adding a watcher, a snapshot store, dedup and a GUI to a deliberately minimal CLI tool is not a contribution. It is a takeover of someone else's design. Individual fixes, the encoder, duplicate-key detection, are worth offering back. |
| **Write fresh** | Discards ~80 lines of correct COM interop, a verified shutdown sequence and a discovery routine that already avoids the project's biggest trap. Weeks of work to arrive at the same place, with new bugs. |

## Consequences

**This enables:** starting at the interesting problems instead of the solved ones, and
inheriting a codebase with a real test ratio and the seams that make it possible.

**This rules out:** a clean-sheet architecture. We inherit upstream's shape, including its
assumptions about where backups live and what identifies one. [[ADR-003]] and [[ADR-004]] are
both, in part, undoing those assumptions.

**We also inherit its defects.** Five of them, itemised in
[technical-debt.md](../technical-debt.md) §1 with severities and owner phases. They become real
debt the moment the fork lands, which is why they are written down before it does. The
critical one, backups inside `LocalState`, is not a bug to fix later; it is the change the
fork exists to make.

**Obligations:** MIT requires attribution. Preserve the licence and the copyright notice, name
the upstream in the root `README.md`, and offer the encoder and duplicate-key fixes back.

**Revisit if:** upstream diverges far enough that merging costs more than maintaining our own,
at which point this stops being a fork in anything but history.

## References

- `SPEC.md` §7, §8
- [Audit: voltybat/WaveLinkSettingsUtility](../audits/2026-08-15-voltybat-wavelinksettingsutility.md)
- [[ADR-001]] · [[ADR-003]] · [[ADR-004]] · [[ADR-007]]
