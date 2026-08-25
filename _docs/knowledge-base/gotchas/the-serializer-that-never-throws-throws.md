---
title: "The serializer that never throws, throws"
status: published
created: 2026-08-20
updated: 2026-08-20
tags: [gotcha, core, json, dates]
---

# The serializer that never throws, throws

**Provenance:** *Observed*, 2026-08-20, adding `binaryLastWriteUtc` to `plugins.json` for tier 2's
hash cache ([technical-debt.md](../../technical-debt.md) §4.16). Caught by twelve existing tests
going red at once.

## Symptom

Twelve unrelated tests, preset restore, plug-in restore. Restore orchestration, fail together
with:

```
System.ArgumentException : The DateTimeStyles value RoundtripKind cannot be used with
the values AssumeLocal, AssumeUniversal or AdjustToUniversal. (Parameter 'styles')
```

None of them is about dates. None of them touches the field that was added. The stack ends in
`PluginManifestSerializer.Read`, whose entire contract is that **it cannot fail**: tier 2 is always
on and cannot be switched off, so a damaged `plugins.json` must cost the warning its detail and
never a restore.

## Cause

```csharp
DateTime.TryParse(text, culture,
    DateTimeStyles.RoundtripKind | DateTimeStyles.AdjustToUniversal, out var parsed)
```

Those two flags are **mutually exclusive**, and `TryParse` says so by throwing `ArgumentException`,
not by returning `false`. A `TryParse` that throws is surprising enough on its own; a `TryParse`
that throws *on its flags rather than its input* means it throws for **every** call, including the
ones that would have parsed.

So a method documented to be total became one that threw on any file containing the new key, which
was every file the new code had just written.

## The plausible explanation, and why it is wrong

The failing tests all restore things, so the first instinct is that the tier-2 schema change broke
the restore path, that a reader somewhere is now rejecting a manifest it used to accept. That is a
reasonable read of twelve restore tests going red, and it sends you into `TierRestore` and
`RestoreOrchestrator`, which are innocent.

The second guess is bad *data*: a timestamp written in a format the reader cannot handle. Also
wrong, and it wastes a round trip through the writer. The exception names a **parameter**, not a
value, `(Parameter 'styles')` is the whole diagnosis, and it is easy to skim past because the
sentence before it talks about values.

## Fix

`RoundtripKind` alone, then convert:

```csharp
DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
    ? parsed.ToUniversalTime()
    : null;
```

`RoundtripKind` already honours the `Z` or the offset that `"O"` writes, so `AdjustToUniversal` was
adding nothing even if it had been legal.

## How to avoid it

**A "never throws" contract needs a test that feeds it rubbish**, and this one had them, which is
why the blast radius was twelve red tests in one run instead of a support ticket about a restore
that stopped working. The lesson is not "be careful with flags"; it is that the guarantee was
written down *and enforced*, so violating it was loud.

When adding a field to a tolerant reader, add the malformed-input case in the same commit. The
serializer's own header comment states the rule; the tests are what make it true.

## References

- [[newest-backup-is-the-broken-one]]: the other place this codebase pays for a reader that is
  tolerant on purpose
- [technical-debt.md](../../technical-debt.md) §4.16, the hash cache the field is for
- `src/WaveLinkBackup.Core/Snapshots/PluginManifestSerializer.cs`
