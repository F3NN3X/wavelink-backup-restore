<h1 align="center">Wave Link Backup</h1>

<p align="center">
  Automatic backups for your Elgato Wave Link setup, with a history you can actually read.
</p>

<p align="center">
  <img src="_docs/images/main-window.png" alt="The main window: the snapshot list with status strip, search and settings gear" width="820">
</p>

Wave Link stores its entire configuration in one small JSON file and only keeps about three days
of its own backups. This project fills that gap: **one snapshot per distinct configuration, kept
for as long as you like**, taken on your machine by a tray app that runs while you forget it
exists, or by a scriptable CLI when you'd rather do it yourself.

<p align="center">
  <a href="#features">Features</a> •
  <a href="#screenshots">Screenshots</a> •
  <a href="#download">Download</a> •
  <a href="#cli">CLI</a> •
  <a href="#privacy">Privacy</a> •
  <a href="#building">Building</a> •
  <a href="#architecture">Architecture</a> •
  <a href="#documentation">Documentation</a> •
  <a href="#credits">Credits</a>
</p>

---

## Features

### Backups that don't get in the way

- **Snapshots on change.** The app watches the settings file, waits for the write to settle, and
  keeps at most one copy per interval (15 min to 24 h, your call). Nothing changes, nothing gets
  written.
- **Optional daily backup** at a time you pick, so quiet machines still get a known-good point.
- **No duplicates.** Identical configurations are stored once; a corrupted snapshot never pushes
  a good one out of retention.
- **Retention you control.** Automatic backups keep to your count (default 30). Backups *you*
  named, and the safety snapshot taken before every restore, are never deleted automatically.

### A history you can read

- **Every snapshot describes itself.** Inputs, channels and effect counts, so you can tell a
  healthy configuration from a collapsed one at a glance, before restoring.
- **Trash, not delete.** Removing a backup moves it to a `.trash` folder inside your store;
  emptying the trash is a deliberate second step (Recycle Bin where one exists).

### Restores that tell you what's happening

- **Always snapshots first**, automatically. The one destructive button is safe to press.
- **Names what's missing.** You get *"FabFilter Pro-Q 3 isn't installed on this computer"*
  rather than "an effect failed to load".
- **Restores your presets; plug-in files are opt-in** (`--with-plugins`, needs admin), because a
  `.vst3` copy is not a licence.

### What gets backed up

- **Effect presets** (your EQ curves, gate thresholds), on by default.
- **VST3 plug-in files.** Off by default, and captured only for the plug-ins your setup
  references, not everything installed.

### A tray app that stays out of the way

- Lives in the system tray with a live "last backup" readout: back up now, open the store, pause
  for an hour, quit (which says so on its menu item).
- **Themes that follow Windows.** Light, dark and high contrast, detected from the OS, plus your
  own override. The accent colour comes from your system accent.
- Start with Windows, hide to tray on close, and a daily update check that tells you when a new
  version exists. It only ever looks: nothing installs without you asking.

### What it will not do

- **Back up plug-in licences.** Reinstall and re-authorise on a new machine, then restore.
- **Move your setup to another computer.** A snapshot names the audio devices plugged into *this*
  machine; restored elsewhere, those channels are dead. Snapshots are machine-local.
- **Send anything anywhere.** There is no setting that would create an upload, because none
  exists.

---

## Screenshots

| Snapshot details | Delete confirmation | Restore preview |
|---|---|---|
| <img src="_docs/images/snapshot-details.png" alt="Snapshot details dialog: what was captured in this backup" width="320"> | <img src="_docs/images/delete-view.png" alt="Delete confirmation for a snapshot" width="320"> | <img src="_docs/images/restore-view.png" alt="Restore preview showing what will change" width="320"> |

---

## Download

