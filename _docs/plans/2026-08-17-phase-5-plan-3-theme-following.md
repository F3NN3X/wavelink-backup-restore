---
title: "Phase 5 Plan 3 — Live theme following, the accent, and Windows 11 chrome"
status: published
created: 2026-08-17
updated: 2026-08-17
related_adrs: [ADR-005, ADR-008]
tags: [plan, implementation, app, theming, mica, phase-5]
---

# Phase 5 Plan 3 — Theme Following and Chrome Implementation Plan


**Goal:** Make the three dictionaries plan 2 built actually *follow Windows* — dark/light, high
contrast and the user's accent, live and without a restart — and give the tray's context menu
the Windows 11 treatment it currently lacks.

**Architecture:** One seam, `ISystemTheme`, wraps every way Windows can tell us the palette
changed and raises a single `Changed` event. `ThemeManager` grows from "pick a theme once" to
"re-apply on demand, and derive the accent brushes at swap time". A second seam, `IWindowChrome`,
wraps `DwmSetWindowAttribute` so the backdrop, the dark frame and the rounded corners are three
calls behind one interface with a fake — testable without a desktop, like the other Windows
seams (design §B).

**Tech Stack:** C# / .NET 10, WPF, `Windows.UI.ViewManagement.UISettings` (WinRT),
`Microsoft.Win32.SystemEvents`, `DwmSetWindowAttribute` via `DllImport`, xunit.v3.

**Spec:** [2026-08-17-phase-5-shell-design.md](2026-08-17-phase-5-shell-design.md) §B and §C ·
[screens/01-tokens-and-mapping.md](../operations/design/screens/01-tokens-and-mapping.md) ·
[screens/11-high-contrast.md](../operations/design/screens/11-high-contrast.md) ·
[screens/12-tray-autostart-update.md](../operations/design/screens/12-tray-autostart-update.md)

## Global Constraints

- `WaveLinkBackup.Core` stays **`net10.0`**. Nothing in this plan touches it.
- `TreatWarningsAsErrors` is on, repo-wide.
- **No colour literals outside `Theming/*.xaml`.** `ThemeTests.No_xaml_outside_the_theme_dictionaries_contains_a_colour_literal` enforces it and will fail on a `#RRGGBB` in a menu template. Radii, sizes and durations are fine; colours are not.
- **`WlDanger` never follows the accent.** Pinned by test in dark and light already; the derivation added here must not widen to it.
- `DllImport`, not `LibraryImport` — the generator wants `AllowUnsafeBlocks` for the whole project (technical-debt §7.1).
- High contrast **outranks** dark/light: it is Windows saying the palette is not ours, not a third preference.
- Build: `dotnet build WaveLinkBackup.slnx` · Test: `dotnet test WaveLinkBackup.slnx`
- Baseline: **439 tests green** (295 Core, 91 CLI, 53 App), Release zero warnings.

## What plan 2 left standing that this plan replaces

Read these before starting; each is a deliberate placeholder, not an oversight.

| In place now | Why it was enough | What this plan does |
|---|---|---|
| `ThemeManager.IsSystemInLightMode` reads `HKCU\...\Themes\Personalize` | Kept the WinRT surface out of plan 2's startup path | Replaced by `ISystemTheme`, which is where `UISettings` was always going to live |
| `ThemeManager.Apply` is called once, in `OnStartup` | Nothing was listening for a change yet | Called again from `ISystemTheme.Changed` |
| `WlAccent` is the authored red; `WlAccentSoft`/`WlAccentLine` are authored at 12%/32% (dark) and 7%/24% (light) | Correct when the user has no accent preference | `WlAccent` follows `UIColorType.Accent`; soft and line are **derived from it at swap time**, so the accent enters the app in exactly one place |
| `TrayIconRenderer.ColourFor` takes `bool highContrast` from `SystemParameters.HighContrast` at the call site | One caller, one read | Fed from `ISystemTheme` so the icon redraws when the scheme changes |
| Tray icon is rendered at a fixed 32px | The shell had no DPI story yet | See §Related, below — not necessarily this plan |

---

## Two findings logged from plan 2's session

These were found while building and running the tray shell. Both are the kind of thing that
costs an afternoon if it is rediscovered rather than read.

### A · Mica on a WPF popup has a hard prerequisite

