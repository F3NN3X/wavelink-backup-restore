---
title: "Phase 7 — Release"
status: review
created: 2026-08-19
updated: 2026-08-20
related_adrs: [ADR-002, ADR-008, ADR-009, ADR-012]
tags: [dev-phase]
---

# Phase 7 — Release

> ## Status, 2026-08-20: most of this landed early, out of order
>
> A debt-clearing pass closed almost every open entry in
> [technical-debt.md](../technical-debt.md), and six of this phase's nine work items were inside
> it. **The plan below is kept unedited** — its reasoning is what the work was done against, and
> §2's notification-route table in particular predicted the exact trade-off that shipped.
>
> | | Work item | State |
> |---|---|---|
> | 1 | Diagnostics and redaction | **Done.** `Redaction` + `Diagnostics` in Core, *Copy diagnostics* in Settings, `wlbackup diagnostics` in the CLI. The 1.0 gate is closed. |
> | 2 | The two notifications | **Done**, by the *shell balloon* route this plan's own table costed — so both notices carry their action in the body and click-anywhere rather than as a button. The plan called that "the degraded form"; it remains the right trade until there is an installer to place a Start-menu shortcut. |
> | 3 | `WHEN WINDOWS STARTS` | **Done.** Both toggles, the Task-Manager-veto note, the Run-key line. |
> | 4 | Packaging, distribution and **updates** | **Updates done** — see [[ADR-012]] and [the runbook](../operations/runbooks/releasing-and-updating.md). **Packaging and signing are not**, and signing is now the largest single thing left. |
> | 5 | First-run "Wave Link not found" | **Done.** §4.10 closed, including the *Choose the settings file…* route a non-MSIX user needs. |
> | 6 | Keyboard, focus and the icon set | **Done.** §7.4 and §4.7 closed; the icon set is real Lucide data. |
> | 7 | Repository, licence and README | **Partly.** `THIRD-PARTY-NOTICES.md` added. The repo is still private and has no remote. |
> | 8 | Release engineering (CI) | **Built, unverified.** `.github/workflows/release.yml` produces the shape the updater looks for. It has never run. |
> | 9 | The 1.0 gate | **See §9's table below**, which is now mostly stale in the *good* direction: every "Gates 1.0? **Yes**" row except packaging is closed, and four of the "No" rows closed anyway. |
>
> **What is genuinely left:** packaging and code signing; making the repository public; running the
> release loop once end to end; and the by-eye pass (§4.15). The
> [release checklist](../../CHANGELOG.md) at the bottom of `CHANGELOG.md` is the current list.

**Status:** Mostly delivered ahead of schedule — see the banner above. Originally: not started.
**Entry criteria:** phases 5 and 6 complete — all four tiers capture and restore, and the shell
renders them.
**Exit criteria:** a stranger can download one file, run it on a clean Windows 11 machine with no
SDK installed, be watched within a minute of first launch, and hand a diagnostic to a public issue
tracker without publishing their hardware serial number or their Windows username. The repo is
public, the licence and attribution are correct, and `1.0.0` is tagged with artifacts and
checksums.

## Why this phase exists

Everything before it is the product. This is the part that decides whether anyone can *have* the
product, and it contains one thing that cannot be done afterwards: **the privacy work**
([technical-debt.md](../technical-debt.md) §6). `Settings.json` carries hardware serial numbers
inside device IDs and absolute paths carrying the Windows username, and users attach backups to
bug reports without thinking about it. Once that is in a public issue tracker it is public
permanently. Redaction ships **before** the repo does.

The rest is the work phase 5 deliberately deferred because it needs Windows APIs WPF does not
provide — the two notifications and the update mechanism — plus the packaging decision upstream
left open ([[ADR-002]], finding 5), and three UI surfaces that are modelled in code but have no
control bound to them.

**This phase writes two ADRs.** Notifications and packaging/updates are both decisions with
alternatives that a later reader will otherwise re-litigate: **ADR-012 — how the app is packaged
and distributed** and **ADR-013 — how notifications are delivered**. Write them when the section
that needs them starts, not before.

> These were pencilled in as 010 and 011 and moved on 2026-08-19, when
> [[ADR-010]] (two preset roots) and [[ADR-011]] (elevate by relaunching the shell) were
> actually written. ADRs are numbered in the order they are **written**, so a reserved number
> yields to a real one — that is renumbering a plan, not renumbering an ADR, which never happens.

## Scope

### In

- **Diagnostics with redaction** — the gate. A pure Core redactor, a "Copy diagnostics" action,
  and one designed surface to invoke it from.
