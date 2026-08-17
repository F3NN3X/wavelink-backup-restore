---
title: "Session: Phase 5 part 2 — Core foundations, and a design that needed splitting"
status: published
created: 2026-08-17
updated: 2026-08-17
related_adrs: [ADR-004, ADR-005]
tags: [session, core, cli, phase-5]
---

# Session: Phase 5 part 2 — Core foundations, and a design that needed splitting

**Date:** 2026-08-17

Eleven commits. **386 tests green** (295 Core, 91 CLI — up from 351), Release clean with zero
warnings, **NativeAOT verified at 3.23 MB**. Working tree clean, `main`, v0.4.0.

Design: [plans/2026-08-17-phase-5-shell-design.md](../plans/2026-08-17-phase-5-shell-design.md).
Executed: [plans/2026-08-17-phase-5-plan-1-core-foundations.md](../plans/2026-08-17-phase-5-plan-1-core-foundations.md).
Written, not executed: [plans/2026-08-17-phase-5-plan-2-tray-shell.md](../plans/2026-08-17-phase-5-plan-2-tray-shell.md).

## What shipped

**A design for the whole shell, then plan 1 of five built against it.** The scope was set
deliberately wide — tray, persistence, the full nine-section Settings dialog, screen 1, high
contrast and §7.4 — so the spec covers all of it and the plans stage the work. Plan 1 is the
part with no WPF in it, which is why the App project is still a stub.

**Settings persist, and the CLI honours the same file.** `SettingsRepository` writes
`%LOCALAPPDATA%\WaveLinkBackup\settings.json` atomically, and `CommandRunner` now layers flags
over it. That last part is not tidiness: *"a command-line flag overrides this file for that one
run and isn't saved"* is a claim about the CLI as much as the GUI, and without it `wlbackup
list` would have gone on ignoring the folder chosen in the app.

**`BackupSettings` gained `ChosenWaveLinkPath`** — the thing error 2 needs and nothing was
storing, so the installation chooser would have asked on every launch. It deliberately did
**not** gain `ClosingHidesToTray`: Core has no window to hide and no tray to hide it in.

**`SnapshotSearch`** filters by name and returns match segments, so highlighting is testable
rather than buried in a converter. **`IFileSystem.GetAvailableFreeBytes`** uses
`GetDiskFreeSpaceEx` rather than `DriveInfo`, which throws on UNC paths — and a UNC store is
supported, being the whole reason deletion goes to `.trash`.

## What broke, and what it taught

**The spec was wrong twice, and both were caught by reading the codebase rather than trusting
it.** It prescribed a source-generated `JsonSerializerContext`; this repo hand-writes JSON and
has a guard that fails the build on `JsonSerializer`. It also put `ClosingHidesToTray` in Core
while arguing two paragraphs earlier that window geometry belonged in a shell-owned file — the
same argument, applied inconsistently. Writing a spec does not make it right; the self-review
step is what caught both.

**`Prune` resolved the keep count twice.** `CommandRunner.cs` had a second
`?? SnapshotRetention.DefaultKeepCount` independent of `Service()`. Left alone it would have
printed one number while pruning to another — a bug that only shows up as a confused bug report.

**`File.Replace` throws when the destination does not exist**, which is exactly the first-ever
save. `SettingsWriter` never meets this because Wave Link's `Settings.json` is always already
there, so copying its shape blindly would have broken on first run. Pinned by a test named for
the case.

**A misleading failure signature cost real time**, and is now written up:
[publish-the-native-aot-binary.md](../knowledge-base/recipes/publish-the-native-aot-binary.md).
`dotnet publish -p:PublishAot=true` fails with an `MSB3073` naming `link.exe` and an exit code,
which reads as *your interop broke the linker*. The actual cause is `vswhere.exe` not being on
`PATH` — the ILCompiler targets shell out to it and splice its error text into the link command.
Managed compilation had already succeeded. I nearly concluded the new `DllImport` had closed AOT
off; it had not.

**Two of my own test-fixture bugs, both instructive.** A heredoc collapsed `\\` to `\`, so the
smoke-test `settings.json` contained an invalid `\s` escape — and the tolerant reader did
exactly what it promises, silently falling back to defaults. That is now a recorded consequence:
a hand-edited file with a single-backslash path loses *every* setting, not one. And a
`FakeFileSystem.EnumerateFiles(dir, "*.tmp")` assertion would have passed vacuously, because the
fake's glob only understands `prefix*`.

## Decisions

| Decision | Reasoning |
|---|---|
| **`SettingsRepository` in Core**, not the App project | The design's own sentence about flags is about the CLI too. In the shell it would have made that sentence false. Cost: the CLI's flag handling now layers over the file |
| **Tolerant reads, no `CoreError`** | A preferences file that fails to parse should cost defaults, not a refusal to start; one broken field must not cost the other three. There is no failure for a caller to handle |
| **Five plans, not one** | The spec covers four subsystems. Each plan produces working, testable software on its own, and the alternative was five documents written before any code existed |
| **Theme dictionaries move into plan 2** | The tray icon's four states are specified in `--wl-*` terms and `11` ties it to system contrast, so the tray cannot be built correctly without them. Plan 3 shrinks to live following, accent, Mica and guards |
| **`long?` for free space** | The bottom bar can omit the figure; printing 0 would quietly claim a full disk |

## Still open

- **Plan 2 is written but not executed.** Six tasks, fully specified.
- **`BackupHostTests` in plan 2 has six described-not-written test bodies** — the one deliberate
  gap, flagged in the document. They need `AutoBackupCoordinatorTests.cs`'s harness shape, which
  the plan did not read. Everything else in that plan is complete code.
- **The design ships no icon assets.** README §icons says to substitute the codebase's real icon
  set; there isn't one. Plan 2 draws the shield to the Lucide 24px grid and flags it for
  replacement.
- **§7.4 keyboard and focus** — still open, now scoped to the surfaces plans 2–5 build.
- **`watch` is still the least-covered verb.** Plan 2 replaces its host; deleting it rather than
  maintaining two hosts is still worth deciding.
- **`H.NotifyIcon.Wpf` will be the repo's first production dependency**, entering the build in
  plan 2 task 1. `src/` is dependency-free until then.

## References

- [phase-5-wpf.md](../dev-phases/phase-5-wpf.md) · [technical-debt.md](../technical-debt.md) §7
- [publish-the-native-aot-binary.md](../knowledge-base/recipes/publish-the-native-aot-binary.md)
- [[ADR-004]] thin shells · [[ADR-005]] WPF
