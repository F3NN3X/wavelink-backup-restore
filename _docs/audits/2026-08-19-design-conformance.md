---
title: "Audit: the shipped app against the design package"
status: published
created: 2026-08-19
updated: 2026-08-19
related_adrs: [ADR-004, ADR-005]
tags: [audit, design, ui]
---

# Audit: the shipped app against `operations/design/`

**Audited:** 2026-08-19 · **Subject:** `src/WaveLinkBackup.App` at `feat/preset-roots-elevation-schedule`
**Against:** `_docs/operations/design/` — `README.md` (screens 1–4), `screens/01`–`14`, `tokens/*.css`
**Method:** every XAML file and view model read against the spec it implements. One claim
(§1.1) was settled by running WPF rather than by reading it. Nothing was checked by eye on a
desktop — that is what
[screen-1-by-eye-checklist.md](../operations/design/screen-1-by-eye-checklist.md) is for, and
this audit does not replace it.

**Verdict: the colour system is faithful; the layout had one structural defect; five designed
surfaces were never built and three more were built to the model but never to the view.**

The twelve `--wl-*` roles map onto the F3NN3X tokens exactly as `01-tokens-and-mapping.md`
requires, in all three themes, including the four app-owned exceptions. No second red survives
anywhere. The type roles, the radii, the 140/220ms motion and the shape-first health encoding are
all in place. The gaps are not colour gaps — they are screens and states that the design specifies
and the app does not draw.

---

## 1 · Fixed in this pass

### 1.1 The list was 156px narrower than its window — **the structural one**

`WlRowTemplate` and `WlColumnHeaderRowTemplate` both declared

```xml
<ColumnDefinition Width="*" MinWidth="200" SharedSizeGroup="WlColName" />
```

**WPF does not honour star sizing inside a shared size group.** A starred definition that names
one is measured as if it were `Auto`. Confirmed by running it, not by reading a doc: a probe grid
measured at 1140px resolved that exact definition to **200**, its `MinWidth`, with the fixed
columns taking their own widths and the remaining 780px going nowhere.

So the design's `minmax(200px,1fr)` NAME column silently became a 200px fixed column, which pinned
the whole six-column block to its 984px minimum. In a 1180px window that left roughly **156px of
dead space to the right of every row's overflow cell**, with the header and every row ending in the
same wrong place — a defect visible in the first screenshot anybody takes, and invisible to a suite
that only ever asked whether the six `SharedSizeGroup` names matched.

**Fixed:** NAME carries no shared size group in either file and is starred again. The other five
are fixed widths, where sharing costs nothing and still pins the header to the rows.

