---
title: "The file parses fine, but Wave Link resets to defaults"
status: published
created: 2026-08-16
updated: 2026-08-16
related_adrs: [ADR-001]
tags: [gotcha, validation, json]
---

# The file parses fine, but Wave Link resets to defaults

**Provenance:** **Observed.** This is the original incident, the one the project exists
because of. Recovery recorded 2026-08-11. The specific defect (case-insensitively duplicated
keys, written by an older build) was identified from Wave Link's own validator behaviour and
its log output.

## Symptom

`Settings.json` is present and the right size. Every JSON tool you reach for reads it without
complaint. `ConvertFrom-Json` returns a clean object with all the expected properties.

Wave Link starts, and the mixer is empty, two inputs (`Elgato Wave:3`, `System`) instead of
five, no effect chains, no routing. The file on disk has been replaced with an ~11 KB default.

The log says `Failed to parse settings file`.

## Cause

The file contains **case-insensitively duplicated property names**, `"Volume"` and
`"volume"` as siblings, written by an older Wave Link build.

Wave Link's `SettingsJsonNormalizer.HasCaseInsensitiveDuplicateProperties` rejects the whole
file on this basis and regenerates defaults. It is strictly correct: duplicate keys make the
document ambiguous, and the app refuses to guess which one you meant.

Duplicate keys are legal JSON syntax. Every parser accepts them; they differ only in what they
*do* with them, and most silently keep one and discard the other.

## The plausible explanation, and why it is wrong

> *"The file must be corrupt or truncated — it won't parse."*

It parses. That is the entire trap. You will validate it three different ways, each will
succeed, and you will conclude the problem is elsewhere, the install, the audio devices, the
update. Meanwhile the defect is sitting in plain sight in a file you have already checked.

Worse, **the tool you reach for to check is the one guaranteed to hide it**:

```powershell
# Lies. Silently collapses "Volume" and "volume" into one property.
$s = Get-Content Settings.json -Raw | ConvertFrom-Json
$s.MixerConfiguration.InputSettings.PSObject.Properties.Count   # looks fine
```

`ConvertFrom-Json` collapses duplicates on the way in. The file "parses fine" while the app
refuses it, and the more carefully you verify with PowerShell the more confident you become in
the wrong answer.

This is the single most decisive reason the project is C# rather than PowerShell or Rust:
`System.Text.Json`'s `JsonDocument` **preserves** duplicates, and `serde_json`'s map does not.
See [[ADR-001]].

## Fix

Walk the document with `JsonDocument`, which preserves duplicates, and group property names
case-insensitively at every object:

```csharp
static bool HasCaseInsensitiveDuplicates(JsonElement element)
{
    if (element.ValueKind == JsonValueKind.Object)
    {
        var names = element.EnumerateObject().Select(p => p.Name).ToList();
        if (names.Distinct(StringComparer.OrdinalIgnoreCase).Count() != names.Count)
            return true;

        return element.EnumerateObject().Any(p => HasCaseInsensitiveDuplicates(p.Value));
    }

    return element.ValueKind == JsonValueKind.Array
        && element.EnumerateArray().Any(HasCaseInsensitiveDuplicates);
}
```

Note `element.EnumerateObject()` yields **both** duplicates. That is the property the whole
check depends on, and the reason `JsonNode` and `ConvertFrom-Json` cannot substitute here.

Record the result as `hasDuplicateKeys` in the snapshot manifest and mark the entry
**suspect**. Do not block the restore: a suspect snapshot may still be the best one available,
and the user is better served by a warning than by a hidden entry.

## How to avoid it

- **Validate before touching anything.** Restoring a file the app will reject looks identical
  to the snapshot being broken, and it costs a restore cycle to distinguish.
- **Never use `ConvertFrom-Json` to check a settings file**, in a script, in a test fixture, or
  interactively while debugging. It is not "good enough for a quick look". It is specifically
  blind to this defect.
- **Fixture test with a hand-written `{"A":1,"a":2}` file.** Cheap, and it is also the check
  that answers the open question in [technical-debt.md](../../technical-debt.md) §2.1,
  whether `JsonNode.Parse` collapses duplicates, which decides whether the *edit* path silently
  drops data.

Upstream's `Validate()` only asserts that `MixerConfiguration.InputSettings` is an object, so
it would pass this file unnoticed. See [technical-debt.md](../../technical-debt.md) §1.3.

## References

- `SPEC.md` §5, §7·3
- [[ADR-001]] · [[newest-backup-is-the-broken-one]] ·
  [[every-snapshot-differs-with-no-real-change]]
- [glossary.md](../../glossary.md), *duplicate keys*, *collapsed*, *suspect*
