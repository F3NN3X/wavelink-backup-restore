---
title: "Session: The update path meets a real release"
status: published
created: 2026-08-25
updated: 2026-08-25
tags: [session, updates, ui, notifications]
---

# Session: The update path meets a real release

**Date:** 2026-08-25

## Goal

Cut v0.7.5, then test the in-app update by running a 0.7.4 build against it. Along the way, add an
"update available" notice, because the app had no way to mention one.

## What happened

**v0.7.5 shipped** — the debt-list work from the earlier session, squash-merged and tagged, with
the release workflow publishing both archives and their checksums.

Then the update path was exercised for the first time, and it did not survive contact.

**Nothing about updates worked, in three separate ways**, each hidden behind the one before it:

1. **The app never mentioned an update existed.** The weekly check ran from the Settings dialog's
   `Loaded` handler, so *"check for updates on its own — weekly, on by default"* really meant
   *"weekly, the next time you happen to open Settings"*. Fixed by moving the check to startup and
   the tick, and by saying so on three surfaces — see [[ADR-018]].
2. **Every update failed its checksum.** [[every-update-fails-its-checksum]] — the feed paired the
   app's archive with the CLI's digest, and had since 0.7.2.
3. **The install then silently did nothing.**
   [[the-update-installs-nothing-and-says-nothing]] — one attempt at the directory swap, and no
   destination for the failure to be reported to.

Each was only findable once the one above it was fixed. The checksum error masked the swap failure
completely, and the missing notice meant nobody would have reached either.

**Help gained a standard About section**, composed from `AboutDialogModel` rather than restating
its strings.

## Decisions made

| Decision | Recorded in |
|---|---|
| The check runs on its own; an update is said on the strip, the tray menu, and once per version | [[ADR-018]] |
| The interval moves from the design's week to a day | [[ADR-018]] |
| A third tray notification, past the design's "exactly two" | [[ADR-018]] |

## What did not work

**Reading the code to find the swap bug.** The logic is correct — roll back, restore the old
install, relaunch either way. Reproducing the two renames by hand, with nothing running, succeeded
instantly. The bug was not in the logic but in its patience, and no amount of reading finds that.
What found it was the *state on disk*: a `.staged` directory holding the new version beside an
install still holding the old one.

**Trusting my own verification script.** A first attempt to check the published checksum reported a
mismatch — `98` against a 64-character digest. The release was fine; `Invoke-WebRequest` had
returned bytes, and `98` is the ASCII code for `b`, the first character of the real hash. Two
minutes were spent believing the release was broken. **A verification that disagrees with a
`curl` of the same URL is a bug in the verification.**

**Driving the app's UI to take screenshots.** `SetForegroundWindow` is refused for a background
process, so two attempts captured whatever happened to be on screen — including unrelated windows,
which were deleted immediately. Rendering dialogs offscreen through the test harness worked, needs
no focus, and does not require switching the whole desktop into high contrast to look at a
high-contrast dialog.

**Assuming the fixture matched production.** Every payload in `UpdateFeedTests` carries one archive
and one `.sha256`. With one of each, *"take any asset ending .sha256"* and *"take the right one"*
are the same test. The 0.7.2 packaging change gave releases a second artifact and never touched the
fixtures, so nothing prompted anyone to look.

## Open questions

**The published v0.7.5 still contains all three bugs.** The fixes only take effect once a build
containing them is the one doing the updating, so updating *from* the released 0.7.5 will still
fail. Cutting the next release from this branch is what breaks the cycle — and it cannot be
verified end to end until there is a release after it.

**The swap failure has never been seen with the retry in place.** The retry is reasoned rather than
demonstrated: the original race was not reproducible on demand. What is tested is the breadcrumb,
which is the part that turns a silent failure into a reported one.

**Whether the notification budget stays at three.** [[ADR-018]] argues the third; the design says
two. If the design package ever produces its own update surface, that one wins.
