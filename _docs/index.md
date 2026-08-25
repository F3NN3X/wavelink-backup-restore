---
title: "Wave Link Backup: Documentation Index"
status: published
created: 2026-08-16
updated: 2026-08-25
tags: [meta, index]
---

# Wave Link Backup, Documentation Index

Wave Link Backup snapshots and restores Elgato Wave Link's mixer configuration. Wave Link keeps
about three days of its own rolling copies, so a configuration that breaks over a long weekend is
already gone by the time anyone notices. This app is the safety net.

The whole payload is one 43 KB JSON file and a typical backup set is about 470 KB, which is small
enough to keep one snapshot per distinct content hash indefinitely.

---

## Start here

| Document | What it is |
|---|---|
| [SPEC.md](SPEC.md) | The build specification. Where the settings live, what's inside them, the restore sequence, the validation traps, the VST3 tiering. The authority on what to build. Read its Provenance section before treating any number as a constant. |
| [dev-phases/README.md](dev-phases/README.md) | What is built and what is left, phase by phase, with entry and exit criteria. [spec-coverage.md](dev-phases/spec-coverage.md) maps each `SPEC.md` requirement to where it stands. |
| [technical-debt.md](technical-debt.md) | What is known-wrong on purpose, and which numbers will bite you if you hard-code them. |

Everything else here explains why something is the way it is, or records what bit us.
[README.md](README.md) covers how the folder is organised and how to add to it.

> Read the Corrections block at the top of [SPEC.md](SPEC.md) before relying on it. Three of its
> claims were measured against a live install on 2026-08-16 and did not survive. The important
> one is the JSON encoder recommendation in §5 and §7·2, which is inverted and would cause the
> problem it describes. The spec body is left unedited on purpose.

---

## Current state

v0.7.6. Both shells work. `wlbackup` backs up, lists, restores, renames, deletes, verifies,
prunes, empties the trash and watches; the WPF tray app does the same through a window with the
designed screens, the twelve errors, the settings dialog and high contrast. The CLI publishes as
a 3.2 MB NativeAOT binary, verified against a real install.

The three founding problems are solved. Snapshots survive an MSIX package reset
([[ADR-003]]), backups happen without anyone remembering to take them ([[ADR-007]]), and there is
something to run ([[ADR-004]]).

All four VST3 tiers capture and restore. Tier 3 reads two preset roots ([[ADR-010]]) after the
first run against a real vendor folder turned up an interface default and a MIDI map where 172
presets should have been; a snapshot went from 61 preset files to 491. Tier 4 restore reaches the
shell once elevation has a designed surface ([[ADR-011]]).

Release is the remaining phase, and 1.0 is gated on the privacy work rather than on features.
[CHANGELOG.md](../CHANGELOG.md) has the version-by-version history.

---

## Decisions

Eighteen records. Read ADR-001 and ADR-002 first; the rest follow from them.

| ADR | Decision |
|---|---|
| [ADR-001](decisions/ADR-001-csharp-over-rust.md) | C# / .NET over Rust |
| [ADR-002](decisions/ADR-002-fork-wavelinksettingsutility.md) | Fork `voltybat/WaveLinkSettingsUtility` rather than write fresh |
| [ADR-003](decisions/ADR-003-backup-store-outside-localstate.md) | The backup store lives outside `LocalState`, identified by manifest |
| [ADR-004](decisions/ADR-004-core-library-thin-shells.md) | A headless core library with WPF and CLI shells |
| [ADR-005](decisions/ADR-005-wpf-for-the-gui.md) | WPF over WinUI 3, Avalonia and WinForms |
| [ADR-006](decisions/ADR-006-vst3-four-tier-capture.md) | Four independently switchable VST3 tiers; capture what is referenced, not what is installed |
| [ADR-007](decisions/ADR-007-hash-dedup-and-file-watching.md) | Content-hash dedup and a file watcher, not a schedule |
| [ADR-008](decisions/ADR-008-windows-only-scope.md) | Windows-only, and say so out loud |
| [ADR-009](decisions/ADR-009-hand-rolled-cli-parsing.md) | Hand-rolled command-line parsing, no dependency |
| [ADR-010](decisions/ADR-010-two-preset-roots-and-a-rooted-snapshot-layout.md) | Two preset roots, and a snapshot layout that names them, corrects ADR-006's tier 3 |
| [ADR-011](decisions/ADR-011-elevate-by-relaunching-the-shell.md) | Elevate by relaunching the shell, for one restore, and never otherwise |
| [ADR-012](decisions/ADR-012-check-only-updates-with-a-staged-swap.md) | Update by staging beside the install and swapping, never elevated |
| [ADR-013](decisions/ADR-013-a-theme-preference-behind-the-system-theme-seam.md) | A theme preference. Auto, Dark, Light, High contrast, behind the existing system-theme seam |
| [ADR-014](decisions/ADR-014-the-health-strip-is-as-wide-as-the-rig.md) | The health strip is as wide as the rig, and collapse is a drop against the previous snapshot |
| [ADR-015](decisions/ADR-015-the-details-view-reads-the-backup-itself.md) | The details view reads the backup's own settings file, on demand |
| [ADR-016](decisions/ADR-016-a-restore-brings-the-service-back-before-it-relaunches.md) | A restore brings the service back before it relaunches |
| [ADR-017](decisions/ADR-017-source-generated-com-and-unsafe-on-core.md) | COM interop is source-generated, and Core gets `AllowUnsafeBlocks` |
| [ADR-018](decisions/ADR-018-a-third-notification-and-an-update-notice-on-the-strip.md) | A third notification, and an update notice on the strip |

