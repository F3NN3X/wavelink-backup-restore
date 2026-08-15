---
title: "Phase 1 — Core: discovery, validation, safe write"
status: published
created: 2026-08-16
updated: 2026-08-16
tags: [dev-phase]
---

# Phase 1 — Core: discovery, validation, safe write

**Status:** Not started
**Entry criteria:** phase 0 complete — the solution builds, upstream's tests pass unchanged.
**Exit criteria:** a settings file can be discovered, validated, fingerprinted and atomically
replaced with Wave Link's exit verified beforehand; every path is covered by tests through the
seam interfaces; and **no code path in `Core` re-serializes a settings file it is only
storing**.

## Why this phase exists

Everything else calls this. The store, the watcher, both shells and every plugin tier sit on
top of four capabilities: *find the file*, *decide whether it is any good*, *describe it
cheaply*, and *replace it without losing anything*.

Three of the project's eight gotchas live entirely inside this phase, and two of upstream's
five defects are fixed here. Getting it wrong is not a bug in one feature — it is a bug in the
premise.

## Scope

### In

- Discovery, including the multiple-package and not-found paths.
- Validation: parse, structure, and **case-insensitive duplicate keys**.
- The health fingerprint: input count, input names, size, Wave Link version.
- Process lifecycle: graceful close, timeout, kill tree, **verified exited**.
- Atomic write via `File.Replace`.
- Upstream findings 2 and 3.
- The `JsonNode.Parse` empirical check.

### Out — and where it went instead

- The snapshot store, manifests, dedup → **phase 2**. This phase writes and reads *files*, not
  snapshots. It has no opinion about where a file came from.
- Watcher, debounce, retention → **phase 3**.
- Anything with a UI → phases 4 and 5.
- Reading `AudioPluginConfigurations` for tier 2 → **phase 6**. The fingerprint here is inputs
  and size only.
- **Repairing** a settings file → not scoped at all. Validation reports; it does not fix.
  Repair needs the foreign-key handling from [[restored-backup-has-dead-channels]] and is a
  separate feature.

## Work

### 1 · Settle the `JsonNode.Parse` question first

**Ten minutes, and it blocks the design of everything below it.** Parse a fixture containing
`{"A":1,"a":2}` with `JsonNode.Parse` and inspect the result.

- **If it collapses duplicates**, upstream's edit path silently drops data, and no `JsonNode`
  round-trip may ever touch a real settings file.
- **If it does not**, duplicates survive into the written file and Wave Link rejects it.

Opposite failures, both bad, and the code reads identically either way. Record the answer in
[technical-debt.md](../technical-debt.md) §2.1 and in a test that fails if the behaviour ever
changes.

### 2 · Discovery

Port upstream's `SettingsDiscovery` as-is; it is right ([[backup-succeeds-but-protects-nothing]]).
Preserve all three properties:

- glob `Elgato.WaveLink_*`, never the hard-coded family name;
- require `Settings.json` to exist;
- **refuse to guess** between multiple packages.

Add the escape hatch from [technical-debt.md](../technical-debt.md) §2.2: an explicit
settings-path override, so the multiple-package case and the possible non-MSIX case both have
a way forward rather than a dead end.

Use `Environment.GetFolderPath` — `%LOCALAPPDATA%` is redirected on some corporate and
OneDrive setups.

**Tests:** the multiple-package case; the not-found case; and **a populated
`%APPDATA%\Elgato\WaveLink` fixture that discovery must ignore**. That last one is the guard
against a well-meaning "add a fallback location" change later, which is exactly how the decoy
comes back.

### 3 · Validation

Three checks, in increasing order of what they catch:

1. Parses as JSON.
2. `MixerConfiguration.InputSettings` is an object — upstream's existing check.
3. **No case-insensitively duplicated property names**, via a `JsonDocument` tree walk
   grouping names with `StringComparer.OrdinalIgnoreCase`.