Grab the latest release from [GitHub Releases](https://github.com/F3NN3X/wavelink-backup-restore/releases):

| Asset | What it is |
|---|---|
| `WaveLinkBackup-*-app-win-x64.zip` | The tray app. Extract and run. Requires the [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0). |
| `WaveLinkBackup-CLI-*-win-x64.zip` | The `wlbackup` CLI, a single file, framework-dependent or AOT. |

Each release ships a `.sha256` sidecar for the archive. The app is **framework-dependent** by
design: the runtime is a prerequisite, not a payload, so the archive stays under 8 MB instead of
~101 MB.

---

## CLI

Twelve verbs, machine-readable output with `--json`, and a distinct exit code per failure so
scripts can branch. The CLI reads the same settings file as the app. A flag overrides that file
for one run, never the other way around.

```powershell
wlbackup backup --name "Before 3.3 beta"    # take one now
wlbackup list                               # what you have
wlbackup restore <id>                       # shows what changes, then asks
wlbackup watch                              # back up on its own until Ctrl+C
wlbackup help                               # everything else
```

The remaining verbs: `rename`, `delete`, `empty-trash`, `verify`, `prune`, `diagnostics`,
`version`.

---

## Privacy

**Read this before sharing a backup.** A Wave Link settings file contains **hardware serial numbers** (inside audio device IDs) and
**absolute paths including your Windows username**. Don't attach raw snapshots to bug reports.

Instead, use the redacting diagnostics: **Copy diagnostics** in Settings, or `wlbackup
diagnostics`. The report strips serials, usernames and snapshot display names, and fails closed
on shapes it does not recognise. It never includes the settings file itself, redacted or
otherwise. Nothing is uploaded; the output goes to your clipboard or your terminal.

---

## Building

Requires the .NET 10 SDK on Windows. The published app is **framework-dependent**: running it
requires the [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0), which a
fresh machine won't have. That's a deliberate trade. The archive drops from about 101 MB to
7.6 MB because the runtime ships nowhere at all, and a machine without it gets the stock .NET
"framework not found" error rather than a friendly in-app prompt. (A framework-dependent WPF app
fails before managed code runs, so there is no surface to show one from.)

```powershell
dotnet build WaveLinkBackup.slnx
dotnet test  WaveLinkBackup.slnx        # 1,668 tests

# CLI: framework-dependent single file, resolves the runtime from the machine at startup
dotnet publish src/WaveLinkBackup.Cli -c Release                      # ~0.2 MB archive
dotnet publish src/WaveLinkBackup.Cli -c Release -p:PublishAot=true   # ~3 MB native (needs MSVC)

# App: framework-dependent, so the .NET 10 Desktop Runtime is a prerequisite, not a payload
dotnet publish src/WaveLinkBackup.App -c Release
```

The AOT build's link step calls `vswhere.exe` unqualified. If it fails with
`MSB3073 ... exited with code 123`, add `%ProgramFiles(x86)%\Microsoft Visual Studio\Installer`
to `PATH`.

A handful of tests read your real Wave Link configuration when one is installed and **skip
otherwise**, so the suite is green either way. None of them write, close Wave Link, or touch
your live settings.

## Architecture

C# / .NET 10. A headless core library with two thin shells, so the backup logic stays testable
and can run without a window.

```
src/WaveLinkBackup.Core     class library, and everything that can be pure is
  Analysis/                 validation, fingerprint, log parsing
  Discovery/ Io/ Process/   finding, reading and safely replacing settings
  Snapshots/ Restore/       the store, the guard, the restore sequence
  Automation/               watcher, debounce, dedup, retention
src/WaveLinkBackup.Cli      wlbackup: twelve verbs, scriptable, AOT-able
src/WaveLinkBackup.App      WPF tray app: list, details, settings, updates, help, about
third_party/                vendored upstream snapshot, excluded from the build
```

The reasoning is in [ADR-001](_docs/decisions/ADR-001-csharp-over-rust.md) (language),
[ADR-004](_docs/decisions/ADR-004-core-library-thin-shells.md) (this split) and
[ADR-005](_docs/decisions/ADR-005-wpf-for-the-gui.md) (the UI framework).

## Documentation

Everything lives in [`_docs/`](_docs/). Start at [`_docs/index.md`](_docs/index.md).

| Document | What it is |
|---|---|
| [`_docs/SPEC.md`](_docs/SPEC.md) | The build specification: where the settings live, what's inside them, the restore sequence, the validation traps. The authority on *what* to build. |
| [`_docs/audits/2026-08-19-design-conformance.md`](_docs/audits/2026-08-19-design-conformance.md) | The app read against the design package: what matched, what was fixed, what remains undesigned. |
| [`_docs/dev-phases/`](_docs/dev-phases/README.md) | What's built, what remains, phase by phase. |
| [`_docs/decisions/`](_docs/decisions/) | Why it's built this way, in 18 ADRs. |
| [`_docs/knowledge-base/gotchas/`](_docs/knowledge-base/gotchas/) | Thirty-three ways this goes wrong, titled by symptom. |
| [`_docs/knowledge-base/patterns/`](_docs/knowledge-base/patterns/) | Shapes that work here, each naming its callers. |
| [`_docs/operations/runbooks/releasing-and-updating.md`](_docs/operations/runbooks/releasing-and-updating.md) | How a release is cut and how the app finds it. |
| [`_docs/technical-debt.md`](_docs/technical-debt.md) | The honest list, including assumptions nobody has checked. |

## Credits

Built on **[voltybat/WaveLinkSettingsUtility](https://github.com/voltybat/WaveLinkSettingsUtility)**
(MIT), which already solved the parts that are tedious to get right: package discovery that
avoids the stale vendor folder, Core Audio endpoint enumeration, the shutdown sequence, and
atomic writes.

This project is a fork that adds what that tool deliberately isn't: a watcher, a snapshot
store with retention, content-hash dedup, duplicate-key validation and a GUI. What was taken,
what needed fixing, and why forking rather than contributing upstream was the right call is
written up in [the audit](_docs/audits/2026-08-15-voltybat-wavelinksettingsutility.md) and
[ADR-002](_docs/decisions/ADR-002-fork-wavelinksettingsutility.md).

---

<p align="center">
  <strong>Platform</strong> Windows 10 20H1+ (<code>net10.0-windows</code>) &middot; <a href="_docs/decisions/ADR-008-windows-only-scope.md">why Windows only</a><br>
  <strong>Status</strong> v0.7.6, tray app and CLI both working. In-app updates work from 0.7.6 onward; earlier builds have to be replaced by hand once<br>
  <strong>Privacy</strong> Nothing leaves your computer, ever. No telemetry, no uploads, no accounts<br>
  <strong>Licence</strong> MIT (fork of <a href="https://github.com/voltybat/WaveLinkSettingsUtility">voltybat/WaveLinkSettingsUtility</a>, MIT)
</p>

---

*Not affiliated with, endorsed by, or supported by Elgato or Corsair. "Wave Link" is their
trademark; this is an independent utility for its users.*
