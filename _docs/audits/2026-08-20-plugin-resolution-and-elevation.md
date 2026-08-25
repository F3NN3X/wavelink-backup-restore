---
title: "Audit: what a tier 4 restore actually needs, and how Wave Link finds a plug-in"
status: published
created: 2026-08-20
updated: 2026-08-25
related_adrs: [ADR-011, ADR-006]
tags: [audit, restore, plugins, security]
---

# Audit: what a tier 4 restore actually needs, and how Wave Link finds a plug-in

**Audited:** 2026-08-20 · **Subject:** the elevation decision in tier 4 restore, and the question it
raised about plug-in resolution
**Machine:** the reference rig (a personal Windows workstation; name and account redacted), Wave Link 3.3.0.4108, 154 VST3 plug-ins

**Verdict: the app was asking for administrator rights it already had. One question it raised is
still open, and this audit is the record a later session should start from.**

> **Provenance.** Everything in §1 and §2 was **measured on one machine** with the commands given
> below — re-run them before trusting any figure here on a different rig. §3 is **not measured**;
> it is the open question and its protocol. Nothing here was inferred from documentation.

---

## 1 · Method

All of it is re-runnable, and deliberately so: the findings contradict an assumption that had been
in the code since phase 6, so they should be cheap for the next person to verify rather than taken
on trust.

### 1.1 Is the shared VST3 folder actually unwritable?

```powershell
# Is this shell elevated? The answer only means something if it is not.
$id = [Security.Principal.WindowsIdentity]::GetCurrent()
(New-Object Security.Principal.WindowsPrincipal($id)).IsInRole(
    [Security.Principal.WindowsBuiltInRole]::Administrator)

icacls "C:\Program Files\Common Files\VST3"
```

### 1.2 What has Wave Link scanned, and in what format?

```python
# KnownPlugins.cache is a JUCE <KNOWNPLUGINS> list.
import collections, os, re
cache = os.path.expandvars(
    r'%LOCALAPPDATA%\Packages\Elgato.WaveLink_g54w8ztgkx496'
    r'\LocalState\AudioPluginCache\KnownPlugins.cache')
text = open(cache, encoding='utf-8-sig').read()

files = re.findall(r'file="([^"]+)"', text)
print(len(files), 'plug-ins')
print(collections.Counter(re.findall(r'format="([^"]+)"', text)))
print(collections.Counter(chr(92).join(f.split(chr(92))[:4]) for f in files))
```

### 1.3 Does a settings entry name a plug-in by path, or by identity?

```python
import json, os, re
base = os.path.expandvars(
    r'%LOCALAPPDATA%\Packages\Elgato.WaveLink_g54w8ztgkx496\LocalState')
settings = json.load(open(os.path.join(base, 'Settings.json'), encoding='utf-8-sig'))
cache = open(os.path.join(base, 'AudioPluginCache', 'KnownPlugins.cache'),
             encoding='utf-8-sig').read()

by_uid = dict(zip(re.findall(r'uniqueId="([^"]+)"', cache),
                  re.findall(r'file="([^"]+)"', cache)))

for channel in settings['MixerConfiguration']['InputSettings'].values():
    for plugin in (channel.get('AudioPluginConfigurations') or []):
        if not (plugin.get('FilePath') or ''):
            continue
        hit = by_uid.get(plugin['PluginId'])
        print(plugin['PluginId'], plugin['Name'],
              'in cache:', hit is not None,
              'path agrees:', (hit or '').lower() == plugin['FilePath'].lower())
```

### 1.4 Does the app's own probe agree with reality?

A throwaway console project referencing `WaveLinkBackup.Core`, calling
`new FileSystem().CanWriteDirectory(...)` against the shared VST3 folder, its `FabFilter`
subfolder, the user-level VST3 folder, `C:\Windows\System32` (the control) and `%TEMP%`.

---

## 2 · Findings

### 2.1 The shared VST3 folder is writable, and the app was prompting anyway — **fixed**

```
Elevated: False
User    : <redacted>

C:\Program Files\Common Files\VST3 Everyone:(OI)(CI)(F)
                                   NT SERVICE\TrustedInstaller:(I)(F)
                                   ...
                                   BUILTIN\Users:(I)(RX)
```

`Everyone:(OI)(CI)(F)` — **an explicit ACE granting Everyone full control**, inherited to files and
subfolders. It is not the Windows default; the inherited `BUILTIN\Users:(I)(RX)` beneath it *is*,
and something has been added above it. Several audio plug-in installers do this so their own
updates need no administrator.

The app's probe agrees:

