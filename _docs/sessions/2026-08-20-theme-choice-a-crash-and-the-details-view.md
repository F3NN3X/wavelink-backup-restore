---
title: "Session: A theme choice, a crash, and what's in a backup"
status: published
created: 2026-08-20
updated: 2026-08-21
related_adrs: [ADR-013, ADR-014, ADR-015]
tags: [session, ui, wpf, analysis]
---

# Session: A theme choice, a crash, and what's in a backup

**Date:** 2026-08-20

## Goal

It began as *"make the app look like the design"* — a screenshot of the shipped app beside the
design's own render — and turned into four separate pieces of work, each one found by the previous
one:

1. the CONTENTS column, which drew three empty pills on every row;
2. a theme choice in Settings — Auto, Dark, Light, High contrast;
3. a crash that killed the app whenever *Back up now* was pressed in the window;
4. the INPUTS strip showing five channels of a nine-channel rig, and a details view for everything
   the row has no room for.

## What happened

**The empty pills were one missing attribute.** `<ContentControl>` in the tier-badge item template
had a `ContentTemplate` chosen by a `DataTrigger` and no `Content`. The trigger reads `DataContext`
and the template renders against `Content`, so the badge picked the right treatment from real data
and then drew its label against nothing. Written up as
[[a-chip-draws-its-box-and-not-its-label]] — the part worth keeping is that **every source-text
guard in that file still passed**, because the markup was intact and only the render was wrong.
The new guard instantiates the row template and reads the labels back out of the tree.

**The theme choice is a decorator, not a second source of truth** ([[ADR-013]]). Six places read
the palette through `ISystemTheme`; wrapping that interface meant none of them changed and none of
them can disagree, and a preference change re-raises the same `Changed` event an OS switch raises,
so a pick repaints the window, the dialogs, the tray menu and the tray icon by the route that
already existed. It lives in `shell.json`, for the reason `settings.json` describes itself on the
same screen.

**The crash was diagnosed from the Windows event log, not from the code.** The user's report tied
it to adding channels; the CLI captured all nine channels without complaint, which ruled Core out
in one command. `Get-WinEvent` had the exception and the stack, three times over: the backing-up
progress strip had moved the capture into a `Task.Run`, and the tray refresh that follows a capture
went with it. Writing `TaskbarIcon.Icon` from a thread-pool thread throws, and an `async void`
handler has nothing above it. Written up as [[pressing-back-up-now-closes-the-whole-app]]; the
guard now lives on the two refresh methods rather than at the call site, because fixing the caller
fixes one caller and this bug arrived exactly that way.

**The nine-channel rig broke two things at once** ([[ADR-014]]). The strip drew five cells because
it allocated five and the panel was `Columns="5"`; and genericness — the app's amber, its word for
*Wave Link reset your settings* — was decided against the store's peak, so raising the peak
repainted every older backup ([[every-older-backup-turns-amber-after-adding-a-channel]]). The strip
is now as wide as the widest configuration in the store, the labels shorten and then disappear as
cells narrow, and collapse is a drop against the previous snapshot.

**And the details view answers the question the row cannot** ([[ADR-015]]). `ConfigurationDetail`
is new pure Core: channels, their effect chains in order with vendor, category, bypass and
built-in-versus-VST3, the mixes and their output devices. It reads the snapshot's own settings file
on demand rather than extending `manifest.json`, so it works on every backup already on disk. The
dialog is the settings dialog's shape, opened with Ctrl+I, the row's overflow menu or a
double-click.

**Everything was verified on the real app**, driven through UI Automation: the button pressed, the
process still alive, the snapshot written with all nine inputs, the theme flipped and persisted
across a restart, and the details dialog opened on the live rig — nine channels, an eleven-effect
chain in order, two channels correctly flagged `NOT IN ANY MIX`.

**One more the next morning, before the commit: the list had never scrolled by wheel.** The scroll
bar worked and every key worked, which is why it had survived — a `ScrollViewer` marks every wheel
event handled whether or not it scrolled, and the list's own is switched off so the header and the
rows share one scroll position. Written up as [[the-list-will-not-scroll-with-the-wheel]]. Two
attempts at the test were wrong before one failed correctly, and both are in the gotcha: raising
the event on the ListBox proves nothing, and `RaiseEvent` raises one event where real input raises
two.

