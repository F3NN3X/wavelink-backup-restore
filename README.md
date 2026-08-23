# Wave Link Backup

**Back up and restore your Elgato Wave Link setup — automatically, with a history you can actually read.**

Wave Link keeps its entire configuration in one small JSON file and rolls its own backups for
about three days. That is the gap this project fills: **one snapshot per distinct configuration,
kept for as long as you like**, taken on your machine, by a tray app that runs while you forget
it exists — or by a scriptable CLI when you want to do it yourself.

| | |
|---|---|
| **Platform** | Windows 10 20H1+ (`net10.0-windows`) — [why Windows only](_docs/decisions/ADR-008-windows-only-scope.md) |
| **Status** | v0.7.2 — tray app and CLI, both working; the release/update loop is built but untested in the wild |
| **Privacy** | Nothing leaves your computer, ever. No telemetry, no uploads, no accounts |
| **Licence** | MIT (fork of [voltybat/WaveLinkSettingsUtility](https://github.com/voltybat/WaveLinkSettingsUtility), MIT) |

---

## Features

### Automatic backups that respect you

- **Snapshots on change.** The app watches the settings file, waits for the write to settle,
  and keeps at most one copy per interval (15 min to 24 h, your choice). Nothing changes, nothing
  is written.
- **Optional daily backup** at a time you pick, so quiet machines still get a known-good point.
- **No duplicates.** Identical configurations are stored once; a corrupted snapshot never pushes
  a good one out of retention.
- **Retention you control.** Automatic backups keep to your count (default 30). Backups *you*
  named, and the safety snapshot taken before every restore, are never deleted automatically.

### A history you can read

- **Every snapshot describes itself** — inputs, channels, effect counts — so you can tell a
  healthy configuration from a collapsed one at a glance, before restoring.
- **Trash, not delete.** Removing a backup moves it to a `.trash` folder inside your store;
  emptying the trash is a deliberate second step (Recycle Bin where one exists).

### Restores that tell the truth

- **Always snapshots first**, automatically. The one destructive button is safe to press.
- **Names what's missing.** Not "an effect failed to load" — *"FabFilter Pro-Q 3 isn't installed
  on this computer."*
- **Restores your presets; plug-in files are opt-in** (`--with-plugins`, needs admin), because a
  `.vst3` copy is not a licence.

### Optional tiers, resolved from what you actually use

- **Effect presets** (your EQ curves, gate thresholds) — on by default.
- **VST3 plug-in files** — off by default; captured only for the plug-ins your setup references,
  not everything installed.

### A tray app that gets out of the way

- Runs in the system tray with a live "last backup" readout: back up now, open the store, pause
  for an hour, quit (which says so on its menu item).
- **Themes that follow Windows** — light, dark and high contrast detected from the OS, plus your
  own override; the accent colour is taken from your system accent.
- Start with Windows, hide-to-tray on close, weekly update check that *only looks* — it never
  installs anything without you.

### A CLI for scripts and stubborn people

```
wlbackup backup --name "Before 3.3 beta"    # take one now
wlbackup list                               # what you have
wlbackup restore <id>                       # shows what changes, then asks
wlbackup watch                              # back up on its own until Ctrl+C
wlbackup help                               # everything else
```

Twelve verbs in total (`rename`, `delete`, `empty-trash`, `verify`, `prune`, `diagnostics`,
`version`, …), machine-readable output with `--json`, and a distinct exit code per failure so
scripts can branch. The CLI reads the same settings file as the app — a flag overrides that file
for one run, never the other way around.

### What it will not do

- **Back up plug-in licences.** Reinstall and re-authorise on a new machine, then restore.
- **Move your setup to another computer.** A snapshot names the audio devices plugged into *this*
  machine; restored elsewhere, those channels are dead. Snapshots are machine-local.
- **Send anything anywhere.** There is no setting that would create an upload, because none
  exists.

---

## Privacy — read this before sharing a backup

A Wave Link settings file contains **hardware serial numbers** (inside audio device IDs) and
**absolute paths including your Windows username**. Do not attach raw snapshots to bug reports.

Instead, use the redacting diagnostics: **Copy diagnostics** in Settings, or `wlbackup
diagnostics`. The report strips serials, usernames and snapshot display names — it fails closed
on shapes it does not recognise — and includes the settings file itself never, redacted or
otherwise. Nothing is ever uploaded; the output goes to your clipboard or your terminal.

---

## Building

Requires the .NET 10 SDK on Windows. The published app is **framework-dependent**: running it
requires the [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0), which a
fresh machine will not have. That is a deliberate trade — the archive drops from ~101 MB to ~7.6 MB
because the runtime ships nowhere at all — and it means a machine without the runtime gets the
stock .NET "framework not found" error rather than a friendly in-app prompt (a framework-dependent
WPF app fails before managed code runs, so there is no surface to show one from).

```powershell
dotnet build WaveLinkBackup.slnx
dotnet test  WaveLinkBackup.slnx        # 1,587 tests

# CLI — framework-dependent single file; resolves the runtime from the machine at startup
dotnet publish src/WaveLinkBackup.Cli -c Release                      # ~0.2 MB archive
dotnet publish src/WaveLinkBackup.Cli -c Release -p:PublishAot=true   # ~3 MB native (needs MSVC)

# App — framework-dependent; the .NET 10 Desktop Runtime is a prerequisite, not a payload
dotnet publish src/WaveLinkBackup.App -c Release
```

The AOT build's link step calls `vswhere.exe` unqualified — if it fails with
`MSB3073 ... exited with code 123`, add `%ProgramFiles(x86)%\Microsoft Visual Studio\Installer`
to `PATH`.

A handful of tests read your real Wave Link configuration when one is installed and **skip
otherwise** — the suite is green either way. None of them write, close Wave Link, or touch your
live settings.

## Architecture

C# / .NET 10. A headless core library with two thin shells, so the backup logic stays testable
and can run without a window.

```
src/WaveLinkBackup.Core     class library — everything that can be pure is
  Analysis/                 validation, fingerprint, log parsing
  Discovery/ Io/ Process/   finding, reading and safely replacing settings
  Snapshots/ Restore/       the store, the guard, the restore sequence
  Automation/               watcher, debounce, dedup, retention
src/WaveLinkBackup.Cli      wlbackup — twelve verbs, scriptable, AOT-able
src/WaveLinkBackup.App      WPF tray app — list, details, settings, updates, help, about
third_party/                vendored upstream snapshot, excluded from the build
```

The reasoning is in [ADR-001](_docs/decisions/ADR-001-csharp-over-rust.md) (language),
[ADR-004](_docs/decisions/ADR-004-core-library-thin-shells.md) (this split) and
[ADR-005](_docs/decisions/ADR-005-wpf-for-the-gui.md) (the UI framework).

## Documentation

Everything lives in [`_docs/`](_docs/). Start at [`_docs/index.md`](_docs/index.md).

| Document | What it is |
|---|---|
| [`_docs/SPEC.md`](_docs/SPEC.md) | The build specification — where the settings live, what is inside them, the restore sequence, the validation traps. The authority on *what* to build. |
| [`_docs/audits/2026-08-19-design-conformance.md`](_docs/audits/2026-08-19-design-conformance.md) | The app read against the design package — what matched, what was fixed, what remains undesigned. |
| [`_docs/dev-phases/`](_docs/dev-phases/README.md) | What is built, what remains, phase by phase. |
| [`_docs/decisions/`](_docs/decisions/) | Why it is built this way — 15 ADRs. |
| [`_docs/knowledge-base/gotchas/`](_docs/knowledge-base/gotchas/) | Twenty-eight ways this goes wrong, titled by symptom. |
| [`_docs/knowledge-base/patterns/`](_docs/knowledge-base/patterns/) | Shapes that work here, each naming its callers. |
| [`_docs/operations/runbooks/releasing-and-updating.md`](_docs/operations/runbooks/releasing-and-updating.md) | How a release is cut and how the app finds it. |
| [`_docs/technical-debt.md`](_docs/technical-debt.md) | The honest list, including assumptions nobody has checked. |

## Credits

Built on **[voltybat/WaveLinkSettingsUtility](https://github.com/voltybat/WaveLinkSettingsUtility)**
(MIT), which already solved the parts that are tedious to get right: package discovery that
avoids the stale vendor folder, Core Audio endpoint enumeration, the shutdown sequence, and
atomic writes.

This project is a fork that adds what that tool deliberately is not — a watcher, a snapshot
store with retention, content-hash dedup, duplicate-key validation and a GUI. What was taken,
what needed fixing, and why forking rather than contributing upstream was the right call is
written up in [the audit](_docs/audits/2026-08-15-voltybat-wavelinksettingsutility.md) and
[ADR-002](_docs/decisions/ADR-002-fork-wavelinksettingsutility.md).

---

*Not affiliated with, endorsed by, or supported by Elgato or Corsair. "Wave Link" is their
trademark; this is an independent utility for its users.*
