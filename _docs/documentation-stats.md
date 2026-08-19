---
title: "Documentation Stats"
status: published
created: 2026-08-16
updated: 2026-08-19
tags: [meta, stats]
---

# Documentation Stats

The living tally, the doc-ecosystem delta log, and the topical cross-reference index.

Update this file **in the same commit** as the document it counts. See
[README.md](README.md) → *Updating documentation stats* for the trigger table.

> This is the **doc-ecosystem** changelog. `CHANGELOG.md` at the repo root is the
> **engineering** changelog. Same commit is fine; different voices.

---

## Tally

*As of 2026-08-19.*

| Artifact | Count |
|---|---|
| ADRs | 9 |
| Gotchas | 10 |
| Patterns | 4 |
| Recipes | 1 |
| Audits | 1 (6 findings) |
| Sessions | 11 |
| Plans | 8 |
| Dev-phase documents | 7 (of 8 phases; 1 remains sketched in the index) |
| **Tests** | **959 passing** — Core 296 · CLI 91 · App 572 |

**Patterns went 0 → 4** when the first production code shipped, which was the trigger recorded
in [README.md](README.md). Each names its real callers and the test holding it down; none was
written before the code it describes.

**Tests.** Upstream carries 40 against ~48 KB of source. Phase 1 ships 93 against a smaller
Core — the ratio the seam interfaces were inherited for ([[ADR-004]]).

---

## Recent additions

### Phase 5, plan 9: the tray shell (2026-08-19)

**959 tests green** (296 Core, 91 CLI, **572 App**) — up from 939. Build clean, zero warnings.
The app is now a *tray app with a window* end to end: the shield-check mark appears in the taskbar
button, Alt-Tab and the Start list as well as the notification area; a second launch activates the
first instance instead of starting a watcher twice; autostart is surfaced in Settings with the Task
Manager veto; and the tray icon tracks the live host on every tick.

