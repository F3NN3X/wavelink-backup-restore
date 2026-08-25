---
title: "Technical Debt"
status: published
created: 2026-08-16
updated: 2026-08-25
tags: [meta, technical-debt, priority]
---

# Technical Debt

What is built and not right, what has never run, and what is known-wrong deliberately.
Distinct from [dev-phases/](dev-phases/README.md), which is for things not built yet.

Be blunt. A debt list that flatters the project is useless.

**Closed entries live in [archive/technical-debt-closed.md](archive/technical-debt-closed.md).**
Phases 1–6 shipped `Core`, `Cli` and a WPF shell, and the debt-clearing passes of 2026-08-17
through 2026-08-22 closed everything in §1, §4, §5, §6 and §7 that a commit can close, plus most
of §8. Those entries moved out on 2026-08-25 with their reasoning intact, because a list of ticked
boxes buries the few things still owed. Section numbers are unchanged in both files, so a
reference to §4.18 from an ADR or a commit message still resolves.

**Two more closed on 2026-08-25.** §2.4 — porting `WindowsAudioEndpointInspector` answered it, and
the answer was no: classic `[ComImport]` does not survive trimming, in two distinct ways, and
source-generated COM does. §7.6 — the experiment ran on the reference rig, and Wave Link resolves a
plug-in by `PluginId`, repairing `FilePath` behind it. Both evidence tables are in
[the archive](archive/technical-debt-closed.md).

## What is actually left

Two entries, and **only one of them is work.**

| | Why it cannot be closed here |
|---|---|
| **§8.2** — the by-eye sittings owed by the §8.6 verdict and matrix | The 2026-08-22 sitting checked the *old* surfaces (the five-slot strip, the pre-matrix dialog) and closed what it saw; the verdict that replaced the strip and the matrix that joined the dialog have not been looked at on a machine yet. Nothing in the suite can assert that a layout looks right. |
| **§3** — the known-wrong list | Not owed work at all. Four choices made with eyes open, recorded so they are not "discovered" later and fixed by someone who does not know they were decided. It stays here permanently. |

§5 also stays, in reduced form: the resolution narrative is archived, but the table of numbers that
look like constants is a standing hazard list, not a closed entry.

### What closing looks like

The tier list this section used to carry had three tiers. **Tier 1 — closeable by a commit, no
human in the loop — is empty:** all three closed on 2026-08-22 (§8.1's crash report, §8.3's watch
rule, and the by-eye checklist that Tier 2 depends on). What remains is a human with eyes, and a
fact from outside this repo.

**Tier 2 — closeable by a human with eyes, no code change.** One look, one machine, against item 5
of [operations/design/screen-1-by-eye-checklist.md](operations/design/screen-1-by-eye-checklist.md).
It needs a real Wave Link install, a store with a five-input snapshot and a nine-plus-channel one,
and both a light theme and a real high-contrast scheme active.

Items 1–4 of that checklist are ticked: the 2026-08-22 sitting confirmed §4.15's frosting, §8.2's
own three surfaces, §8.4's header-to-row alignment after the scroll fix, and §4.9's `WlDangerSoft`
in a real high-contrast scheme. **Only item 5 is owed**, and it exists because that sitting's one
finding — the INPUTS strip read cramped past nine cells — shipped as §8.6 the same day, replacing
the surface the sitting had just checked.

**The rigs are seeded, not built.** [`tools/seed-fixture-store.ps1`](../tools/seed-fixture-store.ps1)
writes a throwaway store holding all five — five named inputs, a collapsed two-input rig, nine
channels, twelve, and one with long effect chains — so the sitting starts at the looking rather
than at half an hour of channel surgery in Wave Link. Point the app's backup folder at it through
Settings, and back afterwards.

| What to look at | What closing looks like |
|---|---|
| **The INPUTS verdict** on a five-input row, and on a collapsed rig | Check-circle in the ok colour, "Complete", mono sub-line reading `5 INPUTS · ALL NAMED`; on the collapsed rig a warning triangle in warn, "Only part of your setup", `UNNAMED` in warn. The word stays full-strength either way — colour is never the only signal |
| **The verdict at nine-plus channels**, where the old strip read cramped | The cell no longer prints a name per channel, so it should read as *less* crowded than the finding it replaced. This is the legibility fix confirmed on pixels rather than by inference |
| **The details dialog's matrix** ("WHERE EACH INPUT IS HEARD") | One cell per mix column on each channel row; a dot exactly where that channel's routing line says it feeds; a channel in no mix shows all-empty cells. In light and again in real high-contrast — nothing clips, and the grid reads as the board it is |

When item 5 is ticked, §8.2 closes for good and nothing in §8 remains but the entries.

**Tier 3 — closeable only by a fact from outside this repo — is now empty.** §2.4 left it on
2026-08-25 when the inspector was ported; §7.6 left it the same day when the experiment ran. Both
had sat there because nothing in this repo could answer them, which was true right up until
somebody went and looked.

