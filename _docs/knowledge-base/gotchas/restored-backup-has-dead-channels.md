---
title: "Someone else's backup restores into dead channels"
status: published
created: 2026-08-16
updated: 2026-08-16
related_adrs: [ADR-006, ADR-008]
tags: [gotcha, portability, privacy]
---

# Someone else's backup restores into dead channels

**Provenance:** **Read, not reproduced.** Derived from the `Settings.json` schema as inspected
2026-08-15 — `InputSettings` keyed by Core Audio endpoint IDs, plugin `FilePath`s absolute.
No cross-machine restore has been attempted.

## Symptom

A user copies a snapshot from one machine to another — a rebuild, a second PC, or a friend's
"here's my mic chain, try it". The restore reports success.

The mixer shows the right *channel names* and the right *effect chains*, and no audio. Inputs
are present but dead. Effects show as missing even though the plugins are installed.

## Cause

**A snapshot describes one specific computer**, in two independent ways.

**Endpoint IDs.** `InputSettings` is keyed by Core Audio endpoint ID:

```
BS33J1A05009\PCM_IN_01_C_00_SD1  =>  Wave Mic 1
```

`BS33J1A05009` is a **hardware serial number**. That key does not exist on any other machine,
so the entry describes a device that is not there. The friendly name survives because it is a
value inside the entry; the binding does not, because it is the key.

**Absolute plugin paths.** Every `FilePath` in `AudioPluginConfigurations` is absolute, and
`C:\Program Files\Common Files\VST3` is a *default*, not a location. A user who installs
plugins elsewhere gets no matches even with every plugin installed.

The result looks like a partial success, which is worse than a clean failure — the user sees
their channel names and reasonably concludes the rest is a bug.

## The plausible explanation, and why it is wrong

> *"The restore was incomplete, or the file got corrupted in transit."*

It restored perfectly. Every byte arrived. The file is describing hardware that is not
present, and doing so correctly.

The design temptation is the real trap:

> *"We should remap device IDs on restore so backups become portable."*

This is a **different feature**, and calling it a bugfix is how it gets built badly. Endpoint
IDs are **foreign keys, not labels** — the ID is referenced elsewhere in the document both as
a bare string and as a composite `<deviceId>|<suffix>`. Anything that rewrites one must walk
the entire tree, rewrite both forms, and handle the destination key already existing.

That is why the config is never modelled as a flat list of channels, even though pure
backup/restore never needs the distinction — it moves whole files. The moment "repair a dead
input by pointing it at a new device" is wanted, the model has to already be right.

If portability is genuinely wanted, the feature is **"export a chain"**, built on
`AudioPluginConfigurations` alone — the effect chain without the device binding. That is a
separate feature with a separate design.

## Fix

**Label snapshots machine-local in the UI.** The Settings dialog note is required copy:

> **A backup describes this computer.** It names the audio devices plugged into this machine,
> so restoring it somewhere else won't line up with that machine's inputs.

**Resolve plugins from `FilePath` first**, with standard directories as fallback only, and use
tier 2's manifest to report what did not resolve — name, vendor and version, so the user knows
what to install ([[ADR-006]]).

**Surface the input drop in the restore dialog.** The now-vs-after table already does this:
any value that changes is emphasised and marked. A restore that would drop five inputs to two
should be visible *before* the button is pressed, not discovered after.

## The privacy consequence, which is the same fact wearing a different hat

That endpoint ID contains a **hardware serial number**. Absolute paths contain the **Windows
username**. And users *will* attach snapshots to bug reports — they will not think about it,
and by then it is in a public issue tracker.

Owed before the repo goes public: a **"copy diagnostics" action that redacts both**, and
nothing auto-uploaded, ever. Tracked in [technical-debt.md](../../technical-debt.md) §6.
The `.gitignore` already refuses real settings files, which protects the repo and not the
issue tracker.

## How to avoid it

- **Say "machine-local" in the UI, the README and the release notes.** Users share configs;
  that is normal and good behaviour in audio communities. The expectation has to be set before
  they try.
- **Never add device remapping as a quiet enhancement to restore.** It is a feature with its
  own design, and a half-built version produces a config that is broken in new ways.
- **Redact before sharing, in the app, as a one-click action.** Anything requiring the user to
  redact by hand will not happen.

## References

- `SPEC.md` §3, §11
- [technical-debt.md](../../technical-debt.md) §3, §6
- [README.md](../../operations/design/README.md) — Screen 2, Screen 3 notes
- [[ADR-006]] · [[ADR-008]] · [[restored-plugin-demands-a-licence]]
- [glossary.md](../../glossary.md) — *endpoint ID*, *machine-local*