```
WRITABLE      C:\Program Files\Common Files\VST3
WRITABLE      C:\Program Files\Common Files\VST3\FabFilter
WRITABLE      C:\Users\<user>\AppData\Local\Programs\Common\VST3
NOT writable  C:\Windows\System32
WRITABLE      C:\Users\<user>\AppData\Local\Temp\
```

`System32` is the control, and it discriminates — a probe that answered "writable" to everything
would be worthless.

**Consequence:** every UAC prompt this app has ever shown for a tier 4 restore, on this machine,
was unnecessary. Fixed as [technical-debt.md](../technical-debt.md) §7.5 — the plan now probes each
plug-in's own folder and elevates only when one refuses.

**Do not generalise this to every machine.** A clean Windows install has `Users:(RX)` and nothing
above it, and there tier 4 genuinely needs the prompt. The point of the fix is that the answer is
now measured per machine rather than assumed.

### 2.2 Wave Link is JUCE-based, and there is a path-independent plug-in identity

`AudioPluginCache/KnownPlugins.cache` is a JUCE `<KNOWNPLUGINS>` list:

```xml
<PLUGIN name="Elgato Compressor" format="VST3" category="Fx" manufacturer="Elgato"
        version="1.0.1.95" file="C:\Program Files\Common Files\VST3\Elgato\ElgatoCompressor.vst3"
        uniqueId="f664ec1f" isInstrument="0" ... uid="e9546b7c"/>
```

And every third-party plug-in in `Settings.json` carries a `PluginId` that **matches a cache
`uniqueId` exactly**:

| PluginId | Name | In cache | Path agrees |
|---|---|---|---|
| `cc72904e` | Clear | yes | yes |
| `cba24108` | Pro-DS | yes | yes |
| `763029ad` | Pro-C 2 | yes | yes |
| `beb1ab47` | Pro-Q 4 | yes | yes |
| `5a67e07f` | Saturn 2 | yes | yes |
| `45398b95` | Pro-L 2 | yes | yes |

So a channel's plug-in reference carries **two** identifiers: an absolute `FilePath`, and a
`PluginId` that is stable across locations.

**The last column is the problem.** The paths all agree today, because nothing has moved — which
means this data **cannot distinguish** "Wave Link resolves by `PluginId`" from "Wave Link resolves
by `FilePath`". That is §3.

### 2.3 The configurable scan folder is VST2 only

`Settings.json` → `PluginHostConfiguration.AudioPluginHostSettings`:

```json
{
  "VST2PluginDirectoryPath": "",
  "AutoScalePluginWindowDefault": false,
  "AutoScalePluginWindowState": { "<endpoint>|<Id>": "Default", ... }
}
```

One configurable directory, **VST2**, empty here. There is no VST3 equivalent stored in the
settings file. JUCE's `VST3PluginFormat` searches the two standard VST3 locations by default, which
is consistent with the user-level folder being scanned — but consistency is not measurement, and
this rig could not show it either way (§2.4).

### 2.4 Every scanned plug-in is in the shared folder, so the user folder is unobserved

```
plugins in cache: 154
scanned roots:    154  C:\Program Files\Common Files\VST3
formats:          {'VST3': 154}
outside the shared VST3 folder: 0
```

`%LOCALAPPDATA%\Programs\Common\VST3` **does not exist** on this machine. Wave Link may well scan
it; nothing here proves it does. The logs confirm a VST3 scan runs
(`VST3 Plugin Scan Started`) but never name the search paths.

> **Retired 2026-08-25 by §3's run.** The folder was created, a plug-in put in it, and Wave Link
> restarted: it appeared in `KnownPlugins.cache` thirteen seconds later with its `uniqueId` intact.
> **The user-level folder is scanned.** The heading above is left as written because the reasoning
> it records — consistency is not measurement — is the reason the experiment was worth running.

---

## 3 · The open question — **ANSWERED 2026-08-25**

> **Wave Link resolves by `PluginId`, and repairs `FilePath` behind it.** Run on this rig with
> [`tools/plugin-resolution-experiment.ps1`](../../tools/plugin-resolution-experiment.ps1):
> Pro-L 2 copied to the user-level VST3 folder, the shared copy renamed, Wave Link restarted.
>
> - The scan found it in the user folder 13 seconds after restart, same `uniqueId=45398b95`.
> - All 11 effects stayed on Wave Mic 1, Pro-L 2 among them.
> - `FilePath` was **rewritten** to the user-level path. `Saturn 2`, untouched, was not — so this
>   was the moved file being repaired, not a blanket refresh.
>
> **Row 1 of the outcome table below: the user folder is a viable fallback destination.** It also
> retires §2.4's caveat that the user-level folder "could not be observed being scanned" — it is
> scanned.
>
> **The recommendation in §3.4 stands unchanged**, and now rests on a measurement: viable is not
> the same as worth building. Tracked as technical-debt.md §7.6, now closed.

