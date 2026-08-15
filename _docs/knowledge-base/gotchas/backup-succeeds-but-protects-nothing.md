---
title: "The backup runs, reports success, and protects nothing"
status: published
created: 2026-08-16
updated: 2026-08-16
related_adrs: [ADR-003]
tags: [gotcha, discovery, paths]
---

# The backup runs, reports success, and protects nothing

**Provenance:** **Observed.** `%APPDATA%\Elgato\WaveLink` inspected on the reference machine
2026-08-15 — present, populated, and nine months stale (newest file 2025-11-17). The failure
mode is the one this folder's existence causes, not one seen in the wild yet, because the
discovery routine we inherited already avoids it.

## Symptom

The tool finds a Wave Link settings folder. It reads a `Settings.json`. It writes a snapshot,
reports success, and shows a plausible size. Everything looks correct.

Then a restore puts back a configuration from months ago — or the snapshot's health
fingerprint never changes no matter what the user does in the mixer, because the file it is
watching is never written.

## Cause

There are two Wave Link settings locations, and the obvious one is dead.

```
%APPDATA%\Elgato\WaveLink                                    ← the decoy. Dead.
%LOCALAPPDATA%\Packages\Elgato.WaveLink_g54w8ztgkx496\LocalState   ← the real one.
```

Wave Link is an MSIX/Store package, so its writes are **redirected** into the package's
`LocalState`. The `%APPDATA%` folder is a leftover: it exists, it is populated, it looks
exactly like where settings belong on Windows, and nothing has written to it in nine months.

## The plausible explanation, and why it is wrong

> *"`%APPDATA%\Vendor\Product` is where Windows apps keep settings. It's there, it has a
> settings file in it, so that's the one."*

That reasoning is correct for conventional Win32 apps — and Elgato ships several. Stream Deck,
Camera Hub, Volume Controller, Audio Plugins, VSTs and the Discord plugin all live under
`%APPDATA%\Elgato\` and for **those**, `%APPDATA%` is genuinely the real path.

So the rule is not "Elgato uses `%LOCALAPPDATA%`". It is: **Wave Link is MSIX, therefore
redirected; its siblings are not.** Do not carry the MSIX assumption across to them, and do
not carry the `%APPDATA%` assumption to Wave Link.

The second wrong turn is subtler: finding the decoy does not *fail*. There is a real
`Settings.json` there. It parses. It has plausible content. Every check short of "is this file
being written to" passes.

## Fix

**Resolve by package family name, never by vendor folder.**

```csharp
// Glob — the family suffix is stable per Store identity, but never assume it.
var candidates = Directory.EnumerateDirectories(
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                     "Packages"),
        "Elgato.WaveLink_*")
    .Select(d => Path.Combine(d, "LocalState", "Settings.json"))
    .Where(File.Exists)
    .ToList();
```

Three properties matter and all three are load-bearing:

1. **Glob `Elgato.WaveLink_*`**, do not hard-code `Elgato.WaveLink_g54w8ztgkx496`.
2. **Require `Settings.json` to exist** — that is what disqualifies a stale or partial package
   directory.
3. **Refuse to guess** when more than one candidate matches. Demand an explicit
   `--settings-path` (CLI) or "Choose the settings file…" (GUI) instead of picking the first.

Use `Environment.GetFolderPath`, never a composed `%LOCALAPPDATA%` string — that path is
redirected on some corporate and OneDrive setups.

## How to avoid it

- **Never construct the settings path from a vendor name.** If a code path contains the string
  `Elgato\WaveLink` under `%APPDATA%`, it is wrong by construction.
- **A test that asserts discovery ignores a populated `%APPDATA%\Elgato\WaveLink` fixture.**
  This is the one that catches a well-meaning "add a fallback location" change six months from
  now — which is exactly how this comes back.
- **Surface the resolved path in the UI.** The empty state already prints it. A user staring
  at the wrong path is the cheapest possible detection.

Upstream's `SettingsDiscovery` gets all of this right and is one of the main reasons to fork
rather than rewrite ([[ADR-002]]).

## References

- `SPEC.md` §1, §6, §11
- [[ADR-002]] · [[ADR-003]] · [[ADR-008]]
- [glossary.md](../../glossary.md) — *the decoy*, *LocalState*, *package family name*
