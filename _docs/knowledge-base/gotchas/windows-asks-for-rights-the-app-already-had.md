---
title: "Windows asks for rights the app already had"
status: published
created: 2026-08-20
updated: 2026-08-20
related_adrs: [ADR-011]
tags: [gotcha, restore, security, windows]
---

# Windows asks for rights the app already had

**Provenance:** *Observed*, 2026-08-20, on the reference rig, and it had been shipping since
phase 6. Found by asking "does this actually need administrator?" rather than by anything failing.

## Symptom

Restoring the plug-in files raises a UAC prompt every time. It looks correct, the row in the
restore dialog says *"Windows will ask for administrator rights, because the effect plug-ins live
in a folder every account shares"*, so nobody questions it.

Then somebody notices that copying a `.vst3` into that same folder from Explorer, as the same
user, with no prompt, **works**.

## Cause

The app decided it needed elevation by looking at the *path*: plug-ins live under
`C:\Program Files\Common Files\VST3`, `Program Files` needs administrator, therefore prompt.

But that folder's ACL is not what the path implies:

```
C:\Program Files\Common Files\VST3 Everyone:(OI)(CI)(F)
                                   ...
                                   BUILTIN\Users:(I)(RX)
```

`Everyone:(OI)(CI)(F)`, **full control, for everyone, inherited to everything inside**. The
`Users:(I)(RX)` beneath it is the inherited Windows default; something was added *above* it.

That something is an audio plug-in installer. Several set this deliberately so their own updates
run without a prompt, which means the assumption is wrong on a large share of the machines this
app is for, precisely because they are machines with plug-ins installed on them.

## The plausible explanation, and why it is wrong

**"`Program Files` always needs administrator."** It is true of the *default* ACL, true of almost
everything else under that tree, and it is the reason nobody looks. It is a fact about how Windows
ships, not a fact about the folder in front of you, and any installer running elevated can change
it.

The second trap is the fix that suggests itself: **read the ACL and work out whether we can
write.** That means resolving group membership, inherited allows and denies in the right order,
UAC's filtered token (an administrator's non-elevated process does *not* carry the Administrators
SID), and the odd virtualisation case. Every one of those is a chance to be subtly wrong, and being
wrong here means either a prompt nobody needed or a restore that silently writes nothing.

**Ask the filesystem instead of reasoning about it.** Create a file in the target directory. If it
works, you can write there.

## Fix

`IFileSystem.CanWriteDirectory` opens a uniquely-named file with `FileMode.CreateNew` and
`FileOptions.DeleteOnClose`, and reports whether that succeeded:

- **`DeleteOnClose`** so an interrupted probe leaves nothing behind, a stray file in somebody's
  VST3 folder would be this program littering in the one place it is trying to be careful about.
- **A GUID in the name** so two probes cannot collide.
- **A missing directory answers for its nearest existing ancestor**, because "could this be
  created?" is the real question for a bundle's own folder.

`RestoreOrchestrator.Plan` probes each captured plug-in's folder and reports
`PluginBinaryPayload.NeedsElevation`; the window elevates only on that, and the dialog's copy
follows the measurement rather than promising a prompt unconditionally.

## How to avoid it

**A permission is a property of a resource, not of its path.** Any time code decides what it is
allowed to do by pattern-matching a location, it has encoded an assumption about somebody else's
machine. The same instinct is what [technical-debt.md](../../technical-debt.md) §5 is a whole
section about, and what §4.18 cost a phase, there the guess was where files *are*, here it is what
may be *done* to them.

**Prompting for rights you do not need is not a safe default.** It is the one that looks safe. A
prompt that appears when it need not is how people learn to click through the ones that matter.

## References

- [[ADR-011]]: why elevation is a second process, and when it is genuinely required
- [audits/2026-08-20-plugin-resolution-and-elevation.md](../../audits/2026-08-20-plugin-resolution-and-elevation.md),
the measurement, with the commands to re-run it
- [technical-debt.md](../../technical-debt.md) §7.5, the fix, §5, the same instinct as a rule
- `tests/WaveLinkBackup.Core.Tests/FileSystemTests.cs`, the probe, including that it leaves nothing behind
