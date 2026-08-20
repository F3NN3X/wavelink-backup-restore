---
title: "Session: asking for administrator rights only when the write needs them"
status: published
created: 2026-08-20
updated: 2026-08-20
related_adrs: [ADR-011, ADR-012]
tags: [session, restore, security, plugins]
---

# Session: asking for administrator rights only when the write needs them

**Date:** 2026-08-20 · Follows [clearing the technical-debt list](2026-08-20-clearing-the-technical-debt.md)

## Goal

A question, not a task: *would a confirmation dialog be enough for a tier 4 restore, instead of
elevating? Not everyone backs up plug-ins, so a confirmation only when they have — is that not
enough?*

## What happened

### The question contained two questions

**The confirmation already existed**, with exactly the rule being asked for.
`RestoreDialogModel.Build` builds the plug-in row only when `plan.BinaryPayload.Any`, so a snapshot
with no plug-in binaries never shows it — and tier 4 is off by default, so that is the common case.

**But a confirmation cannot stand in for elevation**, because they answer different questions. The
confirmation is *"do you want this?"*, which the app can ask. Elevation is *"may this process write
to that folder?"*, which only Windows can answer. Clicking OK in our dialog does not change an ACL.
[[ADR-011]] had already considered and rejected exactly this, under *"surface the opt-in and never
elevate"*: **a control that reports its own failure is not a feature.**

So the answer to the question as asked was no. The interesting part was underneath it.

### The app was elevating unconditionally, and it did not need to

`MainWindow.RestoreSelectedAsync` elevated whenever the opt-in was on, without ever asking whether
the write required it. And the decision was an inference from the path: plug-ins live under
`Program Files`, therefore administrator.

That inference is **wrong on this machine**:

```
Elevated: False
C:\Program Files\Common Files\VST3 Everyone:(OI)(CI)(F)
```

An explicit ACE granting Everyone full control, above the inherited `Users:(RX)`. Not the Windows
default — an audio plug-in installer put it there so its own updates need no prompt, which several
of them do. **Every UAC prompt this app has ever shown for a tier 4 restore, here, was
unnecessary.**

### Measured, not reasoned about

The tempting fix is to read the ACL and compute whether we can write. That means group membership,
inherited allows and denies in order, UAC's filtered token, virtualisation — every step a chance to
be subtly wrong, and wrong here means a needless prompt or a restore that silently writes nothing.

`IFileSystem.CanWriteDirectory` creates a uniquely-named `DeleteOnClose` file instead. It asks the
filesystem the question actually being asked. A missing directory answers for its nearest existing
ancestor, which is the bundle case.

Verified against the real seam before trusting it:

```
WRITABLE      C:\Program Files\Common Files\VST3
WRITABLE      C:\Program Files\Common Files\VST3\FabFilter
NOT writable  C:\Windows\System32
```

`System32` is the control — a probe that said "writable" to everything would be worthless.

### A second defect fell out of the first

`IRestoreService.RestoreAsync` had **no options parameter**, so tier 4 was reachable *only* through
the elevated copy. Not elevating would have restored nothing. Both halves had to change together,
and a source guard now pins that the unelevated path carries `PluginBinaries: wantsPlugins`.

### The larger question, deliberately left open

Wave Link scans configurable folders, and the user-level VST3 location needs no administrator — so
could an unwritable destination fall back to it? That turns on whether Wave Link resolves a
channel's plug-in by `PluginId` or by `FilePath`.

**What the settings and the cache show:** Wave Link is JUCE-based; every third-party `PluginId`
matches a cache `uniqueId` exactly, so a path-independent identity **exists**; the only
configurable scan folder is `VST2PluginDirectoryPath` and it is empty; all 154 cached plug-ins are
VST3 in the shared folder.

**Why that settles nothing.** Every recorded path agrees with the cache, because nothing has moved.
The data cannot distinguish the two strategies. Writing the fallback on the strength of "JUCE
searches both locations by default" would be [[vst3-backs-up-as-nothing]] and §4.18 again — a
heuristic that looks right and captures the wrong thing.

Written up as an audit with the commands and a reversible experiment, and as
[technical-debt.md](../technical-debt.md) §7.6.

## Decisions made

| Decision | Why |
|---|---|
| **Probe, do not infer** | A permission is a property of a resource, not of its path. |
| **Probe by writing, not by reading the ACL** | Effective permissions need group membership, inherited denies and UAC's filtered token to be right; a temp file needs none of them. |
| **Any unwritable destination needs the prompt** | Elevation is one prompt for the whole restore — there is no partial answer that helps. |
| **A plug-in with no recorded path counts as needing it** | The cost of a wrong *yes* is a prompt; the cost of a wrong *no* is a restore that silently puts nothing back. |
| **The row's copy follows the measurement** | A dialog that promises administrator rights and produces none is lying about its own button, on the app's one irreversible screen. |
| **Do not build the user-folder fallback yet** | §7.5 already removes the prompt in the common case. The remaining prompt is one, on an opt-in, for a shared system folder — which is what UAC is for. |

**And one correction to a document written earlier the same day.** [[ADR-012]] deferred MSIX for
lack of a signing certificate. [post-1.0.md](../dev-phases/post-1.0.md) had **already refused it for
a better reason**: an MSIX package writes into a redirected `LocalState` that an uninstall or reset
deletes wholesale — the exact defect [[ADR-003]] exists because upstream had. The ADR now points at
that and says a revisit needs an answer to the store-location problem first, not a certificate.

## What did not work

**Guessing the Lucide-style shortcut again.** The first instinct on the larger question was "JUCE
searches both VST3 locations, so the fallback will work". True and irrelevant: scanning a folder is
not the same as resolving a reference, and this project has already paid twice for that shape of
inference.

## Open questions

**§7.6** — the plug-in resolution experiment. Reversible, ~10 minutes, needs a live Wave Link.
Protocol and outcome table in
[the audit](../audits/2026-08-20-plugin-resolution-and-elevation.md).

**The elevated path is now hard to reach on this machine.** It is still correct and still tested
against a fake, but it cannot be exercised here for real without artificially tightening an ACL.
Worth remembering before assuming it still works end to end.

## Next

The §7.6 experiment, whenever there is an appetite for restarting Wave Link a couple of times.

## References

- [audits/2026-08-20-plugin-resolution-and-elevation.md](../audits/2026-08-20-plugin-resolution-and-elevation.md)
- [[windows-asks-for-rights-the-app-already-had]]
- [[ADR-011]] · [[ADR-012]]
- [technical-debt.md](../technical-debt.md) §7.5, §7.6