- **The two notifications** from `screens/12` — nine days of silence, and a rejected restore.
- **The update mechanism** — the `UPDATES` settings section, a weekly check, and whatever
  "install" honestly means once ADR-012 has decided how the app is distributed.
- **`WHEN WINDOWS STARTS`** — the settings section for autostart and close-to-tray. Both are
  modelled and persisted; **neither has a control bound to it**, so autostart cannot currently be
  switched on from anywhere in the app.
- **The first-run "Wave Link not found" variant** ([technical-debt.md](../technical-debt.md)
  §4.10) — the one screen where an error and the empty state overlap, and the reason a non-MSIX
  user cannot get started today.
- **Keyboard and focus conventions** ([technical-debt.md](../technical-debt.md) §7.4).
- **The real icon set** ([technical-debt.md](../technical-debt.md) §4.7) — the mechanism shipped in
  phase 5; the glyph data did not.
- **Packaging, signing and release engineering** — artifacts, checksums, a release workflow, MIT
  attribution, the README, and the version of record.

### Out — and where it went instead

- **Telemetry, crash reporting, auto-upload of anything** → never. Not deferred; refused. The
  diagnostics action puts text on the clipboard and stops there.
- **A Microsoft Store / MSIX package of our own app** → post-1.0, if ever. It buys signing and
  updates and costs the ability to write outside a redirected `LocalState`, which is the defect
  this whole project exists to fix ([[ADR-003]]).
- **macOS** → out of scope permanently ([[ADR-008]]). The README says so above the fold.
- **Export a chain, repair a dead input, endpoint enumeration** → [post-1.0.md](post-1.0.md).
- **The remaining shipped-code debts** — §4.11 (shared size arithmetic), §4.14 (one flat list),
  §4.16 (rehashing every plugin binary) → [post-1.0.md](post-1.0.md). §9 below records why none
  of them gates a release.

---

## Work

Ordered. §1 gates everything public, so it is first even though §4 is the one that takes longest.

### 1 · Diagnostics and redaction (Core + App) — [technical-debt.md](../technical-debt.md) §6, SPEC §11

**The gate. Nothing is published until this is in.**

Two things are being protected, and they are different shapes:

| In the file | Looks like | Why it is sensitive |
|---|---|---|
| Device ID | `BS33J1A05009\PCM_IN_01_C_00_SD1` | `BS33J1A05009` is the **hardware serial** of the interface |
| Absolute path | `C:\Users\jane\AppData\Local\…` | The Windows username, and often a real name |
| Machine name | Occasionally inside device strings | Identifies the machine on a network |

**A pure `Redaction` in Core**, bytes in and text out, reflection-free through `Utf8JsonWriter`
like every other serializer here — the source-scan guard applies.

**The trap that makes a naive redactor worse than none.** SPEC §3 is explicit that device IDs are
**foreign keys, not labels**: the `InputSettings` key *is* the device ID, and the same ID appears
elsewhere in the document both bare and as a composite `<deviceId>|<suffix>`. A redactor that
rewrites the key but not the references produces a diagnostic where the channels no longer join up
— and someone will then debug the redaction rather than the bug. **Redaction must be a consistent
substitution across the whole tree, in both forms**: one stable pseudonym per distinct real value,
for the life of one redaction run (`DEVICE-1`, `DEVICE-2`, `USER`, `MACHINE`). Deterministic within
a run, not across runs; nothing is being correlated between reports.

**What "Copy diagnostics" actually copies** is a *report*, not the settings file:

```
Wave Link Backup 1.0.0 · Windows 11 26200 · .NET 10.0.0
Wave Link 3.3.0.4108 (from the settings file)
Store: %LOCALAPPDATA%\WaveLinkBackup · 34 backups · 12.4 MB · 118 GB free
Newest: 2026-08-19T23:07 automatic · 5 inputs · 43,052 bytes · no duplicate keys
Tiers in the newest backup: settings, plugin-manifest
Plugins: 6 referenced, 6 resolved, 0 missing, 1 version unknown
Last error: none
```

Paths are shown with the user's profile folded to `%LOCALAPPDATA%`/`%APPDATA%`, plugin paths keep
their vendor and file name and lose everything above `Program Files` or the profile. **A redacted
copy of the settings file is a separate, second action** — most bug reports do not need it, and the
one that does should be a deliberate act.

**This needs a design pass first.** There is no designed surface for it: `screens/06-errors.md`
covers twelve error states and none of them carries a copy affordance, and Screen 3 has no row for
it. Write `screens/13-diagnostics.md` before the code — one secondary button in the Settings
dialog's `WHERE THESE SETTINGS LIVE` section, plus a `Copy details` ghost button on the error
dialog, is the likely answer, but it is a design decision and this phase should not invent it in
XAML.

