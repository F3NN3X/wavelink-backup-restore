---
title: "Decisions as pure functions"
status: published
created: 2026-08-20
updated: 2026-08-20
related_adrs: [ADR-004, ADR-007]
tags: [pattern, architecture, testing]
---

# Decisions as pure functions

## Problem

Some of this app's decisions are consequential and **conditional**: which tray icon to show, whether
to take an automatic backup, whether to warn that nothing has been backed up for nine days, whether
a plug-in binary still needs hashing. Each depends on several facts, and each is wrong in a way
nobody notices — a tray icon that never turns amber, a nine-day warning that fires daily and gets
muted, a cache that returns a stale hash.

Written inline at the call site, such a decision becomes *whatever the code path that reached it
happened to do*. It cannot be listed, so it cannot be reviewed against the design; and it can only
be tested by standing up the thing that calls it, which for a tray icon means a shell and for a
watcher means a filesystem and a clock.

The failure mode is specific: the rule is never wrong in an obvious way, it is wrong **in one
branch nobody exercised**.

## Solution

Put the decision in its own type, with no dependencies and no IO, taking a value in and returning a
value out. The caller does the acting.

```csharp
/// <summary>
/// The tray's entire behaviour, as a pure function. Deliberately not a stored field that
/// something has to remember to update: a derived state cannot go stale.
/// </summary>
public static class TrayState
{
    public static TrayStatus From(TrayConditions conditions)
    {
        // Amber outranks everything. Something the user must act on must not be hidden by a
        // quieter state that also happens to be true.
        if (conditions.LastError is not null) return TrayStatus.NeedsYou;

        if (conditions.IsCapturing) return TrayStatus.BackingUp;
        if (conditions.IsPaused || !conditions.AutoBackupEnabled) return TrayStatus.Paused;

        return TrayStatus.Watching;
    }
}
```

The precedence — *amber outranks everything* — is now a thing you can read in one place and assert
from a table, rather than an emergent property of four `if`s spread across a refresh method.

**Where the decision needs memory, the type holds it and nothing else does.**
`TrayNotifications.NothingBackedUp` fires once per *episode*: it remembers that it fired, and
re-arms when a backup happens. That "once" is the whole difference between a warning and a nag, and
it lives beside the rule it qualifies rather than in a flag on the App.

## Callers

| Where | Why it uses this |
|---|---|
| `Hosting/TrayState.cs:From` | Four icon states from four conditions, with an explicit precedence. Called on every host tick. |
| `Hosting/TrayNotifications.cs:NothingBackedUp` | The nine-day notice, and its once-per-episode rule. **Cannot** produce a success notification, because it takes no success as an input — the design's "a successful backup never notifies" is enforced by the signature. |
| `Core/Automation/AutoBackupPolicy.cs` | Whether a change earns a capture, from the user's interval and daily settings. |
| `Snapshots/PluginManifestEntry.BinaryMatches` | Whether a recorded hash is still current. Says yes only when a hash, a size and a write time all agree — conservative in one place rather than at each call site. |
| `ViewModels/UpdateViewModel.ShouldAutoCheck` | Whether the weekly update check is due. |

## Held down by

- `tests/WaveLinkBackup.App.Tests/TrayStateTests.cs` — every condition combination, as a table.
- `tests/WaveLinkBackup.App.Tests/TrayNotificationTests.cs::A_backup_re_arms_the_notice_for_the_next_time_it_goes_quiet`
  — the episode rule, which is the part a caller would get wrong.
- `tests/WaveLinkBackup.Core.Tests/AutoBackupPolicyTests.cs` — the whole suite runs in about a
  second because none of it needs a watcher.
- `tests/WaveLinkBackup.Core.Tests/TierCaptureTests.cs::A_binary_that_changed_length_is_rehashed_even_at_the_same_write_time`
  — the cache-invalidation edge, expressible only because the rule is separable.

## When not to use it

**When the decision has one caller and no precedence.** A single `if` extracted into a static class
is indirection with a ceremony attached; it makes the code longer and the rule no clearer.

**When the decision genuinely needs the thing it decides about.** `SnapshotGuard` verifies a
snapshot against the disk — it is a decision, but hashing files *is* the decision, and a pure
version would just be a function taking the answer as an argument.

The test is whether you can write the truth table. If you can, that table wants to be a test, and
this shape is what lets it be one. If you cannot, the decision is entangled with an effect and
extracting it will move the entanglement rather than remove it.

## References

- [[ADR-004]] — Core is a library with two seams; this is the same instinct applied to shell code
- [[pure-analysis-core]] — the architectural rule; this is the behavioural one
- [[ADR-007]] — dedup and watching, where `AutoBackupPolicy` came from
- [technical-debt.md](../../technical-debt.md) §7.3 — a tray state that was unreachable until the
  precedence was written down
