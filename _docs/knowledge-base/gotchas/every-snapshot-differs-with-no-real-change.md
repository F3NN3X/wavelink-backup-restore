---
title: "Every snapshot differs from the last, but nothing actually changed"
status: published
created: 2026-08-16
updated: 2026-08-16
related_adrs: [ADR-001, ADR-007]
tags: [gotcha, json, serialization]
---

# Every snapshot differs from the last, but nothing actually changed

**Provenance:** **Observed 2026-08-16**, measured against the live `Settings.json` on the
reference machine.

> **This document replaced an earlier version that had the cause backwards.** The first draft
> was written from `SPEC.md` §7·2 and marked *read, not reproduced*. When it was reproduced,
> the recommendation it carried turned out to be the thing that *causes* the symptom. The
> measurement is below. This is what that provenance label is for.

## Symptom

Content-hash dedup never dedups. Every capture produces a new snapshot even when the user has
not touched Wave Link, and the store fills with entries that are supposedly all different.

Diffing two snapshots shows changes scattered through the effect chains, none of them
meaningful.

## Cause

**Something in the pipeline re-serialized the file instead of copying it.**

Wave Link writes its own `Settings.json` with `System.Text.Json`'s **default** encoder and
`WriteIndented`. That is a measurable fact, not an inference, a default round-trip reproduces
the file byte for byte:

```
original file                        43,052 bytes
round-trip, default encoder          43,052 bytes   identical = True    13 escaped sequences
round-trip, UnsafeRelaxedJsonEscaping 41,641 bytes   identical = False    0 escaped sequences
```

The live file **already contains** those `+` escapes, because Wave Link put them there. Any
re-serialization that does not match its encoder settings exactly rewrites those bytes and
breaks dedup.

## The plausible explanation, and why it is wrong

> *"Set `Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping`, or `+` and `/` in the base64
> plugin state get rewritten to `\uXXXX`."*

**This is the trap, and it was this project's own documented recommendation.** From
`SPEC.md` §5 and §7·2, and recorded as finding 2 against upstream.

It is exactly backwards. The relaxed encoder *un-escapes* what Wave Link deliberately wrote,
shrinking the file by 1,411 bytes and making every one of our snapshots differ from the app's
own output. Applying the "fix" produces the symptom.

Two smaller corrections to the same claim, both measured:

- The default encoder escapes **only `+`**, to its six-character JSON escape for U+002B. It
  does **not** escape `/`. The spec says both.
- Upstream's `SerializeToUtf8Bytes(root, new JsonSerializerOptions { WriteIndented = true })`
  is therefore **correct as written**, not defective. See the
  [audit](../../audits/2026-08-15-voltybat-wavelinksettingsutility.md) finding 2, downgraded.

The second plausible-but-wrong explanation:

> *"Wave Link rewrites the file constantly, so the content genuinely is different every time."*

Half true, and it sends you past the real cause. Wave Link **does** rewrite `Settings.json` on
every launch, with **near-identical bytes**. That is the case hash-dedup exists to absorb
([[ADR-007]]). If dedup is not absorbing it, something downstream is changing bytes, and the
only thing downstream is your own code.

The tell: the diff is not in `WindowPlacement` or a timestamp, the fields you would expect to
churn. It is inside `AudioPluginConfigurations`, in the longest strings in the file.

## Fix

**Never re-serialize a settings file you are only storing. Copy bytes.**

This was always the right rule, and the encoder confusion is the argument for it. A backup
tool that rewrites the thing it is backing up has already lost, and as this document shows,
even a *well-intentioned, carefully-chosen* encoder setting can be the thing that breaks it.

```csharp
// Capture: hash the source bytes, write the source bytes. No parse, no serialize.
var bytes = ReadSettingsBytes(path);        // shared-mode read; see the file-lock gotcha
var sha   = Convert.ToHexString(SHA256.HashData(bytes));
```

Parsing exists for **validation and the health fingerprint only**, and its output is metadata,
never a file.

Where a rewrite is genuinely required, a repair path, not a capture path, **match Wave
Link's own settings**: default encoder, `WriteIndented = true`. Verify byte-identity against
the source on a file you have not modified, rather than trusting any encoder recommendation
including this one.

## How to avoid it

- **Capture is a byte copy**, enforced by having exactly one function that reads settings bytes
  and no `JsonSerializer` call anywhere in the capture path.
- **Test byte-identity against a real fixture.** One containing `+` inside a `ParameterState`,
  asserting captured bytes equal source bytes. One line, and it catches every variant of this.
- **Treat a dedup miss as a bug.** Two consecutive captures with no user action producing
  different hashes means something is rewriting bytes. Surface it.
- **Do not "fix" the encoder.** If a future reader finds
  `SerializeToUtf8Bytes(..., WriteIndented = true)` with no `Encoder` set and reaches for
  `UnsafeRelaxedJsonEscaping`, that is this bug being reintroduced. The measurement above is
  the reason it is left alone.

## References

- `SPEC.md` §5, §7·2, **both contain the superseded recommendation**; see the Corrections
  block at the top of that document
- [Audit](../../audits/2026-08-15-voltybat-wavelinksettingsutility.md) finding 2, downgraded
- [[ADR-001]] · [[ADR-007]] · [[file-parses-but-wave-link-resets]] ·
  [[capture-fails-while-wave-link-is-running]]