---

## Gotchas

Thirty-three ways this goes wrong, titled by symptom. You do not know the cause when you go
looking, which is why you are looking.

### Capture and restore

| Symptom | Gotcha |
|---|---|
| The backup tool runs, reports success, and protects nothing | [backup-succeeds-but-protects-nothing.md](knowledge-base/gotchas/backup-succeeds-but-protects-nothing.md) |
| The file parses fine but Wave Link resets to defaults | [file-parses-but-wave-link-resets.md](knowledge-base/gotchas/file-parses-but-wave-link-resets.md) |
| Restoring the newest backup restores the broken config | [newest-backup-is-the-broken-one.md](knowledge-base/gotchas/newest-backup-is-the-broken-one.md) |
| Every snapshot differs from the last, and diffs are useless | [every-snapshot-differs-with-no-real-change.md](knowledge-base/gotchas/every-snapshot-differs-with-no-real-change.md) |
| Capture fails with "being used by another process" | [capture-fails-while-wave-link-is-running.md](knowledge-base/gotchas/capture-fails-while-wave-link-is-running.md) |
| The restore writes cleanly, then the old settings come back | [restored-settings-revert-seconds-later.md](knowledge-base/gotchas/restored-settings-revert-seconds-later.md) |
| The plugin is restored but refuses to run | [restored-plugin-demands-a-licence.md](knowledge-base/gotchas/restored-plugin-demands-a-licence.md) |
| A plugin backs up as zero bytes | [vst3-backs-up-as-nothing.md](knowledge-base/gotchas/vst3-backs-up-as-nothing.md) |
| Someone else's backup produces dead channels | [restored-backup-has-dead-channels.md](knowledge-base/gotchas/restored-backup-has-dead-channels.md) |
| Deleting one backup takes its neighbours with it | [deleting-one-backup-takes-its-neighbours.md](knowledge-base/gotchas/deleting-one-backup-takes-its-neighbours.md) |
| The backup says it saved your presets, and they are not in it | [backup-says-it-saved-your-presets-and-it-did-not.md](knowledge-base/gotchas/backup-says-it-saved-your-presets-and-it-did-not.md) |
| Windows asks for rights the app already had | [windows-asks-for-rights-the-app-already-had.md](knowledge-base/gotchas/windows-asks-for-rights-the-app-already-had.md) |

### The shell

Several of these came out of the 0.5.1 design audit, and the group matters more than any member:
every one lived in a view no test had ever constructed.

| Symptom | Gotcha |
|---|---|
| The window never opens and nothing says why | [the-window-never-opens-and-nothing-says-why.md](knowledge-base/gotchas/the-window-never-opens-and-nothing-says-why.md) |
| A dialog opens as a black rectangle | [a-dialog-opens-as-a-black-rectangle.md](knowledge-base/gotchas/a-dialog-opens-as-a-black-rectangle.md) |
| Dialogs are see-through in high contrast | [dialogs-are-see-through-in-high-contrast.md](knowledge-base/gotchas/dialogs-are-see-through-in-high-contrast.md) |
| A binding expression appears on screen | [a-binding-expression-appears-on-screen.md](knowledge-base/gotchas/a-binding-expression-appears-on-screen.md) |
| Three backups look selected at once | [three-backups-look-selected-at-once.md](knowledge-base/gotchas/three-backups-look-selected-at-once.md) |
| A control in the Settings dialog moves and nothing happens | [a-settings-control-moves-and-nothing-happens.md](knowledge-base/gotchas/a-settings-control-moves-and-nothing-happens.md) |
| The row shows stale data after you update it | [the-row-shows-stale-data-after-you-update-it.md](knowledge-base/gotchas/the-row-shows-stale-data-after-you-update-it.md) |
| The tray icon refuses every image you draw | [the-tray-icon-refuses-every-image-you-draw.md](knowledge-base/gotchas/the-tray-icon-refuses-every-image-you-draw.md) |
| The tray menu keeps the theme it started with | [tray-menu-keeps-the-theme-it-started-with.md](knowledge-base/gotchas/tray-menu-keeps-the-theme-it-started-with.md) |
| An accelerator shows as a literal underscore | [an-accelerator-shows-as-a-literal-underscore.md](knowledge-base/gotchas/an-accelerator-shows-as-a-literal-underscore.md) |
| Pressing Back up now closes the whole app | [pressing-back-up-now-closes-the-whole-app.md](knowledge-base/gotchas/pressing-back-up-now-closes-the-whole-app.md) |
| A chip draws its box and not its label | [a-chip-draws-its-box-and-not-its-label.md](knowledge-base/gotchas/a-chip-draws-its-box-and-not-its-label.md) |
| Every older backup turns amber after adding a channel | [every-older-backup-turns-amber-after-adding-a-channel.md](knowledge-base/gotchas/every-older-backup-turns-amber-after-adding-a-channel.md) |
| The list will not scroll with the wheel | [the-list-will-not-scroll-with-the-wheel.md](knowledge-base/gotchas/the-list-will-not-scroll-with-the-wheel.md) |
| Scrolling the list selects a row | [scrolling-the-list-selects-a-row.md](knowledge-base/gotchas/scrolling-the-list-selects-a-row.md) |
| The app dies before the window with a culture error | [the-app-dies-before-the-window-with-a-culture-error.md](knowledge-base/gotchas/the-app-dies-before-the-window-with-a-culture-error.md) |