**Tests:** a serial-bearing fixture, asserting the serial appears nowhere in the output; the same
device ID redacted identically in its key, its bare reference and its `<id>|<suffix>` composite;
a path under `C:\Users\<name>` folded; the redactor never throwing on a malformed file (a
diagnostic is most wanted when the file is broken).

---

### 2 · The two notifications (App) — `screens/12`

Exactly two, and the design is emphatic that a successful backup never notifies:

1. **Nothing has been backed up for 9 days.** *The backup folder can't be used. Wave Link's own
   copies cover about three days.* → **Choose a folder…**
2. **Wave Link reset your settings.** *It rejected the backup you restored. "Before restore" will
   put you back.* → **Restore "Before restore"**

**Both data sources already exist.** The nine-day condition is `HealthProbe` plus the store's
newest `createdUtc`; the rejected-restore condition is the log verification `RestoreOrchestrator`
already performs at step 6 (SPEC §4 — success is the *absence* of `Failed to parse settings file`).
Neither needs new Core work. What is missing is delivery and the once-only rule.

**Fires once, not daily.** That needs durable state: a `NotifiedAt`/`NotifiedFor` pair in
`ShellState` (the shell's file, not `BackupSettings` — Core has nothing to notify with, [[ADR-004]]),
cleared when the condition clears. A notification that repeats every day is muted within a week, and
then it is not a safety net.

**How it is delivered is ADR-013**, and the choice is real:

| Route | Buys | Costs |
|---|---|---|
| **Shell balloon** via the existing tray icon | Zero new dependency, works unpackaged, already have the icon | **No action buttons.** Both designed notifications carry one. Click-to-open-the-right-place is the degraded form |
| **`CommunityToolkit` app notifications** | The designed toast, with buttons | A dependency, an AppUserModelID, and a Start-menu shortcut the installer must place — an unpackaged WPF app cannot toast without one |
| **WinAppSDK `AppNotificationManager`** | Same, first-party | Drags the Windows App SDK in for two notifications ([[ADR-005]] declined it for the whole GUI) |

**Recommendation:** the toolkit route, because the action button *is* the notification's value —
"Choose a folder…" is what fixes the nine-day case. If the shortcut requirement turns out to
conflict with the portable-zip decision in §4, fall back to the balloon and record the lost buttons
as debt rather than shipping a notification nobody can act on.

---

### 3 · `WHEN WINDOWS STARTS` (App) — `screens/12`, [technical-debt.md](../technical-debt.md) §4.8

Two toggles, both **modelled, persisted and unreachable**:

- **Start with Windows and sit in the tray** — `ShellViewModel.ToggleAutostart()` exists, is
  tested, and **has no caller in the application**. `RunKeyAutostart` writes
  `HKCU\…\CurrentVersion\Run` and `AutostartState.BlockedByTaskManager` is already handled.
- **Closing the window hides it in the tray** — `ShellState.ClosingHidesToTray` is persisted and
  read by `MainWindow`, with no control bound to it.

So this is a XAML section plus two bindings, in the Settings dialog between `WHEN TO BACK UP` and
`WHAT GOES IN A BACKUP`. Small, and it is the difference between the app watching your settings
after a reboot and not.

**The Task Manager rule stays as designed:** if the Run entry has been disabled there, the toggle
reads off, cannot be switched on, and the note says why. Task Manager wins.

---

### 4 · Packaging, distribution and updates (build + App) — [[ADR-002]] finding 5, [technical-debt.md](../technical-debt.md) §1.5, §2.4

**ADR-012.** Three decisions, and the update mechanism cannot be designed until they are made.

**a · How the app ships.** `wlbackup` (CLI) is settled: NativeAOT, 3.2 MB, verified working
against a real install. The WPF app is not:

| Option | Size | First run on a clean machine |
|---|---|---|
| Framework-dependent single file | ~2 MB | Fails until the user installs the .NET 10 desktop runtime |
| **Self-contained single file** | ~70 MB | Works |
| NativeAOT | — | **Not available.** WPF does not support it |

**Recommendation: self-contained.** A backup tool whose first run is an error dialog about a
missing runtime has failed at the only moment it gets one chance. 70 MB is a download, not a
problem.

> **Superseded 2026-08-22 (v0.7.2): the app now ships framework-dependent**, and the CLI as its
> own release artifact rather than inside the app's archive. Measured: the app archive dropped from
> 101 MB to 7.6 MB, the CLI is 0.22 MB, and the .NET runtime ships nowhere at all — both resolve it
> from the machine's installed .NET 10 Desktop Runtime. The trade accepted in exchange: a machine
> without that runtime fails at native load before managed code runs, so there is no in-app surface
> to offer a friendly prompt — the user gets the stock .NET error dialog, and the README names the
> prerequisite. The self-contained row above was right for what it weighed (download size vs first
> run); what changed is that the runtime dependency turned out cheaper than 94 MB per update. See
> [technical-debt.md](../technical-debt.md) §8.5, now closed with the before/after measurement, and
> the runbook for the new two-artifact shape.

**b · Signing.** There is no certificate. Unsigned means SmartScreen's "Windows protected your PC"
on first run for every user until reputation accumulates — which for a low-volume tool is
effectively never. Options: buy an OV certificate (~£200/yr, still needs reputation), buy EV
(instant reputation, more expensive, hardware token), or ship unsigned and document the warning
honestly in the README with the SHA-256 to check. **Decide it in ADR-012 rather than discovering it
at tag time.**

**c · What the update mechanism can honestly do**, which falls out of (a) and (b). The design
(`screens/12`) specifies three rows and an **Install and restart** button. A self-replacing updater
that is unsigned is exactly the shape of thing security software objects to, and it has to survive
replacing a running executable.

**Recommendation for 1.0:** build the whole `UPDATES` section and the weekly check — a `latest.json`
on GitHub Releases, `Check now`, `What changed` deep-linking to the release notes, and the
`Check for updates on its own` toggle, on by default — and make the action **Download it yourself**,
opening the release page. Ship "Install and restart" when there is a certificate to sign the
replacement with. Error 8's deep link into this section works either way, which is what phase 5
built it for.

**Weekly, never a badge.** An available update is not a notification, not a banner, not a badge —
`screens/12` is explicit, and error 8 is the single exception.

**The `[ComImport]` question ([technical-debt.md](../technical-debt.md) §2.4) does not block this**
and probably never will: `WindowsAudioEndpointInspector` was never ported, so there is no COM
interop in the codebase to break under AOT. Close the entry as "not applicable while endpoint
inspection is out of scope" rather than leaving it open forever.

---

### 5 · The first-run "Wave Link not found" variant (App) — [technical-debt.md](../technical-debt.md) §4.10, §2.2

Screen 4 ships its **found** variant. When Wave Link is absent on a first run, the app shows the
amber status-strip line and nothing else — which reads as "this app is broken" rather than "point
me at your settings file". `SettingsLocator.Locate(explicitSettingsPath)` **already bypasses
discovery entirely**, so a non-MSIX user can be served; nothing surfaces the door.

**Design first** (`06-errors.md` gains the variant), then a Screen 4 branch keyed on
`WaveLinkInputs == null` that drops the green found-line, shows a neutral note, and keeps
*Choose where to keep them* — plus a way to supply a settings path, which is the actual fix for
§2.2 and today is a CLI flag only.

---

### 6 · Keyboard, focus and the icon set (App) — [technical-debt.md](../technical-debt.md) §7.4, §4.7

- **Keyboard and focus:** Windows conventions, not just the design's list — `Esc` closes a dialog,
  `Enter` presses the default button, `F6`/`Ctrl+Tab` move between panes, focus is visible for
  every interactive element, and the focus ring survives high contrast (`FocusRingTests` covers
  part of this already). Tab order gets asserted where it is asserted at all.
- **The icon set:** `TrayIconRenderer` draws all four tray states to the 24px grid at runtime and
  its four glyph constants are the substitution point. The eleven Lucide glyphs the design names
  are still hand-drawn stand-ins. This is a data change, and it is the kind of temporary thing that
  becomes permanent if it is not listed.

---

### 7 · Repository, licence and README — [[ADR-008]], [[ADR-002]]

- **MIT attribution for `voltybat/WaveLinkSettingsUtility`** — required by the licence, and the
  fork is the reason discovery, the shutdown sequence and the atomic write are as good as they are.
  A `NOTICE`/`THIRD-PARTY` file plus the README's own paragraph.
- **Windows-only above the fold** ([[ADR-008]]) — before the screenshot, not in a footnote.
- **What the README has to say that the marketing does not:** backups are machine-local; licences
  are not captured; tier 4 needs elevation to restore; the app must be running for automatic
  backups to happen.
- **A screenshot, and the brand mark as the app icon.**
- **The repo goes public only after §1 lands.**

---

### 8 · Release engineering (CI)

- A release workflow: build, run the full suite, publish `wlbackup` (AOT) and the WPF app
  (per ADR-012), emit `SHA256SUMS`, create the GitHub release from the CHANGELOG's section for
  that version.
- `Directory.Build.props` stays the version of record, and the CHANGELOG's newest heading matches
  it — the rule is already written down; the release workflow should assert it rather than trust it.
- A tag protection or a checklist step so a release cannot be cut from a branch with a failing
  suite.

---

### 9 · The 1.0 gate — what must close, and what may ship open

Written as a table so that "we'll fix it before release" cannot quietly mean "we won't".

| Item | Gates 1.0? | Why |
|---|---|---|
| §6 privacy / redaction | **Yes** | Cannot be retrofitted once reports are public |
| §4.10 not-found first run | **Yes** | The app looks broken to every non-MSIX user |
| `WHEN WINDOWS STARTS` section | **Yes** | Autostart cannot be enabled from the app at all |
| MIT attribution, Windows-only README | **Yes** | Licence obligation, and [[ADR-008]] |
| Packaging + checksums | **Yes** | There is no release without an artifact |
| §4.7 real icon set | No | Stand-ins render correctly; substituting is a data change |
| §4.15 dialog frosting seen by a human | No, but do it | Needs a desktop, not a build |
| §4.8 tray minors (DPI re-render, menu toggle glyph) | No | Cosmetic at 100–150% scaling |
| §4.11 shared size arithmetic | No | DRY gap, not a defect |
| §4.14 one flat list / cross-group arrow keys | No | Accepted limitation, real refactor |
| §4.16 rehashing plugin binaries per capture | No | Cost, not correctness; needs a measurement first |
| §2.2 whether non-MSIX installs exist | No | Unanswerable without the installers; the escape hatch exists |
| §2.4 `[ComImport]` under AOT | No | No COM interop in the codebase to break |
| Code signing | **Decide, not necessarily do** | ADR-012 must state the answer and the README must match it |

---

## Testing

| Test | Pins |
|---|---|
| A serial-bearing device ID appears nowhere in a redacted diagnostic | The gate |
| One device ID redacts identically as key, bare reference and `<id>\|<suffix>` composite | SPEC §3's foreign keys |
| A path under the user profile is folded, in every field that carries one | The username leak |
| The redactor returns a report for a malformed settings file rather than throwing | It is wanted most when things are broken |
| The nine-day notification fires once and not again while the condition holds | "Fires once, not daily" |
| The rejected-restore notification fires from the log verdict, not from the UI | SPEC §4 |
| A successful backup notifies nothing, ever | The design's central rule |
| Autostart toggle round-trips through the Run key, and reads off + disabled when Task Manager blocked it | Task Manager wins |
| `ClosingHidesToTray` off routes a close through the full shutdown path including the capture | Coherent, not dangerous |
| First run with no Wave Link renders the neutral note and still offers both actions | §4.10 |
| The published artifact runs on a machine with no SDK | The exit criterion |
| `Directory.Build.props` version equals the newest CHANGELOG heading | The rule that is currently trust-based |

## Risks

| Risk | Early signal | Response |
|---|---|---|
| Redaction is partial and a serial ships in a "redacted" report | A fixture finds the serial in a field nobody enumerated | Redact by **substitution over the whole tree**, then assert absence of the raw value in the output — never by allow-listing fields |
| The toast route needs a Start-menu shortcut the portable zip does not place | Notifications silently never appear when run from a folder | Decide ADR-013 **after** ADR-012, and keep the balloon fallback |
| "Install and restart" is built, unsigned, and flagged by security software | SmartScreen or an AV quarantine on the updater | Ship the download-it-yourself action for 1.0; gate self-update on a certificate |
| The privacy work slips because it has no screen | §1 still unstarted when §4 is in progress | It is the first work item and the first gate row; a design pass for `screens/13` is its own deliverable |
| Release scope grows into a second GUI phase | New screens appearing in this phase's plan | Everything not in the list above is [post-1.0.md](post-1.0.md) |

## References

- `SPEC.md` §11 — shipping publicly, the privacy note, and the open questions
- [[ADR-002]] — the fork, its licence, and finding 5 (the packaging decision)
- [[ADR-008]] — Windows-only scope
- [technical-debt.md](../technical-debt.md) §6 (privacy), §4.7, §4.8, §4.10, §7.4, §2.2, §2.4
- `operations/design/screens/12-tray-autostart-update.md` — the tray, autostart and update design
- [post-1.0.md](post-1.0.md) — what is deliberately after this