**DWM backdrops are incompatible with layered windows.** `DWMWA_SYSTEMBACKDROP_TYPE` silently
does nothing on an HWND with `WS_EX_LAYERED`, and a WPF `Popup` is layered whenever it allows
transparency. For a `ContextMenu` that is controlled by **`HasDropShadow`, which defaults to
`true`** — so the default tray menu can never take a backdrop, and the failure mode is "the
call succeeded and nothing looks different", which is the worst kind.

So the order is: `HasDropShadow="False"` **first**, then the DWM attributes, then draw the
shadow and the rounded corner ourselves (or let DWM round it, below).

**And the material is Acrylic, not Mica.** Windows 11 uses Mica for long-lived window
backgrounds and *Acrylic* for transient surfaces — menus, flyouts, context menus. The tray menu
is transient. Using `DWMSBT_MAINWINDOW` on it would look wrong in exactly the way that reads as
"someone applied an effect", which is why the caption bar and the menu take **different**
backdrop types from the same seam:

| Attribute | Value | For the tray menu | For the caption bar (plan 4) |
|---|---|---|---|
| `DWMWA_USE_IMMERSIVE_DARK_MODE` | `20` | follow `ISystemTheme` | follow `ISystemTheme` |
| `DWMWA_WINDOW_CORNER_PREFERENCE` | `33` | `DWMWCP_ROUND` = `2` | `DWMWCP_DEFAULT` = `0` |
| `DWMWA_SYSTEMBACKDROP_TYPE` | `38` | `DWMSBT_TRANSIENTWINDOW` = `3` (Acrylic) | `DWMSBT_MAINWINDOW` = `2` (Mica) |

> `DWMWA_USE_IMMERSIVE_DARK_MODE` was `19` before Windows 10 2004, and
> `DWMWA_SYSTEMBACKDROP_TYPE` needs Windows 11 22H2 (build 22621); on build 22000 the only way
> in was the undocumented `DWMWA_MICA_EFFECT` = `1029`. The App project's
> `SupportedOSPlatformVersion` is `10.0.19041.0`, so **every one of these must be
> feature-detected and fail soft** — an unstyled menu on Windows 10 is fine, a crash is not.
> `DwmSetWindowAttribute` returns an `HRESULT`; check it rather than assuming.

**Getting the HWND.** A `ContextMenu`'s popup HWND does not exist until the menu is opening.
Hook `ContextMenu.Opened` (not `Opening` — the HWND is not created yet) and resolve it with
`PresentationSource.FromVisual(menu)` cast to `HwndSource`. It is a **new HWND on some opens**,
so apply the attributes on every `Opened`, not once.

### B · The tray menu header does not yet match its spec

`12-tray-autostart-update.md` specifies the header as a **mono 10px label** plus a readout:

```
LAST BACKUP                        (mono 10px label + TODAY 23:07 · 5 INPUTS)
```

What plan 2 shipped is a single default-styled string, `LAST BACKUP · 23:07`, with **no input
count** and no mono treatment. The label and the mono type are fixed by the template work in
Task 4 below. The input count is the real dependency: it needs the newest snapshot's
`SnapshotManifest.InputNames.Count`, which means `BackupHost` (or the App) holding a reference
to the `SnapshotStore` it currently only reaches through `BackupService`. That is a small,
deliberate widening — see Task 4 Step 4 — and it is the reason this was left out of plan 2
rather than guessed at.

Also unimplemented from the same block: *"`Pause for an hour` becomes `Resume` while paused"*
**is** done, but the design's `TODAY`/date qualifier on the readout is not — `23:07` alone is
ambiguous once a backup is more than a day old.

---

## File Structure

