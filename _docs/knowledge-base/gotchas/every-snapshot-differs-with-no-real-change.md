---
title: "Every snapshot differs from the last, but nothing actually changed"
status: published
created: 2026-08-16
updated: 2026-08-16
related_adrs: [ADR-001, ADR-007]
tags: [gotcha, json, serialization]
---

# Every snapshot differs from the last, but nothing actually changed

**Provenance:** **Read, not reproduced.** Identified by reading
`voltybat/WaveLinkSettingsUtility` source at `main` (pushed 2026-07-19) on 2026-08-15, and
from `System.Text.Json`'s documented default encoder behaviour. The consequence is
predicted, not observed — no snapshot has been written by this project yet.

## Symptom

Content-hash dedup never dedups. Every capture produces a new snapshot even when the user has
not touched Wave Link, and the store fills with entries that are supposedly all different.

Diffing two snapshots shows thousands of changed characters scattered through the effect
chains, none of them meaningful.

## Cause

The settings file was **re-serialized** somewhere in the pipeline with the default JSON
encoder, which escapes `+` and `/` to `\uXXXX`.

Those two characters are the base64 alphabet, and base64 is what `ParameterState` is —
every VST3 plugin's saved state, on every effect, on every channel. A single mic chain with
six plugins carries a lot of it.

The output stays valid JSON. Wave Link accepts it without complaint. That is precisely why
this survives: nothing breaks, so nothing draws attention to it.

Upstream has this defect today:

```csharp
// No Encoder set — the default escapes + and / to \uXXXX.
JsonSerializer.SerializeToUtf8Bytes(root, new JsonSerializerOptions { WriteIndented = true });
```

## The plausible explanation, and why it is wrong

> *"Wave Link must be rewriting the file constantly, so the content genuinely is different
> every time."*

Half true, and it sends you down the wrong path. Wave Link **does** rewrite `Settings.json` on
every launch — with **near-identical bytes**. That is exactly the case hash-dedup is built to
absorb ([[ADR-007]]). If dedup is not absorbing it, the bytes are being changed by something
downstream, and the only thing downstream is your own serializer.

The tell: the diff is not in `WindowPlacement` or a timestamp — the fields you would expect to
churn. It is inside `AudioPluginConfigurations`, in the longest strings in the file.

## Fix

**Never re-serialize a settings file you are only storing.** Copy bytes. A backup tool that
rewrites the thing it is backing up has already lost the argument.

Where a rewrite is genuinely required — a repair path, not a capture path — set the encoder
explicitly:

```csharp
var options = new JsonSerializerOptions
{
    WriteIndented = true,
    Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,   // + and / survive
};
```

The name is alarming and the risk is not: "unsafe" here means it does not escape characters
that are only dangerous when JSON is interpolated into HTML. We are writing a file to disk.

For repair, stream element-by-element with `Utf8JsonWriter` so every value is copied verbatim.

**And never round-trip through `ConvertFrom-Json | ConvertTo-Json`.** Two separate ways it
destroys the file:

- It truncates at `-Depth` (default 2 — it warns once, then silently drops the rest).
- It rewrites number and string formatting.

That is on top of collapsing duplicate keys ([[file-parses-but-wave-link-resets]]).

## How to avoid it

- **Capture is a byte copy.** Hash the source bytes, write the source bytes. No parse, no
  serialize. Parsing happens for *validation and metadata only*, and its output is a
  fingerprint, never a file.
- **Test that captured bytes are identical to source bytes**, with a fixture containing `+`
  and `/` in a `ParameterState`. This is a one-line assertion that would have caught the
  upstream defect on the day it was written.
- **Treat a dedup miss as a bug, not as noise.** If two consecutive captures with no user
  action produce different hashes, something in the pipeline is rewriting bytes. That signal
  is worth surfacing in a debug log.

Upstream fix tracked in [technical-debt.md](../../technical-debt.md) §1.2 — worth offering
back, since it is two lines.

## References

- `SPEC.md` §5, §7·2
- [[ADR-001]] · [[ADR-007]] · [[file-parses-but-wave-link-resets]]
- [glossary.md](../../glossary.md) — *`ParameterState`*, *dedup key*
