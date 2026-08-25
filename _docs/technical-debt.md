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
boxes buries the four things still owed. Section numbers are unchanged in both files, so a
reference to §4.18 from an ADR or a commit message still resolves.

## What is actually left

Four entries, and **none of them is a commit somebody has not written.**

| | Why it cannot be closed here |
|---|---|
| **§7.6** — where a restored plug-in should go when its own folder is unwritable | One reversible experiment on a live Wave Link, written up in the entry. Not a defect — an unanswered question, and §7.5 already removed the prompt in the common case, so the answer may well be "leave it". **Status 2026-08-22: the user will run the experiment on the reference rig; it stays open until that run.** |
| **§8.2** — the by-eye sittings owed by the §8.6 verdict and matrix | The 2026-08-22 sitting checked the *old* surfaces (the five-slot strip, the pre-matrix dialog) and closed what it saw; the verdict that replaced the strip and the matrix that joined the dialog have not been looked at on a machine yet. Nothing in the suite can assert that a layout looks right. |
| **§2.4** — whether `[ComImport]` interop survives NativeAOT | There is still no `[ComImport]` in the codebase. `WindowsAudioEndpointInspector` has not been ported, so the interop that prompted the doubt cannot be exercised. A re-open trigger, not an open task: re-run when endpoint inspection lands. The AOT publish itself already works. |
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

| What to look at | What closing looks like |
|---|---|
| **The INPUTS verdict** on a five-input row, and on a collapsed rig | Check-circle in the ok colour, "Complete", mono sub-line reading `5 INPUTS · ALL NAMED`; on the collapsed rig a warning triangle in warn, "Only part of your setup", `UNNAMED` in warn. The word stays full-strength either way — colour is never the only signal |
| **The verdict at nine-plus channels**, where the old strip read cramped | The cell no longer prints a name per channel, so it should read as *less* crowded than the finding it replaced. This is the legibility fix confirmed on pixels rather than by inference |
| **The details dialog's matrix** ("WHERE EACH INPUT IS HEARD") | One cell per mix column on each channel row; a dot exactly where that channel's routing line says it feeds; a channel in no mix shows all-empty cells. In light and again in real high-contrast — nothing clips, and the grid reads as the board it is |

When item 5 is ticked, §8.2 closes for good and nothing in §8 remains but the entries.

**Tier 3 — closeable only by a fact from outside this repo.** No commit and no amount of looking
at this codebase closes these. They are listed last not because they are least important but
because *nothing can be done until the external fact arrives* — so the only action available is to
keep the cost of the answer being wrong at zero, which for both is already done.

| Item | The external fact it waits on | What to do in the meantime | Why the wait is acceptable |
|---|---|---|---|
| **§7.6** — where a restored plug-in should go when its own folder is unwritable | One reversible experiment on a live Wave Link: copy one on-channel plug-in to the user-level VST3 folder, rename the shared copy, restart, and see whether the channel still loads and whether `FilePath` was rewritten. The full protocol is in [audits/2026-08-20-plugin-resolution-and-elevation.md](audits/2026-08-20-plugin-resolution-and-elevation.md) | **The user will run it on the reference rig (status 2026-08-22).** Take a backup first (the experiment is reversible but not free). The answer also settles whether tier 2's drift check could key on `PluginId` rather than path, which is a second debt this one entry closes | §7.5 already removed the prompt in the common case, so the *recommendation* pending the answer is "probably do not build the fallback". The experiment is worth having regardless, but it is an hour of careful work on a live install, not a task to slot into a quiet afternoon |
| **§2.4** — whether `[ComImport]` interop survives NativeAOT | A fact about a piece of code that does not exist yet: `WindowsAudioEndpointInspector` has not been ported, so the interop that prompted the doubt cannot be exercised. Re-run when endpoint inspection lands | Nothing. The AOT publish itself already works (3.2 MB binary, zero trim warnings), so the NativeAOT option stays open at no cost until the inspector arrives | This is a *re-open* trigger, not an open task: the entry says so, and the action is "when X lands, do Y". It is Tier 3 because X is outside this repo's current scope (post-1.0), and pretending it is ready to close would be flattery |

---

## 2 · Unverified assumptions

Things the design rests on that **nobody has checked**. Each one, if wrong, invalidates real
work — so each has a cheap check attached and an owner phase.

*2.1, 2.2 and 2.3 are answered — see [the archive](archive/technical-debt-closed.md).*

### 2.4 Whether `[ComImport]` interop survives NativeAOT

`WindowsAudioEndpointInspector` is ~80 lines of hand-declared Core Audio COM interfaces. AOT
and `[ComImport]` need care. If it does not survive, the NativeAOT CLI option in §1.5
evaporates and the answer there is forced.

**Phase:** 7, but cheap to check earlier and worth doing before the §1.5 decision is framed.

