---
title: "Deleting one backup takes its neighbours with it"
status: published
created: 2026-08-17
updated: 2026-08-17
tags: [gotcha, interop, delete]
---

# Deleting one backup takes its neighbours with it

**Provenance:** **Read and guarded, never experienced.** The rule is from the Win32
documentation for `SHFILEOPSTRUCT`; the failure has not happened here because
`RecycleBinTests.Sending_leaves_its_siblings_alone` was written alongside the code. Recorded
because the next person to touch that file will not have that test's name in mind, and the
symptom is unrecoverable.

## Symptom

*Empty trash* removes the backups it was asked to remove, **and some it was not.** Sometimes
the entire trash folder. Sometimes directories beside it. There is no error; the operation
reports success.

Most likely seen after a change to `RecycleBin.Send` that looked like tidying.

## Cause

`SHFileOperation`'s `pFrom` field is **a double-null-terminated list of paths**, not a string.

```
"C:\store\.trash\2026-08-15T2307-a3f81c\0\0"
                                        ^^^^ both are load-bearing
```

The first `\0` ends the path. The second ends the *list*. With only one terminator the API
keeps reading past the end of the buffer and treats whatever memory follows as further paths
to delete. What it finds is undefined, which is why the damage varies between runs and why
this can pass a casual test.

C# makes the mistake easy: `path + '\0'` looks complete, and both compile.

## The plausible explanation, and why it is wrong

> *"It deleted too much, so the path I passed must have been wrong, probably a parent
> directory, or a trailing separator issue."*

The path is right. Inspect it and it will be exactly the one snapshot. The bug is not in the
value, it is in **where the API stops reading**, which no amount of examining the path will
reveal.

The second wrong turn:

> *"`SHFileOperation` is one call and takes one path, so a string is a string."*

Its signature takes a *file operation* describing a batch. Single-path deletion is the
degenerate case of a list API, and the terminator is how the batch ends.

## Fix

```csharp
// Both terminators. The first ends the path, the second ends the list.
var from = path + '\0' + '\0';
var buffer = Marshal.StringToHGlobalUni(from);
```

Also set `FOF_NOCONFIRMATION | FOF_NOERRORUI | FOF_SILENT`, without them the shell may show
UI from a background operation, and keep `FOF_ALLOWUNDO`, which is the entire reason the call
exists rather than `Directory.Delete`.

## How to avoid it

- **Test a sibling, not just the target.** `Sending_leaves_its_siblings_alone` creates two
  directories, sends one, and asserts the other survives with its contents. A test that only
  checks "the target is gone" passes while this bug is live.
- **Never simplify the terminator.** It reads like a redundant character and is not. The line
  carries a comment saying so; keep it.
- **Prefer the two-stage delete.** Ordinary deletion is a directory move and never reaches
  this code ([[ADR-003]] and the trash design). `SHFileOperation` runs only on *Empty trash*,
  which is the smallest blast radius available.

## References

- `src/WaveLinkBackup.Core/Abstractions/RecycleBin.cs`
- `tests/WaveLinkBackup.Core.Tests/RecycleBinTests.cs`, the sibling test
- [technical-debt.md](../../technical-debt.md) §7.1 · `operations/design/screens/05-delete-dialogs.md`
