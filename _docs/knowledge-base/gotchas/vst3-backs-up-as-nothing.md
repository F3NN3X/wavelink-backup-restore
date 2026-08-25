---
title: "A plugin backs up as zero bytes, and the snapshot still says it succeeded"
status: published
created: 2026-08-16
updated: 2026-08-16
related_adrs: [ADR-006]
tags: [gotcha, vst3, filesystem]
---

# A plugin backs up as zero bytes, and the snapshot still says it succeeded

**Provenance:** **Spec-derived. Never observed.** From the VST3 specification, not from this
machine, **all six referenced plugins on the reference machine are single files**, so this
path cannot be exercised by the author's own setup. That is the reason it is written down
before it happens rather than after.

## Symptom

A tier 4 snapshot completes. Its size is smaller than expected, but plausibly so. The tier
badge shows `PLUGINS` present.

On restore, one or more plugins are missing entirely, or restore as an empty directory. Wave
Link loads the channel with the effect switched off.

## Cause

**A `.vst3` may be a directory, not a file.**

The VST3 specification defines a *bundle*:

```
Plugin.vst3\                          ← a directory, despite the extension
  Contents\
    x86_64-win\
      Plugin.vst3                     ← the actual binary, same name again
    Resources\
    moduleinfo.json
```

Code that assumes `FilePath` points at a file does not throw when handed a bundle. Depending
on the API, `File.Copy` fails with an access error that gets swallowed, or a length check
returns something meaningless, or an existence check passes and the copy produces nothing.
**The snapshot reports success** because nothing surfaced an exception.

Bundles are not exotic and are becoming more common, installers increasingly ship them that
way.

## The plausible explanation, and why it is wrong

> *"Every `.vst3` I have is a file, so `FilePath` points at a file."*

True on this machine, for these six plugins, today. It is a property of one sample of size
six, and this document exists because that sample will never disprove it.

The second wrong turn:

> *"If it were wrong, the backup would fail loudly."*

It does not. A directory with a file extension satisfies most casual existence checks, and the
failure is a *silent absence*, the hardest kind to notice, because the snapshot looks
complete and nothing is discovered until a restore, on a different machine, months later.

## Fix

Test for directory, and recurse:

```csharp
static void CapturePlugin(string filePath, string destination)
{
    if (Directory.Exists(filePath))          // ← check FIRST: a bundle is a directory
        CopyDirectoryRecursive(filePath, destination);
    else if (File.Exists(filePath))
        File.Copy(filePath, destination, overwrite: true);
    else
        throw new PluginNotFoundException(filePath);
}
```

Order matters: check `Directory.Exists` **before** `File.Exists`, and let the missing case
throw rather than silently skipping. A plugin that cannot be captured must fail the tier, not
quietly reduce it.

Record the captured size and file count per plugin in `plugins.json`, and **verify the
capture** rather than trusting the copy: a plugin whose recorded size is zero is a bug
report, not a valid snapshot.

## How to avoid it

- **Test with a synthetic bundle fixture.** Build a directory named `Fake.vst3` containing
  `Contents\x86_64-win\Fake.vst3` and assert the capture recurses it. This is the only way
  this path gets exercised, because the author's machine never will
  ([technical-debt.md](../../technical-debt.md) §2.3).
- **Never let a per-plugin capture failure be silent.** Aggregate failures and surface them,
  a snapshot that captured five of six plugins is not a successful snapshot with a footnote.
- **Assert non-zero size after every capture.** Cheap, and it catches this plus several
  failure modes nobody has thought of.

## References

- `SPEC.md` §9
- [technical-debt.md](../../technical-debt.md) §2.3
- [[ADR-006]] · [[restored-plugin-demands-a-licence]]
- [glossary.md](../../glossary.md), *bundle*, *referenced, not installed*
