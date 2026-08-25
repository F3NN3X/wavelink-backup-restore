---
title: "The plugin is restored but demands a licence"
status: published
created: 2026-08-16
updated: 2026-08-16
related_adrs: [ADR-006]
tags: [gotcha, vst3, expectations]
---

# The plugin is restored but demands a licence

**Provenance:** **Inspected, not reproduced.** Vendor folders examined on the reference
machine 2026-08-15: `%APPDATA%\FabFilter` holds 246 files, all presets;
`%APPDATA%\Supertone\Clear` holds only crash reports. **No licence material exists in what
tier 3 copies**, that absence is measured. The user-facing failure has not been staged.

## Symptom

A tier 4 restore onto a rebuilt machine puts every `.vst3` back where it belongs. Wave Link
finds them, the effect chains load, the channel names are right.

Every third-party plugin opens in demo mode, or refuses to process audio, or shows a
nag screen. The backup was "complete" and the rig still does not work.

## Cause

**Copying a plugin restores the code, not the authorisation.** Those are separate things
stored in separate places, and nothing licence-shaped lives in the folders this app copies.

Vendors authorise via one of:

- the registry,
- machine-bound licence files elsewhere on disk,
- an online account check.

All three are outside `%APPDATA%\<Vendor>\<Plugin>\` (tier 3) and outside the `.vst3` itself
(tier 4). Several are deliberately machine-bound, copying them to another machine would not
work even if we found them, because *not being copyable* is the entire feature.

## The plausible explanation, and why it is wrong

> *"The licence file must be somewhere in the vendor's `%APPDATA%` folder, we just missed
> it. Widen the tier 3 glob."*

It is not there. That was checked, folder by folder, and the check is the provenance line at
the top of this document. Widening the glob copies more presets and finds nothing new, while
making tier 3 larger and slower.

The second, more damaging wrong turn is a design one:

> *"We should back up licences too, so a restore is genuinely complete."*

This is a request to defeat machine-binding, and the honest answer is that it cannot be done
and should not be attempted. A tool that appeared to succeed at it would be worse than one
that declines, it would produce a rebuild that works until it silently does not.

## Fix

There is no technical fix. The fix is **the UI saying so, plainly, before the user relies on
it**. The Settings dialog's note is not decorative copy:

> **Licences are never included.** Backing up a plug-in doesn't back up your right to run it.
> On a new machine you'll install and re-authorise those plug-ins yourself, then restore.

Scope tier 4 honestly in every place it is described: it gets a **working plugin on the same
machine**, after an uninstall, a bad update, a vendor pulling a version. On a rebuild the
user reinstalls and re-authorises regardless, and then restores.

**Tier 2 is what actually helps on a rebuild**, and it costs 4 KB. Name, vendor, version and
uniqueId per referenced plugin turns *"my effects are gone and I don't know why"* into
*"install FabFilter Pro-Q 4 v4.x, it's missing"*. That is why tier 2 is always on and not
switchable ([[ADR-006]]).

## How to avoid it

- **Never describe a snapshot as "complete" or "everything".** It is a settings backup with
  optional plugin capture. Copy that overclaims here is a support burden with a delay fuse.
- **Keep the licence note in Settings** even when it looks like clutter. It appears at the
  moment the user is deciding whether to enable tier 4, the only moment they will read it.
- **Do not add a "back up licences" tier**, and record here why, so the idea does not return
  as a feature request that looks reasonable.

## References

- `SPEC.md` §9
- [README.md](../../operations/design/README.md). Screen 3, plain-language notes
- [[ADR-006]] · [[vst3-backs-up-as-nothing]] · [[restored-backup-has-dead-channels]]
