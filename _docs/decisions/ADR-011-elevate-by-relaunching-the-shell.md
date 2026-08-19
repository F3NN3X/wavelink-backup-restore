---
title: "ADR-011: Elevate by relaunching the shell, for one restore, and never otherwise"
status: accepted
created: 2026-08-19
updated: 2026-08-19
related_adrs: [ADR-006, ADR-004]
tags: [decision, restore, security, wpf]
---

# ADR-011: Elevate by relaunching the shell, for one restore, and never otherwise

**Status:** Accepted
**Date:** 2026-08-19

## Context

[[ADR-006]] made tier 4 — the `.vst3` binaries — opt-in and off by default, and named the
reason it is different from every other tier: `C:\Program Files\Common Files\VST3` is not
user-writable. Tiers 1 to 3 land in `LocalState` and `%APPDATA%`, which the user owns, so
**everything that matters restores on an ordinary account with no prompt at all.**

Tier 4 restore shipped in phase 6, tested, and reachable only from the CLI. The WPF shell could
not ask for it. [technical-debt.md](../technical-debt.md) §4.17 recorded why, and the reason was
not the code: elevation had no designed surface. No UAC prompt in any screen, no error state for
a declined one among the twelve in `06-errors.md`. Building one in XAML during phase 6 would
have been inventing design in code, which is what [[ADR-004]] and the design package both exist
to prevent.

A WPF process cannot acquire administrator rights in place. Windows grants them at process
creation and never afterwards, so *any* answer here is a second process; the decision is which
one, carrying what, and for how long.

## Decision

**The shell starts a second copy of its own executable, elevated, to perform one whole restore,
and exits.** `ShellExecute` with the `runas` verb, arguments `--restore <id> --with-plugins`,
and the parent blocks until the child exits and maps its exit code.

The elevated copy runs **headless**: no window, no tray, no watcher, and **no single-instance
mutex**. It takes the pre-restore snapshot itself.

The app never runs elevated otherwise, and never draws an administrator prompt of its own.

## Alternatives considered

| Option | Why not |
|---|---|
| **Elevate only the file copy, via a helper** | Narrower privilege window, which is genuinely better on that axis. But it needs a second binary the app must locate, trust and version-match — and the obvious candidate, the CLI, is a separate artifact that may not be installed beside the app. It also splits one restore across two processes, so a failure between them leaves a half-restored state with no single owner. The privilege window it saves is a few hundred milliseconds of a copy the user explicitly asked for. |
| **Surface the opt-in and never elevate** | Smallest change, and `TierRestore` already returns `NeedsElevation`, so the shell could report it honestly. Rejected because it is the current behaviour with better wording: the row would exist, the user would switch it on, and the files still would not be restored. A control that reports its own failure is not a feature. |
| **Elevate the whole app at launch** | Removes the prompt from the restore path entirely. A backup tool that runs as administrator all day so it can occasionally write to `Program Files` is a far worse trade than one prompt on an opt-in path — and it would make the file watcher, the tray, and every future feature run elevated for the sake of one. |
| **A scheduled task or a Windows service to do privileged work on demand** | The standard way to avoid repeated prompts. It means an always-installed privileged component, an install step, and a new attack surface, for a feature that is off by default and used rarely. Wrong ratio. |
| **Draw our own consent dialog and elevate silently after** | Not possible without a privileged component, and actively harmful as a pattern: a program that paints something resembling an administrator prompt is teaching people to trust a thing they should not. Windows draws it, or nothing does. |

## Consequences

**This enables:** tier 4 restore from the app, with `RestoreOptions` already wired for it; a real
UAC prompt drawn by Windows; and a decline that costs nothing, because the elevated copy takes
the pre-restore snapshot itself — at the moment Windows asks, the child does not exist and
nothing has been touched.

**This rules out:**

- **A single-instance check that applies to every launch.** The mutex is `Local\` and per-user,
  so the elevated copy runs as the *same* user, finds the mutex held by the window that started
  it, concludes it is a second launch and exits without restoring anything. `IsHeadlessRestore`
  is the exemption, and it must be evaluated **before** the mutex is acquired. Anyone touching
  `App.OnStartup` inherits this constraint.
- **Reporting the elevated restore as *confirmed*.** The child verifies from Wave Link's log and
  the parent cannot see that verdict across a process boundary, so the parent reports
  `Unconfirmed` — the write went through, this process did not confirm it. Claiming a
  confirmation nobody read is exactly the dishonesty `03-restore-outcomes.md` exists to prevent.
  A richer result would mean an IPC channel, which this deliberately does not have.
- **Stage-by-stage progress during an elevated restore.** The strip shows *Closing Wave Link* for
  the duration. Same cause: no channel back.
- **Remembering the answer.** Elevation is per-restore by construction — Windows will not
  remember it, so neither does the row. There is no "don't ask again".
- **A silent retry.** `Try again as administrator` re-runs the same restore *when pressed*. A
  program that re-prompts on its own is one people learn to click through.

**Revisit if:** the app acquires an installer with a privileged component for another reason
(an update service, say). At that point the helper option becomes cheap, because the trust and
versioning problems are already solved — and it would narrow the privilege window for free.

## References

- [technical-debt.md](../technical-debt.md) §4.17 — the entry this closes
- [`screens/13-elevation.md`](../operations/design/screens/13-elevation.md) — the designed
  surface, written before the code
- [[ADR-006]] — the four tiers, and why only tier 4 needs this
- [[ADR-004]] — core library, thin shells: the reason the surface was designed first