**One asset, two jobs — but not the way the plan said.** The shield-check mark is authored once
from the same geometry `TrayIconRenderer` already draws, so the static asset and the four live
states read as one object. It is the exe's `<ApplicationIcon>` (file properties, taskbar, Alt-Tab),
but **not** `Window.Icon`: a WPF resource-pack URI for an `<ApplicationIcon>`-only asset fails at
runtime (dotnet/wpf#209), so the window's caption glyph is rendered from geometry in code
(`AppCaptionGlyph`). The exe icon via the linker works fine; only the WPF resource pipeline chokes.

**The hide branch of `OnClosing` is manual-verify-only, and it is documented as such.** It needs a
real `App` installed as `Application.Current`, but WPF allows exactly one `Application` per
AppDomain and the test harness's shared bare `Application` occupies the slot — `new App()` throws
`InvalidOperationException`. The exit branch is exercised by the existing crash-regression test;
the hide-vs-exit distinction is a look-at-it item, same class of exclusion as the DWM interop and
unshown-window geometry already documented in `MainWindowGeometryTests`.

**The context menu is pinned item-for-item.** Beyond order and checkability (already tested), the
two load-bearing labels are now asserted: **Quit — stops backing up** (the consequence rides on the
label, not a confirmation dialog) and **Pause for an hour** (the designed starting label;
`RefreshTray` rewrites it to "Resume" while paused).

Session note — [phase 5 tray shell](sessions/2026-08-19-phase-5-tray-shell.md). Plan 9 is complete;
plan 10 (high contrast) is the last surface in the phase.

**Counts moved:** sessions 10 → 11 · tests 939 → 959.

---

### Phase 5, plan 8: the settings dialog (2026-08-19)

**939 tests green** (296 Core, 91 CLI, **552 App**) — up from 764 as plans 5–8 landed their
surfaces. Build clean, zero warnings. The settings dialog ships in full: the real 680px modal
replaces the placeholder `MessageBox`, every control commits on change (there is no Save button),
and settings persist atomically to `%LOCALAPPDATA%\WaveLinkBackup\settings.json` on change, never
on exit — a command-line flag overrides the file for one run and is never written back.

**The proportion bar is computed, not hard-coded.** Enabling or disabling a tier recomputes the
stacked widths from what is actually included; the locked rows (Your setup, A list of your
effects) cannot be moved at all, and a programmatic set on them is rejected by the view model.

**Unbuilt tiers stay on screen — present but disabled.** PRESETS and PLUGINS render with the NOT
BUILT YET badge and a footnote explaining why they are not hidden. The Task 7 keyboard/SR pass
made the locked toggles *present-but-disabled* rather than collapsed, so a screen reader announces
them as off/unavailable switches instead of dropping them from the tree; focus also returns to the
list when the dialog closes, reusing the same seam every other dialog uses.

**Two debts closed in the same commit.** [technical-debt.md](technical-debt.md) §4.8 item 4 (the
settings placeholder) and §4.9 (the dormant restore-outcome strip — plan 5 wired it to
`RestoreOrchestrator`) are both struck through with their reasoning kept. Session note —
[phase 5 settings dialog](sessions/2026-08-19-phase-5-settings-dialog.md).

**Counts moved:** sessions 9 → 10 · tests 764 → 939 (cumulative across plans 5–8).

---

### Phase 5: the last two plans — tray shell and high contrast (2026-08-18)

The phase is now **fully planned end to end.** The two surfaces that remained after plans 5–8
each got a dated plan under [`plans/`](plans/):

| Plan | Surface | Design source |
|---|---|---|
| [plan-9](plans/2026-08-18-phase-5-plan-9-the-tray-shell.md) | The tray shell: the app icon (tray **and** window — no `.ico` existed, so this authors one and sets `<ApplicationIcon>` + `Window.Icon`), second-launch activation, the autostart toggle with its Task Manager veto, live-host icon states on every tick, hide-on-close + context menu verification | `screens/12-tray-autostart-update.md` |
| [plan-10](plans/2026-08-18-phase-5-plan-10-high-contrast.md) | High contrast: a verification pass over the already-built third theme, pinning the runtime swap end to end, a guard that `HighContrast.xaml` carries no hard-coded colour, the HC contract plans 5–8 must sign before their surfaces are done, and a both-schemes sweep | `screens/11-high-contrast.md` |

**Both plans are shaped by what already exists, and that is the point of reading the code
before planning it.** Plan 9 *extends* rather than rebuilds: single-instance, hide-on-close,
the context menu, the four icon states and autostart are all implemented — the new work is the
shared app icon asset, second-launch activation, the Settings toggle and pinning the live-host
state changes. Plan 10 found high contrast **~90% built and tested** (`HighContrast.xaml`
matches spec key for key; shape-encoded health, verdict words, focus ring and the PAUSED tray
glyph are all pinned), so it is a gap-filling pass: the runtime-swap chain was never pinned
end to end, the no-hard-coded-colour rule had no guard, and nothing enforced that plans 5–8's
new surfaces arrive HC-complete.

The status line in [phase-5-wpf.md](dev-phases/phase-5-wpf.md) now says what it has not said
since 2026-08-17: every surface in the phase is planned; what remains is execution.

Doc-only commit; no code, so the test count is unchanged at 764.

**Counts moved:** plans 6 → 8.

---

### Phase 5: execution plans for every remaining surface (2026-08-18)

The backup list (part 4) and the restore-outcome strip are shipped. The rest of the phase is now
broken into **four dated execution plans** under [`plans/`](plans/), each following part 4's task
format (pure model → tests → view → wiring → keyboard/SR → guards + full verification):

| Plan | Surface | Design source |
|---|---|---|
| [plan-5](plans/2026-08-18-phase-5-plan-5-the-restore-flow.md) | Real restore flow: confirmation dialog, four-stage in-progress strip, wire `RestoreOrchestrator`, feed the outcome strip | `screens/04-in-progress.md`, `09` |
| [plan-6](plans/2026-08-18-phase-5-plan-6-delete-rename-trash.md) | In-place rename, three-variant two-stage delete, empty-trash row + per-volume detection | `screens/05-delete-dialogs.md`, `08` |
| [plan-7](plans/2026-08-18-phase-5-plan-7-errors-and-first-run.md) | The twelve errors in their four placements (weight rule), error 9/12 full screen, first-run/empty state | `screens/06-errors.md`, `08`, README Screen 4 |
| [plan-8](plans/2026-08-18-phase-5-plan-8-settings-dialog.md) | Settings dialog: in-place commit (no Save button), atomic persistence, WHICH WAVE LINK + WHERE THESE SETTINGS LIVE, unbuilt tiers | README Screen 3, `screens/08` |

The phase's status line in [phase-5-wpf.md](dev-phases/phase-5-wpf.md) was refreshed from
"Not started — next" to "In progress", with a plan table and an explicit note that the **tray
shell** (icon states, context menu, hide-on-close, single-instance, autostart) and **high
contrast** are still not broken into a dated plan — that is the next planning step once plans
5–8 have landed.

Doc-only commit; no code, so the test count is unchanged at 764.

**Counts moved:** plans 2 → 6.

---

### Phase 5: the restore-outcome strip (2026-08-18)

**764 tests green** (296 Core, 91 CLI, 377 App). The App project's first WPF test surface for a
shell-level view model lands here: `RestoreOutcomeStripTests` pins the four designed outcomes —
succeeded-and-confirmed (quiet, auto-dismiss), succeeded-unconfirmed (neutral, "Check again"),
rejected (amber, not dismissible until acted on), and failed (danger, dismissible) — plus the
dismiss rules and the 6-second auto-dismiss constant.

**One Core test added from a list I nearly skipped.** `RestoreOutcome.Confirmed` is a computed
projection over `Verdict.Succeeded`, and its null-verdict branch (log unreadable) was never
asserted directly — only through `outcome.Confirmed == false`. The new test pins that the
unreadable-log path returns `Confirmed == false` without a `NullReferenceException`, which is
exactly the branch the strip's `Show(RestoreOutcome)` maps to *succeeded-unconfirmed*.

**A brush added, and the guard test caught it.** `WlDangerSoft` (the failed-outcome fill) was
added to all three theme dictionaries. The existing `ThemeTests.Every_theme_declares_every_brush`
guard would have failed on the missing key in any one of them — so the three-theme check is done
by the suite, not by eye. High contrast gets a transparent tint per `11-high-contrast.md`.

**Documented, and tracked as dormant.** Session note —
[phase 5 restore-outcome strip](sessions/2026-08-18-phase-5-restore-outcome-strip.md). The strip
is fully built but nothing feeds it yet (the restore button still shows the placeholder), so it is
recorded in [technical-debt.md](technical-debt.md) §4.9 as a *dormant seam*, not a bug — the same
shape as the `Settings…` placeholder in §4.8 item 4. The debt register's opening paragraph was
also refreshed: "no application code" is no longer true, and §1/§7 entries have since been
resolved against shipped code.

**Counts moved:** sessions 8 → 9 · tests 746 → 764.

---

### Phase 5, part 1: the four Core changes + design v5 (2026-08-17)

**351 tests green** (266 Core, 85 CLI). Core 85.7% line / 82.3% branch, CLI 84.1% / 82.0%.
NativeAOT still 3.2 MB despite new shell interop.

**Shipped:** technical-debt §7.1, §7.2 and §7.3. Only §7.4 (keyboard) remains, and it is WPF
work that arrives with the shell.

**Design v5 integrated — and the amendment is upstream now, not just in this repo.**
`screens/05` specifies the two-stage delete; `screens/08` specifies the Empty trash row. The
code and the design no longer disagree.

**The designer solved the sentence I flagged as possibly unsolvable**, and rejected the
fallback I offered:

> *"After that it is gone" is exactly true on a network share and slightly **pessimistic** on a
> local disk… Pessimism is the safe direction in a destructive dialog, and it is the one
> sentence that never breaks on any volume.*

Worth recording because the brief explicitly invited a "no", and the answer was better than
either option in it.

**One divergence found by reading the spec rather than assuming:** `screens/08` says Empty
trash takes **no confirmation on a local drive** — *"a dialog guarding a reversible action is
the noise that teaches people to click through the ones that matter."* My CLI confirmed
unconditionally. Now it confirms only where the Recycle Bin cannot catch it.

**Five tests added from a list I nearly skipped.** `screens/05` closes with *".trash must be
invisible to the list, the search, every count and size readout, and the keep-count."* Those
passed first time — the implementation already satisfied them — but "it already works" is not
the same as "it is pinned", and each is a place where a trashed backup leaking back would look
like a bug in deletion rather than in counting.

---

### Phase 5 scope split (2026-08-17)

The tray design looked like it doubled phase 5. Examined rather than accepted: **the framing is
free, the Windows integrations are not.**

`AutoBackupCoordinator` already owns no timer and waits for a host to call `Tick()` — the CLI's
`watch` verb is one today — so "tray app with a window" is what Core was built for, and
`ShutdownMode` is one line. What actually costs is that **WPF provides none of the three
integrations the design assumes**: tray icon, toast notifications, autostart registry.

**Split accordingly.** Phase 5 keeps the tray shell, hide-on-close, single-instance, `--tray`,
autostart, and high contrast. Phase 7 takes the two notifications and the update mechanism —
both are *"something has been wrong for a while"* cases, and the tray's `NEEDS YOU` icon
carries the same information passively until then. Nothing else in the design depends on them.

The framing stays because dropping it would be wrong, not because it is cheap: **if closing the
window stops backups, the app fails its own promise** and becomes upstream's tool with extra
steps.

---

### Design handoff v4 + four decisions (2026-08-17) — no version, no code

**Integrated:** `11-high-contrast.md` and `12-tray-autostart-update.md` with three PNGs.
**All six design gaps in §4 are now closed** — nothing in the UI is undesigned.

`12` changes what the app is: *"it lives in the tray and the window is the exception."* That is
a tray app with a window, not a window app with a tray, and it lands scope phase 5 did not
carry — four icon states, a context menu as the primary interface, exactly two notifications,
`HKCU\...\Run` autostart that **Task Manager can veto**, and an update section whose *UI* is
phase 5 while its *mechanism* stays phase 7.

**The four §7 conflicts are decided**, and one of them improved on my recommendation:

- **Delete → two-stage.** Move to `<store>/.trash/`, *Empty trash* forwards to the Recycle Bin.
  Better than the direct `SHFileOperation` I proposed, for a reason I had missed: **the store is
  user-chosen, and the Recycle Bin does not exist on network shares** — so the design's promise
  was one the app could not keep there. A directory move behaves identically on every volume,
  and interop leaves the delete path entirely. **Amends design decision 3.**
- **Damaged vs keep-count → verify lazily, only the condemned.** Hashes one or two snapshots per
  prune instead of the whole store, so it does not reintroduce the cost phase 2 avoided.
- **Watcher → clear the pending write on failure and carry the error.** The error is what feeds
  the tray's `NEEDS YOU` state; without it the tray has a state it cannot enter.
- **Keyboard → Windows conventions generally**, and screen-reader labels are part of it rather
  than a follow-up.

---

### Design handoff part 2 (2026-08-17) — no version, no code

An updated design package landed and was integrated into `operations/design/`. Doc-only, but
it changes what phase 5 is.

**Integrated**

- **11 state-group specs** in `screens/` with 12 PNGs, `MANIFEST.md`, and `CHANGES-SINCE-V1.md`.
- Regenerated prototype (1.24 MB) and canvas (235 KB).
- **Tokens and brand assets are hash-verified byte-identical** to what was already here — the
  token-drift risk flagged before the export turned out to be zero.

**Structural**

- `design-handoff.md` reverted to the export's own `README.md`, and **the whole folder is now
  exempt from the frontmatter rule** — stated in `README.md` with the reason. It is a vendored
  drop-in export; patching frontmatter on every re-export would guarantee the repo copy drifts
  from the design tool's, which is the one thing a handoff must not do. Same exemption as
  `third_party/`, same reason. 13 files repointed; the two references left in session notes are
  deliberately historical.

**Closed**

- **[technical-debt.md](technical-debt.md) §4** — five of the six design gaps. Only Windows
  high-contrast and tray/autostart/update remain.

**Opened — and this is the substantive part**

- **§7, four decisions that outdated shipped code.** Delete must go to the Recycle Bin
  (`SnapshotStore.Delete` is permanent, and `SHFileOperation` is Win32 interop against a
  library deliberately targeting `net10.0`); damaged backups must not count toward the
  keep-count (retention cannot see damage at all); automatic backup must not queue when the
  folder is missing (it currently retries every 15s, silently, forever). None is a mistake in
  either place — the code was built to the best spec available and the design has since decided
  better — but "the design says X, the code does Y" goes invisible once everyone is looking at
  XAML.

**Also worth recording:** the first handoff specified the SUSPECT badge in red inside an amber
row — the forbidden second red, by its own rules. The design caught it. Nothing had been built
against it, so the correction cost nothing.

---

### v0.4.0 — Phase 4: the CLI (2026-08-16)

**Added**

- **[[ADR-009]]** — hand-rolled command-line parsing. The first ADR since the scaffold, and it
  exists because a reader seeing a hand-written parser will reasonably ask why no library.
- **Session note** — [phase 4 CLI build](sessions/2026-08-16-phase-4-cli-build.md).
- **`dev-phases/phase-5-wpf.md`** — including an explicit list of what the GUI needs that
  **Core does not have yet** (search, settings persistence, disk-free, a hosted watcher),
  because those are the items that will feel like "just UI work" and are not.

**Corrected by measurement**

- **[[ADR-001]]** — NativeAOT produces a **3.2 MB** binary, not the 10–15 MB estimated. The
  table credited Rust with 2–5 MB as the one row it won; that row is now roughly a tie. The
  decision is unchanged (it turned on lossless JSON, not size) and the estimate is corrected
  rather than left standing. Third time a measurement has overturned something written down.

**Partially resolved, and said so**

- **[technical-debt.md](technical-debt.md) §2.4** — AOT compiles clean with zero trim
  warnings, **but there is no `[ComImport]` in the codebase**, so the interop that prompted
  the doubt was never exercised. Recorded as *partially answered*; claiming closure would have
  been the more satisfying lie.

**Counts moved:** ADRs 8 → 9 · sessions 5 → 6 · dev-phase docs 6 → 7 · tests 235 → 308.

---

### v0.3.0 — Phase 3: automation (2026-08-16)

**Added**

- **Session note** — [phase 3 automation build](sessions/2026-08-16-phase-3-automation-build.md).
- **`dev-phases/phase-4-cli.md`** — phase 4 detailed.

**Resolved**

- **[technical-debt.md](technical-debt.md) §1.4** — upstream being a manual tool rather than a
  safety net. Struck through, original retained. This was never a *defect* upstream; it was the
  gap this project exists to fill, and it is now filled.

**A documented exemption withdrawn**

- `FileSystemSettingsWatcher` was briefly left untested on the reasoning that excuses
  `WaveLinkProcess` at 5% coverage. That reasoning does not transfer — closing a user's Wave
  Link to test a shutdown is unacceptable, but *watching a temp directory is harmless*. The
  session note records it as "laziness wearing a principle's clothes", because the distinction
  is worth keeping sharp: an exemption is only legitimate while the thing it protects is real.
  One of the resulting tests found that a `LastWrite`-only filter would have been a bug.

**Still no new patterns.** Phases 2 and 3 both applied the four from phase 1. The set has
stopped growing, which is the expected shape — patterns come from novelty, and the last two
phases were composition.

**Counts moved:** sessions 4 → 5 · dev-phase docs 5 → 6 · tests 186 → 235.

**Corpus audit (same day, after the release)**

A pass over `_docs/` against its own README turned up three stale claims, all created by
`patterns/` coming into existence and nothing updating the document that said it had not:

- the directory-structure block omitted `patterns/`;
- *Folders deliberately absent* still listed it;
- the `patterns/` folder guide still opened with "Not yet created".

Fixed, and the absent-folders entry is kept as a **note that its trigger fired** rather than
deleted — a mechanism that demonstrably worked is worth more as evidence than as a blank space,
and the two remaining rows are the same bet.

Also added: three topics to the cross-reference index (**the snapshot store**, **automatic
capture**, **keeping the corpus honest**), which had not moved since v0.0.1 despite three
phases of work; and a *Words the code uses precisely* section to the glossary covering the
vocabulary phases 1–3 introduced — expected failure, finding, pure, seam, guard, tick,
debounce, rate limit, prunable, schema version, as built.

---

### v0.2.0 — Phase 2: the snapshot store (2026-08-16)

The release that closes the project's founding defect. The doc delta is small and mostly
consists of striking things through — which is the point.

**Added**

- **Session note** — [phase 2 store build](sessions/2026-08-16-phase-2-store-build.md).
- **`dev-phases/phase-3-automation.md`** — phase 3 detailed, per the "current or next phase"
  rule.

**Resolved**

- **[technical-debt.md](technical-debt.md) §1.1 and audit finding 1 — the critical defect.**
  Struck through, original text retained, because the reasoning still explains why the store is
  shaped the way it is.
- **The audit now has nothing open.** Six findings: three fixed, one withdrawn as wrong, one
  resolved as incomplete, one answered by building a different product.

**Corrected by measurement, again**

- The phase 2 design said `waveLinkVersion` "needs reading from the package manifest". Probing
  first showed `C:\Program Files\WindowsApps` is unreadable without elevation, and that the
  version is already in `Settings.json`. Recorded as an *as built* delta rather than silently
  implemented differently.

**No new patterns.** Phase 2 applied the four from phase 1 rather than producing new ones,
which is what a pattern set is for. Writing a fifth to have a fifth would be documenting an
intention.

**Counts moved:** sessions 3 → 4 · dev-phase docs 4 → 5 · tests 93 → 186. Audit open findings
1 → 0.

---

### v0.1.0 — Phase 1: Core (2026-08-16)

The first release with code in it. The documentation delta is mostly *promotion*: claims that
were read became claims that are tested.

**Added**

- **`knowledge-base/patterns/` — created, with 4 patterns.** The trigger in `README.md` was
  "the first line of production code ships", and it did. [[pure-analysis-core]],
  [[named-method-seams]], [[preconditions-inside-the-operation]], [[guards-that-can-fail]].
- **Session note** — [phase 1 Core build](sessions/2026-08-16-phase-1-core-build.md).
- **`plans/` gained a second document** — the [phase 2 design](plans/2026-08-16-phase-2-store-design.md).
- **`dev-phases/phase-2-store.md`** — phase 2 detailed, per the "current or next phase" rule.
- **`third_party/WaveLinkSettingsUtility/VENDOR.md`** — the vendored snapshot's record: SHA,
  baseline, what was ported, and seven deliberate divergences.

**Resolved**

- **Audit finding 5** — not wrong, *incomplete*. The release workflow overrides the csproj, so
  the README and the project file never contradicted each other. Method failure named in the
  audit: a claim about what users receive was answered from one build file.
- **`technical-debt.md` §1.5** — closed, no debt carried forward.
- **§2.2 mitigated** — `--settings-path` now bypasses discovery entirely, unlike upstream's.

**Added to the debt register**

- **§1.6 / audit finding 6** — upstream never closes `WavelinkSEService`, so its
  "verified exited" check can pass with half of Wave Link running. Fixed in our port; worth
  offering back.

**Promoted from claim to test**

- [[capture-fails-while-wave-link-is-running]] — now pinned by
  `RealInstallTests.The_naive_read_fails_while_Wave_Link_is_running`, which asserts the naive
  call throws against the live file.

**Counts moved:** patterns 0 → 4 · sessions 2 → 3 · plans 1 → 2 · dev-phase docs 3 → 4 ·
tests 0 → 93. Audit findings 5 → 6, of which 2 did not survive contact with a running system.

---

### v0.0.2 — Probe corrections (2026-08-16)

A ten-minute probe run before designing phase 1 answered one open question and **invalidated
two documented decisions**. The doc-ecosystem effect is mostly *subtractive*, which is unusual
enough to note.

**Added**

- Gotcha 9 — [[capture-fails-while-wave-link-is-running]]. `Settings.json` is locked while
  Wave Link runs; `File.ReadAllBytes` fails on most captures. Not in `SPEC.md` at all.
- Session note — [phase-1 probe](sessions/2026-08-16-phase-1-probe.md).
- `LICENSE` at the repo root (MIT, upstream's copyright line verbatim).
- A **Corrections block** at the top of `SPEC.md`. The body is left unedited on purpose: it is
  the record of what was believed on 2026-08-15, and rewriting it would destroy the thing that
  makes the corrections legible.

**Withdrawn**

- **Audit finding 2 (JSON encoder)** — struck through, not deleted, in the audit,
  `technical-debt.md` §1.2 and `SPEC.md`. Wave Link writes with the *default* encoder;
  the recommended `UnsafeRelaxedJsonEscaping` would have caused the churn it was meant to
  prevent. A wrong recommendation that merely disappears gets re-derived by the next reader.

**Resolved**

- `technical-debt.md` §2.1 (`JsonNode.Parse` duplicates) — answered, and the question was
  mis-framed. New sub-finding 3b recorded instead.

**Rewritten**

- [[every-snapshot-differs-with-no-real-change]] — same symptom, opposite cause. The
  superseded version's `Provenance: read, not reproduced` line is what made this catchable.

**Counts moved:** gotchas 8 → 9 · sessions 1 → 2. Audit findings: 5 → 4 actionable, plus one
new sub-finding and one disputed.

---

### v0.0.1 — Documentation scaffold (2026-08-16)

The documentation system, seeded from `SPEC.md` and the design handoff. No application code.

**Added**

- The docs system itself: `README.md`, `index.md`, `templates.md`, `glossary.md`,
  `technical-debt.md`, this file.
- **8 ADRs**, `ADR-001` … `ADR-008` — every structural decision `SPEC.md` had already made
  but never recorded as a decision with alternatives and consequences attached.
- **8 gotchas**, each carrying a `Provenance` line: 3 observed, 4 read-not-reproduced,
  1 spec-derived. That split is itself the most useful thing in the set.
- **1 recipe** — the restore sequence, where the order is load-bearing at every step.
- **1 audit** — the read of `voltybat/WaveLinkSettingsUtility` at `main`.
- **3 dev-phase documents** — the 8-phase roadmap index plus detail for phases 0 and 1.
- **1 session note**.

**Moved**

- `design_handoff_wave_link_backup/` → `_docs/operations/design/`, its `README.md` renamed
  `README.md` so it does not read as a folder readme.
- `_docs/README-temp.md` → `_docs/archive/README-temp.md`, consumed.

**Counts moved:** ADRs 0 → 8 · gotchas 0 → 8 · recipes 0 → 1 · audits 0 → 1 · sessions 0 → 1.

---

## Related documentation

Topics spanning several artifacts. A single-file topic is discoverable by search and does not
belong here.

### Where the settings live, and where they don't

The decoy folder is the first thing that goes wrong and the easiest to get wrong silently.

| Artifact | Contribution |
|---|---|
| `SPEC.md` §1 | The paths, the sizes, the classification of every file under `LocalState` |
| [[ADR-003]] | Why the store is outside `LocalState` |
| [[backup-succeeds-but-protects-nothing]] | The symptom when discovery finds the decoy |
| [glossary.md](glossary.md) | `LocalState`, the decoy, package family name, backup store |
| [[phase-1-core]] | Where discovery is built |

### Validating a settings file

Three separate traps, one of which is the incident that started the project.

| Artifact | Contribution |
|---|---|
| `SPEC.md` §5 | Duplicate keys, round-trip loss, ranking by content |
| [[file-parses-but-wave-link-resets]] | Duplicate keys — the original incident |
| [[newest-backup-is-the-broken-one]] | Why timestamp ranking picks the reset config |
| [[every-snapshot-differs-with-no-real-change]] | The encoder mangling base64 state |
| [technical-debt.md](technical-debt.md) §1.3, §2.1 | The upstream gap and the unverified assumption blocking its fix |
| [Audit: voltybat](audits/2026-08-15-voltybat-wavelinksettingsutility.md) | Upstream `Validate()` and what it misses |

### Restoring safely

The part that looks obvious and fails.

| Artifact | Contribution |
|---|---|
| `SPEC.md` §4 | The sequence, and verification from the log |
| [[restore-a-settings-file-safely]] | The recipe, with the reason attached to each ordering constraint |
| [[restored-settings-revert-seconds-later]] | The flush race |
| [[preconditions-inside-the-operation]] | Why the write refuses rather than trusting the caller |
| `Restore/RestoreOrchestrator.cs` | The assembled sequence (phase 2) |
| [README.md](operations/design/README.md) Screen 2 | The confirmation dialog, and the automatic pre-restore snapshot |
| [glossary.md](glossary.md) | Verified exited, atomic write, shell AppID, pre-restore snapshot |

### The snapshot store

Where backups live, and why not where upstream put them.

| Artifact | Contribution |
|---|---|
| [[ADR-003]] | The decision: outside `LocalState`, identity in `manifest.json` |
| [Phase 2 design](plans/2026-08-16-phase-2-store-design.md) | Layout, manifest schema, the guard |
| [technical-debt.md](technical-debt.md) §1.1 | The inherited defect, struck through with its reasoning kept |
| [Audit](audits/2026-08-15-voltybat-wavelinksettingsutility.md) finding 1 | What upstream does and why it cannot be kept |
| [[newest-backup-is-the-broken-one]] | Why the list ranks by content, not by date |
| [glossary.md](glossary.md) | Snapshot, managed backup, trigger, dedup key, backup store |

### Automatic capture

The phase that made this a different product from the tool it was forked from.

| Artifact | Contribution |
|---|---|
| [[ADR-007]] | Watch don't poll; dedup by hash; never prune what the user named |
| [phase-3-automation.md](dev-phases/phase-3-automation.md) | The plan, and the no-real-time constraint |
| `SPEC.md` §2, §6 | Retention measurements and the design target |
| [README.md](operations/design/README.md) Screen 3 | **Copy that is a specification** — the debounce and rate limit are quoted to users |
| [technical-debt.md](technical-debt.md) §1.4 | The gap this filled, struck through |
| [[capture-fails-while-wave-link-is-running]] | Why the watcher's reads must be shared-mode |

### Keeping the corpus honest

This project's most distinctive practice, and it spans nearly everything.

| Artifact | Contribution |
|---|---|
| [README.md](README.md) | The `Provenance` rule for gotchas, and the "state provenance" best practice |
| `SPEC.md` Provenance + Corrections | The example the rule is modelled on, and three claims it later caught |
| [[guards-that-can-fail]] | The same idea in code: a guard nobody has watched reject something is a guess |
| [Audit](audits/2026-08-15-voltybat-wavelinksettingsutility.md) | Two of five findings did not survive a running system — both marked *read, not reproduced* |
| [[every-snapshot-differs-with-no-real-change]] | A gotcha rewritten when its cause turned out to be inverted |
| [Probe session](sessions/2026-08-16-phase-1-probe.md) | Where the discipline paid for itself |

### VST3 capture

Four tiers, three ways it bites.

| Artifact | Contribution |
|---|---|
| `SPEC.md` §9 | The tiering, the measurements, the three warnings |
| [[ADR-006]] | The decision, and what it rules out |
| [[restored-plugin-demands-a-licence]] | Licences do not travel |
| [[vst3-backs-up-as-nothing]] | Bundles are directories |
| [technical-debt.md](technical-debt.md) §2.3 | The untested path, and why the author's machine will never catch it |

### Shipping publicly

| Artifact | Contribution |
|---|---|
| `SPEC.md` §11 | Numbers that are not constants, privacy, open questions |
| [[ADR-008]] | Windows-only, stated rather than implied |
| [[restored-backup-has-dead-channels]] | Machine-local snapshots |
| [technical-debt.md](technical-debt.md) §5, §6 | The constants list and the privacy debt that gates going public |
| `.gitignore` | Refuses real settings files, VST3 binaries and the backup store |
