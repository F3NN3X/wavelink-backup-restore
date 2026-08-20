---
title: "A progress report never arrives in a test"
status: published
created: 2026-08-20
updated: 2026-08-20
tags: [gotcha, testing, async, core]
---

# A progress report never arrives in a test

**Provenance:** *Observed*, 2026-08-20, adding byte-level progress to `SnapshotStore.Write` for the
backing-up strip ([technical-debt.md](../../technical-debt.md) §4.21 item 2).

## Symptom

The production code reports progress correctly. The test collects nothing:

```csharp
var reports = new List<SnapshotWriteProgress>();
store.Write(bytes, analysis, trigger, "x", payload: payload,
            progress: new Progress<SnapshotWriteProgress>(reports.Add));

Assert.NotEmpty(reports);   // Collection was empty
```

`Write` is **synchronous** and has returned. Every report was made. The list is empty.

## Cause

`Progress<T>` does not invoke its callback where `Report` was called. It captures the
`SynchronizationContext` at construction and **posts** to it.

In WPF that context is the dispatcher, which is the whole point — a background capture can report
straight into a binding. In a test there is no context, so `Progress<T>` falls back to the thread
pool: the callbacks are queued, the assertion runs, and the queued work lands afterwards on some
other thread.

It is a race the test loses deterministically, which is at least honest — an intermittent version
of this would be far worse.

## The plausible explanation, and why it is wrong

The obvious read is **"the reports are not being made"**, and the obvious next move is to go
looking in `Write` for a missing `progress?.Report(...)`. That search finds nothing, because the
calls are there and correct.

The second guess is that the synchronous method needs awaiting somehow, or that the test needs to
be `async`. It does not — `Write` really has finished. Making the test `async` and awaiting
something arbitrary sometimes makes it pass, which is worse than failing: it looks fixed while
staying a race.

The tell is that **the production behaviour is right**. When a seam works in the app and not in the
test, suspect the test's *environment* before the seam.

## Fix

Collect on the calling thread. `IProgress<T>` is an interface, and nothing requires the
`Progress<T>` implementation:

```csharp
private sealed class Reports : IProgress<SnapshotWriteProgress>, IEnumerable<SnapshotWriteProgress>
{
    private readonly List<SnapshotWriteProgress> reports = [];

    public SnapshotWriteProgress this[Index index] => reports[index];

    public void Report(SnapshotWriteProgress value) => reports.Add(value);
    // ...
}
```

Production keeps `Progress<T>` and keeps its marshalling.

## How to avoid it

**Take `IProgress<T>`, never `Progress<T>`, at a seam.** Core does, which is what made the fix a
test-only change. A parameter typed as the concrete class would have forced the marshalling on
every caller and made this unfixable without touching production code.

The same reasoning is why `SnapshotListViewModel` has a `Marshal` property the tests set to run
inline rather than depending on a dispatcher.

## References

- [technical-debt.md](../../technical-debt.md) §4.21 item 2 — the backing-up strip this was for
- `tests/WaveLinkBackup.Core.Tests/SnapshotStoreTests.cs` — the `Reports` collector
- `src/WaveLinkBackup.Core/Snapshots/SnapshotStore.cs`
