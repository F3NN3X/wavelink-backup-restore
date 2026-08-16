# Wave Link Backup

**A Windows utility that backs up and restores your Elgato Wave Link setup.**

> **Windows only.** Wave Link ships on macOS too; this does not. Everything here depends on
> the Windows MSIX package layout, Core Audio COM and NTFS atomic replace. See
> [ADR-008](_docs/decisions/ADR-008-windows-only-scope.md).

> **Status: pre-alpha, v0.2.0. Nothing is installable yet.**
> `WaveLinkBackup.Core` can find your settings, validate them, fingerprint them, **write and
> restore snapshots**, and confirm a restore from Wave Link's log — 186 tests, 83% coverage.
> What it cannot do is notice on its own: every backup has to be asked for, in code. The
> watcher is phase 3, and the CLI and window are still stubs.

---

## Why this exists

Wave Link keeps its entire configuration — every channel, every routing assignment, every
effect chain — in **one 43 KB JSON file**. It writes its own rolling backups, and keeps
roughly ten of them, covering about **three days**.

That is the gap. A configuration that breaks over a long weekend has no good copy left by the
time anyone notices, and the newest backup is often a copy of the damage, written seconds
after the reset.

Wave Link Backup keeps **one snapshot per distinct configuration, for as long as you like**.
At 43 KB a copy, a year of daily changes is under 16 MB. Configured once, then ignored until
the day it saves your rig.

## What it will do

- **Snapshot automatically** when your settings change — noticing the write, waiting a minute,
  keeping at most one copy an hour. No duplicates: identical configurations are stored once.
- **Show you what each snapshot contains** before you restore it — how many inputs, which
  channels, how many effects — so you can tell a healthy configuration from a collapsed one at
  a glance.
- **Always snapshot before restoring**, automatically. The one destructive button is safe to
  press.
- **Optionally capture your VST3 presets and plug-ins**, resolved from what your setup
  actually references rather than everything installed — about 40 MB instead of 4.9 GB.
- **Tell you what's missing** on a restore: not "an effect failed to load", but "install
  FabFilter Pro-Q 4 v4.x".

### What it will not do

- **Back up your plug-in licences.** Copying a `.vst3` restores the code, not your right to
  run it. On a new machine you reinstall and re-authorise, then restore.
- **Move your setup to another computer.** A snapshot names the audio devices plugged into
  *this* machine. Restored elsewhere, those channels are dead. Snapshots are machine-local.
- **Send anything anywhere.** Nothing leaves your computer, ever.

---

## Privacy — read this before sharing a backup

A Wave Link settings file contains **hardware serial numbers** (inside audio device IDs) and
**absolute paths including your Windows username**.

If you attach a backup to a bug report, you are publishing both. A redacting "copy
diagnostics" action ships before this repository goes public; until it does, do not attach raw
snapshots to anything.

---

## Documentation

Everything lives in [`_docs/`](_docs/). Start at [`_docs/index.md`](_docs/index.md).

| Document | What it is |
|---|---|
| [`_docs/SPEC.md`](_docs/SPEC.md) | The build specification — where the settings live, what is inside them, the restore sequence, the validation traps. The authority on *what* to build. |
| [`_docs/operations/design/design-handoff.md`](_docs/operations/design/design-handoff.md) | The complete visual and interaction design: tokens, four screens, states, copy. |
| [`_docs/dev-phases/`](_docs/dev-phases/README.md) | What is left to build, phase by phase. |
| [`_docs/decisions/`](_docs/decisions/) | Why it is built this way — 8 ADRs. |
| [`_docs/knowledge-base/gotchas/`](_docs/knowledge-base/gotchas/) | Eight ways this goes wrong, titled by symptom. |
| [`_docs/technical-debt.md`](_docs/technical-debt.md) | The honest list, including assumptions nobody has checked. |

## Architecture

C# / .NET 10. A headless core library with two thin shells, so the backup logic stays testable
and can run without a window.

```
src/WaveLinkBackup.Core     class library                                       ✅ phases 1–2
  Analysis/                 pure — validation, fingerprint, log parsing
  Discovery/ Io/ Process/   finding, reading and safely replacing settings
  Snapshots/ Restore/       the store, the guard, the restore sequence
src/WaveLinkBackup.Cli      thin shell — scriptable, unattended                 stub, phase 4
src/WaveLinkBackup.App      thin shell — WPF, the four designed screens         stub, phase 5
third_party/                vendored upstream snapshot, excluded from the build
```

`Core` is split so that everything which *can* be pure *is* — validation, the health
fingerprint, log parsing — with all IO behind two seams. The pure half cannot write a file
even by accident, which is how "a backup tool must not modify what it is backing up" becomes a
property of the type system rather than a rule to remember.

The reasoning is in [ADR-001](_docs/decisions/ADR-001-csharp-over-rust.md) (language),
[ADR-004](_docs/decisions/ADR-004-core-library-thin-shells.md) (this split) and
[ADR-005](_docs/decisions/ADR-005-wpf-for-the-gui.md) (the UI framework).

## Building

Requires the .NET 10 SDK on Windows.

```
dotnet build WaveLinkBackup.slnx
dotnet test  WaveLinkBackup.slnx
```

Seven tests read your real Wave Link configuration if one is installed, and **skip when it is
not** — so the suite is green either way. None of them write, close Wave Link, or touch your
live settings.

---

## Credits

Built on **[voltybat/WaveLinkSettingsUtility](https://github.com/voltybat/WaveLinkSettingsUtility)**
(MIT), which already solved the parts that are tedious to get right: package discovery that
avoids the stale vendor folder, Core Audio endpoint enumeration, the shutdown sequence, and
atomic writes.

This project is a fork that adds what that tool deliberately is not — a watcher, a snapshot
store with retention, content-hash dedup, duplicate-key validation and a GUI. What was taken,
what needed fixing, and why forking rather than contributing upstream was the right call is
written up in
[the audit](_docs/audits/2026-08-15-voltybat-wavelinksettingsutility.md) and
[ADR-002](_docs/decisions/ADR-002-fork-wavelinksettingsutility.md).

## Licence

MIT, preserving upstream's copyright notice. See `LICENSE`.

---

*Not affiliated with, endorsed by, or supported by Elgato or Corsair. "Wave Link" is their
trademark; this is an independent utility for its users.*
