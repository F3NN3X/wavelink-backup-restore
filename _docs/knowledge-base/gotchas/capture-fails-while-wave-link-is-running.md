---
title: "Capture fails with \"being used by another process\" whenever Wave Link is running"
status: published
created: 2026-08-16
updated: 2026-08-16
related_adrs: [ADR-007]
tags: [gotcha, filesystem, capture]
---

# Capture fails with "being used by another process" whenever Wave Link is running

**Provenance:** **Observed 2026-08-16** on the reference machine, with `Elgato.WaveLink` and
`WavelinkSEService` both running. Not documented in `SPEC.md` — found by probe.

## Symptom

```
System.IO.IOException: The process cannot access the file
'...\Elgato.WaveLink_g54w8ztgkx496\LocalState\Settings.json'
because it is being used by another process.
```

Every capture throws while Wave Link is open. Captures taken with Wave Link closed work
perfectly, so the code looks correct and the failure looks environmental.

## Cause

**Wave Link holds `Settings.json` open with a share mode that denies other readers.**

`File.ReadAllBytes` — and `File.ReadAllText`, `File.Open(path, FileMode.Open)`, and every other
convenience overload — defaults to `FileShare.Read`. That default asks the OS for a lock
compatible only with *other readers*, and Wave Link already holds the file in a way that
refuses it.

Measured, same file, same moment:

```
File.ReadAllBytes                        FAILED — being used by another process
FileShare.ReadWrite | FileShare.Delete   OK — 43,052 bytes
```

## The plausible explanation, and why it is wrong

> *"Wave Link is mid-write. Retry with a backoff and it'll succeed."*

It will not. This is not a transient write window — it is the app's steady state for as long
as it is running. A retry loop turns an immediate, clearly-worded failure into a slow one that
eventually reports a timeout, which is strictly worse: you have hidden the cause behind a
symptom that reads as "the disk is busy".

> *"Close Wave Link before capturing, like the restore path does."*

Reasonable-sounding and it destroys the product. The watcher's entire purpose is to capture
**while the user works** ([[ADR-007]]) — noticing a settings change and keeping a copy without
interrupting anything. Closing the app to back it up would make automatic capture worse than
useless.

Note the asymmetry, because it is the thing to hold onto: **restore requires the app to be
fully exited; capture requires the opposite.** Reading is safe under sharing. Writing is not
([[restored-settings-revert-seconds-later]]).

## Fix

Open explicitly with a permissive share mode:

```csharp
static byte[] ReadSettingsBytes(string path)
{
    using var fs = new FileStream(
        path, FileMode.Open, FileAccess.Read,
        FileShare.ReadWrite | FileShare.Delete);   // ← the fix

    var bytes = new byte[fs.Length];
    fs.ReadExactly(bytes);
    return bytes;
}
```

`FileShare.ReadWrite` permits Wave Link's existing handle. `FileShare.Delete` additionally
tolerates the file being renamed or replaced underneath us — which is exactly what Wave Link's
own atomic-save does, so it is not hypothetical.

**One read is not atomic against a concurrent write.** A capture taken during Wave Link's save
can catch a torn file. Validate every capture before storing it, and treat a parse failure as
"retry once", not "the config is broken" — this is the one place a retry *is* the right answer,
and it is a different failure from the one above.

## How to avoid it

- **Exactly one function reads settings bytes**, and every caller uses it. `File.ReadAllBytes`
  should not appear anywhere in the codebase against this path.
- **A test that fails the build if it does.** A source scan for `File.ReadAllBytes` /
  `File.ReadAllText` in `Core` is cruder than it sounds and catches the reintroduction, which
  will otherwise only surface on a machine with Wave Link actually running.
- **Run at least one integration test with Wave Link open.** CI will not have it running, so
  CI cannot catch this — which is precisely why it survived into a written spec unnoticed.

## References

- Discovered during the phase-1 probe; see
  [session 2026-08-16 — phase-1 probe](../../sessions/2026-08-16-phase-1-probe.md)
- [[ADR-007]] · [[restored-settings-revert-seconds-later]] ·
  [[every-snapshot-differs-with-no-real-change]]
