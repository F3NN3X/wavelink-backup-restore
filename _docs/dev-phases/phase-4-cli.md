---
title: "Phase 4 — CLI shell"
status: published
created: 2026-08-16
updated: 2026-08-16
related_adrs: [ADR-004, ADR-008]
tags: [dev-phase]
---

# Phase 4 — CLI shell

**Status:** ✅ **Complete — 2026-08-16.** 308 tests green (228 Core, 80 CLI). NativeAOT verified at **3.2 MB**. See the [session note](../sessions/2026-08-16-phase-4-cli-build.md).
**Entry criteria:** phase 3 complete. ✅ 2026-08-16.
**Exit criteria:** every Core capability is reachable from the command line, the CLI is the
integration-test driver for phases 2–3, and `dotnet publish` produces a single executable that
runs on a machine without the .NET SDK.

## Why this phase exists

**Core has no callers.** Three phases of library exist and nothing in production invokes any
of it — `Tick()` and `CaptureOnShutdown()` have never been called outside a test. A shell is
what turns a library into a program.

The CLI comes before the GUI for two reasons ([[ADR-004]]):

1. It is a few days' work and makes phases 2–3 testable end to end. Building the GUI first
   would mean testing the snapshot store through a window, which is slow and tests the wrong
   thing.
2. It is the **only AOT-eligible artifact** — WPF does not support NativeAOT. Keeping it in its
   own project from the first commit is what preserved that option; this phase is where the
   option gets exercised.

## Scope

### In

- Verbs: `backup`, `list`, `restore`, `rename`, `delete`, `watch`, `verify`, `prune`.
- `--settings-path` — the escape hatch for the multiple-package case and the possible
  non-MSIX case ([technical-debt.md](../technical-debt.md) §2.2).
- `--store` — the user-chosen store location.
- The first production caller of `AutoBackupCoordinator.Tick()`, which means **choosing the
  tick interval**.
- Exit codes that mean something to a script.
- Publish: single file, self-contained.

### Out — and where it went instead

- Any windowing → **phase 5**.
- Tier 2–4 capture → **phase 6**. `backup` captures tier 1 only, and says so.
- Autostart, tray, update mechanics → **phase 7**, and currently out of scope entirely.
- A config file. Settings pass as flags for now; where they persist is phase 5's problem,
  because that is where a user first changes one without editing a command line.

## Work

### 1 · Argument parsing, without a dependency

`System.CommandLine` is the obvious choice and is worth a deliberate decision rather than a
reflex: Core carries no third-party dependencies, the CLI's needs are eight verbs and four
options, and NativeAOT compatibility is a hard requirement here. **Evaluate hand-rolled
parsing first.** If a library goes in, it goes in with an ADR.

### 2 · Verbs

| Verb | Calls | Notes |
|---|---|---|
| `backup [--name]` | `BackupService.BackUpNow` | Never deduplicated |
| `list` | `SnapshotStore.List` | Health fingerprint per row; this is the list the GUI will render |
| `restore <id>` | `RestoreOrchestrator.Restore` | **Prints the plan and requires confirmation** unless `--yes` |
| `rename <id> <name>` | `SnapshotStore.Rename` | A metadata write |
| `delete <id>` | `SnapshotStore.Delete` | Confirmation unless `--yes` |
| `verify <id>` | `SnapshotGuard.Verify` | Reports corruption; the guard already distinguishes the cases |
| `prune [--keep N]` | `BackupService.Prune` | Reports what went |
| `watch` | `AutoBackupCoordinator` | Runs until interrupted |

### 3 · `watch` — the first production `Tick()` caller

The coordinator deliberately owns no timer, so **this phase decides the interval**. A tick is
cheap — it compares three timestamps and usually returns immediately — so something like every
10–15 seconds is ample against a 60-second debounce.

Handle `Ctrl+C` by calling `CaptureOnShutdown()` before exiting. That path exists precisely
because the original incident happened during a restart, and phase 3 has never had a caller
for it.

### 4 · Exit codes

Scripts are a real consumer. `0` success; distinct non-zero codes for "Wave Link not found",
"nothing to do", "refused", "failed". Map from `CoreError` subtypes — the hierarchy was built
for this.

### 5 · Output

Plain text by default, `--json` for scripts. **No colour by default**, since output gets piped.

**Never print a raw device ID.** They contain hardware serial numbers
([technical-debt.md](../technical-debt.md) §6). `list` shows friendly input names, which is
what a person recognises anyway.

### 6 · Publish

`SelfContained=true` and `PublishSingleFile=true` are already set. This phase verifies the
result actually runs on a machine without the SDK, and **tries NativeAOT** — the answer feeds
§2.4 and the phase 7 packaging decision.

## Testing

The CLI is thin by contract, so most tests are about *translation*: arguments in, Core calls
out, exit code back.

| Test | Pins |
|---|---|
| Each verb maps to its Core call with the right arguments | The shell stays thin |
| `restore` without `--yes` does **not** restore | The one irreversible action |
| Each `CoreError` maps to a distinct exit code | Scripts can branch |
| `--json` output parses | Scripts can read |
| No output path can emit a raw device ID | Privacy §6 |
| `--settings-path` reaches `SettingsLocator` unchanged | §2.2 |

**No Core logic may move into the CLI.** If a verb wants something Core cannot do, that is a
Core change with its own tests, not a helper in the shell ([[ADR-004]]).

## Risks

| Risk | Early signal | Response |
|---|---|---|
| Logic leaking into the shell | A verb doing more than translate | Push it into Core with tests |
| A parsing library breaking AOT | Publish warnings about trimming | Evaluate before adopting; hand-rolled is viable at this size |
| `restore` becoming accidentally non-interactive | `--yes` defaulting on | Confirmation is the default; `--yes` is opt-in |
| Device IDs reaching stdout | Any output printing a raw key | A test asserts it |
| `watch` holding the settings file open | Wave Link failing to save | Core reads in shared mode and never holds a handle |

## References

- [[ADR-004]] — why the CLI is its own project · [[ADR-008]] — Windows-only
- [technical-debt.md](../technical-debt.md) §1.5, §2.2, §2.4, §6
- `SPEC.md` §10 — "keep a CLI shell alongside"
