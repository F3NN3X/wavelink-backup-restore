---
title: "Session: the two things phase 6 deferred, and a settable backup schedule"
status: published
created: 2026-08-19
updated: 2026-08-19
related_adrs: [ADR-010, ADR-011]
tags: [session, capture, restore, wpf]
---

# Session: the two things phase 6 deferred, and a settable backup schedule

**Date:** 2026-08-19

## Goal

Close the two items phase 6 closed its session record with — §4.17 (the shell cannot ask for a
tier 4 restore) and §4.18 (the preset heuristic has never met a real vendor folder) — and add a
control for when automatic backups happen.

## What happened

**§4.18 was checked first, because it was cheap and it changed the scope of everything else.**
The entry specified the check as one capture and one look at `plugins.json`. This machine turned
out to *be* the reference rig — FabFilter, Supertone Clear and the Wave Link package all present
— so the check was a build and a `wlbackup backup` into a scratch store.

The heuristic was wrong, in the way the entry feared: **capturing the wrong thing quietly.** It
found `%APPDATA%\FabFilter\Pro-Q 4` first try, exactly as designed, and that folder holds an
interface default, a MIDI map and a cache. The 172 `.ffp` files were in
`Documents\FabFilter\Presets\Pro-Q 4\`. Clear was capturing two crash reports and calling them
presets.

That turned §4.18 from a checkbox into a snapshot-format change, so it was worth stopping to ask
how far to take it before building anything. [[ADR-010]] records the answer and the four
alternatives.

**§4.17's blocker was never the code.** Tier 4 restore has been built and tested since phase 6;
the shell could not ask for it because elevation had no designed surface. So the surface was
written first — `screens/13-elevation.md`, in `06-errors.md`'s own shape and under its own rules
— and the code followed it. [[ADR-011]] records why the shell relaunches itself rather than
shipping a helper or running elevated all day.

**The timing controls found two defects on the way**, both in the trap the new steppers would
have landed in. The Settings dialog's save callback wrote the file and stopped, so every control
on that screen took effect on the next launch rather than immediately — including the tier
toggles shipped in 0.6.0, despite a comment in `Compose` saying the closure existed precisely so
they would not. And the keep-count stepper's `−` and `+` had **no handler at all**: declared,
bound, never wired, through two phases. Both are §4.20 and
[[a-settings-control-moves-and-nothing-happens]].

Three commits on `feat/preset-roots-elevation-schedule`. Tests 1,146 → 1,207.

## Decisions made

| Decision | Recorded in |
|---|---|
| Read both preset roots; snapshot paths name their root; `plugins.json` schema 2 | [[ADR-010]] |
| Elevate by relaunching the shell headless for one restore, never otherwise | [[ADR-011]] |
| The declined-elevation state is error **13**, inline and **neutral** | `screens/13-elevation.md` |
| The interval is a settable ladder; the daily backup is separate and the cap never suppresses it | `screens/14-backup-timing.md` |
| Design files authored in this repo live in `screens/` with frontmatter, as a last resort | [README.md](../README.md) → *operations/* |

## What did not work

**Treating §4.18 as a verification task.** The entry framed it as "check by eye, alongside
0.5.1's visual items" — a look, not a change. That framing was reasonable and it was wrong: the
thing being checked was a *destination*, and a wrong destination is not visible in any output
the app produces. The check had to be run against the machine's real folders, and once it was,
the fix was unavoidable and touched the snapshot format.

**The first plan for the preset fix was to reorder the candidates and keep one source per
plug-in.** It was abandoned when it became clear that the expensive part — recording which root
a file came from, so restore can reverse it — was required by *any* fix that adds a second
location. Once that cost is paid, taking both roots is nearly free and loses nothing.

**Two attempts at patching markdown through shell heredocs** produced mangled escape sequences
(`\a.vst3` where `\\a.vst3` was written) and one bash parse error. Switching to a script file in
the scratchpad fixed it. Worth knowing before doing it a third time.

**Assuming the app's existing controls worked.** Both defects in §4.20 were found by accident,
while looking for where to put a new stepper. Neither had a failing test; both had passing ones.

## Open questions

- **The heuristic is still a heuristic.** Two vendors were checked, not twenty. A vendor that
  keeps presets directly in `Documents\<Vendor>\` with no `Presets` subfolder is not captured,
  deliberately — the project-library risk is worse than the miss — but nobody has looked for one.
- **The elevated restore reports `Unconfirmed`, always.** The child verifies from Wave Link's log
  and the parent cannot see that verdict across a process boundary. Honest, and less than the
  restore actually knows. Fixing it means IPC, which [[ADR-011]] rules out for now.
- **Nobody has clicked through the elevated restore end to end.** The headless path was exercised
  against the real binary (`--restore does-not-exist --with-plugins` exits 6, opens no window),
  and the row renders in all three themes, but the actual UAC prompt has not been answered by a
  human on a machine where a tier 4 snapshot exists.
- **The daily backup has never seen a real 03:00.** Every test moves a fake clock.

## Next

Phase 7 — release. The privacy gate (§6, "copy diagnostics" with redaction) is what 1.0 is
gated on, and it is the first place the settings tree is *rewritten* rather than copied, which
makes SPEC §3's device-ID rule real for the first time.

Before that, two small things this session leaves on the floor: the branch is unmerged, and the
three open debt items from phase 6 (§4.16 rehashing, §4.19 whole binaries in memory) are
untouched and still want a measurement rather than a fix.
