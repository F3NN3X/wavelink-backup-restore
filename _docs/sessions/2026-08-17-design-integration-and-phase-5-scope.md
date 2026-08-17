---
title: "Session: Design integration, and deciding what phase 5 actually is"
status: published
created: 2026-08-17
updated: 2026-08-17
related_adrs: [ADR-003, ADR-004]
tags: [session, design, phase-5]
---

# Session: Design integration, and deciding what phase 5 actually is

**Date:** 2026-08-17

Three commits, no code. Release build clean, **308 tests green** (228 Core, 80 CLI) before and
after — nothing in this session touched a line that runs. Working tree clean, `main`, v0.4.0.

Governing plan: [dev-phases/phase-5-wpf.md](../dev-phases/phase-5-wpf.md).

## What shipped

**The design handoff went from four screens to thirteen state groups.** Two packages arrived
(v3, then v4) and were integrated verbatim into `operations/design/`: eleven then thirteen
spec files in `screens/`, fifteen PNGs, a `MANIFEST.md` and a `CHANGES-SINCE-V1.md`. **All six
design gaps in [technical-debt.md](../technical-debt.md) §4 are now closed** — nothing in the
UI is undesigned, including Windows high contrast and the tray.

**Adopted verbatim rather than patched.** `design-handoff.md` reverted to the export's own
`README.md`, and the whole folder is now **exempt from the frontmatter rule**, stated in
`_docs/README.md` with the reason: it is a drop-in export that gets replaced wholesale, and
patching frontmatter on every re-export would guarantee the repo copy drifts from the design
tool's — the one thing a handoff must not do. Same exemption as `third_party/`, same reason.
Thirteen files repointed; two references left in session notes, deliberately.

**Tokens and brand assets hash-verified byte-identical.** The token-drift risk flagged before
the export turned out to be zero, so nothing in the design system moved underneath the code.

## What broke, and what it taught

**Nothing broke — but four of the design's decisions had quietly outdated shipped code**, and
finding that was the point of reading it properly instead of filing it. Recorded as
[technical-debt.md](../technical-debt.md) §7, all four now decided:

**The delete decision improved on my recommendation, for a reason I had missed.** I proposed
`SHFileOperation` behind an `IRecycleBin` seam. The two-stage alternative — move to
`<store>/.trash/`, then *Empty trash* forwards to the Recycle Bin — is better because **the
store is user-chosen and the Recycle Bin does not exist on network shares**. On a NAS,
`SHFileOperation` either deletes permanently or prompts, so the design's promise was one the
app *could not keep*. A directory move behaves identically on every volume and takes interop
off the delete path entirely, so `Core` stays `net10.0` and the `GuardNoDesktopFramework` guard
stays meaningful. The naming mess it seemed to risk does not exist: ids are already
timestamp-plus-hash with identity in the manifest ([[ADR-003]]).

**The design caught its own rule violation.** The first handoff specified the SUSPECT badge in
red inside an amber row — the forbidden second red, by its own rules, and it made a health
state look like an action. It is amber now. Nothing had been built against it, so the
correction cost nothing, which is the whole argument for designing before building.

**"The tray doubles the phase" did not survive examination.** The framing — *a tray app with a
window, not the reverse* — is nearly free, because `AutoBackupCoordinator` already owns no
timer and waits for a host to call `Tick()`; the CLI's `watch` verb is one today. `ShutdownMode`
is one line. What actually costs is that **WPF provides none of the three Windows integrations
the design assumes**: tray icon (no `NotifyIcon`, and it must survive Explorer restarting),
toast notifications, autostart registry. Splitting on *that* line rather than on the framing is
what kept the phase honest.

## Decisions

| Decision | Reasoning |
|---|---|
| **Two-stage delete** — `.trash/` then *Empty trash* | Works on volumes where the Recycle Bin does not exist; keeps interop off the delete path. **Amends design decision 3** — the dialog must name `.trash`, not the Recycle Bin |
| **Verify lazily, only the condemned** | Pruning removes one or two, so hash one or two — not the thirty a verify-on-list would. No manifest field to keep in sync. A damaged snapshot becomes immortal until deleted by hand, which beats destroying the evidence of one's own corruption |
| **Watcher clears `lastWriteAt` on failure; `TickResult` carries the `CoreError`** | Stops the every-15-seconds silent retry that was both halves of what the design forbids. The error is what feeds the tray's `NEEDS YOU` state — without it the tray has a state it cannot enter |
| **Windows keyboard conventions generally**, screen-reader labels included | The five-slot health strip reads as five unlabelled cells without an `AutomationProperties` name |
| **Notifications and the update mechanism → phase 7** | Both need Windows APIs WPF lacks; both are *"something has been wrong for a while"* cases. The tray's `NEEDS YOU` icon carries the same information passively |
| **The tray framing stays** | If closing the window stops backups, the app fails its own promise and becomes upstream's tool with extra steps |

## Still open

- **`[ComImport]` under NativeAOT** ([technical-debt.md](../technical-debt.md) §2.4) — AOT works
  at 3.2 MB with zero trim warnings, but no COM interop has been ported, so the risky part is
  untested. Unchanged by this session.
- **Non-MSIX installs** (§2.2) — mitigated by `--settings-path`, still unverified.
- **`watch` is the least-covered verb** — its loop and `Ctrl+C` handling are exercised only by
  hand. Phase 5 replaces its host anyway.
- **The design amendment for decision 3** is written up here and in §7.1 but has not been sent
  back to the design package.

## Next

Phase 5. **The four Core changes first**, with tests, before any XAML — three are `Core` and
one of them blocks a tray state.

## References

- [phase-5-wpf.md](../dev-phases/phase-5-wpf.md) — the plan, rescoped
- [technical-debt.md](../technical-debt.md) §4 (closed), §7 (the four decisions)
- [operations/design/screens/00-index.md](../operations/design/screens/00-index.md) — read
  `01-tokens-and-mapping.md` first; `10-decisions.md` closes every open question