## Decisions made

| Decision | Recorded in |
|---|---|
| The theme preference is a decorator over `ISystemTheme`, stored in `shell.json`; Windows' high contrast outranks it | [[ADR-013]] |
| The health strip is as wide as the store's widest rig; labels yield, channels never do; collapse is a drop against the previous snapshot | [[ADR-014]] |
| The details view reads the snapshot's own settings file on demand; `manifest.json` is not extended | [[ADR-015]] |
| Three surfaces now exist that the design package does not specify, and they are owed a by-eye pass | `technical-debt.md` §8.2 |

## What did not work

**Reading the source to find the crash.** The threading fault was visible in the code once known,
and it was not what any of the reading suggested — the report said "adding channels broke backups",
and the code that changed was a progress bar. Two commands settled it: `wlbackup backup` (Core is
fine) and `Get-WinEvent` (here is the stack). **Reach for the event log first on a WPF app that
disappears**; a .NET process that dies of an unhandled exception always leaves one, and this app
leaves nothing else (§8.1).

**Two guesses at the label width were wrong before one was measured.** 6.4px per character gave a
budget of 8 and quietly contradicted an existing test; the real figure is 6.24. The flat cap of ten
characters that had been there since the strip was built never fitted either — ten characters
measure 62.4px in a 56.8px cell — so `WAVE MIC 1` had been overflowing its cell all along. **The
guard is now a measurement**, not a constant with a comment.

**And the same shape twice in one session:** the tier badges were 224px of content in a 200px
column, which only became visible once the labels rendered at all. Both fixes are held by tests
that render and measure rather than tests that read markup.

**A test was deleted rather than adjusted**, deliberately and with the reason in the file:
`More_than_five_inputs_shows_the_first_five` asserted the truncation this session removed. It was
written against `technical-debt.md` §5's own note that five is one user's rig — and pinned the
truncation instead of the alignment it was worried about.

## Open questions

- **§8.1 — what an unexpected exception should look like.** The design specifies twelve errors and
  none of them is "something unexpected happened". A crash log beside `shell.json` needs no design
  and is not written yet; the surface does.
- **§8.4 — the list does not virtualise, and its markup says it does.** Measured: 500 of 500 rows
  realised. The structural fix is the same one that would have fixed the wheel — let the ListBox
  scroll itself — and it moves the column header's gutter binding, which is the thing the audit's
  §1.1 was about. Not a change to make in the same breath as a scroll fix.
- **§8.2 — three surfaces nobody has looked at.** The theme segments, the wide strip, the details
  dialog. In particular the strip at twelve cells, where labels are three characters, and the
  details dialog in a real high-contrast scheme.
- **Whether the details dialog should offer *Restore this backup*.** It is the screen where someone
  decides, and it currently ends in *Close* alone. Deliberate for now — the restore path has its own
  confirmation and its own irreversibility rules, and reaching it from two places doubles the ways
  in without doubling the thought.

## Next

Concretely, from cold: **the by-eye pass**. Run the app on a 150% display, open the three new
surfaces, and work down
[screen-1-by-eye-checklist.md](../operations/design/screen-1-by-eye-checklist.md), adding rows for
them as you go — §4.15's dialog frosting is owed the same visit, so it is one sitting rather than
two.

After that, §8.1's crash log: a file beside `shell.json`, written from an
`AppDomain.UnhandledException` handler, holding the exception and the stack. No design needed, and
it is what turns the next report from *"it crashed"* into a line number.

## References

- [[ADR-013]] · [[ADR-014]] · [[ADR-015]]
- [[pressing-back-up-now-closes-the-whole-app]] · [[a-chip-draws-its-box-and-not-its-label]] ·
  [[every-older-backup-turns-amber-after-adding-a-channel]]
- `_docs/technical-debt.md` §5 (corrected) and §8 (new)