---|---|---|---|
| **§7.6** — where a restored plug-in should go when its own folder is unwritable | One reversible experiment on a live Wave Link: copy one on-channel plug-in to the user-level VST3 folder, rename the shared copy, restart, and see whether the channel still loads and whether `FilePath` was rewritten. The full protocol is in [audits/2026-08-20-plugin-resolution-and-elevation.md](audits/2026-08-20-plugin-resolution-and-elevation.md) | **The user will run it on the reference rig (status 2026-08-22).** Take a backup first (the experiment is reversible but not free). The answer also settles whether tier 2's drift check could key on `PluginId` rather than path, which is a second debt this one entry closes | §7.5 already removed the prompt in the common case, so the *recommendation* pending the answer is "probably do not build the fallback". The experiment is worth having regardless, but it is an hour of careful work on a live install, not a task to slot into a quiet afternoon |

---

## 3 · Known-wrong deliberately

Choices made with eyes open. Listed so they are not "discovered" later and fixed by someone
who does not know they were decided.

**Snapshots are not portable between machines, and we are not fixing that.** Endpoint IDs
embed device serials; plugin paths are absolute. "Export a chain" is a *different feature*
built on `AudioPluginConfigurations` alone, and it is out of scope. The UI labels snapshots
machine-local instead. See [[restored-backup-has-dead-channels]].

**Licences are not captured, and the UI says so rather than working around it.** Copying a
`.vst3` restores the code, not the authorisation — those vendors authorise via registry,
machine-bound licence files elsewhere, or an online account. Nothing licence-shaped exists in
the vendor folders we copy. Tier 4 gets a working plugin on the same machine; on a rebuild the
user still reinstalls and re-authorises. See [[restored-plugin-demands-a-licence]].

**`EBWebView\` is never captured**, despite being inside `LocalState`. ~100 MB of WebView2
browser profile — shader caches, IndexedDB, code caches. Capturing it turns a 470 KB snapshot
into a 100 MB one, and restoring it can wedge the UI.

**Wave Link's own AutoBackups are captured but never managed.** We copy them as payload
because they carry history our first run will not have. We do not prune, rotate or write them, and
a restore never puts them back — they are evidence, not payload. *(True since 0.6.0. It was stated
here as settled fact from 2026-08-16, and the code did not do it until phase 6 §8 — found by the
spec-coverage pass, which is the argument for having written one.)*

---

## 5 · Numbers that are not constants

**This is a standing hazard list, not an open debt.** Four of the five are enforced by guard tests
in `SourceGuardTests`; the fifth is held by `HealthFingerprint` and `InputSlotsTests`. The table
stays because the traps do — the audit and the correction that closed it are in
[the archive](archive/technical-debt-closed.md).

Measured on one machine on 2026-08-15. Each becomes a bug if hard-coded. Reproduced from
`SPEC.md` §11 because this is the list most likely to be violated by someone moving fast.

| Looks like a constant | Actually | Do this instead |
|---|---|---|
| `Elgato.WaveLink_g54w8ztgkx496` | Stable per Store identity, but never assume | Glob `Elgato.WaveLink_*` |
| 5 inputs / 43 KB | One user's rig | Compare against *that user's* previous snapshot |
| `C:\Program Files\Common Files\VST3` | Default, and overridable | Resolve from `FilePath`; standard dirs are fallback only |
| 3.3.0.4108 | A beta; release users are on 3.2.9 | Record it, warn on mismatch, never gate on it |
| `%LOCALAPPDATA%` | Redirected on some corporate/OneDrive setups | `Environment.GetFolderPath`, never a composed string |

---

## 8 · Incurred 2026-08-20, building past the design package

Three surfaces now exist that the design package does not specify, and one hole the whole session
walked through. Recorded here rather than in the audit, because the audit is a point-in-time
reading of the app against the package and this is a standing cost.

*8.1, 8.1a, 8.3, 8.4, 8.5 and 8.6 are all closed — see
[the archive](archive/technical-debt-closed.md). What §8.6 left behind is pixels, not code, and it
rides on §8.2 below.*

### 8.2 Three surfaces have no design, and no by-eye check — **open, needs a human**

| Surface | Built to | Package says |
|---|---|---|
| Settings → `HOW IT LOOKS`, the four theme segments ([[ADR-013]]) | The package's rules — section label, `WlBg` block, hairlines, the stepper's segment geometry, `WlToggle`'s own checked treatment | Nothing. The prototype draws a caption-bar sun icon; the README specifies a gear that opens Settings, and that is what ships |
| The N-cell INPUTS strip ([[ADR-014]]) | The design's own five-cell strip, widened by arithmetic over a measured character width | *"Five equal flex cells"* — a rig of exactly five |
| `What's in "…"`, the details dialog ([[ADR-015]]) | The settings dialog's shape and vocabulary, reused wholesale | Nothing. Four screens are designed and this is not one of them |

**None of them is a new visual idea** — that was the constraint each was built under, and it is why
they read as part of the app. But three surfaces now exist that no design pass has looked at, and
the same is true of them as of §4.15: nothing in the suite can assert that a layout looks right.
They belong on [the by-eye checklist](operations/design/screen-1-by-eye-checklist.md), which is
still owed a human.

**Specifically unchecked by eye:** the four-segment control at 100% and 150% scaling; the strip at
nine and at twelve cells, where the labels are four characters and three; the details dialog in
light and in a real high-contrast scheme; and the dialog's height on a rig with several long effect
chains, where it hits its 720px cap and scrolls.