A second miss rode along with it. The design's `gap: 20px` is implemented as a right `Margin` on
each cell's content — inside the column, not between columns — so every fixed column was 20px
narrower in content than the design draws it. Most visibly the INPUTS column, which carries the
five-slot health strip the whole row is built around: 280px of slots where the design gives 300.
The five fixed widths now carry the gap (140 / 144 / 320 / 220, NAME's minimum 220), which
reproduces the design's own arithmetic exactly — NAME resolves to 256px content in a 1180px window.

Because the header and the rows now resolve NAME's star independently, and the rows lose 10px to
the list's scroll bar while the header does not, the header reserves that gutter when
`ListScrollViewer` shows a scroll bar. Without it the header would drift 10px right of the cells it
heads the moment the list got long enough to scroll.

> **A guard test was changed, deliberately.**
> `MainWindowTemplateTests.The_column_header_uses_the_same_shared_size_group_names_as_the_row`
> lost its `WlColName` case. It was not weakened to make a change pass: the assertion was
> *encoding the defect* — it required the one attribute that broke the column. It is replaced by
> two stronger guards that pin what actually has to hold (NAME is starred and never shared; the
> five fixed columns carry the gap) plus one for the scroll-bar gutter.

### 1.2 Enter fired the destructive button on all three confirmation dialogs

`10-decisions.md` §6: *"Enter fires the primary button — except Delete and Restore, where focus
starts on Cancel and the destructive button must be reached deliberately (Tab or click)."*

All three dialogs opened focused on Cancel, correctly — and marked the destructive button
`IsDefault="True"`, which hands it Enter from **anywhere** in the dialog, focus on Cancel included.
Enter on a freshly-opened delete dialog deleted the backup. The code comment beside it asserted the
opposite ("Enter confirms only when the user has deliberately moved there"), which is why it
survived review.

**Fixed** in `DeleteDialog`, `RestoreDialog` and `EmptyTrashDialog`: `IsDefault` off, and Enter
handled on the button itself so it still confirms once the user has actually tabbed onto it. Empty
trash is included because `08` gives it the delete dialog's shape and its focus rule, and it is
irreversible on exactly the volumes it exists to warn about.

### 1.3 The inline error strip drew neither its number nor its dot, and printed its sentence in the wrong typeface

`06-errors.md`'s inline anatomy is *"22px mono error number · 11px hollow dot · sentence (Rubik 400
13.5px) over mono meta (11.5px) · action · dismiss ×"*. `RestoreOutcomeStrip` has carried
`ErrorNumber`, `MonoMeta` and `IsInlineError` since it was built. **Nothing rendered any of them.**

An inline error therefore arrived with an empty glyph slot where the number belongs, no dot at all
(the four glyph cases covered the four restore outcomes and not this fifth kind), the catalog's
short `Title` as its sentence, the *designed sentence* underneath it in the mono meta role, and the
machine-specific line — the path, the PID, the checksum, the whole reason the strip is useful —
dropped on the floor.

**Fixed** in the view alone; the model and its tests are untouched.

### 1.4 Settings: a footnote that stopped being true, and a label with its number deleted out of it

- **"Both switches are off and can't be moved yet."** Phase 6 built presets and plug-in files, and
  removed the `NOT BUILT YET` badge — but left this line under the group. It was the one sentence
  on the screen that was untrue. Removed. `08-settings-persistence.md`'s *Unbuilt tiers* section
  describes a state this app has left.
- **"Keep the last automatic backups."** `14-backup-timing.md` gives *"Keep the last 30 automatic
  backups"*, the value read back as a sentence, exactly as the interval row above it does. The
  number had been deleted from the string rather than bound, leaving a half-finished sentence
  beside a stepper showing the number it left out. Now `SettingsViewModel.KeepCountLabel`.

### 1.5 The proportion bar was one colour, picked by matching an English string

README Screen 3 colours the bar in row order — `--wl-ok`, `--wl-warn`, then `--wl-accent` at 75%.
The view chose accent by matching `Name` against the literal `"The effect plug-ins themselves"` and
painted **everything else** `--wl-ok`, so the effects-list segment was never amber and the presets
segment — the one that dominates the bar — was never accent. A copy edit to that row label would
have taken the last colour with it too. `ProportionSegment` now carries `Tier`, and the view keys
on that.

### 1.6 The first-run checkbox controlled nothing

Screen 4's *"Keep backing up on its own when my settings change"* was `IsChecked="True"` in XAML
with no handler: a control that always looked on, never read the real setting, and never wrote it.
It now reads `App.AutoBackupEnabled` and commits through a new `App.SetAutoBackup`, which is
`ToggleAutoBackup`'s absolute-value sibling.

### 1.7 The no-results action was a ghost button

`07-search.md` specifies a **secondary** *"Clear the search"*. It was ghost — no edge, in the
middle of an otherwise empty panel, as the only way out of an empty result.

**All 1,219 tests pass** (`Core` 423, `Cli` 97, `App` 699), zero build warnings.

---

## 2 · Designed and never built

Nothing below is a bug in what exists. Each is a surface the design specifies that the app does not
draw. They are listed heaviest first.