Check 3 is upstream finding 3 and the original incident ([[file-parses-but-wave-link-resets]]).
It must use `JsonDocument` specifically — `JsonNode` and `ConvertFrom-Json` cannot see it.

Validation **reports**; it never modifies. A file that fails check 3 is flagged suspect, not
rejected: a suspect snapshot may be the only one there is.

### 4 · Health fingerprint

Input count, input names, file size, Wave Link version. Computed once, cheap enough for every
capture, and sufficient to distinguish a real configuration from a collapsed one
([[newest-backup-is-the-broken-one]]).

**The comparison is relative, always.** Five inputs and 43 KB is one user's rig. Core exposes
the fingerprint and a *comparison against a previous fingerprint*; it must not expose a
`bool IsHealthy` computed against a constant.

### 5 · Byte-faithful reads, and upstream finding 2

**Capture is a byte copy.** Hash the source bytes, write the source bytes — no parse, no
serialize. Parsing exists for validation and the fingerprint, and its output is metadata,
never a file.

Where a rewrite is genuinely needed later, `JavaScriptEncoder.UnsafeRelaxedJsonEscaping` is
mandatory, or `+` and `/` in every `ParameterState` get rewritten
([[every-snapshot-differs-with-no-real-change]]).

**Test:** a fixture with `+` and `/` in a `ParameterState`, asserting captured bytes are
identical to source bytes. One line, and it would have caught upstream's defect on day one.

### 6 · Process lifecycle

Port upstream's sequence. Graceful close → 10 s timeout → kill tree on timeout → **assert not
running** → only then write. Both processes: `Elgato.WaveLink` **and** `WavelinkSEService`.

Make **"verified exited" a precondition of the write function itself**, not a step the caller
is trusted to have performed. Enforced at the boundary, the flush race cannot be reintroduced
by a future caller ([[restored-settings-revert-seconds-later]]).

Relaunch via `shell:AppsFolder\<packageFamilyName>!App` — an MSIX app will not start from its
`.exe` path.

**Test through `IWaveLinkProcess`:** a fake reporting `IsRunning == true` after close must make
the write throw. That is the entire purpose of the seam.

### 7 · Atomic write

Temp file **in the same directory** — `File.Replace` requires the same volume — then
`File.Replace(temp, target, backupPath)`.

### 8 · Log verification

Read the newest file in `LocalState\Logs`, match against `Failed to parse`,
`Created a new backup file` and `Applied saved`. Success is the **absence** of the parse
failure plus the presence of an applied friendly name.

Belongs in Core because both shells need it and because it is the only trustworthy
confirmation a restore worked — a UI that looks correct can be a freshly generated default.

## Risks

| Risk | Early signal | Response |
|---|---|---|
| The `JsonNode.Parse` answer is "collapses" | Item 1, day one | Every edit path is redesigned around `JsonDocument` + `Utf8JsonWriter`. Better to learn in ten minutes than in phase 2. |
| A "helpful" fallback path to `%APPDATA%` gets added | Discovery gaining a second location | The decoy fixture test. It exists for this. |
| Validation grows into repair | A `Fix()` method appearing | Out of scope, explicitly. Repair needs foreign-key handling nobody has designed. |
| `Core` acquires a console or UI reference | CI reference guard | Fix the leak, not the guard. |
| The exit assertion becomes a `Sleep` | A fixed delay in the write path | A sleep fails exactly under the load that causes the race. Assert `IsRunning`. |

## References

- `SPEC.md` §1, §3, §4, §5
- [[ADR-001]] · [[ADR-002]] · [[ADR-004]]
- [[backup-succeeds-but-protects-nothing]] · [[file-parses-but-wave-link-resets]] ·
  [[newest-backup-is-the-broken-one]] · [[every-snapshot-differs-with-no-real-change]] ·
  [[restored-settings-revert-seconds-later]]
- [[restore-a-settings-file-safely]] — the sequence this phase implements
- [technical-debt.md](../technical-debt.md) §1.2, §1.3, §2.1, §2.2
