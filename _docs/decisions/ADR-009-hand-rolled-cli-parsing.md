---
title: "ADR-009: Hand-rolled command-line parsing"
status: accepted
created: 2026-08-16
updated: 2026-08-16
related_adrs: [ADR-004]
tags: [decision, cli, dependencies]
---

# ADR-009: Hand-rolled command-line parsing

**Status:** Accepted
**Date:** 2026-08-16

## Context

`WaveLinkBackup.Cli` needs to parse eight verbs (`backup`, `list`, `restore`, `rename`,
`delete`, `verify`, `prune`, `watch`) and about five options (`--name`, `--settings-path`,
`--store`, `--keep`, `--yes`, `--json`).

Reaching for `System.CommandLine` is the reflex, and the phase 4 plan required evaluating the
alternative first rather than taking the reflex. Three things make this project's answer
different from the usual one:

1. **`Core` carries no third-party dependencies at all.** Adding the first one to the *shell*
   is less severe, but it is still the first.
2. **The CLI is the only NativeAOT-eligible artifact**. WPF does not support AOT, which is
   why [[ADR-004]] kept them in separate projects. Anything that compromises AOT here forecloses
   the option the project has been protecting for three phases
   ([technical-debt.md](../technical-debt.md) §2.4).
3. **`System.CommandLine` is still pre-release.** Taking a beta dependency to save a hundred
   lines is a poor trade when the hundred lines are this boring.

## Decision

**Hand-roll it.** A pure `CommandLineParser` in the CLI project: `string[]` in, a
`ParsedCommand` record out, no IO and no dependencies.

Being pure is the point, it makes the parser testable with no console, no filesystem and no
setup, exactly like `Analysis/` in Core ([[pure-analysis-core]]).

## Alternatives considered

| Option | Why not |
|---|---|
| **`System.CommandLine`** | Excellent, and aimed at a problem several sizes larger than this one. Pre-release, and its AOT story would need verifying against the one artifact whose AOT eligibility this project has spent three phases protecting. Revisit if the CLI grows subcommand trees, completion, or localised help. |
| **`CommandLineParser` / `McMaster` / `Spectre.Console.Cli`** | All mature. All add a dependency and a rendering opinion to a program whose output must stay pipe-friendly and colour-free by default. |
| **`Spectre.Console` for output** | Tempting for the `list` table, and wrong: output is going into scripts, and pretty tables are what `--json` exists to avoid needing. |

## Consequences

**This enables:** a CLI with zero dependencies, so the NativeAOT attempt in phase 4 tests
*our* code rather than a library's trimming annotations. It also keeps the parser pure and
therefore cheap to test exhaustively.

**This rules out:**

- Free niceties: no auto-generated help, no completion scripts, no `--help` per subcommand
  unless written. Help text is hand-maintained, which means **it can drift from the parser**,
  a test asserts every verb appears in it.
- Sophisticated syntax. No option bundling (`-yj`), no `--opt=value` *and* `--opt value` both
  supported, no response files. One form each, documented.

**Revisit if:** the verb list grows a second level (`wlbackup store list`, `wlbackup store
prune`), or help/completion becomes a real user request. At that point the hundred lines stop
being boring and a library earns its place, and this ADR gets superseded rather than
quietly ignored.

## References

- [[ADR-004]]: why the CLI is its own project, and why AOT matters
- [phase-4-cli.md](../dev-phases/phase-4-cli.md) §1
- [technical-debt.md](../technical-debt.md) §1.5, §2.4