| File | Responsibility |
|---|---|
| `src/WaveLinkBackup.App/Windows/ISystemTheme.cs` · `UiSettingsTheme.cs` | *Create* — one `Changed` event over UISettings + SystemEvents + HighContrast |
| `src/WaveLinkBackup.App/Windows/IWindowChrome.cs` · `DwmWindowChrome.cs` | *Create* — the three DWM attributes, feature-detected |
| `src/WaveLinkBackup.App/Theming/AccentPalette.cs` | *Create* — accent → the four derived brushes, pure |
| `src/WaveLinkBackup.App/Theming/ThemeManager.cs` | *Modify* — take `ISystemTheme`, re-apply, overlay the accent |
| `src/WaveLinkBackup.App/Views/TrayMenuStyles.xaml` | *Create* — the Windows 11 `ContextMenu`/`MenuItem`/`Separator` templates |
| `src/WaveLinkBackup.App/Views/TrayIcon.xaml` | *Modify* — `HasDropShadow="False"`, adopt the styles, mono header |
| `src/WaveLinkBackup.App/App.xaml.cs` | *Modify* — own the seams, re-render the icon on change |
| `tests/WaveLinkBackup.App.Tests/Fakes/FakeSystemTheme.cs` · `FakeWindowChrome.cs` | *Create* |
| `tests/WaveLinkBackup.App.Tests/AccentPaletteTests.cs` · `ThemeFollowingTests.cs` · `TrayMenuStyleTests.cs` | *Create* |

`tests/Wpf.cs` already exists and is the STA + `Application` harness every resource-dictionary
assertion needs — plan 2 confirmed xunit.v3 3.2.2 ships **no** STA attribute, and that an STA
thread alone is not enough because the `pack` URI scheme and its `WebRequest` prefix are both
registered by constructing a `System.Windows.Application`. Reuse it; do not re-derive it.

---

### Task 1: The system theme seam

**Files:**
- Create: `src/WaveLinkBackup.App/Windows/ISystemTheme.cs`
- Create: `src/WaveLinkBackup.App/Windows/UiSettingsTheme.cs`
- Create: `tests/WaveLinkBackup.App.Tests/Fakes/FakeSystemTheme.cs`

**Interfaces:**
- Produces:
  - `interface ISystemTheme : IDisposable { AppTheme Theme { get; } Color Accent { get; } bool IsHighContrast { get; } event EventHandler? Changed; void Start(); }`
  - `sealed class UiSettingsTheme : ISystemTheme`

- [x] **Step 1: Write the interface**

Three reads and one event. One event rather than three, because every source below means the
same thing to the app — *the palette moved, re-apply* — and a consumer that has to subscribe to
three is a consumer that will subscribe to two.

- [x] **Step 2: Write `UiSettingsTheme` over all three sources**

| Source | Tells us |
|---|---|
| `UISettings.ColorValuesChanged` | accent, and dark/light |
| `UISettings.GetColorValue(UIColorType.Accent)` | the accent itself |
| `UISettings.GetColorValue(UIColorType.Background)` | dark vs light — a dark background means dark mode; more reliable than the registry value plan 2 used |
| `SystemEvents.UserPreferenceChanged` (`Category.Color`) | high contrast turning on or off (`11` names this explicitly) |
| `SystemParameters.HighContrast` | the current high-contrast state |

> **`ColorValuesChanged` does not arrive on the UI thread.** Marshal to the dispatcher before
> raising `Changed`, or the first thing that touches a `ResourceDictionary` throws. Hold the
> `UISettings` instance in a **field** — the event unsubscribes itself if it is collected, and
> the symptom is theme following that works for a minute and then quietly stops.

- [x] **Step 3: Write the fake, and the tests it makes possible**

`FakeSystemTheme` with settable `Theme`/`Accent`/`IsHighContrast` and a `RaiseChanged()`,
mirroring `FakeSettingsWatcher`'s shape.

---

### Task 2: The accent derivation, and re-applying on change

The accent enters the app in **exactly one place**. `01-tokens-and-mapping.md`: *"When the
user's accent is set, `--wl-accent-soft` = accent at 12% (dark) / 7% (light) and
`--wl-accent-line` = accent at 32% / 24%."*

**Files:**
- Create: `src/WaveLinkBackup.App/Theming/AccentPalette.cs`
- Modify: `src/WaveLinkBackup.App/Theming/ThemeManager.cs`
- Create: `tests/WaveLinkBackup.App.Tests/AccentPaletteTests.cs`
- Create: `tests/WaveLinkBackup.App.Tests/ThemeFollowingTests.cs`

**Interfaces:**
- Produces:
  - `static IReadOnlyDictionary<string, Color> AccentPalette.Derive(Color accent, AppTheme theme)`
  - `static void ThemeManager.Apply(AppTheme theme, Color? accent = null)`
  - `static void ThemeManager.Follow(ISystemTheme system)`

- [x] **Step 1: Write the failing tests**

The ones that carry risk:

- Dark derives soft at 12% and line at 32%; light derives 7% and 24%. A table test.
- **`WlDanger` is untouched by every accent.** Extend the existing pinned assertion: derive from a violently different accent and assert `WlDanger` still reads `#FFF01616` / `#FFAA0000`.
- `WlAccentInk` does **not** derive — it is the ink *on* the accent, and deriving it from the accent is how you get white-on-yellow. Leave it authored.
- High contrast **ignores the accent entirely**: `11` says the accent is gone and primary becomes `Highlight`. Assert `Apply(HighContrast, someAccent)` leaves the dictionary's `WlAccent` bound to `SystemColors.HighlightColorKey`.
- A `Changed` on the fake re-applies: flip `FakeSystemTheme.Theme` to Light, raise, assert `Application.Current.Resources["WlBg"]` is the light value.
- **Slot ordering survives a swap.** `App.xaml` merges the theme at slot 0 and `Views/TrayIcon.xaml` after it; a re-apply that appended instead of replacing slot 0 would put the theme *after* the menu styles and break every `DynamicResource` in them. Assert slot 0 is still the theme after two swaps.

- [x] **Step 2: Write `AccentPalette`, pure**

Colour in, colours out. No WPF beyond the `Color` struct, so the percentages are a table test
rather than something only a screenshot can catch.

- [x] **Step 3: Overlay, do not rewrite, in `ThemeManager.Apply`**

Load the dictionary, then write the derived keys over the authored ones. Overlaying rather than
editing the XAML keeps `Dark.xaml`/`Light.xaml` readable on their own and keeps them correct
for the no-accent-preference case.

- [x] **Step 4: `Follow(ISystemTheme)`**

Subscribe, apply once immediately, and re-apply on `Changed`. This is also where the tray icon
must be re-rendered — a scheme change that repaints the window but leaves a stale icon in the
notification area is the visible half of the bug.

---

### Task 3: The DWM seam

**Files:**
- Create: `src/WaveLinkBackup.App/Windows/IWindowChrome.cs`
- Create: `src/WaveLinkBackup.App/Windows/DwmWindowChrome.cs`
- Create: `tests/WaveLinkBackup.App.Tests/Fakes/FakeWindowChrome.cs`

**Interfaces:**
- Produces:
  - `enum Backdrop { None, Mica, Acrylic }`
  - `enum Corners { Default, Rounded }`
  - `interface IWindowChrome { bool Apply(IntPtr hwnd, Backdrop backdrop, Corners corners, bool dark); }`

- [x] **Step 1: Write it against the constants in finding A above**

Do not re-derive the attribute numbers; they are in the table. Three `DwmSetWindowAttribute`
calls, each `HRESULT`-checked, each allowed to fail independently — an older Windows that
rejects the backdrop should still get the dark frame.

`Apply` returns whether the **backdrop** took, because that is the one a caller may want to
compensate for by painting a solid `WlChrome` instead.

- [x] **Step 2: Fail soft, and prove it**

`FakeWindowChrome` records the calls. The real one is not unit-tested — design §E lists Mica
under **Not tested** — but the *decision table* is: assert the tray menu asks for
`Acrylic` + `Rounded` and the main window asks for `Mica` + `Default`, so finding A's
distinction cannot be quietly lost in a later refactor.

---

### Task 4: The tray menu, as Windows 11 draws it

The user-visible half, and the reason this plan moved up the queue.

**Files:**
- Create: `src/WaveLinkBackup.App/Views/TrayMenuStyles.xaml`
- Modify: `src/WaveLinkBackup.App/Views/TrayIcon.xaml`
- Modify: `src/WaveLinkBackup.App/App.xaml`, `App.xaml.cs`
- Create: `tests/WaveLinkBackup.App.Tests/TrayMenuStyleTests.cs`

- [x] **Step 1: `HasDropShadow="False"` on the `ContextMenu`**

**Do this first and understand why** — finding A. Without it every DWM call in Step 3 succeeds
and changes nothing.

- [x] **Step 2: Template the menu to Windows 11 geometry**

Every colour a `DynamicResource` on a `Wl*` key; the guard test fails the build on a literal.
Windows 11's own metrics, for reference: 8px outer corner radius, 4px inner padding, ~32px item
height, 4px item corner radius on hover, and **no legacy icon gutter or checkmark column** —
`Back up automatically` is a check on the right or a trailing glyph, not a Windows-7 sunken box.