### 2.1 The rejected restore cannot be acted on, and cannot be dismissed

**`03-restore-outcomes.md` §3.** The one strip the design says is *not dismissible until acted on*
— because a rejected restore means Wave Link has reset the user's live configuration and the way
back is a specific row in the list.

The design gives it: a headline (*"Wave Link rejected this backup and reset your settings."*), a
body naming the way back, a mono meta line, **two actions** — ghost *"Show the log"* and primary
*"Restore \"Before restore\""* — and the "Before restore" row rendering **selected** immediately
below, so the button and the row are visibly the same object.

What exists: a title, a detail line, `HasAction = false`, `Dismissible = false`. `AcknowledgeReject`
is implemented and **nothing in the app calls it**. So on the app's worst day, the user gets an
amber bar that states a problem, offers nothing, and cannot be closed for the rest of the process's
life.

This is the single most valuable gap on the list: it is the recovery path for the only failure that
actually costs someone their mixer.

### 2.2 There is no backing-up state

**`04-in-progress.md`, first half.** The restore half is fully built — four named stages,
connectors, `STEP n OF 4`, the reassurance line, no spinner. The backup half does not exist:
no *"Backing up your setup…"* sentence, no `470 KB · WRITING` mono meta, and no 2px determinate
bar across the strip's bottom edge. `BackupHost.IsCapturing` exists and only the tray icon reads it.

The design's own note — *"replaced in place by the result line; the strip never disappears and
reappear-flashes"* — is the part that needs the state to exist at all.

### 2.3 First run, with Wave Link not found

**`06-errors.md` "First-run variant"**, and the long-standing
[technical-debt §4.10](../technical-debt.md). `ShellViewModel.FirstRunError1Label` and
`FirstRunLookedInLabel` are both implemented, correct, and **bound by nothing**; the XAML comment
says the window "swaps this in (Task 6)", and Task 6 did not. Today, a first run on a machine with
no Wave Link shows the empty state with the ok-dot line simply absent — no amber dot, no
"looked in" line, no *"Choose the settings file…"*.

### 2.4 Settings: `WHEN WINDOWS STARTS`

**`12-tray-autostart-update.md`.** `RunKeyAutostart`, `IAutostart`, `ShellViewModel.AutostartState`,
`CanEnableAutostart`, `AutostartBlockedNote` and `ToggleAutostart` all exist and are tested,
including the Task-Manager-disabled case the design calls out. **No control binds to any of them.**
`ClosingHidesToTray` is likewise persisted in `ShellState` with nothing to set it. Two toggles and a
note away from done.

### 2.5 Settings: `UPDATES`

**`12-tray-autostart-update.md`.** Not started: no version readout, no "Check now", no available-update
row, no failed-update block. Error 8's *"Get the update"* button therefore lands nowhere — the design
has it deep-linking to this section with the new version's row already showing.

### 2.6 The two tray notifications

**`12-tray-autostart-update.md`.** *"Nothing has been backed up for 9 days"* and *"Wave Link reset
your settings"*. Neither exists; the app has no notification code of any kind. The design's harder
rule — a successful backup **never** notifies — is trivially satisfied.

### 2.7 Error 2's chooser is a placeholder

**`06-errors.md` §2.** The dialog exists and the persistence behind it (`WHICH WAVE LINK`) is built,
so the app no longer asks on every launch. But each row draws only a bare radio and a path. The
design asks for the version in Rubik 500, a `RUNNING` chip in `--wl-ok-soft`, the ellipsised install
path, a `SETTINGS SAVED … · N INPUTS · N KB` meta line, a selected-row treatment (`--wl-bg` fill,
3px `--wl-accent` left edge, a 16px accent radio) and a *"Remember this one and stop asking"*
checkbox. Choosing between two installations by path alone is the decision this dialog exists to
make easier.

### 2.8 Error 9 has no surface

