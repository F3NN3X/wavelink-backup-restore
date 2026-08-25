---
title: "Restore a settings file safely"
status: published
created: 2026-08-16
updated: 2026-08-16
related_adrs: [ADR-002, ADR-003]
tags: [recipe, restore]
---

# Restore a settings file safely

**When:** every restore, whether performed by the app, the CLI, or by hand during recovery.

**Prerequisites:** a candidate snapshot, and write access to `LocalState`. Tiers 1, 3 need no
elevation; **tier 4 does**, because `C:\Program Files\Common Files\VST3` is not user-writable.
Prompt for it only if tier 4 is actually being restored.

> **Copying a file back is the part that looks obvious and fails.** Every step below is in its
> position for a reason, and the reasons are attached. This is the one procedure in the
> project where reordering for convenience reliably breaks something.

---

## Steps

### 1. Validate the source before touching anything

Check the candidate parses, that `MixerConfiguration.InputSettings` is an object, and, the
one that matters, that it has **no case-insensitively duplicated keys**, using a
`JsonDocument` tree walk.

> **Why first:** restoring a file the app will reject looks *identical* to the snapshot being
> broken. You lose a full restore cycle, and the current config, distinguishing them. Validate
> while everything is still safely running.

Do not use `ConvertFrom-Json` for this. It silently collapses duplicates. See
[[file-parses-but-wave-link-resets]].

### 2. Check the health fingerprint against the current config

Compare `inputCount` and `inputNames` from the snapshot's manifest against what is live now.
A restore that drops five inputs to two is probably a mistake, and the user should see it
**before** pressing the button, not discover it after.

> **Why here:** this is information for the confirmation dialog, so it must be gathered before
> anything is closed. The now-vs-after table exists for exactly this.

A collapsed candidate is not automatically wrong, sometimes it is the only snapshot there is.
Warn; do not block.

### 3. Take a pre-restore snapshot

Capture the current configuration, `trigger: preRestore`, named `Before restore`.

> **Why before closing:** you want the state as it is now, and you want it even though it is
> the bad one. It is both the rollback and the evidence. **Automatic, never a checkbox**
> ([[ADR-003]]): it is what makes the destructive button safe to press, and a user in a hurry
> is exactly the user who would skip it.

### 4. Close both processes

`Elgato.WaveLink` **and** `WavelinkSEService`. Gracefully, with a 10-second timeout, then a
force-kill of the tree only on timeout.

> **Why graceful first:** it lets the app checkpoint cleanly. An unconditional kill risks
> leaving other state inconsistent, and buys nothing. See step 5 for what actually protects
> the write.

`WavelinkSEService` is the one people forget. Two processes.

### 5. Assert the processes are gone

Re-check `IsRunning`. If either is still up, **abort**. Do not write.

> **Why this is the load-bearing step:** a graceful exit **flushes in-memory config on the way
> out**. Harmless if that happens before your write; fatal if it races it, and the failure is
> invisible, your write succeeds, verifies, and is silently overwritten seconds later.
> See [[restored-settings-revert-seconds-later]].
>
> **The invariant is exit, not kill method.** A fixed `Sleep` is not a substitute: on a loaded
> machine the exit takes longer, and the sleep fails precisely under the conditions that cause
> the problem.

### 6. Write atomically

Write to a temp file **in the same directory**, then:

```csharp
File.Replace(tempPath, settingsPath, rollbackPath);
```

> **Why not `WriteAllBytes`:** `File.Replace` is atomic on NTFS and produces the rollback copy
> in the same operation. There is no window in which `Settings.json` is half-written, which
> matters, because a half-written file is the one state from which nothing recovers cleanly.
>
> **Why the same directory:** `File.Replace` requires source and destination on the same
> volume. A temp file in `%TEMP%` may not be.

Copy bytes. Do not re-serialize. See [[every-snapshot-differs-with-no-real-change]].

### 7. Relaunch via the shell AppID

```powershell
Start-Process "shell:AppsFolder\Elgato.WaveLink_g54w8ztgkx496!App"
```

> **Why not the `.exe` path:** MSIX apps cannot be started from it. This is not a preference;
> launching by path does not work.

### 8. Verify from the log, not the UI

```powershell
$log = Get-ChildItem "$ls\Logs" -File | Sort-Object LastWriteTime -Desc | Select-Object -First 1
Select-String $log.FullName -Pattern 'Failed to parse|Created a new backup file|Applied saved'
```

Success is the **absence** of `Failed to parse settings file` plus the presence of
`Applied saved friendly name 'Wave Mic 1'`.

> **Why not the UI:** a mixer that looks correct can be a **freshly generated default**. Five
> plausible channel names are not evidence of anything. The log is the only place that
> distinguishes "restored your config" from "rejected your config and made a new one".

---

## Verifying it worked

Three things, in order of trustworthiness:

1. **The log**, no parse failure, and `Applied saved friendly name` for a name you recognise.
2. **The input count and names** match the snapshot's manifest.
3. **The file size** on disk is close to the snapshot's. A file that has become ~11 KB has been
   rejected and regenerated, whatever the window shows.

If Wave Link wrote a *new* backup of its own immediately after launch. That is worth noticing,
it is the signature of a reset ([[newest-backup-is-the-broken-one]]).

## If it goes wrong

The pre-restore snapshot from step 3 is a complete, valid snapshot. Restore it by running this
same procedure with it as the source. `File.Replace`'s `rollbackPath` from step 6 is a second
copy of the same thing, at file level.

**If the config was rejected**, do not immediately retry with the next-newest snapshot. Rank
by content first, the newest is very often the post-reset default
([[newest-backup-is-the-broken-one]]).

**If the restore fails after a Wave Link update**, check `waveLinkVersion` in the manifest
against what is installed. 3.3.0.4108 Beta rejected a file 3.2.9 accepted; beta channels ship
new validators, and the first question when a restore fails is whether the config is bad or
the validator changed.

## References

- `SPEC.md` §4, §5
- [[ADR-002]] · [[ADR-003]]
- [[restored-settings-revert-seconds-later]] · [[file-parses-but-wave-link-resets]] ·
  [[newest-backup-is-the-broken-one]]