> **Partially answered 2026-08-16 (phase 4) — and the part that matters is still open.**
>
> A NativeAOT publish of `wlbackup` **succeeds**, produces a **3.2 MB** binary (against 70.2 MB
> self-contained), and that binary runs correctly against a real Wave Link install. The IL
> compiler emitted **zero trim/AOT warnings**, so nothing in `Core` or `Cli` is
> AOT-incompatible today.
>
> **But this does not answer the question this entry asks.** `WindowsAudioEndpointInspector`
> has not been ported — there is no `[ComImport]` in the codebase — so the interop that
> prompted the doubt was never exercised. When endpoint inspection lands, re-run this and
> expect it to be the interesting case.
>
> **Build requirement worth knowing:** the AOT link step invokes `vswhere.exe` unqualified and
> fails with a misleading `MSB3073 ... exited with code 123` if it is not on `PATH`, even
> though the MSVC toolset is installed. Adding
> `%ProgramFiles(x86)%\Microsoft Visual Studio\Installer` to `PATH` fixes it. CI's
> `windows-latest` image has this wired already.

> **Related signal, 2026-08-16.** The phase-1 probe ran as a .NET 10 file-based app, which
> defaults to trimming-friendly settings, and reflection-based `JsonSerializer` threw
> `InvalidOperationException: Reflection-based serialization has been disabled`. That is not a
> product bug — but it is the same constraint AOT imposes. **`Core` should avoid
> reflection-based serialization regardless of the AOT decision**, using `JsonDocument`,
> `JsonNode` and `Utf8JsonWriter` (all reflection-free) rather than
> `JsonSerializer.Serialize<T>`. Doing that from the start keeps §1.5's NativeAOT option open
> at no cost; discovering it in phase 7 would mean rewriting the manifest layer.

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

## 7 · Design decisions that outdated shipped code

The phase-5 design package (handoff part 2) closed the six gaps **and** made four behavioural
decisions that contradicted code already written and tested. None of that was a mistake in either
place: the code was built to the best spec available, and the design had since decided better.

*7.1–7.5 all shipped — see [the archive](archive/technical-debt-closed.md). **§7.6 is not a
defect** — it is a question §7.5 raised, with a reversible experiment attached and a
recommendation that the answer may well be "leave it alone".*

### 7.6 Where a restored plug-in should go when its own folder is unwritable — **OPEN, needs one experiment**

**Not a defect. An unanswered question**, raised by §7.5 and recorded because answering it wrongly
would break a channel silently — the failure mode [[vst3-backs-up-as-nothing]] and §4.18 both
already cost this project a phase.

**Full method, findings and the experiment protocol:**
[audits/2026-08-20-plugin-resolution-and-elevation.md](audits/2026-08-20-plugin-resolution-and-elevation.md).
Summarised here because that is where a debt belongs; the audit is where the commands are.

**The question.** Tier 4 restores a `.vst3` to the absolute `FilePath` the settings recorded. When
that folder refuses a write, the alternative is the user-level VST3 location
(`%LOCALAPPDATA%\Programs\Common\VST3`), which needs no administrator. Whether that works turns on
one thing nobody has verified: **does Wave Link resolve a channel's plug-in by `PluginId`, or by
`FilePath`?**

**What is measured** (this rig, 2026-08-20): Wave Link is JUCE-based; every third-party `PluginId`
in `Settings.json` matches a cache `uniqueId` exactly, so a path-independent identity **exists**;
the only configurable scan folder is VST2 and it is empty; and all 154 cached plug-ins are VST3 in
the shared folder, so the user-level one could not be observed being scanned.

**Why that is not enough.** The recorded paths all agree with the cache today, because nothing has
moved — so the data **cannot distinguish** the two resolution strategies. That is the whole reason
this is an entry rather than an implementation.

**What closes it:** the experiment in the audit — copy one on-channel plug-in to the user folder,
rename the shared copy, restart, see whether the channel still loads and whether `FilePath` was
rewritten. Three outcomes, each with its consequence, tabulated there. Reversible; take a backup
first.

**Recommendation, pending the answer: probably do not build the fallback.** §7.5 already removes
the prompt on any machine whose VST3 folder has been loosened, which is the common case and
includes this one. What remains is one prompt, on an explicit opt-in, for writing to a folder every
account shares — which is what UAC is for. A fallback destination trades that for a file somewhere
other than where it came from, a possible duplicate at the old path, and the loss of a promise tier
4 currently keeps.

**The answer is worth having regardless**, because it also settles whether tier 2's drift check
could key on `PluginId` rather than path — making "the plug-in moved" a state this app can
describe instead of one indistinguishable from "the plug-in is gone" — and because it removes one
of the two reasons [post-1.0.md](dev-phases/post-1.0.md) refuses portable backups.

> **Status 2026-08-22 — user will run the experiment.** The check above is a live-install
> procedure (copy, rename, restart, observe), which is the user's to perform on the reference rig
> rather than something CI or a test can stand in for. It stays open until that run; nothing here
> blocks shipping, and the recommendation — probably do not build the fallback — stands meanwhile.
> When the experiment is done, close it with the observed outcome and its consequence from the
> audit's table.

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