Surfaces: `WlChrome` for the menu (it is the Mica tint role), `WlHover` for hover, `WlLine` for
the separators, `WlText` for items and `WlMuted` for the readout. A disabled item is
`WlMuted` — and in high contrast `GrayText` at **full** opacity, never 40% (`11`).

- [x] **Step 3: Apply the chrome on every `Opened`**

`PresentationSource.FromVisual(menu)` as `HwndSource`, on `Opened` not `Opening`, every time —
finding A. `Acrylic` + `Rounded` + dark from `ISystemTheme`.

- [x] **Step 4: Make the header match its spec — finding B**

Mono 10px label, and the readout gains its date qualifier and input count:
`LAST BACKUP · TODAY 23:07 · 5 INPUTS`.

The input count needs the newest snapshot's `Manifest.InputNames.Count`. `App` already builds
the `SnapshotStore` in `Compose`; return it alongside the host and the service rather than
reaching through `BackupService`. Keep the read cheap — `RefreshTray` runs every 15 seconds, so
cache it and invalidate on a successful capture rather than listing the store on every tick.

> If the store is unreadable, the readout says `LAST BACKUP · NEVER` rather than showing a
> count of zero inputs. A zero there reads as "your backup has no inputs", which is a different
> and much more alarming claim than "we could not look".

- [x] **Step 5: Check by hand**

None of this is unit-testable:

- [x] The menu has rounded corners, an app-coloured surface, and no square legacy frame — **confirmed by eye 2026-08-17.** Note the wording: this step originally asked for "a translucent Acrylic background" and that is no longer what is wanted. See Outcome, below.
- [x] The menu header reads `LAST BACKUP · TODAY 10:17 · 5 INPUTS` — confirmed by eye
- [ ] Switching Windows to light mode restyles the menu **without restarting the app**
- [ ] Switching the Windows accent recolours what uses `WlAccent`, and **nothing** that was red stops being red
- [ ] Turning high contrast on gives a menu with no tints, `WindowText` borders, and a `GrayText` disabled item
- [ ] The tray icon repaints on all three changes
- [ ] On a Windows 10 machine (or with the backdrop forced to fail) the menu is plain but not broken

---

### Task 5: Guards, and the seams reaching `App`

- [x] **Step 1: Own the seams in `App.OnStartup`**

`UiSettingsTheme` and `DwmWindowChrome` are constructed after single-instance and before the
first `ThemeManager` call, and disposed in `ShutdownEverything` alongside the tray and the host.
`SystemEvents` holds a **static** subscription — an undisposed `UiSettingsTheme` keeps the
process alive at exit, which on a tray app looks exactly like a leak because it is one.

- [x] **Step 2: The guard that matters most**

The colour-literal scan already covers new XAML. Add its counterpart for the accent: a test
that walks `ThemeManager.BrushKeys` and asserts **only** `WlAccent`, `WlAccentSoft` and
`WlAccentLine` change when the accent does. `WlDanger` is the one the design calls out, but a
whitelist catches the next one too.

- [x] **Step 3: Full suite and Release**

Run: `dotnet test WaveLinkBackup.slnx` · `dotnet build WaveLinkBackup.slnx -c Release`
Expected: green, zero warnings, **≥ 465** tests.

---

## Done when

- [x] `dotnet build WaveLinkBackup.slnx -c Release` — zero warnings
- [x] `dotnet test WaveLinkBackup.slnx` — all green, **473** tests (295 Core, 91 CLI, 87 App)
- [x] Changing Windows dark/light restyles the app and the tray menu with no restart — **by test**, not by eye; see Outcome
- [x] Changing the Windows accent moves `WlAccent` and **not** `WlDanger` — by test
- [x] Turning high contrast on removes every tint and ignores the accent — by test
- [x] The tray menu has rounded corners on Windows 11 and is plain-but-working without
- [x] The menu header reads `LAST BACKUP · TODAY 23:07 · 5 INPUTS`
- [ ] The tray icon repaints on a scheme change — **not verified.** The wiring is asserted (`The_after_apply_callback_runs_after_the_swap_not_before`), but nobody has watched the icon change colour while flipping Windows to light mode.

## Outcome — 2026-08-17

Built and shipped in six commits. Three things went differently from the plan.