**`06-errors.md` §9.** *"That folder is not a Wave Link Backup"* is in the catalog with the right
placement and weight, and is specified to appear **in Settings, in place, after "Change folder…"**.
Nothing renders it there.

### 2.9 Smaller, and genuinely small

| | Where | What |
|---|---|---|
| a | Settings, `WHERE BACKUPS ARE KEPT` | The stats line prints free space only. The design's line is `N BACKUPS · X MB USED · Y GB FREE ON THIS DRIVE`; the count and the used bytes live on the shell view model and were never plumbed through. Flagged in the code's own comment. |
| b | Settings, high contrast | `11-high-contrast.md`: *"The proportion bar loses its colour segments; label the segments instead."* It keeps its colours and gains no labels. |
| c | Row grid, small windows | The row's own minimum is 1084px + 40px padding = 1124, and the window's minimum is **980**. Below ~1124 the overflow column is clipped — there is no horizontal scroll and none is designed. The design's numbers do this on their own (its own six columns need 1084 in a window it allows to be 980 wide); it needs a design answer, not an invented one. |
| d | Everywhere | Still no real icon set — see [technical-debt §4.7](../technical-debt.md). Every glyph is a hand-drawn Lucide-idiom path. |

---

## 3 · Checked and correct

Recorded so the next pass does not re-derive it.

- **Tokens.** All twenty brush keys in all three theme dictionaries match
  `01-tokens-and-mapping.md`, including the four app-owned exceptions (`--wl-chrome`, the whole
  light theme, light `ok` `#0F6B4A` and light `warn` `#8A5A05`). Dark's line alphas are dark's, not
  light's. `AccentPalette` derives soft/line at 12%/32% dark and 7%/24% light and refuses to derive
  `WlDanger` or `WlAccentInk`.
- **The second red is gone.** SUSPECT is transparent-filled, 1px `--wl-warn`, `--wl-warn` text —
  `10-decisions.md` §1, with a guard test behind it. DAMAGED takes no colour at all.
- **Shape-first health.** 2px solid / dashed / dotted, five slots always, the damaged row's single
  `CONTENTS UNKNOWN` cell, and the high-contrast verdict word in the NAME cell with no seventh
  column. This is the part of the design that was hardest to get right and it is right.
- **High contrast.** Every fill transparent, `SystemColors.*ColorKey` bindings (not `*Color`), no
  scrim, no shadow, disabled at full opacity in `GrayText`, hover as a 1px `HotTrack` outline,
  selected as a full `Highlight` fill with `HighlightText` inversion — with the four nested-scope
  exceptions the by-eye checklist already triages as known.
- **The restore dialog.** Title → body → mono version note → NOW/AFTER table with accent change
  dots → amber missing-plug-in block with its bold lead → plug-in-files elevation row → reassurance
  → footer. Order, widths, paddings and copy all match `README` §Screen 2 + `09` + `13`.
- **Copy.** The delete dialogs never name the Recycle Bin; the trash row is the only place in the
  app that does. `CHANGES APPLY AS YOU MAKE THEM` is a fact, not a promise — there is no Save
  button and every control commits.
- **Status strip and bottom bar**, including the damaged variant's second line
  (`DAMAGED — RESTORE IS OFF FOR THIS ONE · …`) and the folder-missing variant.
- **Letter-spacing.** `TrackedText` honours the four tracked roles' `.06`/`.12`/`.14`/`.18em`,
  which WPF's `TextBlock` cannot do at all.

---

## 4 · What this audit did not do

- **Nothing was looked at on a desktop.** Every finding above is a reading of source, except §1.1,
  which was measured by running WPF. The by-eye checklist is still owed a human.
- **Light theme was checked by token, not by eye.** That the values are right does not prove the
  amber tint composites over an opaque base everywhere it is drawn.
- **The design's own PNGs were not opened.** The markdown is authoritative by the package's own
  rule ("they are illustration, not measurement"), so the text is what this was read against.
