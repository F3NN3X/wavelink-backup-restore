---
title: "Session: clearing the technical-debt list"
status: published
created: 2026-08-20
updated: 2026-08-20
related_adrs: [ADR-012, ADR-011, ADR-004]
tags: [session, debt, updates, accessibility, privacy]
---

# Session: clearing the technical-debt list

**Date:** 2026-08-20

## Goal

Close everything in [technical-debt.md](../technical-debt.md) that a commit can close, excluding §3
(`EBWebView\`, which is known-wrong deliberately and stays that way).

Seven commits, 1,219 → 1,417 tests, zero warnings.

## What happened

### Core first, because two entries turned out to be the same entry

§4.19 said tier 4 read whole binaries into memory, "one file at a time, so the peak is one plugin
rather than the set — acceptable". **That was wrong, and the entry was wrong in the direction that
matters.** `CapturedFile` held a `byte[]`, and `TierCapture` built the entire list before the store
wrote any of it — so a capture held every preset and every binary at once, ~40 MB on the reference
rig and unbounded with a sample-library instrument on a channel. Not one plug-in. All of them.

Fixing it properly meant `CapturedFile` naming a source rather than carrying bytes, which meant the
store copies rather than writes, which meant `IFileSystem.CopyFile` streaming and hashing in one
pass. And that seam is exactly what §4.16 (tier 2 rehashing every binary on every capture) needed,
which is why the two closed together — as §4.19's own entry had predicted.

Two things fell out that were not asked for:

- **The manifest now records what the copy wrote**, not what the capture measured beforehand. The
  two used to be identical by construction; now they can differ, and the honest number is the one
  that landed. Pinned by a test where they differ, which the old shape could not express.
- **The tier-4 all-or-nothing decision needed a new seam.** It used to read a file to discover it
  could not be read; `IFileSystem.CanReadShared` opens and closes.

§4.16's entry asked for a measurement before writing a cache. The cheaper answer was to make the
skip conservative enough that being wrong costs a hash rather than a stale value — `BinaryMatches`
needs a hash, a size *and* a write time, all three agreeing.

### The UI batch was the same lesson eight times

§4.21's eight undrawn surfaces were, every one of them, [[a-settings-control-moves-and-nothing-happens]]
in a different costume: `AcknowledgeReject` implemented and called by nothing; `FirstRunError1Label`
correct and bound by nothing; `IAutostart` fully tested with no control on it; error 9 in the
catalog with no surface. **A view model with a tested property is not evidence that anything renders
it.** So each got a view test that walks the real tree or reads the real markup.

The rejected-restore strip mattered most. It is the recovery path for the only failure that costs
somebody their mixer, and it stated the problem, offered nothing, and could not be closed for the
life of the process.

**Two `RestoreOutcomeStripTests` assertions were changed rather than worked around.** They pinned
`HasAction == false` and the older copy — the *absence* of the actions the design specifies. That is
a test encoding a defect, so it was updated with the reason written in place.

### The auto-updater, which the user chose over a check-only version

Asked whether to build the full thing; answer was full. So: feed, checksum-verified download,
staged install, swap, relaunch, plus `release.yml` producing the shape it looks for.

The interesting decisions are in [[ADR-012]], but the one worth repeating here is **not
elevating**. [[ADR-011]]'s elevation path existed and was free to reuse. Tier 4 restore writes files
the user chose from their own disk; an update writes this program's own binaries fetched from the
network. With no code signing the app cannot prove those bytes are its own, so escalating to
administrator to install them is the shape a supply-chain attack wants. An unwritable install gets
the failed-update block the design already draws.

### §4.14 deleted more than it added

One flat `ListBox` over a grouped `CollectionView` replaced one `ListBox` per date. `GroupSelection.cs`
is gone as a file, so is the `Home`/`End` code-behind, so is the `SelectionChanged` routing, and
`SelectedItem` is an ordinary TwoWay binding again. The rule that used to live in explicit code is a
property of the structure.

`MainWindowSelectionTests` was rewritten rather than repaired — it existed to pin the workaround.
It now asserts two things the old shape could not express: that the Selector and the view model
agree, and that rows under different dates sit in one continuous `Items` collection.

### §4.7 needed the network, and that mattered

The entry said the deferred part was the asset and substituting real Lucide data would be a data
change. It was — but writing "real Lucide paths" from memory would have replaced hand-drawn
stand-ins with differently-hand-drawn stand-ins while calling them the real set, which is worse than
the honest state the entry described. **Two would have been wrong**: Lucide's current `settings`
gear is a 2.34-radius arc chain rather than the twelve-spoke star, and `triangle-alert`'s dot is
`M12 17h.01`.

`IconSetTests` guards the silent failure — a path WPF cannot parse renders as an empty box with no
error anywhere.

### §5 and §6

§5's list said "each becomes a bug if hard-coded" and "most likely to be violated by someone moving
fast", which is the definition of something a test should hold. Four are now source guards. **Each
was verified to fail** against a file violating all four, before that file was deleted — a guard
nobody has seen fail is a guard nobody knows works ([[guards-that-can-fail]]).

§6's redactor **fails closed**: an endpoint ID whose shape it does not recognise is masked wholesale
rather than passed through hopefully. One that works on the shapes it was written for and lets an
unknown one through is worse than none, because it teaches the user the output is safe.

## Decisions made

| Decision | Why |
|---|---|
| **Full auto-updater, not check-only** | User's call when asked. [[ADR-012]] records the shape and what it rules out. |
| **The updater never elevates** | See above and [[ADR-012]]. The failed-update block is the honest answer. |
| **Feed location is configuration, not a constant** | No remote exists yet; a compiled-in owner/repo would be §5's exact mistake. Unset hides the section. |
| **Window minimum 980 → 1124** | Audit §2.9c. The design's own six columns need 1084 in a window it allows to be 980 wide, so it does this to itself. Raising the floor invents no visuals and adds no interaction the design never specifies. |
| **§4.8 minor 3 settled, not changed** | The tray's `Back up automatically` keeps its trailing check. Windows draws no switch in a native context menu; a hand-drawn one would be the only control in the app ignoring the platform it sits in, in the surface the user reaches most often. Recorded as a decision so it stops being an open question. |
| **Error 9 renders in place, not as a dialog** | `06`'s placement table files it under Dialogs; §9's own text says "appears in Settings, in place, after Change folder…". The specific instruction wins. |
| **Pre-release tags read as their release version** | `1.4.0-beta.2` → `1.4.0`. Inventing an ordering would silently decide whether a beta is newer than the release it precedes. Flagged in [[ADR-012]] as needing a real decision before a `-beta` tag exists. |

## What did not work

**Shell heredocs kept mangling backslash escapes** when patching C# string literals — `\\n` arriving
as a literal newline, `\\Program` as `\Program`. Cost several rounds on `CommandRunner` and the JSON
fixtures. Switching to writing the patch script to a file, or to the `Edit` tool, fixed it. Worth
remembering: for anything with escapes, do not pipe it through a shell.

**`Progress<T>` collected nothing in tests** — it posts to a captured `SynchronizationContext`,
which a test does not have. Written up as [[a-progress-report-never-arrives-in-a-test]].

**`RoundtripKind | AdjustToUniversal` throws**, and it turned a serializer contractually incapable
of failing into one that failed on every call. Twelve tests went red at once, which is the system
working. [[the-serializer-that-never-throws-throws]].

**`RecognizesAccessKey` defaults to false**, so the Alt-accelerators would have shipped looking
wired. [[an-accelerator-shows-as-a-literal-underscore]].

## Open questions

**Three debts remain, and none is a commit:**

- **§4.15** — 0.5.1's dialog frosting has never been seen. Nothing in the suite can assert a blur
  rendered; it needs a human, alongside the rest of the by-eye checklist.
- **§2.2** — whether non-MSIX Wave Link installs exist. A fact about the world. The *mitigation* is
  now complete (§4.10 draws the "choose the settings file" route), so the answer costs nothing
  either way.
- **§2.4** — whether `[ComImport]` survives NativeAOT. Still no `[ComImport]` in the codebase;
  re-run when the endpoint inspector is ported.

**Owed before the updater is used in anger** — listed in the runbook: run the loop once end to end,
code signing, the pre-release rule, and setting the feed variables.

## Next

The by-eye pass, and the first real release — which is what turns the update loop from built to
verified.

## References

- [`operations/runbooks/releasing-and-updating.md`](../operations/runbooks/releasing-and-updating.md)
- [[ADR-012]] · [[ADR-011]]
- [[decisions-as-pure-functions]]
- [technical-debt.md](../technical-debt.md)
