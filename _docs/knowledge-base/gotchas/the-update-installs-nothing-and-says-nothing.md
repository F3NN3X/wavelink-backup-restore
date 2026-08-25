---
title: "The update installs nothing and says nothing"
status: published
created: 2026-08-25
updated: 2026-08-25
related_adrs: [ADR-012, ADR-018]
tags: [gotcha, updates, windows, reporting]
---

# The update installs nothing and says nothing

**Provenance:** **Observed**, 2026-08-25, immediately after
[[every-update-fails-its-checksum]] was fixed — the checksum error was hiding this one.

## Symptom

You press install. The app downloads, shows progress, closes itself and reopens.

It is the same version as before.

No error, no dialog, no log line, nothing in the tray. The only way to know anything happened at
all is to look at the install directory and find a `.staged` folder sitting beside it holding the
version you wanted.

## Cause

Two separate problems wearing one appearance.

**The swap made one attempt.** The final step renames the install directory aside and moves the new
version into place. It is guarded by `WaitForExit` on the old process — but *a process exiting is
not the same as Windows finishing with its files*. An image section for the just-terminated
executable, a shell extension holding the folder, or a virus scanner reading eight megabytes of
freshly-extracted DLLs will each keep it locked for a moment. `Directory.Move` threw, the code
caught it, put the old install back, and returned false.

**Nothing could report it.** The swap runs in the *staged* process, launched with `--apply-update`,
and by then the process the user was looking at has already exited on purpose — it has to, or its
own files would be locked. There is no window left to report into, no status strip, and this app
writes no log. The failure path did exactly what it was designed to do, correctly, and had nowhere
to say so.

The second problem is the serious one. A tool whose job is protecting your settings **did nothing,
successfully, and told nobody.**

## The plausible explanation, and why it is wrong

**"The download or the extract failed."** It is the natural read — the app came back unchanged, so
presumably it never got the new version. Wrong, and checking is what points at the real cause: the
`.staged` directory is right there beside the install, complete, and its executable reports the new
version. Everything up to the last step worked.

**"Then the swap logic must be broken."** Also no. Reproducing both renames by hand, with nothing
running, succeeds instantly. The logic is right; it was simply not *patient*, and a race you lose
one time in five looks identical to a bug you have every time when there is no way to tell them
apart.

## Fix

**Retry the renames.** Ten attempts, 250ms apart — two and a half seconds, which costs a user who
is already restarting their app nothing:

```csharp
private static bool TryMove(string from, string to)
{
    for (var attempt = 1; ; attempt++)
    {
        try { Directory.Move(from, to); return true; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            if (attempt >= SwapAttempts) return false;
            Thread.Sleep(delay);
        }
    }
}
```

**Leave a breadcrumb when it still fails.** Beside `settings.json` — never in the install
directory, which is the thing being renamed — in the same shape as the crash report §8.1 writes.
The next launch reads it once, deletes it, and says so on the status strip and as a notification.

Recording is best-effort by design: it runs while something is already going wrong, and a second
failure there must not turn a failed update into a crash.

## How to avoid it

The retry is testable and tested. The lesson that generalises is the reporting one:

**When a code path's whole purpose is to run after the UI is gone, ask where its failure lands
before writing the failure handling.** This one had thorough, careful error handling — roll back,
put the old install back, return false, relaunch either way — and every bit of it was invisible.
The handling was not missing; the *destination* was.

`UpdateSwapFailureTests` covers the breadcrumb surviving the process that wrote it, being news
exactly once, and an unwritable state directory not becoming a second failure.

## References

- [[ADR-012]] — check-only updates with a staged swap
- [[ADR-018]] — where the app says things about updates
- [[every-update-fails-its-checksum]] — the failure that was masking this one
- `src/WaveLinkBackup.App/Updates/UpdateInstaller.cs`