**The tray menu takes no backdrop at all.** Finding A got the material question right in the
abstract and wrong for this app. Acrylic was implemented, and the result — seen by eye — read as
neither native Windows nor Wave Link Backup: a flat grey box belonging to nothing. The cause was
the surface role, not the material. `WlChrome` is defined as the *"Windows 11 Mica caption/strip
tint"*, so it only means anything with Mica behind it; with the backdrop not visibly landing, the
grey was all that was left.

So the menu is now an opaque **`WlCard`** surface with a `WlLine2` hairline — the design's role
for a raised panel, which is what a floating menu is. `ChromeChoice.ForTrayMenu` returns
`Backdrop.None`. The rounded corner and the theme-matched frame stayed, because those are DWM
doing two things the app cannot do for itself and neither is a colour decision.

Finding A's table is still correct for **the caption bar**, which is where a backdrop earns its
keep and where the design asks for Mica by name. Plan 4 should read it as a window contract, not
a menu one.

**The menu must be rebuilt, not just restyled.** A tray icon's `ContextMenu` has no parent in any
visual tree, so the resources-changed notification an `Application.Resources` swap raises never
reaches it: its `DynamicResource`s resolve once, at load, and then never again. Reopening the menu
does not refresh them and neither does `UpdateLayout` — both were tried, and
`TrayMenuStyleTests.The_menus_colours_follow_the_theme_rather_than_being_baked_in` failed on both
before `App.RebuildTrayMenu` existed. Styling alone would have shipped a menu permanently frozen
in whichever theme was current at startup, which is exactly what "follows the OS" must not mean.

**The readout was wrong for a reason the plan did not name.** Finding B blamed the missing input
count on having no `SnapshotStore` reference, which was true. It missed that
`BackupHost.LastBackupAt` only knows about captures made during the current run — so a freshly
started app said "no backup yet" with backups on disk, in the tooltip as well as the header. Both
now read the store.

Two smaller notes: `ChromeChoice` was extracted so the which-surface-gets-what decision is
asserted rather than folklore and so plan 4 inherits it as a contract; and the accent guard landed
as a **whitelist** (`Only_the_three_accent_roles_move_when_the_accent_does`) rather than the
`WlDanger`-shaped hole the plan described, so the next role that must not follow the accent is
caught without anyone remembering to add a test.

One design rule is worth re-confirming with a human: the check on *Back up automatically* is
`WlAccent`, so it follows the Windows accent and is **not** the brand red. That is
`01-tokens-and-mapping.md` working as written, and it does mean the check is whatever colour the
user's accent is.

## What this plan does not do

- **The 34px custom caption bar** — it is screen 1's chrome and belongs with the window in plan 4. `IWindowChrome` is built here and the bar asks it for `Mica` + `Default` when it exists; finding A's table is the contract between the two plans.
- **The backup list** — plan 4.
- **The Settings dialog**, including the autostart toggle `IAutostart` was built for — plan 5. Autostart still has no UI and is reachable only from tests.

## Related, not scheduled

- **Mono letter-spacing is not implemented.** The type scale gives section labels `.18em`
  tracking, and `TextBlock` has no WPF equivalent — there is no `CharacterSpacing` outside WinUI.
  The tray menu's readout therefore renders untracked. Faking it (per-character `Run`s, or a
  `Glyphs` element) is possible and was judged not worth it for one label; it becomes worth
  deciding properly when the column headers and status strip arrive in plan 4, since they use the
  same two mono styles at much greater width. `TrayMenuStyles.xaml` points here.
- **`Back up automatically` shows a trailing check, not a switch.** `screens/12`'s ASCII sketch
  writes `[toggle]`. A switch inside a menu is not something Windows itself draws, so the sketch
  was read as shorthand. Flagged rather than silent: it is a one-line template change if the
  literal reading was meant.

- **The tray icon is rendered at a fixed 32px.** Correct at 100% and 150%; soft at 200%+. The
  right size comes from the DPI of the screen holding the taskbar, and it should re-render on a
  DPI change. Small, real, and not blocking anything — worth its own entry rather than being
  smuggled into a theming plan.
- **There is no icon set** (technical-debt §4.7). `TrayIconRenderer`'s four glyphs are drawn to
  the Lucide 24px grid as placeholders and should be replaced by the real shield-check mark when
  one exists.
