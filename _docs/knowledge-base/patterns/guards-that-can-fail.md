---
title: "Guards that can fail, and are proven to"
status: published
created: 2026-08-16
updated: 2026-08-16
tags: [pattern, testing, ci]
---

# Guards that can fail, and are proven to

## Problem

Three of this project's rules are invisible at the point they are broken and expensive far
away from it:

| Rule | Where breaking it surfaces |
|---|---|
| No `File.ReadAllBytes` in Core | On a machine with Wave Link running. **Never in CI.** |
| No reflection-based `JsonSerializer` in Core | Only under NativeAOT, phase 7 |
| Core stays headless | When AOT or the test host fails, phases later |

A comment saying "don't do X" catches none of them. Worse, a *guard* that silently never
matches also catches none of them, while looking like it does.

## Solution

Two mechanisms, chosen by what the rule is about, plus a test that the guard itself works.

**Rules about the build graph → MSBuild target.** Reference resolution is the only place that
knows what got pulled in:

```xml
<!-- src/WaveLinkBackup.Core/WaveLinkBackup.Core.csproj -->
<Target Name="GuardNoDesktopFramework" AfterTargets="ResolveAssemblyReferences">
  <Error Condition="$([System.String]::Copy('%(ReferencePath.FullPath)').Contains('Microsoft.WindowsDesktop.App'))"
         Text="WaveLinkBackup.Core must stay headless (ADR-004) but resolved %(ReferencePath.Filename)…" />
</Target>
```

Match on the **ref pack**, not on assembly names: `WindowsBase.dll` and `System.Windows.dll`
also exist in `Microsoft.NETCore.App.Ref` as legacy type-forwarding shims, present in every
.NET app. Guarding by filename produced a false positive the first time this ran.

**Rules about source text → a test.** The test project receives Core's source directory as
assembly metadata and scans it, with comments stripped so the rules can be written down
without tripping themselves:

```csharp
// tests/WaveLinkBackup.Core.Tests/SourceGuardTests.cs
var offenders = Offenders(@"File\.ReadAll(Bytes|Text|Lines)\b|File\.OpenRead\b");
Assert.True(offenders.Length == 0, $"Core must read through IFileSystem.ReadSharedBytes…");
```

**Then prove the guard can fail.** A guard nobody has seen reject anything is a guess:

```csharp
[Fact]
public void The_scanner_actually_matches_something_it_should_reject()
{
    Assert.Matches(regex, "var b = File.ReadAllBytes(path);");
    Assert.DoesNotMatch(regex, "var b = fileSystem.ReadSharedBytes(path);");
}
```

The MSBuild guard was verified the same way, by hand: temporarily setting `UseWPF=true` made
it fail with the intended message, and reverting restored a clean build. That check is worth
repeating whenever the guard is edited.

## Callers

| Where | Rule enforced |
|---|---|
| `src/WaveLinkBackup.Core/WaveLinkBackup.Core.csproj:GuardNoDesktopFramework` | Headless ([[ADR-004]]) |
| `tests/…/SourceGuardTests.cs:Core_never_reads_a_file_without_choosing_a_share_mode` | [[capture-fails-while-wave-link-is-running]] |
| `tests/…/SourceGuardTests.cs:Core_never_uses_reflection_based_json_serialization` | NativeAOT stays open ([technical-debt.md](../../technical-debt.md) §2.4) |
| `tests/…/SourceGuardTests.cs:Core_never_writes_to_the_console` | Headless |

## Held down by

`SourceGuardTests.The_scanner_actually_matches_something_it_should_reject`, which pins the
regexes and the comment-stripping. Without it, a typo in a pattern turns all three guards into
decoration and every build stays green.

## When not to use it

When the compiler or an analyzer can express the rule. A source scan is a blunt instrument,
it sees text, not semantics, and will not notice `var f = File; f.ReadAllBytes(p);`. It is the
right tool only when the rule is about *what may appear in this project's source*, which no
general analyzer knows.

Keep the pattern list short. Each one is a small tax on every reader who has to work out why
their obvious code is rejected, so the error message must say *why*, not just *no*.

## References

- [[ADR-004]] · [[capture-fails-while-wave-link-is-running]]
