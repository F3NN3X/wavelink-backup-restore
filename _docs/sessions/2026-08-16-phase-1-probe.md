---
title: "Session: Phase-1 probe — three documented decisions overturned"
status: published
created: 2026-08-16
updated: 2026-08-16
tags: [session, validation, json]
---

# Session: Phase-1 probe — three documented decisions overturned

**Date:** 2026-08-16

## Goal

Settle [technical-debt.md](../technical-debt.md) §2.1 — whether `JsonNode.Parse` collapses
duplicate keys — before designing phase 1, so the validation design would rest on a fact
rather than branching on an unknown.

Ten minutes of throwaway code. It answered the question and invalidated two other things
nobody had thought to doubt.

## What happened

The machine turned out to be the reference machine: Wave Link installed, `Settings.json` at
43,052 bytes modified that morning, and the decoy `%APPDATA%\Elgato\WaveLink` still present
with a newest file of 2025-11-17 — independently confirming `SPEC.md` §1 nine months on.

So the probe was widened from one question to four.

### 1 · The `JsonNode` question was mis-framed

Both of the answers the debt register offered were wrong, because the behaviour depends on
which kind of duplicate:

| Input | `JsonDocument` | `JsonNode.Parse` |
|---|---|---|
| `{"A":1,"a":2}` — case-insensitive, *the actual Wave Link defect* | preserves both | **preserves both**, round-trips intact |
| `{"A":1,"A":2}` — exact duplicate | preserves both; `GetProperty` returns the **last** | **throws `ArgumentException`** |

The feared outcome — silent data loss on round-trip — is not real. But `JsonNode.Parse`
hard-crashes on exact duplicates, which no document anticipated, and unhandled that surfaces
as *"An item with the same key has already been added. Key: A"* rather than *"this settings
file is malformed"*. A new, smaller finding, logged as audit finding 3b.

### 2 · The encoder recommendation was inverted

This is the one that mattered.

```
original file                          43,052 bytes
round-trip, default encoder            43,052 bytes   identical = True    13 escapes
round-trip, UnsafeRelaxedJsonEscaping   41,641 bytes   identical = False    0 escapes
```

**Wave Link writes its own file with the default encoder**, indentation and all. The escapes
in the live file are Wave Link's. `SPEC.md` §5 and §7·2, audit finding 2, and
[technical-debt.md](../technical-debt.md) §1.2 all recommended
`UnsafeRelaxedJsonEscaping` — which would have un-escaped 13 sequences, shrunk the file by
1,411 bytes, and made every snapshot differ from the app's own output. **The prescribed fix
caused the disease.**

Upstream's `SerializeToUtf8Bytes(root, new JsonSerializerOptions { WriteIndented = true })` is
correct as written. Finding 2 is withdrawn, and must not be offered upstream as a patch.

### 3 · A file lock nobody had documented

```
File.ReadAllBytes                        FAILED — being used by another process
FileShare.ReadWrite | FileShare.Delete   OK — 43,052 bytes
```

Wave Link holds `Settings.json` open in a mode that denies `File.ReadAllBytes` — the obvious
call, and the one a port of upstream-shaped code reaches for. Not a transient write window;
it is the steady state while the app runs, which is when *most* captures happen. This would
have broken the watcher on day one and looked environmental.

New gotcha: [[capture-fails-while-wave-link-is-running]].

### 4 · The live config, read-only

5 inputs — `Wave Mic 1` (11 effects), `Voice`, `Browser`, `Music`, `System`. No duplicate
keys. Device IDs were masked in the probe's output as a matter of habit, since they carry
hardware serials.

Worth noting the design handoff's sample data says `Game` where this machine has `Music` —
harmless, since that data is explicitly labelled as sample, but a reminder that the
five-input fingerprint is one user's rig and the health check must stay relative.

## Decisions made

| Decision | Recorded in |
|---|---|
| Withdraw the encoder finding; capture copies bytes and never re-serializes | Audit finding 2, [[every-snapshot-differs-with-no-real-change]] |
| Correct `SPEC.md` by prepending a Corrections block rather than editing its body | `SPEC.md` top |
| Shared-mode reads become a Core design rule, not just a gotcha | [[capture-fails-while-wave-link-is-running]] |
| `Core` avoids reflection-based `JsonSerializer` regardless of the AOT decision | [technical-debt.md](../technical-debt.md) §2.4 |

## What did not work

**The first probe run failed twice before producing anything**, and both failures were
findings rather than mistakes. `JsonSerializer.Serialize` on an anonymous type threw
*"Reflection-based serialization has been disabled"* — a .NET 10 file-based-app default that
happens to mirror the constraint NativeAOT imposes, so it went into §2.4 as a design rule.
Then `File.ReadAllBytes` threw on the locked file, which became gotcha 9.

**`SPEC.md` was not edited in place.** Rewriting the body would have destroyed the record of
what was believed on 2026-08-15, which is what makes the corrections legible as corrections.
The block goes at the top and says the body is deliberately stale.

**The withdrawn finding was struck through, not deleted**, in all three places. A wrong
recommendation that merely disappears gets re-derived from first principles by the next
reader — the reasoning behind it is genuinely plausible, which is how it got written down in
the first place.

## Open questions

- **Upstream finding 5 is now in doubt.** The audit read `SelfContained=false` off the csproj;
  upstream's README claims no runtime is needed. One is wrong. Resolve at fork intake.
- **§2.2 (non-MSIX installs)** and **§2.3 (VST3 bundles)** are untouched by this session.
- **Is one read atomic against Wave Link's own save?** Almost certainly not. A capture during
  the app's atomic-save may catch a torn file, so validation must treat a parse failure as
  "retry once" rather than "the config is broken".

## Next

Phase 1's design, now resting on measurements rather than the spec's recommendations. Three
things change in [phase-1-core.md](../dev-phases/phase-1-core.md): the encoder task is deleted
as a would-be regression, the `JsonNode` probe is done, and shared-mode reads become a
first-class design rule with a single `ReadSettingsBytes` seam.

## References

- [phase-1-core.md](../dev-phases/phase-1-core.md)
- [Audit](../audits/2026-08-15-voltybat-wavelinksettingsutility.md) — findings 2, 3, 3b, 5
- [technical-debt.md](../technical-debt.md) §1.2, §2.1, §2.4
