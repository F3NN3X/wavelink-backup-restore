---
title: "ADR-001: C# / .NET over Rust"
status: accepted
created: 2026-08-16
updated: 2026-08-16
tags: [decision, language, platform]
---

# ADR-001: C# / .NET over Rust

**Status:** Accepted
**Date:** 2026-08-16

## Context

A greenfield Windows utility needs a language. The workload is: resolve an MSIX package path,
read a 43 KB JSON file, hash it, copy it, watch it for changes, enumerate Core Audio
endpoints, stop and start a process, and put a small GUI on top.

Rust is the more interesting answer. It is worth writing down why it is the wrong one, because
"we should have used Rust" is the kind of second-guessing that costs a week eighteen months
from now.

Nothing in this workload is hot. Nothing is memory-unsafe. Performance and safety, the two
arguments that usually settle this. Do not apply. What decides it is that **every hard part
is a Windows API, and one of them is already written.**

## Decision

Build on **C# / .NET 10**.

## Alternatives considered

| Option | Why not |
|---|---|
| **Rust** | Loses on the one requirement that is genuinely hard. See the table below. |
| **PowerShell** | The prototyping language for this problem, and actively dangerous as the product, `ConvertFrom-Json` cannot see the duplicate-key defect that motivated the project ([[file-parses-but-wave-link-resets]]), and `ConvertFrom-Json \| ConvertTo-Json` truncates at `-Depth 2`. A tool written in it would silently corrupt the thing it is protecting. |
| **C++ / WinRT** | Every Windows API available first-hand, and nothing else. No upstream to fork, no test story, no JSON story. |

Rust in detail:

| Requirement | C# / .NET | Rust |
|---|---|---|
| Core Audio COM enumeration | Hand-declared `[ComImport]`, verbose, but already written upstream and copyable | `windows-rs` generates bindings; arguably *less* code |
| **Lossless JSON with duplicate keys** | **Decisive.** `JsonDocument` preserves duplicates, `JsonNode` edits, `Utf8JsonWriter` controls bytes exactly | `serde_json`'s map collapses duplicates by design. Detecting the defect that motivated this project needs `json-syntax` or a custom parser |
| File watching, MSIX paths, shell activation | First-party | Reachable, more assembly required |
| Small native-feeling GUI | WPF, WinUI 3, WinForms, Avalonia, all mature | egui or Tauri; none feel native on Windows 11 |
| Standalone binary | ~70 MB self-contained, or NativeAOT ~10, 15 MB | **Rust wins:** 2, 5 MB static |
| Reuse of existing MIT code | Fork and go | Full rewrite |

Rust wins exactly one row, and it is binary size on a utility nobody will notice the size of.

> **Measured 2026-08-16, phase 4, that one row is now roughly a tie.**
> The sizes above were estimates. Actual figures for `wlbackup`:
>
> | Publish mode | Size |
> |---|---|
> | Self-contained, single file | **70.2 MB**, the estimate was right |
> | **NativeAOT** | **3.2 MB**, the estimate of 10, 15 MB was 3, 5× too pessimistic |
>
> 3.2 MB sits inside the 2, 5 MB range this table credited to Rust. It does not change the
> decision, that turned on lossless JSON with duplicate keys, not on size, but the honest
> record is that the trade-off was less lopsided than written.
>
> **Caveat, and it is the important one:** this measures the code as it exists, which contains
> **no COM interop**. `[ComImport]` under AOT, the actual open question in
> [technical-debt.md](../technical-debt.md) §2.4, is still unanswered, because endpoint
> inspection has not been ported. Do not read 3.2 MB as "AOT is settled".

## Consequences

**This enables:** forking `voltybat/WaveLinkSettingsUtility` ([[ADR-002]]) rather than
rewriting ~60 KB of solved problems. It also enables the duplicate-key validator to be built
at all, cheaply, using `System.Text.Json` primitives that were designed for exactly this.

**This rules out:**

- A 2, 5 MB binary. The realistic floor is ~10, 15 MB with NativeAOT, and NativeAOT is
  CLI-only because WPF does not support it. See [technical-debt.md](../technical-debt.md) §1.5,
  where the packaging decision is still open.
- Cross-platform, in practice. Not a loss, [[ADR-008]] scopes the project to Windows for
  independent reasons.

**Revisit if:** the project ever needs to run somewhere .NET does not, or binary size becomes
a real distribution constraint rather than an aesthetic preference. Neither is foreseeable.

## References

- `SPEC.md` §8
- [[ADR-002]] · [[ADR-004]] · [[ADR-008]]
- [[file-parses-but-wave-link-resets]]
