---
title: "Pure analysis core"
status: published
created: 2026-08-16
updated: 2026-08-16
related_adrs: [ADR-004]
tags: [pattern, architecture, core]
---

# Pure analysis core

## Problem

A backup tool must not modify what it is backing up. That is easy to state and easy to
violate: any code path that parses a file and writes something derived from it can silently
substitute a re-serialized version for the original. This project's withdrawn encoder finding
([[every-snapshot-differs-with-no-real-change]]) is exactly that mistake, and it survived
review twice, once in the spec, once in an audit, because "don't re-serialize" is a
convention, and conventions are only as good as the reviewer's attention that day.

## Solution

Put everything that reasons about bytes in a namespace that **cannot perform IO**, and give it
no way to acquire the ability.

`WaveLinkBackup.Core.Analysis` has:

- no constructors, every type is `static` or a `record`;
- no injected dependencies, so no seam can be handed in;
- no `async`, so nothing awaits a stream;
- no reference to `IFileSystem` or `IWaveLinkProcess`.

```csharp
// src/WaveLinkBackup.Core/Analysis/SettingsAnalysis.cs
public static class SettingsAnalysis
{
    public static Result<SettingsAnalysisResult> Analyse(ReadOnlySpan<byte> utf8Json)
    {
        // parses once; returns records; touches nothing
    }
}
```

The signature is the whole argument: it takes a `ReadOnlySpan<byte>` and returns records.
There is no parameter it could write through and no field it could have been given.

## Callers

| Where | Why it uses this |
|---|---|
| `src/WaveLinkBackup.Core/Io/SettingsInspector.cs:ReadAndAnalyse` | Analyses each read; retries once when the result is `MalformedSettings`, because a torn read is not a broken config |
| `src/WaveLinkBackup.Core/Io/SettingsWriter.cs:Write` | Validates content **before** replacing anything, restoring a file the app will reject looks identical to the snapshot being broken |
| `src/WaveLinkBackup.Core/Analysis/LogAnalysis.cs` | Same shape for log text: `Verify(string) → RestoreVerdict` |

## Held down by

`tests/WaveLinkBackup.Core.Tests/SettingsAnalysisTests.cs`, 11 tests, no fakes, no setup.
That is the practical payoff: the component carrying the most risk is the cheapest in the
codebase to test exhaustively. `DuplicateKeyScannerTests` adds 8 more the same way.

The boundary itself is enforced by
`tests/WaveLinkBackup.Core.Tests/SourceGuardTests.cs:Core_never_reads_a_file_without_choosing_a_share_mode`
and its two siblings. See [[guards-that-can-fail]].

## When not to use it

When the analysis genuinely needs to stream. `Settings.json` is 43 KB, so reading it whole
costs nothing; a 4 GB file would make `ReadOnlySpan<byte>` the wrong parameter and force a
different shape.

Also not worth it for logic with one caller and no risk. The split earns its keep here
because *half* of Core is analysis and that half is where silent corruption would live.

## References

- [[ADR-004]]: the Core/shell split this sits inside
- [[every-snapshot-differs-with-no-real-change]]: the bug this shape makes unrepresentable
- [Phase 1 design](../../plans/2026-08-16-phase-1-core-design.md) §1
