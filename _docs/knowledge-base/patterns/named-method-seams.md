---
title: "Named-method seams instead of parameterised ones"
status: published
created: 2026-08-16
updated: 2026-08-16
tags: [pattern, architecture, io]
---

# Named-method seams instead of parameterised ones

## Problem

`Settings.json` is locked while Wave Link runs, so every read must pass
`FileShare.ReadWrite | FileShare.Delete` ([[capture-fails-while-wave-link-is-running]]). The
obvious abstraction hands that decision to the caller:

```csharp
Stream Open(string path, FileMode mode, FileAccess access, FileShare share);
```

Which means every call site can get it wrong, and the failure only appears on a machine with
Wave Link actually running, never in CI. Upstream has exactly this shape
(`IFileOperations.ReadAllBytes` delegating to `File.ReadAllBytes`) and the bug with it.

## Solution

Do not expose the parameter. Expose the **decision, already made**, under a name that says
what it is for.

```csharp
// src/WaveLinkBackup.Core/Abstractions/IFileSystem.cs
/// <summary>Reads with FileShare.ReadWrite | FileShare.Delete.</summary>
byte[] ReadSharedBytes(string path);
```

```csharp
// src/WaveLinkBackup.Core/Abstractions/FileSystem.cs
public byte[] ReadSharedBytes(string path)
{
    using var stream = new FileStream(
        path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
    ...
}
```

Callers cannot pick the wrong share mode because they never pick one. The knowledge lives in
one place, next to the comment explaining why, and the name `ReadSharedBytes` is a prompt to
the next reader that sharing is the point, rather than an incidental flag.

## Callers

| Where | Why it uses this |
|---|---|
| `src/WaveLinkBackup.Core/Io/SettingsReader.cs:Read` | The only settings read in Core; translates IO exceptions into `SettingsUnreadable` |
| `src/WaveLinkBackup.Core/Io/SettingsReader.cs:ReadNewestLog` | Logs are written by a running Wave Link, so they are locked for the same reason |
| `src/WaveLinkBackup.Core/Io/SettingsWriter.cs:Write` | Reads the temp file back to verify what landed before `File.Replace` |

## Held down by

- `tests/WaveLinkBackup.Core.Tests/FileSystemTests.cs:Reads_a_file_that_another_handle_holds_open_for_writing`,
proves the share mode works, using a second `FileStream` rather than needing Wave Link.
- `tests/WaveLinkBackup.Core.Tests/RealInstallTests.cs:The_naive_read_fails_while_Wave_Link_is_running`,
proves the naive call *does* fail, so the pattern is not solving an imaginary problem.
- `SourceGuardTests` fails the build if `File.ReadAllBytes` reappears anywhere in Core.

## When not to use it

When the parameter is genuinely a caller's business. `IFileSystem.EnumerateFiles(path,
pattern)` keeps its pattern parameter because different callers legitimately want different
globs, and no single choice would be right.

The test is whether there is **one correct value**. If there is, the parameter is not
flexibility, it is an opportunity to be wrong.

## References

- [[capture-fails-while-wave-link-is-running]]: the bug that motivated it
- [[guards-that-can-fail]]: how the rule stays enforced