### Builds, updates and the suite

| Symptom | Gotcha |
|---|---|
| COM interop stops compiling the moment the project is AOT-compatible | [com-interop-stops-compiling-the-moment-the-project-is-aot-compatible.md](knowledge-base/gotchas/com-interop-stops-compiling-the-moment-the-project-is-aot-compatible.md) |
| Every update fails its checksum | [every-update-fails-its-checksum.md](knowledge-base/gotchas/every-update-fails-its-checksum.md) |
| The update installs nothing and says nothing | [the-update-installs-nothing-and-says-nothing.md](knowledge-base/gotchas/the-update-installs-nothing-and-says-nothing.md) |
| A progress report never arrives in a test | [a-progress-report-never-arrives-in-a-test.md](knowledge-base/gotchas/a-progress-report-never-arrives-in-a-test.md) |
| The serializer that never throws, throws | [the-serializer-that-never-throws-throws.md](knowledge-base/gotchas/the-serializer-that-never-throws-throws.md) |

---

## Patterns

Extracted from shipped code, each naming its real callers.

| Pattern | What it makes impossible |
|---|---|
| [pure-analysis-core.md](knowledge-base/patterns/pure-analysis-core.md) | Re-serializing the file you are backing up |
| [named-method-seams.md](knowledge-base/patterns/named-method-seams.md) | Choosing the wrong file share mode |
| [preconditions-inside-the-operation.md](knowledge-base/patterns/preconditions-inside-the-operation.md) | Writing while Wave Link is still exiting |
| [guards-that-can-fail.md](knowledge-base/patterns/guards-that-can-fail.md) | A guard that silently never matches |
| [decisions-as-pure-functions.md](knowledge-base/patterns/decisions-as-pure-functions.md) | A conditional rule that is wrong in the one branch nobody exercised |

## Recipes and runbooks

| Document | When |
|---|---|
| [Restore a settings file safely](knowledge-base/recipes/restore-a-settings-file-safely.md) | Every restore. The order is load-bearing at every step. |
| [Publish the NativeAOT binary](knowledge-base/recipes/publish-the-native-aot-binary.md) | Cutting the CLI release artifact. |
| [Releasing a version, and how the app updates itself](operations/runbooks/releasing-and-updating.md) | Cutting a release, and every question about how the app finds one. One document, because a release in the wrong shape is invisible to the updater. |
| [Screen 1 by-eye checklist](operations/design/screen-1-by-eye-checklist.md) | Checking the things only a human looking at the window can check. |

## Audits

| Audit | Subject |
|---|---|
| [2026-08-15, voltybat/WaveLinkSettingsUtility](audits/2026-08-15-voltybat-wavelinksettingsutility.md) | The upstream we forked: what to take, what to fix first |
| [2026-08-19, the app against the design package](audits/2026-08-19-design-conformance.md) | Every screen read against the design: one structural layout defect, six smaller fixes, eight designed surfaces never drawn |
| [2026-08-20, plug-in resolution and elevation](audits/2026-08-20-plugin-resolution-and-elevation.md) | The app was asking for administrator rights it already had. Read this before touching tier 4 restore. |

---

## The rest

- [glossary.md](glossary.md). The words this project uses precisely. "Backup" alone means three
  different things, so start here if a document reads oddly.
- [templates.md](templates.md). Copy from here when adding a document.
- [archive/](archive/). Closed technical debt, kept with its reasoning intact.