### The original entry, and how it was answered

**Does Wave Link resolve a channel's plug-in by `PluginId`, or by `FilePath`?**

Tracked as [technical-debt.md](../technical-debt.md) §7.6.

### Why it matters

Tier 4 restores a `.vst3` to the absolute `FilePath` the settings recorded. When that folder
refuses a write, the obvious alternative is the user-level VST3 location, which needs no
administrator — but only if moving the file does not break the reference.

It also bears on two other things:

- **Tier 2's drift check** currently keys on path and hash. If `PluginId` is authoritative, "the
  plug-in moved" becomes a state this app can detect and describe, rather than one that looks
  identical to "the plug-in is gone".
- **[post-1.0.md](../dev-phases/post-1.0.md)'s refused "portable backups"** rests partly on plug-in
  paths being absolute. `PluginId` does not make backups portable — endpoint IDs still embed device
  serials — but it removes one of the two reasons.

### The experiment

Reversible, about ten minutes, and it must be run on a machine with Wave Link installed.

**Scripted:** [`tools/plugin-resolution-experiment.ps1`](../../tools/plugin-resolution-experiment.ps1)
does steps 2, 3, 6 and 7, and keeps the state in a journal outside the repo so `-Undo` works from a
fresh shell, after a reboot, or a week later. The manual version below is not hard; it is just easy
to get half-way through and lose track of which copy is the original. Steps 1, 4 and 5 are still
yours — take the backup, restart Wave Link, look at the channel.

```powershell
.\tools\plugin-resolution-experiment.ps1 -Status   # read-only, safe any time
.\tools\plugin-resolution-experiment.ps1 -Setup
# restart Wave Link, let the scan finish, look at the channel, quit Wave Link
.\tools\plugin-resolution-experiment.ps1 -Record -EffectLoaded   # or -EffectDropped
.\tools\plugin-resolution-experiment.ps1 -Undo
```

`-Record` prints the outcome row from the table below and writes the verdict into the journal, so
the answer survives the shell it was observed in.

1. **Take a backup first.** The tool exists for this, and this is exactly the moment it is for.
2. Copy one plug-in that is **on a channel** — `FabFilter Pro-L 2.vst3` is a good pick, it is on
   Wave Mic 1 — into `%LOCALAPPDATA%\Programs\Common\VST3\`.
3. Rename the shared copy (`…\FabFilter\FabFilter Pro-L 2.vst3` → `.vst3.bak`) so the recorded path
   no longer resolves.
4. Restart Wave Link. Let the scan finish.
5. **Look at the channel.** Does the effect still load, or has it dropped?
6. Read `Settings.json` again: was that plug-in's `FilePath` rewritten to the new location?
7. **Undo:** rename the shared copy back, delete the user-folder copy, restart, confirm the channel
   is as it was.

### What each outcome means

| Effect loads? | `FilePath` after | Means | Consequence for tier 4 |
|---|---|---|---|
| Yes | Rewritten | Resolves by `PluginId`, then repairs the path | The user folder is a **viable** fallback destination |
| Yes | Unchanged | Resolves by `PluginId`; the path is advisory | Viable, but the settings keep a stale path — worth noting before relying on it |
| No | — | Resolves by `FilePath` | **Not viable.** Restoring elsewhere silently breaks the channel |

### The recommendation this audit leaves

**Probably do not build the fallback, whatever the answer.** §2.1's fix already removes the prompt
on any machine whose VST3 folder has been loosened, which is the common case and includes this one.
What remains is one prompt, on an explicit opt-in, for writing to a folder every account shares —
which is what UAC is for.

A fallback destination would trade that for: a file somewhere other than where it came from, a
possible duplicate at the old path once the original folder becomes writable again, and the loss of
a promise tier 4 currently keeps.

**Run the experiment anyway.** The answer is worth more than the feature it was asked for.

---

## 4 · What this audit did not do

- **One machine.** Every figure in §2 is this rig's. The ACL finding in particular is a property of
  what has been *installed* here, not of Windows.
- **Nothing was tested on a clean-ACL machine.** The elevated path still exists and is still
  correct; it has not been re-exercised since §2.1's fix, because this machine can no longer reach
  it without an artificial ACL change.
- **§3 was not run.** It touches a live Wave Link install and is the user's call.

## References

- [technical-debt.md](../technical-debt.md) §7.5 (fixed) and §7.6 (this question)
- [[ADR-011]] — why elevation is a second process at all
- [[ADR-006]] — why tier 4 is opt-in and off by default
- [`operations/design/screens/13-elevation.md`](../operations/design/screens/13-elevation.md) — the designed row
- [post-1.0.md](../dev-phases/post-1.0.md) — portable backups, and what `PluginId` would and would not change
