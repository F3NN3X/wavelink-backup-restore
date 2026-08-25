---
title: "ADR-018: A third notification, and an update notice on the strip"
status: accepted
created: 2026-08-25
updated: 2026-08-25
tags: [decision, updates, notifications, ui]
---

# ADR-018: A third notification, and an update notice on the strip

**Status:** Accepted
**Date:** 2026-08-25

## Context

The design says *"Check for updates on its own — weekly, on by default."* The code did check
weekly, and the setting was on by default. But the check ran from the **Settings dialog's `Loaded`
handler**, so the cadence was really *"weekly, the next time you happen to open Settings"*.

The Settings dialog is a place people visit once, to choose a backup folder, and then never again.
A user who never returns was never told a fix existed — including a fix for something that had bitten
them. Nothing about that was visible: the setting said on, the interval was seven days, and the code
matched both. The gap was entirely in *where the check was attached*.

That leaves two questions, and they are separable: when does the app look, and where does it say so.

## Decision

**The check runs at startup**, off the UI thread, honouring the same weekly interval and the same
`CheckForUpdates` setting.

**An available update is said in three places**, all reading one field:

| Surface | What it says | When |
|---|---|---|
| Status strip | A fourth segment — `· UPDATE 0.7.5 AVAILABLE` | Whenever the window is open |
| Tray menu | A line above `LAST BACKUP`, which opens Settings | Whenever the menu is opened |
| Tray balloon | `Update 0.7.5 is available.` | Once per version |

`TrayNotificationKind` gains a third member. The design allows two.

## Alternatives considered

| Option | Why not |
|---|---|
| Leave the check in the Settings dialog and only add the strip segment | The segment would be blank until something else triggered a check, so the fix would be cosmetic. The check's location *is* the bug |
| Check on every launch rather than weekly | A network call per launch for a figure the design deliberately set to seven days. Weekly is not an arbitrary number — it is the rate at which being interrupted about a version stays tolerable |
| Strip only, no notification | The app is tray-resident and `closingHidesToTray` is on by default, so the window is shut most of the time. A notice only on the strip is a notice most users never meet |
| Balloon only | Fires once and is gone. Someone who dismisses it or is away from the machine has no standing reminder, and the strip and menu cost almost nothing next to it |
| A new banner or dialog in the main window | A thirteenth surface, which [[ADR-004]] exists to prevent and §8.1a already refused once for the crash report. The strip is the app's existing line for facts about state, and an update is a fact about state |
| Persist the found version so a restart keeps the notice | A new settings field for a case that only arises when the app is restarted inside the weekly window. Held in memory instead; *Check now* is always there. Recorded as a known limit rather than hidden |

## Consequences

**This enables:** a user learning about a fix without visiting a settings screen — which is the
entire point, and was not true before.

**This spends the design's "exactly two notifications" budget.** That rule is worth reading
precisely, because it is nearly right here: *"A successful backup NEVER notifies. A safety net that
congratulates itself weekly gets muted, and then it is not a safety net."* The rule is about the app
talking about **itself doing its job** — routine, repeating, self-congratulatory. An update notice is
none of those. It is rare, it is about a version rather than a run, and it fires **once per version**
rather than once per check.

The guard the rule was actually protecting is unchanged and still enforced by the type:
`TrayNotifications` has no method that takes a completed backup, and a test asserts it stays that
way.

**This rules out** the update notice ever becoming a nag without someone deleting a test. Per-version
once-ness is the load-bearing part: per-episode would mean once ever (an update stays available until
installed), and per-process would mean once per launch until the user gave in.

**The strip's not-found path now carries it too.** `WAVE LINK NOT FOUND ON THIS COMPUTER` replaces the
whole line, correctly — everything else there is a fact about a configuration that could not be read.
An update is not; it is a fact about this app, and it is still true. The rule that empties the rest of
the line does not reach it.

**A known limit:** the found version lives in memory. Restarting inside the weekly window loses the
notice until the next check is due. The alternative was a persisted settings field for a narrow case,
and *Check now* covers it.

**Revisit if:** the design package produces its own update surface, in which case that one wins and
this becomes the local version to retire — the same rule `screens/13` and `14` already carry.

## References

- `screens/12` — the tray menu, and "Notifications — exactly two"
- [[ADR-004]] — Core is headless behind thin shells; the shells own presentation
- [[ADR-012]] — check-only updates with a staged swap
- `src/WaveLinkBackup.App/Hosting/TrayNotifications.cs` — the decision as a pure function
