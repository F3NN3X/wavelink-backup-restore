---
title: "Phase 5 Plan 4 — The backup list (screen 1)"
status: published
created: 2026-08-17
updated: 2026-08-17
related_adrs: [ADR-004, ADR-005, ADR-008]
tags: [plan, implementation, app, wpf, screen-1, phase-5]
---

# Phase 5 Plan 4 — The Backup List Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build screen 1 — the main window: its 34px caption bar with Mica, the status
strip, the column header, the five-slot list with its three health states, search, the
bottom action bar, and the high-contrast treatment of all of it.

**Architecture:** Three view models over Core's existing records — `SnapshotRowViewModel`
(one row, five slots always), `SnapshotListViewModel` (grouping, search, selection) and
`ShellViewModel` (the two strips and what the buttons may do). Everything the design encodes
in *shape* is decided in a view model and rendered by a template, so the whole screen is
table-testable without a desktop. High contrast is a flag on the shell that switches
templates, not a fourth palette. Health arrives asynchronously: `List()` reads manifests, a
background probe hashes, and rows flip to DAMAGED as answers come back.

**Tech Stack:** C# / .NET 10, WPF, `System.Windows.Shell.WindowChrome`, embedded OFL fonts
(Rubik, JetBrains Mono), `DrawingContext.DrawText`, xunit.v3.

**Spec:** [2026-08-17-phase-5-shell-design.md](2026-08-17-phase-5-shell-design.md) §C ·
[operations/design/README.md](../operations/design/README.md) §Screen 1 ·
[screens/02-backup-health-states.md](../operations/design/screens/02-backup-health-states.md) ·
[screens/07-search.md](../operations/design/screens/07-search.md) ·
[screens/10-decisions.md](../operations/design/screens/10-decisions.md) ·
[screens/11-high-contrast.md](../operations/design/screens/11-high-contrast.md)

---

## Global Constraints

- `WaveLinkBackup.Core` stays **`net10.0`**. **Nothing in this plan touches Core.** Design §D's
  four Core additions all shipped in plan 1.
- `TreatWarningsAsErrors` is on, repo-wide.
- **No colour literals outside `Theming/*.xaml`.**
  `ThemeTests.No_xaml_outside_the_theme_dictionaries_contains_a_colour_literal` scans every
  `.xaml` under `src/WaveLinkBackup.App` except `Theming/` and fails on `#RRGGBB`. Sizes, radii
  and durations are fine; colours are not. **This is why `Typography.xaml` goes in `Views/`,
  not `Theming/`** — putting it in `Theming/` would exempt it from the scan for free.
- **README §Screen 1 is WRONG about the SUSPECT pill.** It specifies `--wl-accent-soft` fill /
  `--wl-accent` text — the red-inside-amber version `10-decisions.md` §1 and `02` overturned.
  **The pill is transparent fill, 1px `WlWarn` border, `WlWarn` text.** Everywhere.
- **`WlDanger` never follows the accent.**
  `ThemeFollowingTests.Only_the_three_accent_roles_move_when_the_accent_does` already pins this.
- High contrast **outranks** dark/light, and in high contrast:
  - every tint and fill is transparent; surfaces are told apart by 1px `WindowText` borders,
  - disabled is `GrayText` at **full opacity**, never 40%,
  - the 3px left edge is gone and the row's meta line becomes a verdict word,
  - the accent and red are gone; primary is `Highlight` + `HighlightText`.
- **No 2px outlines anywhere.** The health slots use a 2px bottom **rule** (`10-decisions.md` §5).
- Disabled in the normal themes is **40% opacity of the normal treatment**, never a different colour.
- Every mono micro-label is UPPERCASE. Rubik carries sentences; mono carries anything a machine
  produced. No emoji.
- Build: `dotnet build WaveLinkBackup.slnx` · Test: `dotnet test WaveLinkBackup.slnx`
- Baseline: **473 tests green** (295 Core, 91 CLI, 87 App), Release zero warnings.

## Four decisions taken before writing this plan

The design closes every question it raises; these four it does not raise. Each was put to a
human and answered, and each shapes more than one task.

| Question | Answer | Where it lands |
|---|---|---|
| How is DAMAGED detected, when `List()` deliberately does not hash? | **Verify every snapshot on a background thread**, on open and on F5; rows start WHOLE/SUSPECT and flip | Task 5 |
| Rubik and JetBrains Mono are not in the repo | **Embed the static OFL instances**; the design says "should be embedded with the app" | Task 1 |
| Mono letter-spacing has no WPF equivalent (technical-debt §4.8 item 2) | **Build a tracked-text element**; column headers and the status strip use it at width | Task 2 |
| Rename / Delete / Restore open dialogs deferred to a later session | **Render live** — enable/disable exactly as designed — and **placeholder on click**, the answer plan 3 gave Settings | Task 8, Task 11 |

## Inherited contracts

Read these before starting. Each cost somebody an afternoon already.

| Contract | Source |
|---|---|
| `ChromeChoice.ForMainWindow(highContrast)` returns **Mica + `Corners.Default`**. The caption bar asks it rather than deciding for itself | Plan 3 finding A, as corrected by plan 3's Outcome |
| `IWindowChrome.Apply` **returns whether the backdrop took**, so the caller can paint a solid `WlChrome` instead when it did not | Plan 3 Task 3 Step 1 |
| **A DWM backdrop is silently ignored on a layered window.** `AllowsTransparency="True"` makes a WPF window layered. It must stay **`False`** | Plan 3 finding A |
| `tests/Wpf.cs` is the STA + `Application` harness every resource-dictionary assertion needs. xunit.v3 3.2.2 ships **no** STA attribute, and an STA thread alone is not enough — the `pack` URI scheme is registered by constructing a `System.Windows.Application`. **Reuse it; do not re-derive it** | Plan 2 |
| `ThemeManager.Apply` replaces merged dictionary **slot 0** in place. Anything merged after it resolves against it | `ThemeManager.cs` |
| A `DynamicResource` in a tree with no parent never refreshes. The tray menu is rebuilt for that reason; **`MainWindow` has a visual parent and does not need rebuilding** | `App.RebuildTrayMenu` |

## Where README, the screens folder and the design disagree

Resolved here so nobody resolves them differently mid-build.

| Subject | Sources | Use |
|---|---|---|
| SUSPECT pill colour | README says accent; `02` and `10-decisions` §1 say warn | **Warn.** The screens folder extends README and postdates it |
| Suspect meta line | README `471 KB · failed validation`; `02` `471 KB · FAILED VALIDATION` | **`02`** — it is the screen-specific, later spec |
| Bottom bar line 2 | README `4 BACKUPS · 12.4 MB IN %LOCALAPPDATA%\WaveLinkBackup · 118 GB FREE`; design §D and `IFileSystem` doc say `… 12.4 MB USED · 118 GB FREE` | **README.** It is the only spec for screen 1's chrome |
| Row detail filename | README `2026-08-11_2136_before-3-3-beta.wlbk` | **`snapshot.Id`.** This store keeps directories, not `.wlbk` files; printing a filename that does not exist is worse than printing the one that does |

---

## File Structure

| File | Responsibility |
|---|---|
| `src/WaveLinkBackup.App/Fonts/*.ttf` · `Fonts/OFL-*.txt` | *Create* — five static font instances and their licences |
| `src/WaveLinkBackup.App/Views/Typography.xaml` | *Create* — the two families and the ten type styles |
| `src/WaveLinkBackup.App/Views/TrackedText.cs` | *Create* — a text element that honours letter-spacing |
| `src/WaveLinkBackup.App/Hosting/ShellState.cs` | *Create* — window geometry + `ClosingHidesToTray`, App-owned |
| `src/WaveLinkBackup.App/Hosting/ShellStateRepository.cs` | *Create* — `shell.json` beside `settings.json`, tolerant read |
| `src/WaveLinkBackup.App/Hosting/HealthProbe.cs` | *Create* — background verification, `SnapshotHealth` |
| `src/WaveLinkBackup.App/ViewModels/Readable.cs` | *Create* — bytes, relative time, dates. Pure |
| `src/WaveLinkBackup.App/ViewModels/InputSlots.cs` | *Create* — five slots always five. Pure |
| `src/WaveLinkBackup.App/ViewModels/SnapshotRowViewModel.cs` | *Create* — one row, every cell |
| `src/WaveLinkBackup.App/ViewModels/SnapshotListViewModel.cs` | *Create* — grouping, search, selection, list state |
| `src/WaveLinkBackup.App/ViewModels/ShellViewModel.cs` | *Create* — the two strips, and what the buttons may do |
| `src/WaveLinkBackup.App/ViewModels/ObservableObject.cs` | *Create* — the one `INotifyPropertyChanged` base |
| `src/WaveLinkBackup.App/ViewModels/ShellCommands.cs` | *Create* — the key map, as data |
| `src/WaveLinkBackup.App/Views/MainWindow.xaml` · `.xaml.cs` | *Move + rewrite* — from the project root; caption bar, strips, list |
| `src/WaveLinkBackup.App/Views/RowStyles.xaml` | *Create* — row template, slots, pills, badges, HC switches |
| `src/WaveLinkBackup.App/Views/ControlStyles.xaml` | *Create* — buttons, search field, focus ring |
| `src/WaveLinkBackup.App/App.xaml` · `App.xaml.cs` | *Modify* — merge the new dictionaries, own the probe and shell state |
| `src/WaveLinkBackup.App/WaveLinkBackup.App.csproj` | *Modify* — the fonts as `Resource` |
| `tests/WaveLinkBackup.App.Tests/*` | *Create* — twelve new test files and one harness, listed per task |

`ViewModels/` and `Fonts/` are new folders; `Hosting/`, `Views/` and `Theming/` already exist.
This is design §A's layout, minus `Settings` and `SettingsWindow`, which are plan 5.

---

### Task 1: Embed the two families, and give the app one type dictionary

Screen 1 is where type carries the design: ten of its labels are mono micro-labels whose whole
job is to look machine-made. `TrayMenuStyles.xaml` currently declares the two `FontFamily` keys
locally over a fallback stack; this promotes them and makes them real.

**Files:**
- Create: `src/WaveLinkBackup.App/Fonts/Rubik-Regular.ttf`, `Rubik-Medium.ttf`, `Rubik-Bold.ttf`
- Create: `src/WaveLinkBackup.App/Fonts/JetBrainsMono-Regular.ttf`, `JetBrainsMono-Medium.ttf`
- Create: `src/WaveLinkBackup.App/Fonts/OFL-Rubik.txt`, `OFL-JetBrainsMono.txt`
- Create: `src/WaveLinkBackup.App/Views/Typography.xaml`
- Modify: `src/WaveLinkBackup.App/WaveLinkBackup.App.csproj`
- Modify: `src/WaveLinkBackup.App/App.xaml`
- Modify: `src/WaveLinkBackup.App/Views/TrayMenuStyles.xaml`
- Create: `tests/WaveLinkBackup.App.Tests/TypographyTests.cs`

**Interfaces:**
- Produces: resource keys `WlDisplayFont`, `WlMonoFont` (both `FontFamily`), and the ten
  `Style` keys `WlDialogTitleText`, `WlRowNameText`, `WlBodyText`, `WlSecondaryText`,
  `WlMonoReadoutText`, `WlMonoMetaText`, `WlStatusStripText`, `WlColumnHeaderText`,
  `WlTierBadgeText`, `WlSlotLabelText` (all `TargetType="TextBlock"`; the four tracked ones
  gain their `Tracking` in Task 2).

- [ ] **Step 1: Fetch the static instances — not the variable fonts**

> **WPF does not support variable fonts.** A `Rubik[wght].ttf` loads as a single default
> instance, so `FontWeight="Medium"` and `FontWeight="Bold"` both render at 400 and the whole
> type scale silently collapses to one weight. Google Fonts publishes the variable file at the
> top of each directory and the usable ones in `static/`. **Take the `static/` files.**

```bash
mkdir -p src/WaveLinkBackup.App/Fonts
BASE=https://raw.githubusercontent.com/google/fonts/main/ofl

curl -fSL "$BASE/rubik/static/Rubik-Regular.ttf"  -o src/WaveLinkBackup.App/Fonts/Rubik-Regular.ttf
curl -fSL "$BASE/rubik/static/Rubik-Medium.ttf"   -o src/WaveLinkBackup.App/Fonts/Rubik-Medium.ttf
curl -fSL "$BASE/rubik/static/Rubik-Bold.ttf"     -o src/WaveLinkBackup.App/Fonts/Rubik-Bold.ttf
curl -fSL "$BASE/rubik/OFL.txt"                   -o src/WaveLinkBackup.App/Fonts/OFL-Rubik.txt

curl -fSL "$BASE/jetbrainsmono/static/JetBrainsMono-Regular.ttf" -o src/WaveLinkBackup.App/Fonts/JetBrainsMono-Regular.ttf
curl -fSL "$BASE/jetbrainsmono/static/JetBrainsMono-Medium.ttf"  -o src/WaveLinkBackup.App/Fonts/JetBrainsMono-Medium.ttf
curl -fSL "$BASE/jetbrainsmono/OFL.txt"                          -o src/WaveLinkBackup.App/Fonts/OFL-JetBrainsMono.txt
```

Both families are SIL OFL 1.1. The licence requires the text to travel with the binary, which
is what the two `OFL-*.txt` files are for — they ship as `Resource` alongside the fonts.

> If a `static/` path 404s, list the directory at
> `https://github.com/google/fonts/tree/main/ofl/rubik/static` and take the equivalently-named
> file. Do **not** substitute the variable font.

- [ ] **Step 2: Include them as `Resource`**

In `src/WaveLinkBackup.App/WaveLinkBackup.App.csproj`, add before the closing `</Project>`:

```xml
  <!--
    Resource, not Content: the fonts must live INSIDE the assembly so a pack URI resolves them
    and a single-file publish keeps working. The .ttf extension is not picked up by the WPF SDK
    globs, so this include is required rather than tidy.

    Static instances, never the variable files. WPF has no variable-font support - a [wght] file
    loads as one default instance and every weight in the type scale renders identically.
  -->
  <ItemGroup>
    <Resource Include="Fonts\*.ttf" />
    <Resource Include="Fonts\OFL-*.txt" />
  </ItemGroup>
```

- [ ] **Step 3: Write the failing tests**

Create `tests/WaveLinkBackup.App.Tests/TypographyTests.cs`:

```csharp
using System.Windows;
using System.Windows.Media;

namespace WaveLinkBackup.App.Tests;

/// <summary>
/// The embedding, and the trap underneath it: WPF cannot use a variable font. A [wght] file
/// resolves to ONE instance, so Rubik 500 and Rubik 700 would render identically and nothing
/// would look obviously broken - it would just look flat.
/// </summary>
public sealed class TypographyTests
{
    private static readonly Uri Base = new("pack://application:,,,/WaveLinkBackup;component/");

    private static FontFamily Family(string key) => Wpf.Run(() =>
    {
        var dictionary = new ResourceDictionary
        {
            Source = new Uri(Base, "Views/Typography.xaml"),
        };

        return (FontFamily)dictionary[key];
    });

    [Fact]
    public void The_display_family_is_rubik_and_is_embedded()
    {
        var family = Family("WlDisplayFont");

        Assert.Contains("Rubik", family.Source, StringComparison.Ordinal);
        Assert.NotEmpty(family.GetTypefaces());
    }

    [Fact]
    public void The_mono_family_is_jetbrains_mono_and_is_embedded()
    {
        var family = Family("WlMonoFont");

        Assert.Contains("JetBrains", family.Source, StringComparison.Ordinal);
        Assert.NotEmpty(family.GetTypefaces());
    }

    // The variable-font trap, pinned. Three DISTINCT glyph typefaces means three real weights
    // were embedded; a variable file would collapse them onto one.
    [Fact]
    public void Rubik_ships_regular_medium_and_bold_as_separate_faces()
    {
        var family = Family("WlDisplayFont");

        var faces = Wpf.Run(() => new[] { FontWeights.Regular, FontWeights.Medium, FontWeights.Bold }
            .Select(w => new Typeface(family, FontStyles.Normal, w, FontStretches.Normal))
            .Select(t => t.TryGetGlyphTypeface(out var g) ? g.FontUri.ToString() : null)
            .ToArray());

        Assert.All(faces, f => Assert.NotNull(f));
        Assert.Equal(3, faces.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void Jetbrains_mono_ships_regular_and_medium_as_separate_faces()
    {
        var family = Family("WlMonoFont");

        var faces = Wpf.Run(() => new[] { FontWeights.Regular, FontWeights.Medium }
            .Select(w => new Typeface(family, FontStyles.Normal, w, FontStretches.Normal))
            .Select(t => t.TryGetGlyphTypeface(out var g) ? g.FontUri.ToString() : null)
            .ToArray());

        Assert.All(faces, f => Assert.NotNull(f));
        Assert.Equal(2, faces.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    // Every size in README's type table has a style. A missing one is a control that quietly
    // renders at WPF's 12px default, which looks like a spacing bug rather than a type bug.
    [Theory]
    [InlineData("WlDialogTitleText")]
    [InlineData("WlRowNameText")]
    [InlineData("WlBodyText")]
    [InlineData("WlSecondaryText")]
    [InlineData("WlMonoReadoutText")]
    [InlineData("WlMonoMetaText")]
    [InlineData("WlStatusStripText")]
    [InlineData("WlColumnHeaderText")]
    [InlineData("WlTierBadgeText")]
    [InlineData("WlSlotLabelText")]
    public void Every_type_role_has_a_style(string key)
    {
        var style = Wpf.Run(() =>
        {
            var dictionary = new ResourceDictionary { Source = new Uri(Base, "Views/Typography.xaml") };
            return dictionary[key] as Style;
        });

        Assert.NotNull(style);
        Assert.Equal(typeof(System.Windows.Controls.TextBlock), style.TargetType);
    }
}
```

> `Wpf.Run` is the existing STA + `Application` harness in `tests/Wpf.cs`. Open it first and
> match how `ThemeTests` calls it — its exact signature is what the snippets above assume.

- [ ] **Step 4: Run the tests to verify they fail**

Run: `dotnet test tests/WaveLinkBackup.App.Tests --filter FullyQualifiedName~TypographyTests`
Expected: FAIL — `Views/Typography.xaml` does not exist.

- [ ] **Step 5: Write `Views/Typography.xaml`**

Create `src/WaveLinkBackup.App/Views/Typography.xaml`:

```xml
<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">

    <!--
      "Anything a machine produced is mono; anything a person wrote or reads as a sentence is
      Rubik." - README. Times, sizes, counts, paths, filenames, column headers, status readouts
      and slot labels are mono. Everything else is Rubik.

      Both families are EMBEDDED as static instances. The trailing '#Family Name' is the family
      inside the resource, and the folder URI must end in a slash. WPF has no variable-font
      support, so the [wght] files would give one weight for all three - see the csproj comment.

      This file lives in Views/ and not Theming/ on purpose: the colour-literal guard skips
      Theming/, and a type dictionary that could smuggle a #RRGGBB past the scan is not worth
      the tidier folder. Nothing here sets a colour anyway.
    -->
    <FontFamily x:Key="WlDisplayFont">pack://application:,,,/WaveLinkBackup;component/Fonts/#Rubik</FontFamily>
    <FontFamily x:Key="WlMonoFont">pack://application:,,,/WaveLinkBackup;component/Fonts/#JetBrains Mono</FontFamily>

    <!-- Rubik. Sentences, names, labels, buttons. -->

    <Style x:Key="WlDialogTitleText" TargetType="TextBlock">
        <Setter Property="FontFamily" Value="{DynamicResource WlDisplayFont}" />
        <Setter Property="FontWeight" Value="Medium" />
        <Setter Property="FontSize" Value="20" />
        <Setter Property="LineHeight" Value="26" />
    </Style>

    <Style x:Key="WlRowNameText" TargetType="TextBlock">
        <Setter Property="FontFamily" Value="{DynamicResource WlDisplayFont}" />
        <Setter Property="FontWeight" Value="Medium" />
        <Setter Property="FontSize" Value="14.5" />
        <Setter Property="TextTrimming" Value="CharacterEllipsis" />
    </Style>

    <Style x:Key="WlBodyText" TargetType="TextBlock">
        <Setter Property="FontFamily" Value="{DynamicResource WlDisplayFont}" />
        <Setter Property="FontSize" Value="14" />
        <Setter Property="LineHeight" Value="21.7" />
        <Setter Property="TextWrapping" Value="Wrap" />
    </Style>

    <Style x:Key="WlSecondaryText" TargetType="TextBlock">
        <Setter Property="FontFamily" Value="{DynamicResource WlDisplayFont}" />
        <Setter Property="FontSize" Value="12.5" />
        <Setter Property="LineHeight" Value="18.75" />
        <Setter Property="TextWrapping" Value="Wrap" />
    </Style>

    <!-- JetBrains Mono. Times, paths, sizes, counts, and every micro-label. -->

    <Style x:Key="WlMonoReadoutText" TargetType="TextBlock">
        <Setter Property="FontFamily" Value="{DynamicResource WlMonoFont}" />
        <Setter Property="FontSize" Value="12.5" />
    </Style>

    <Style x:Key="WlMonoMetaText" TargetType="TextBlock">
        <Setter Property="FontFamily" Value="{DynamicResource WlMonoFont}" />
        <Setter Property="FontSize" Value="11.5" />
    </Style>

    <!--
      The four TRACKED roles. README's type table gives them .14em, .18em, .12em and .06em, and
      TextBlock has no WPF equivalent - there is no CharacterSpacing outside WinUI. Task 2 adds
      the element that honours it; until then these render untracked, which is exactly what the
      tray readout has been doing since plan 3 (technical-debt 4.8 item 2).
    -->
    <Style x:Key="WlStatusStripText" TargetType="TextBlock">
        <Setter Property="FontFamily" Value="{DynamicResource WlMonoFont}" />
        <Setter Property="FontWeight" Value="Medium" />
        <Setter Property="FontSize" Value="11" />
    </Style>

    <Style x:Key="WlColumnHeaderText" TargetType="TextBlock">
        <Setter Property="FontFamily" Value="{DynamicResource WlMonoFont}" />
        <Setter Property="FontWeight" Value="Medium" />
        <Setter Property="FontSize" Value="10.5" />
    </Style>

    <Style x:Key="WlTierBadgeText" TargetType="TextBlock">
        <Setter Property="FontFamily" Value="{DynamicResource WlMonoFont}" />
        <Setter Property="FontWeight" Value="Medium" />
        <Setter Property="FontSize" Value="10" />
    </Style>

    <Style x:Key="WlSlotLabelText" TargetType="TextBlock">
        <Setter Property="FontFamily" Value="{DynamicResource WlMonoFont}" />
        <Setter Property="FontWeight" Value="Medium" />
        <Setter Property="FontSize" Value="9.5" />
    </Style>

</ResourceDictionary>
```

- [ ] **Step 6: Merge it, and point the tray menu at it**

In `src/WaveLinkBackup.App/App.xaml`, merge `Views/Typography.xaml` **after** the theme slot.
Open the file first: `ThemeManager.Apply` owns slot 0 and replaces it in place, so Typography
must be merged after whatever is already declared there, exactly as `TrayIcon.xaml` is.

In `src/WaveLinkBackup.App/Views/TrayMenuStyles.xaml`, **delete** these two lines:

```xml
    <FontFamily x:Key="WlDisplayFont">Rubik, Segoe UI Variable, Segoe UI</FontFamily>
    <FontFamily x:Key="WlMonoFont">JetBrains Mono, Cascadia Mono, Consolas</FontFamily>
```

and replace them with a merge of the shared dictionary, so the menu and the window cannot drift:

```xml
    <ResourceDictionary.MergedDictionaries>
        <ResourceDictionary Source="pack://application:,,,/WaveLinkBackup;component/Views/Typography.xaml" />
    </ResourceDictionary.MergedDictionaries>
```

> `TrayMenuStyles.xaml` is loaded by `App.RebuildTrayMenu` through `TrayIcon.xaml` into a tree
> with no parent, which is why it must carry its own merge rather than relying on
> `Application.Resources`. The `Wl*` colour keys still come from the application dictionary via
> `DynamicResource` — only the fonts, which are static, are merged locally.

- [ ] **Step 7: Run the tests to verify they pass**

Run: `dotnet test tests/WaveLinkBackup.App.Tests --filter FullyQualifiedName~TypographyTests`
Expected: PASS, 14 tests.

- [ ] **Step 8: Run the whole suite**

Run: `dotnet test WaveLinkBackup.slnx`
Expected: PASS. `TrayMenuStyleTests` exercises the menu's fonts and must still be green — if it
fails on a missing `WlDisplayFont`, the merge in Step 6 went into the wrong dictionary.

- [ ] **Step 9: Commit**

```bash
git add src/WaveLinkBackup.App/Fonts \
        src/WaveLinkBackup.App/Views/Typography.xaml \
        src/WaveLinkBackup.App/Views/TrayMenuStyles.xaml \
        src/WaveLinkBackup.App/App.xaml \
        src/WaveLinkBackup.App/WaveLinkBackup.App.csproj \
        tests/WaveLinkBackup.App.Tests/TypographyTests.cs
git commit -m "feat: embed Rubik and JetBrains Mono, and declare the type scale once

Static instances, not the variable files: WPF has no variable-font support, so
a [wght] file would render every weight in the scale identically. The test
asserts three distinct Rubik faces so that trap cannot come back quietly."
```

---

### Task 2: `TrackedText` — the element that honours letter-spacing

technical-debt §4.8 item 2 names this plan as where the decision gets made. Four of the ten
type roles carry tracking, and between them they cover the column header, the status strip,
every tier badge, every slot label, both bottom-bar lines and the WHY pill — the labels the
design leans on hardest, at 9.5–11px where untracked uppercase mono is at its least legible.

**Why an element rather than an attached property.** These labels are data-bound, so an
attached property would have to observe `TextBlock.Text` changes through
`DependencyPropertyDescriptor.AddValueChanged`, which has no matching removal on the label's
way out and leaks the container. A `Text` dependency property on our own element reacts for
free. The cost is an `AutomationPeer`, which §7.4 requires us to write anyway.

**Files:**
- Create: `src/WaveLinkBackup.App/Views/TrackedText.cs`
- Create: `tests/WaveLinkBackup.App.Tests/TrackedTextTests.cs`

**Interfaces:**
- Produces:
  - `sealed class TrackedText : FrameworkElement`
  - `string TrackedText.Text { get; set; }` · `double TrackedText.Tracking { get; set; }` (em)
  - inherited `FontFamily` / `FontSize` / `FontWeight` / `Foreground` via `TextElement` add-owner
  - `double TrackedText.MeasureWidth(string text, Typeface typeface, double size, double trackingEm)` — static, pure, the part under test

- [ ] **Step 1: Write the failing tests**

Create `tests/WaveLinkBackup.App.Tests/TrackedTextTests.cs`:

```csharp
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using WaveLinkBackup.App.Views;

namespace WaveLinkBackup.App.Tests;

/// <summary>
/// Letter-spacing, which WPF does not have. The arithmetic is what is testable; that the
/// glyphs land where the arithmetic says is a by-eye check in Task 11.
/// </summary>
public sealed class TrackedTextTests
{
    private static Typeface Mono() => Wpf.Run(() =>
    {
        var dictionary = new ResourceDictionary
        {
            Source = new Uri("pack://application:,,,/WaveLinkBackup;component/Views/Typography.xaml"),
        };

        return new Typeface(
            (FontFamily)dictionary["WlMonoFont"],
            FontStyles.Normal, FontWeights.Medium, FontStretches.Normal);
    });

    [Fact]
    public void No_tracking_measures_the_same_as_the_plain_string()
    {
        var typeface = Mono();

        var tracked = Wpf.Run(() => TrackedText.MeasureWidth("INPUTS", typeface, 10.5, 0));
        var plain = Wpf.Run(() => new FormattedText(
            "INPUTS", CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            typeface, 10.5, Brushes.Black, 1.0).WidthIncludingTrailingWhitespace);

        Assert.Equal(plain, tracked, precision: 3);
    }

    // .18em at 10.5px is 1.89px per gap, and six characters have five gaps.
    [Fact]
    public void Tracking_adds_one_gap_per_pair_and_none_after_the_last()
    {
        var typeface = Mono();

        var untracked = Wpf.Run(() => TrackedText.MeasureWidth("INPUTS", typeface, 10.5, 0));
        var tracked = Wpf.Run(() => TrackedText.MeasureWidth("INPUTS", typeface, 10.5, 0.18));

        Assert.Equal(untracked + (5 * 0.18 * 10.5), tracked, precision: 3);
    }

    [Fact]
    public void A_single_character_gains_no_tracking()
    {
        var typeface = Mono();

        Assert.Equal(
            Wpf.Run(() => TrackedText.MeasureWidth("N", typeface, 10.5, 0)),
            Wpf.Run(() => TrackedText.MeasureWidth("N", typeface, 10.5, 0.18)),
            precision: 3);
    }

    [Fact]
    public void An_empty_string_measures_zero()
    {
        Assert.Equal(0, Wpf.Run(() => TrackedText.MeasureWidth("", Mono(), 10.5, 0.18)));
    }

    [Fact]
    public void The_element_measures_to_the_tracked_width()
    {
        var size = Wpf.Run(() =>
        {
            var element = new TrackedText
            {
                Text = "INPUTS",
                Tracking = 0.18,
                FontSize = 10.5,
                FontWeight = FontWeights.Medium,
            };

            element.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            return element.DesiredSize;
        });

        Assert.True(size.Width > 0);
        Assert.True(size.Height > 0);
    }

    // The five-slot strip reads as five unlabelled cells to a screen reader without this.
    [Fact]
    public void The_automation_name_is_the_text()
    {
        var name = Wpf.Run(() =>
        {
            var element = new TrackedText { Text = "5 INPUTS" };

            return System.Windows.Automation.Peers.UIElementAutomationPeer
                .CreatePeerForElement(element)!.GetName();
        });

        Assert.Equal("5 INPUTS", name);
    }

    // AutomationProperties.Name wins where a label needs to READ differently from how it looks -
    // "3 OF 14 MATCH BETA" is a mono strip, but a reader should hear a sentence.
    [Fact]
    public void An_explicit_automation_name_overrides_the_text()
    {
        var name = Wpf.Run(() =>
        {
            var element = new TrackedText { Text = "3 OF 14 MATCH \"BETA\"" };
            System.Windows.Automation.AutomationProperties.SetName(element, "3 of 14 backups match beta");

            return System.Windows.Automation.Peers.UIElementAutomationPeer
                .CreatePeerForElement(element)!.GetName();
        });

        Assert.Equal("3 of 14 backups match beta", name);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/WaveLinkBackup.App.Tests --filter FullyQualifiedName~TrackedTextTests`
Expected: FAIL — `TrackedText` does not exist.

- [ ] **Step 3: Write `TrackedText`**

Create `src/WaveLinkBackup.App/Views/TrackedText.cs`:

```csharp
using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Documents;
using System.Windows.Media;

namespace WaveLinkBackup.App.Views;

/// <summary>
/// A single-line text element that honours LETTER-SPACING, which WPF does not have.
///
/// README's type scale gives four mono roles .18em, .14em, .12em and .06em tracking, and
/// TextBlock has no equivalent - CharacterSpacing exists in WinUI and nowhere else. Faking it
/// with per-character Runs is not possible either: Inline has no Margin. So the characters are
/// drawn one at a time at accumulated offsets, which is exact and costs one DrawText per
/// character on a label of ten or so.
///
/// USE IT ONLY for the tracked mono micro-labels. Anything that wraps, selects, trims or holds
/// mixed inline runs - the row name with its search highlight, every sentence - stays a
/// TextBlock. This element deliberately does not do any of that.
///
/// Per-character drawing discards kerning and shaping. For uppercase Latin in a MONOSPACED
/// face that is a non-issue, and tracking is additive spacing anyway.
/// </summary>
public sealed class TrackedText : FrameworkElement
{
    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text), typeof(string), typeof(TrackedText),
        new FrameworkPropertyMetadata(
            string.Empty,
            FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Extra space between characters, in EM - .18 means .18em, as the design writes it.</summary>
    public static readonly DependencyProperty TrackingProperty = DependencyProperty.Register(
        nameof(Tracking), typeof(double), typeof(TrackedText),
        new FrameworkPropertyMetadata(
            0d,
            FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    // AddOwner rather than new properties: these then INHERIT down the visual tree exactly like
    // a TextBlock's, so a style that sets FontFamily on a container still reaches this element.
    public static readonly DependencyProperty FontFamilyProperty =
        TextElement.FontFamilyProperty.AddOwner(typeof(TrackedText), Metadata(new FontFamily("Segoe UI")));

    public static readonly DependencyProperty FontSizeProperty =
        TextElement.FontSizeProperty.AddOwner(typeof(TrackedText), Metadata(12d));

    public static readonly DependencyProperty FontWeightProperty =
        TextElement.FontWeightProperty.AddOwner(typeof(TrackedText), Metadata(FontWeights.Normal));

    public static readonly DependencyProperty FontStyleProperty =
        TextElement.FontStyleProperty.AddOwner(typeof(TrackedText), Metadata(FontStyles.Normal));

    public static readonly DependencyProperty ForegroundProperty =
        TextElement.ForegroundProperty.AddOwner(typeof(TrackedText), Metadata(SystemColors.ControlTextBrush));

    private static FrameworkPropertyMetadata Metadata(object defaultValue) => new(
        defaultValue,
        FrameworkPropertyMetadataOptions.AffectsMeasure
        | FrameworkPropertyMetadataOptions.AffectsRender
        | FrameworkPropertyMetadataOptions.Inherits);

    public TrackedText()
    {
        // The design's micro-labels are 9.5px to 11px. Rounded layout is what keeps a 2px rule
        // under one of them from landing on a half pixel and going grey.
        UseLayoutRounding = true;
        SnapsToDevicePixels = true;
    }

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public double Tracking
    {
        get => (double)GetValue(TrackingProperty);
        set => SetValue(TrackingProperty, value);
    }

    public FontFamily FontFamily
    {
        get => (FontFamily)GetValue(FontFamilyProperty);
        set => SetValue(FontFamilyProperty, value);
    }

    public double FontSize
    {
        get => (double)GetValue(FontSizeProperty);
        set => SetValue(FontSizeProperty, value);
    }

    public FontWeight FontWeight
    {
        get => (FontWeight)GetValue(FontWeightProperty);
        set => SetValue(FontWeightProperty, value);
    }

    public FontStyle FontStyle
    {
        get => (FontStyle)GetValue(FontStyleProperty);
        set => SetValue(FontStyleProperty, value);
    }

    public Brush Foreground
    {
        get => (Brush)GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    private Typeface Typeface => new(FontFamily, FontStyle, FontWeight, FontStretches.Normal);

    /// <summary>
    /// The arithmetic, pure and static so it can be asserted without a visual tree: every
    /// character's own advance, plus one gap between each PAIR. There is no gap after the last
    /// character - trailing tracking would push a right-aligned label off its edge.
    /// </summary>
    public static double MeasureWidth(string text, Typeface typeface, double size, double trackingEm)
    {
        if (string.IsNullOrEmpty(text)) return 0;

        var width = Text(text, typeface, size, Brushes.Black).WidthIncludingTrailingWhitespace;

        return width + (Math.Max(0, text.Length - 1) * trackingEm * size);
    }

    private static FormattedText Text(string text, Typeface typeface, double size, Brush brush) => new(
        text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, typeface, size, brush, 1.0);

    protected override Size MeasureOverride(Size availableSize)
    {
        if (string.IsNullOrEmpty(Text)) return new Size(0, 0);

        var line = Text(Text, Typeface, FontSize, Foreground);

        return new Size(MeasureWidth(Text, Typeface, FontSize, Tracking), line.Height);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        if (string.IsNullOrEmpty(Text)) return;

        // One DrawText per character at an accumulated offset. Drawing the whole string once
        // when there is no tracking is not just an optimisation - it keeps kerning and shaping
        // intact for the untracked case, which is the one that might not be monospaced.
        if (Tracking == 0)
        {
            drawingContext.DrawText(Text(Text, Typeface, FontSize, Foreground), new Point(0, 0));
            return;
        }

        var gap = Tracking * FontSize;
        var x = 0d;

        foreach (var character in Text)
        {
            var glyph = Text(character.ToString(), Typeface, FontSize, Foreground);

            drawingContext.DrawText(glyph, new Point(x, 0));
            x += glyph.WidthIncludingTrailingWhitespace + gap;
        }
    }

    /// <summary>
    /// Without this the four tracked roles are invisible to a screen reader, and 7.4 is explicit
    /// that reader labels are part of this work rather than a follow-up.
    ///
    /// AutomationProperties.Name still wins where it is set, so a label that should be HEARD
    /// differently from how it reads - "3 OF 14 MATCH BETA" - can say so.
    /// </summary>
    protected override AutomationPeer OnCreateAutomationPeer() => new TrackedTextAutomationPeer(this);

    private sealed class TrackedTextAutomationPeer(TrackedText owner) : FrameworkElementAutomationPeer(owner)
    {
        protected override string GetNameCore() =>
            AutomationProperties.GetName(owner) is { Length: > 0 } explicitName
                ? explicitName
                : owner.Text ?? string.Empty;

        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Text;

        protected override bool IsControlElementCore() => true;
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/WaveLinkBackup.App.Tests --filter FullyQualifiedName~TrackedTextTests`
Expected: PASS, 7 tests.

- [ ] **Step 5: Give the tray readout its tracking**

`TrayMenuStyles.xaml`'s readout is the one label technical-debt §4.8 item 2 says renders
untracked today. In `src/WaveLinkBackup.App/Views/TrayIcon.xaml`, replace the readout's
`TextBlock` with a `TrackedText` carrying `Tracking="0.14"` (it is status-strip type, mono 500
11px). Add the namespace to the file's root element:

```xml
xmlns:views="clr-namespace:WaveLinkBackup.App.Views"
```

Delete the pointer to technical-debt §4.8 item 2 in `TrayMenuStyles.xaml`'s header comment;
it is no longer true.

- [ ] **Step 6: Run the whole suite**

Run: `dotnet test WaveLinkBackup.slnx`
Expected: PASS. `TrayMenuStyleTests` reads the header's colour and text — if it asserts on
`TextBlock` by type, update that assertion to `TrackedText` rather than reverting the element.

- [ ] **Step 7: Commit**

```bash
git add src/WaveLinkBackup.App/Views/TrackedText.cs \
        src/WaveLinkBackup.App/Views/TrayIcon.xaml \
        src/WaveLinkBackup.App/Views/TrayMenuStyles.xaml \
        tests/WaveLinkBackup.App.Tests/TrackedTextTests.cs
git commit -m "feat: letter-spacing, as an element that draws its own characters

WPF has no CharacterSpacing and Inline has no Margin, so per-character DrawText
is the only exact way to honour the .18em the type scale asks for. Closes
technical-debt 4.8 item 2, which named this plan as where to decide it."
```

---

### Task 3: Shell state — window geometry and `ClosingHidesToTray`

Design §C: *"`08` enumerates `settings.json` as folder / auto switch / keep-count / chosen
installation, and shows it at 1 KB. Two things the shell needs are absent from that list:
window geometry and `ClosingHidesToTray`. Both go in an App-owned file beside it."*

That keeps `settings.json` matching its own on-screen description in the Settings dialog, and
keeps `BackupSettings` free of concepts Core cannot have an opinion about — Core has no window
to hide and no tray to hide it in ([[ADR-004]]).

**Files:**
- Create: `src/WaveLinkBackup.App/Hosting/ShellState.cs`
- Create: `src/WaveLinkBackup.App/Hosting/ShellStateRepository.cs`
- Create: `tests/WaveLinkBackup.App.Tests/ShellStateTests.cs`

**Interfaces:**
- Consumes: `IFileSystem`, `SettingsRepository.DefaultDirectory`
- Produces:
  - `sealed record ShellState(double? Left, double? Top, double? Width, double? Height, bool IsMaximized, bool ClosingHidesToTray)` with `static ShellState Default`
  - `static bool ShellState.IsOnScreen(ShellState state, IReadOnlyList<Rect> screens)`
  - `sealed class ShellStateRepository(IFileSystem fileSystem, string directoryPath)` with `const string FileName = "shell.json"`, `string FilePath`, `ShellState Read()`, `void Save(ShellState state)`

- [ ] **Step 1: Write the failing tests**

Create `tests/WaveLinkBackup.App.Tests/ShellStateTests.cs`:

```csharp
using System.Windows;
using WaveLinkBackup.App.Hosting;
using WaveLinkBackup.Core.Tests.Fakes;

namespace WaveLinkBackup.App.Tests;

/// <summary>
/// The App-owned half of persistence. settings.json describes itself in the Settings dialog as
/// the folder, the automatic-backup switch, how many to keep and which Wave Link you picked;
/// adding a window rectangle to it would make that sentence false.
/// </summary>
public sealed class ShellStateTests
{
    private const string Directory = @"C:\Users\t\AppData\Local\WaveLinkBackup";
    private const string File = @"C:\Users\t\AppData\Local\WaveLinkBackup\shell.json";

    private static ShellStateRepository Repository(FakeFileSystem fileSystem) => new(fileSystem, Directory);

    [Fact]
    public void Reads_defaults_when_the_file_does_not_exist()
    {
        Assert.Equal(ShellState.Default, Repository(new FakeFileSystem()).Read());
    }

    // Closing hides by default: the app is the process, not the window.
    [Fact]
    public void Closing_hides_to_tray_by_default()
    {
        Assert.True(ShellState.Default.ClosingHidesToTray);
    }

    [Fact]
    public void The_default_has_no_remembered_geometry()
    {
        Assert.Null(ShellState.Default.Left);
        Assert.Null(ShellState.Default.Width);
        Assert.False(ShellState.Default.IsMaximized);
    }

    [Fact]
    public void Saves_then_reads_the_same_state()
    {
        var repository = Repository(new FakeFileSystem());
        var state = new ShellState(120, 80, 1240, 800, IsMaximized: true, ClosingHidesToTray: false);

        repository.Save(state);

        Assert.Equal(state, repository.Read());
    }

    [Fact]
    public void The_file_sits_beside_settings_json()
    {
        Assert.Equal(File, Repository(new FakeFileSystem()).FilePath);
    }

    // Same tolerance as SettingsSerializer, for the same reason: this is a preferences file.
    [Fact]
    public void Unparseable_content_falls_back_to_defaults()
    {
        var fileSystem = new FakeFileSystem().AddFile(File, "not json at all");

        Assert.Equal(ShellState.Default, Repository(fileSystem).Read());
    }

    [Fact]
    public void A_broken_field_falls_back_alone()
    {
        var fileSystem = new FakeFileSystem().AddFile(File, """
            {"schemaVersion":1,"left":120,"top":"eighty","width":1240,"closingHidesToTray":false}
            """);

        var state = Repository(fileSystem).Read();

        Assert.Equal(120, state.Left);
        Assert.Null(state.Top);
        Assert.Equal(1240, state.Width);
        Assert.False(state.ClosingHidesToTray);
    }

    [Fact]
    public void Saving_never_throws_when_the_directory_cannot_be_created()
    {
        var fileSystem = new FakeFileSystem { FailDirectoryCreation = true };

        Repository(fileSystem).Save(ShellState.Default);
    }

    // The trap this file exists to avoid: a window remembered on a monitor that has since been
    // unplugged opens where nobody can see it, and a tray app whose window "won't open" reads
    // exactly like one that has crashed.
    [Fact]
    public void Geometry_entirely_off_every_screen_is_rejected()
    {
        var screens = new[] { new Rect(0, 0, 1920, 1080) };

        Assert.False(ShellState.IsOnScreen(new ShellState(3200, 200, 1180, 760, false, true), screens));
    }

    [Fact]
    public void Geometry_overlapping_a_screen_is_accepted()
    {
        var screens = new[] { new Rect(0, 0, 1920, 1080) };

        Assert.True(ShellState.IsOnScreen(new ShellState(1800, 900, 1180, 760, false, true), screens));
    }

    [Fact]
    public void Geometry_on_a_second_monitor_is_accepted()
    {
        var screens = new[] { new Rect(0, 0, 1920, 1080), new Rect(1920, 0, 2560, 1440) };

        Assert.True(ShellState.IsOnScreen(new ShellState(2400, 100, 1180, 760, false, true), screens));
    }

    [Fact]
    public void State_with_no_geometry_is_not_on_screen_and_is_not_meant_to_be()
    {
        Assert.False(ShellState.IsOnScreen(ShellState.Default, [new Rect(0, 0, 1920, 1080)]));
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/WaveLinkBackup.App.Tests --filter FullyQualifiedName~ShellStateTests`
Expected: FAIL — neither type exists.

- [ ] **Step 3: Write `ShellState`**

Create `src/WaveLinkBackup.App/Hosting/ShellState.cs`:

```csharp
using System.Windows;

namespace WaveLinkBackup.App.Hosting;

/// <summary>
/// What the SHELL remembers, as opposed to what the app is configured to do.
///
/// Separate from BackupSettings on purpose. settings.json describes itself in the Settings
/// dialog as "the folder, the automatic-backup switch, how many to keep and which Wave Link
/// you picked" (screens/08-settings-persistence.md) - a window rectangle in there would make
/// that sentence false. And Core has no window to hide and no tray to hide it in (ADR-004).
/// </summary>
/// <param name="ClosingHidesToTray">
/// On by default. Off routes a window close through the full shutdown path, INCLUDING the
/// shutdown capture - coherent rather than dangerous, because the user turned it off in
/// Settings, where the description says automatic backups only happen while the app runs.
/// </param>
public sealed record ShellState(
    double? Left,
    double? Top,
    double? Width,
    double? Height,
    bool IsMaximized,
    bool ClosingHidesToTray)
{
    public static ShellState Default { get; } = new(
        Left: null, Top: null, Width: null, Height: null,
        IsMaximized: false, ClosingHidesToTray: true);

    /// <summary>
    /// Whether a remembered rectangle still overlaps a screen that exists.
    ///
    /// Overlap, not containment: half off the edge is somewhere the user deliberately put it,
    /// while entirely off every screen is a monitor that has been unplugged since.
    /// </summary>
    public static bool IsOnScreen(ShellState state, IReadOnlyList<Rect> screens)
    {
        if (state.Left is not { } left || state.Top is not { } top
            || state.Width is not { } width || state.Height is not { } height)
        {
            return false;
        }

        var window = new Rect(left, top, width, height);

        return screens.Any(screen => screen.IntersectsWith(window));
    }
}
```

- [ ] **Step 4: Write `ShellStateRepository`**

Create `src/WaveLinkBackup.App/Hosting/ShellStateRepository.cs`:

```csharp
using System.Buffers;
using System.Text.Json;
using WaveLinkBackup.Core.Abstractions;

namespace WaveLinkBackup.App.Hosting;

/// <summary>
/// shell.json, beside settings.json in %LOCALAPPDATA%\WaveLinkBackup.
///
/// Hand-written with Utf8JsonWriter and JsonDocument, matching SettingsSerializer and
/// ManifestSerializer. SourceGuardTests only polices Core, but a second serialization style in
/// the same product for the same job is its own kind of debt.
///
/// Read is TOLERANT per field and Save NEVER THROWS. Losing a window position is not worth an
/// exception on a shutdown path, and it is not worth refusing to start either.
/// </summary>
public sealed class ShellStateRepository(IFileSystem fileSystem, string directoryPath)
{
    public const string FileName = "shell.json";

    public const int CurrentSchemaVersion = 1;

    public string FilePath { get; } = Path.Combine(directoryPath, FileName);

    public ShellState Read()
    {
        if (!fileSystem.FileExists(FilePath)) return ShellState.Default;

        byte[] bytes;
        try { bytes = fileSystem.ReadSharedBytes(FilePath); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return ShellState.Default; }

        JsonDocument document;
        try { document = JsonDocument.Parse(bytes); }
        catch (Exception ex) when (ex is JsonException or ArgumentException) { return ShellState.Default; }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return ShellState.Default;

            return new ShellState(
                Left: Number(root, "left"),
                Top: Number(root, "top"),
                Width: Number(root, "width"),
                Height: Number(root, "height"),
                IsMaximized: Bool(root, "isMaximized") ?? ShellState.Default.IsMaximized,
                ClosingHidesToTray: Bool(root, "closingHidesToTray") ?? ShellState.Default.ClosingHidesToTray);
        }
    }

    public void Save(ShellState state)
    {
        var buffer = new ArrayBufferWriter<byte>();

        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            writer.WriteNumber("schemaVersion", CurrentSchemaVersion);

            WriteNumber(writer, "left", state.Left);
            WriteNumber(writer, "top", state.Top);
            WriteNumber(writer, "width", state.Width);
            WriteNumber(writer, "height", state.Height);

            writer.WriteBoolean("isMaximized", state.IsMaximized);
            writer.WriteBoolean("closingHidesToTray", state.ClosingHidesToTray);

            writer.WriteEndObject();
        }

        // Not atomic, deliberately. SettingsRepository writes through a temp file because
        // losing settings.json costs the user their configuration; losing a window position
        // costs one restore to 1180x760, which is where it starts anyway.
        try
        {
            fileSystem.CreateDirectory(directoryPath);
            fileSystem.WriteBytes(FilePath, buffer.WrittenSpan.ToArray());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best effort. This runs on the shutdown path, where throwing would turn a lost
            // window position into a failure to exit.
        }
    }

    private static void WriteNumber(Utf8JsonWriter writer, string name, double? value)
    {
        if (value is { } number && !double.IsNaN(number) && !double.IsInfinity(number))
        {
            writer.WriteNumber(name, number);
        }
        else
        {
            writer.WriteNull(name);
        }
    }

    private static double? Number(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.Number
        && value.TryGetDouble(out var number)
            ? number
            : null;

    private static bool? Bool(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : null;
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test tests/WaveLinkBackup.App.Tests --filter FullyQualifiedName~ShellStateTests`
Expected: PASS, 12 tests.

- [ ] **Step 6: Commit**

```bash
git add src/WaveLinkBackup.App/Hosting/ShellState.cs \
        src/WaveLinkBackup.App/Hosting/ShellStateRepository.cs \
        tests/WaveLinkBackup.App.Tests/ShellStateTests.cs
git commit -m "feat: remember the window, in a file the shell owns

Geometry and closing-hides-to-tray are not settings.json's business - that file
describes itself on screen as four things, and Core has no window to hide.
Off-screen geometry is rejected on read: a window restored onto an unplugged
monitor looks exactly like a crash."
```

---

### Task 4: The window — 34px caption bar, Mica, geometry, hide on close

The first visible milestone: a real window instead of plan 2's stub. What goes inside it is
still empty; the chrome is what this task delivers.

**Files:**
- Create: `src/WaveLinkBackup.App/Views/MainWindow.xaml` · `MainWindow.xaml.cs` (moved from the project root)
- Create: `src/WaveLinkBackup.App/Views/ControlStyles.xaml`
- Modify: `src/WaveLinkBackup.App/Theming/Dark.xaml` · `Light.xaml` · `HighContrast.xaml`
- Modify: `src/WaveLinkBackup.App/App.xaml`, `App.xaml.cs`, `WaveLinkBackup.App.csproj`
- Modify: `tests/WaveLinkBackup.App.Tests/Fakes/FakeWindowChrome.cs`
- Create: `tests/WaveLinkBackup.App.Tests/WindowChromeTests.cs`

**Interfaces:**
- Consumes: `ChromeChoice.ForMainWindow`, `IWindowChrome.Apply`, `ISystemTheme`, `ShellState`, `ShellStateRepository`
- Produces:
  - `Views.MainWindow(IWindowChrome chrome, ISystemTheme systemTheme, ShellState state)`
  - `internal ShellState Views.MainWindow.CurrentGeometry(bool closingHidesToTray)`
  - `internal ShellState App.ShellState { get; }` · `internal void App.SaveGeometry(Views.MainWindow window)`
  - `bool FakeWindowChrome.BackdropSucceeds { get; set; }` (default `true`)

**Four traps, in the order they bite:**

1. **`AllowsTransparency` must stay `False`.** It makes the window layered, and a layered window
   silently ignores every DWM backdrop attribute — plan 3's finding A, which already cost an
   afternoon on the tray menu. The custom caption comes from `System.Windows.Shell.WindowChrome`,
   which needs no transparency.
2. **Mica only shows where the window is not painted.** `DWMSBT_MAINWINDOW` renders behind the
   client area, so `Window.Background` must be `Transparent` and the *content* paints `WlBg`
   everywhere except the 34px caption row. Painting the window `WlBg` gives a perfectly
   successful DWM call and no visible difference — the same failure signature as finding A.
3. **When the backdrop does not take, the bar must be painted.** `IWindowChrome.Apply` returns
   exactly that, so the caller can fall back to a solid `WlChrome` — which is what the role is
   defined as: *"Windows 11 Mica caption/strip tint"*.
4. **`WindowChrome` eats clicks in the caption strip.** The three caption buttons need
   `WindowChrome.IsHitTestVisibleInChrome="True"` or they cannot be pressed at all.

- [ ] **Step 1: Write the failing tests**

Create `tests/WaveLinkBackup.App.Tests/WindowChromeTests.cs`:

```csharp
using WaveLinkBackup.App.Windows;

namespace WaveLinkBackup.App.Tests;

/// <summary>
/// The window's half of the contract ChromeChoice holds. The interop is not testable - design
/// section E lists Mica under "not tested" - but the DECISION is, and it is the part a
/// refactor loses quietly.
/// </summary>
public sealed class WindowChromeTests
{
    [Fact]
    public void The_main_window_asks_for_mica_and_the_default_corners()
    {
        var (backdrop, corners) = ChromeChoice.ForMainWindow(highContrast: false);

        Assert.Equal(Backdrop.Mica, backdrop);
        Assert.Equal(Corners.Default, corners);
    }

    // 11-high-contrast: every tint and fill is removed, and surfaces are told apart by 1px
    // WindowText borders. A translucent backdrop is a tint.
    [Fact]
    public void High_contrast_takes_no_backdrop()
    {
        var (backdrop, _) = ChromeChoice.ForMainWindow(highContrast: true);

        Assert.Equal(Backdrop.None, backdrop);
    }

    // The window and the tray menu take DIFFERENT chrome from the same seam. Plan 3's finding A
    // is only half right now, and this is the half that is still right.
    [Fact]
    public void The_window_and_the_tray_menu_do_not_take_the_same_chrome()
    {
        Assert.NotEqual(ChromeChoice.ForTrayMenu(false), ChromeChoice.ForMainWindow(false));
    }

    // The caption bar paints WlChrome only when the backdrop did not land, so the fake has to be
    // able to say it did not.
    [Fact]
    public void A_failed_backdrop_is_reported_rather_than_thrown()
    {
        var chrome = new FakeWindowChrome { BackdropSucceeds = false };

        Assert.False(chrome.Apply(IntPtr.Zero, Backdrop.Mica, Corners.Default, dark: true));
    }

    [Fact]
    public void A_successful_backdrop_is_reported_too()
    {
        Assert.True(new FakeWindowChrome().Apply(IntPtr.Zero, Backdrop.Mica, Corners.Default, dark: true));
    }
}
```

- [ ] **Step 2: Run the tests to see which fail**

Run: `dotnet test tests/WaveLinkBackup.App.Tests --filter FullyQualifiedName~WindowChromeTests`
Expected: the first three PASS immediately — `ChromeChoice` already holds this contract, and
asserting it from the window's side is the point. The last two FAIL until
`FakeWindowChrome.BackdropSucceeds` exists.

- [ ] **Step 3: Give the fake a failing backdrop**

In `tests/WaveLinkBackup.App.Tests/Fakes/FakeWindowChrome.cs`, add a settable
`public bool BackdropSucceeds { get; set; } = true;` and return it from `Apply` after the call
is recorded. Keep the existing recording — `ChromeChoiceTests` asserts on it.

- [ ] **Step 4: Move the window into `Views/` and write its chrome**

```bash
git mv src/WaveLinkBackup.App/MainWindow.xaml src/WaveLinkBackup.App/Views/MainWindow.xaml
git mv src/WaveLinkBackup.App/MainWindow.xaml.cs src/WaveLinkBackup.App/Views/MainWindow.xaml.cs
```

Change the namespace to `WaveLinkBackup.App.Views`, set
`x:Class="WaveLinkBackup.App.Views.MainWindow"`, and fix the two references in `App.xaml.cs`
(`ShowMainWindow` and the `MainWindow` property). Design §A's layout puts the window in
`Views/` beside `TrayIcon`.

Replace the contents of `src/WaveLinkBackup.App/Views/MainWindow.xaml`. Measurements are
README §Window geometry and §Screen 1 item 1, verbatim:

```xml
<Window x:Class="WaveLinkBackup.App.Views.MainWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:shell="clr-namespace:System.Windows.Shell;assembly=PresentationFramework"
        Title="Wave Link Backup"
        Height="760" Width="1180" MinHeight="560" MinWidth="980"
        WindowStartupLocation="CenterScreen"
        UseLayoutRounding="True" SnapsToDevicePixels="True"
        Background="Transparent">

    <!--
      Background="Transparent", and AllowsTransparency stays FALSE.

      Mica renders BEHIND the client area, so anything the window paints hides it. And
      AllowsTransparency="True" would make the window layered, which makes DWM ignore the
      backdrop silently - the call succeeds and nothing looks different. Plan 3, finding A.

      WindowChrome gives the custom caption without needing either.
    -->
    <shell:WindowChrome.WindowChrome>
        <shell:WindowChrome CaptionHeight="34"
                            ResizeBorderThickness="6"
                            CornerRadius="0"
                            GlassFrameThickness="0"
                            UseAeroCaptionButtons="False" />
    </shell:WindowChrome.WindowChrome>

    <Grid>
        <Grid.RowDefinitions>
            <RowDefinition Height="34" />   <!-- caption: left unpainted, so Mica shows -->
            <RowDefinition Height="Auto" /> <!-- status strip   (Task 9) -->
            <RowDefinition Height="Auto" /> <!-- column header  (Task 9) -->
            <RowDefinition Height="*" />    <!-- the list       (Task 9) -->
            <RowDefinition Height="Auto" /> <!-- bottom bar     (Task 9) -->
        </Grid.RowDefinitions>

        <!-- Solid WlBg behind everything BELOW the bar. README: "Mica/SystemBackdrop on the
             bar, solid --wl-bg behind the list." -->
        <Border Grid.Row="1" Grid.RowSpan="4" Background="{DynamicResource WlBg}" />

        <Border x:Name="CaptionBar" Grid.Row="0"
                BorderBrush="{DynamicResource WlLine}" BorderThickness="0,0,0,1">
            <Grid>
                <StackPanel Orientation="Horizontal" Margin="12,0,0,0" VerticalAlignment="Center">
                    <Path Data="{StaticResource WlShieldCheckGeometry}"
                          Fill="{DynamicResource WlMuted}"
                          Width="14" Height="14" Stretch="Uniform" />
                    <TextBlock Text="Wave Link Backup" Margin="9,0,0,0" VerticalAlignment="Center"
                               FontFamily="{DynamicResource WlDisplayFont}" FontSize="12"
                               Foreground="{DynamicResource WlText}" />
                </StackPanel>

                <StackPanel Orientation="Horizontal" HorizontalAlignment="Right"
                            shell:WindowChrome.IsHitTestVisibleInChrome="True">
                    <Button x:Name="MinimiseButton" Style="{StaticResource WlCaptionButton}"
                            AutomationProperties.Name="Minimise" Content="&#xE921;" />
                    <Button x:Name="MaximiseButton" Style="{StaticResource WlCaptionButton}"
                            AutomationProperties.Name="Maximise" Content="&#xE922;" />
                    <Button x:Name="CloseButton" Style="{StaticResource WlCaptionCloseButton}"
                            AutomationProperties.Name="Close" Content="&#xE8BB;" />
                </StackPanel>
            </Grid>
        </Border>
    </Grid>
</Window>
```

> The three caption glyphs are Segoe Fluent Icons code points, which is what Windows itself
> draws them with. This is the one place the app does **not** use its own families: a caption
> button that does not match every other window on the machine is worse than one that does not
> match the app.

Create `src/WaveLinkBackup.App/Views/ControlStyles.xaml` holding:

- `WlShieldCheckGeometry` — the Lucide-idiom shield-check on the 24px grid. `TrayIconRenderer`
  already draws this glyph; copy its path data rather than drawing a second one (technical-debt
  §4.7 names that file as the substitution point for a real icon set).
- `WlCaptionButton` — 46 × 34, transparent, `WlHover` fill on hover, `WlText` glyph, font
  `Segoe Fluent Icons` at 10px, no border, no focus visual of its own beyond the shared ring.
- `WlCaptionCloseButton` — `BasedOn` the above, `WlDanger` fill and `WlAccentInk` glyph on hover.
  In high contrast that trigger is `Highlight` / `HighlightText`, because nothing in high
  contrast is red (`11`).

Every colour a `DynamicResource`; the guard test fails the build on a literal.

- [ ] **Step 5: Wire the chrome, geometry and close behaviour**

Replace `src/WaveLinkBackup.App/Views/MainWindow.xaml.cs`:

```csharp
using System.Windows;
using System.Windows.Interop;
using WaveLinkBackup.App.Hosting;
using WaveLinkBackup.App.Theming;
using WaveLinkBackup.App.Windows;

namespace WaveLinkBackup.App.Views;

public partial class MainWindow : Window
{
    private readonly IWindowChrome chrome;
    private readonly ISystemTheme systemTheme;

    public MainWindow(IWindowChrome chrome, ISystemTheme systemTheme, ShellState state)
    {
        this.chrome = chrome;
        this.systemTheme = systemTheme;

        InitializeComponent();

        Restore(state);

        MinimiseButton.Click += (_, _) => WindowState = WindowState.Minimized;
        MaximiseButton.Click += (_, _) => WindowState =
            WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        CloseButton.Click += (_, _) => Close();

        // The HWND does not exist before SourceInitialized, and DwmSetWindowAttribute needs
        // one. Re-applied on every theme change because the dark-frame attribute is a colour
        // decision and high contrast withdraws the backdrop entirely.
        SourceInitialized += (_, _) => ApplyChrome();
        systemTheme.Changed += OnSystemThemeChanged;
    }

    private void OnSystemThemeChanged(object? sender, EventArgs e) => Dispatcher.Invoke(ApplyChrome);

    private void ApplyChrome()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero) return;

        var (backdrop, corners) = ChromeChoice.ForMainWindow(systemTheme.IsHighContrast);

        var tookBackdrop = chrome.Apply(
            handle, backdrop, corners, dark: systemTheme.Theme != AppTheme.Light);

        // WlChrome IS the Mica tint role - it only means anything with Mica behind it. Where the
        // backdrop did not land (Windows 10, high contrast, a remote session) the bar paints it
        // rather than showing whatever is behind the window. Apply returns this value for
        // exactly this caller; plan 3 Task 3 Step 1.
        CaptionBar.SetResourceReference(BackgroundProperty, tookBackdrop ? "WlTransparent" : "WlChrome");
    }

    private void Restore(ShellState state)
    {
        if (!ShellState.IsOnScreen(state, SystemScreens())) return;

        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = state.Left!.Value;
        Top = state.Top!.Value;
        Width = state.Width!.Value;
        Height = state.Height!.Value;

        if (state.IsMaximized) WindowState = WindowState.Maximized;
    }

    /// <summary>
    /// Every screen's working area. SystemParameters knows about the primary monitor only, and
    /// a window remembered on the second one is the case this whole check exists for.
    /// </summary>
    private static IReadOnlyList<Rect> SystemScreens() =>
    [
        .. System.Windows.Forms.Screen.AllScreens.Select(s => new Rect(
            s.WorkingArea.X, s.WorkingArea.Y, s.WorkingArea.Width, s.WorkingArea.Height)),
    ];

    /// <summary>
    /// The RESTORE bounds, never the maximised ones - a window remembered as 3840 wide because
    /// it happened to be maximised opens absurd on the next machine.
    /// </summary>
    internal ShellState CurrentGeometry(bool closingHidesToTray) => new(
        Left: RestoreBounds.Left,
        Top: RestoreBounds.Top,
        Width: RestoreBounds.Width,
        Height: RestoreBounds.Height,
        IsMaximized: WindowState == WindowState.Maximized,
        ClosingHidesToTray: closingHidesToTray);

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        base.OnClosing(e);

        var app = (App)Application.Current;

        // Before the branch: geometry must survive a HIDE as well as an exit, and a hidden
        // window is the normal case for this app.
        app.SaveGeometry(this);

        if (app.IsShuttingDown || !app.ShellState.ClosingHidesToTray) return;

        e.Cancel = true;
        Hide();
    }

    protected override void OnClosed(EventArgs e)
    {
        systemTheme.Changed -= OnSystemThemeChanged;
        base.OnClosed(e);
    }
}
```

`System.Windows.Forms.Screen` needs `<UseWindowsForms>true</UseWindowsForms>` beside `<UseWPF>`
in the App csproj. If that raises warnings under `TreatWarningsAsErrors`, replace
`SystemScreens` with an `EnumDisplayMonitors` P/Invoke rather than suppressing them —
`DllImport`, not `LibraryImport` (technical-debt §7.1).

Add `WlTransparent` to all three theme dictionaries as a transparent `SolidColorBrush`, and to
`ThemeManager.BrushKeys` so `ThemeTests`' every-theme-declares-every-key assertion covers it. A
brush key rather than a literal because `Background="Transparent"` set in code would need a
second, different mechanism to undo a `DynamicResource`.

- [ ] **Step 6: Teach `App` to own the shell state**

In `src/WaveLinkBackup.App/App.xaml.cs`:

- add fields: `private ShellStateRepository? shellStateRepository;` and
  `internal ShellState ShellState { get; private set; } = ShellState.Default;`
- in `OnStartup`, immediately after `settingsRepository` is constructed:
  ```csharp
  shellStateRepository = new ShellStateRepository(fileSystem, SettingsRepository.DefaultDirectory);
  ShellState = shellStateRepository.Read();
  ```
- change `ShowMainWindow`'s first line to
  `MainWindow ??= new Views.MainWindow(chrome!, systemTheme!, ShellState);`
- add:
  ```csharp
  /// <summary>Called from the window's Closing, so geometry survives a hide as well as an exit.</summary>
  internal void SaveGeometry(Views.MainWindow window) =>
      shellStateRepository?.Save(window.CurrentGeometry(ShellState.ClosingHidesToTray));
  ```
- in `ShutdownEverything`, before `Shutdown(0)`:
  `if (MainWindow is Views.MainWindow main) SaveGeometry(main);`

- [ ] **Step 7: Run the tests and a Release build**

Run: `dotnet test WaveLinkBackup.slnx`
Then: `dotnet build WaveLinkBackup.slnx -c Release`
Expected: PASS, and Release with zero warnings.

- [ ] **Step 8: Look at it**

None of this is unit-testable. Run the app and confirm:

- [ ] The caption bar is 34px with the shield glyph and title left, three buttons right
- [ ] The bar takes Mica — the desktop tints through it — and the area below it does not
- [ ] Dragging the bar moves the window; double-clicking it maximises; all three buttons work
- [ ] Closing hides to the tray, and the tray icon is still there
- [ ] Reopening from the tray restores the window at the size and position it was closed at
- [ ] Switching Windows to light mode recolours the frame without a restart

- [ ] **Step 9: Commit**

```bash
git add -A src/WaveLinkBackup.App tests/WaveLinkBackup.App.Tests
git commit -m "feat: the 34px caption bar, on Mica, remembering where it was

The window paints nothing behind the caption row so the backdrop can show, and
AllowsTransparency stays false because a layered window makes DWM ignore the
backdrop silently. Where the backdrop does not land the bar paints WlChrome,
which is the role that was defined for exactly that."
```

---

### Task 5: Health — the background verification probe

`SnapshotStore.List()` reads manifests and deliberately does not hash: *"that would rehash the
whole store on every window open."* But screen 1 renders DAMAGED rows, so something must.

**The decision taken:** verify everything, off the UI thread, on open and on F5. Rows appear
immediately as WHOLE or SUSPECT and flip to DAMAGED as answers arrive. Tier 1 is one small
`settings.json` per snapshot, so thirty backups is milliseconds today; the cost arrives in
phase 6 with presets and plugins, and it arrives on a background thread where it can be seen
rather than in a window that will not open.

**Files:**
- Create: `src/WaveLinkBackup.App/Hosting/HealthProbe.cs`
- Create: `tests/WaveLinkBackup.App.Tests/HealthProbeTests.cs`

**Interfaces:**
- Consumes: `SnapshotStore.Verify`, `Snapshot`, `SnapshotManifest.IsSuspect`, `IFileSystem`, `IClock`
- Produces:
  - `enum SnapshotHealth { Whole, Suspect, Damaged }`
  - `sealed record HealthVerdict(SnapshotHealth Health, long ManifestBytes, long? ActualBytes, DateTimeOffset CheckedAt)`
  - `static SnapshotHealth HealthProbe.Decide(bool verified, bool isSuspect)`
  - `sealed class HealthProbe(SnapshotStore store, IFileSystem fileSystem, IClock clock)` with
    `HealthVerdict Check(Snapshot snapshot)` and
    `Task ProbeAsync(IReadOnlyList<Snapshot> snapshots, Action<string, HealthVerdict> report, CancellationToken token)`

- [ ] **Step 1: Write the failing tests**

Create `tests/WaveLinkBackup.App.Tests/HealthProbeTests.cs`:

```csharp
using System.Text;
using WaveLinkBackup.App.Hosting;
using WaveLinkBackup.Core.Analysis;
using WaveLinkBackup.Core.Snapshots;
using WaveLinkBackup.Core.Tests.Fakes;

namespace WaveLinkBackup.App.Tests;

/// <summary>
/// Where DAMAGED comes from. The store will not hash on List() and is right not to, so the
/// shell hashes on its own thread and the rows flip when it answers.
/// </summary>
public sealed class HealthProbeTests
{
    private const string StorePath = @"C:\store";

    private static readonly byte[] Settings = Encoding.UTF8.GetBytes("""
        {"MixerConfiguration":{"InputSettings":{
          "A":{"InputName":"Wave Mic 1","AudioPluginConfigurations":[]},
          "B":{"InputName":"Voice","AudioPluginConfigurations":[]}}}}
        """);

    private static (HealthProbe Probe, FakeFileSystem Fs, Snapshot Snapshot) Rig(bool suspect = false)
    {
        var fs = new FakeFileSystem();
        var clock = new FakeClock();
        var store = new SnapshotStore(fs, clock, StorePath);

        var analysis = SettingsAnalysis.Analyse(Settings).Value;

        if (suspect)
        {
            analysis = analysis with
            {
                Report = analysis.Report with { HasCaseInsensitiveDuplicateKeys = true },
            };
        }

        var written = store.Write(Settings, analysis, SnapshotTrigger.Manual, "Before 3.3 beta");

        return (new HealthProbe(store, fs, clock), fs, written.Value);
    }

    [Fact]
    public void A_snapshot_that_verifies_and_is_not_suspect_is_whole()
    {
        var (probe, _, snapshot) = Rig();

        Assert.Equal(SnapshotHealth.Whole, probe.Check(snapshot).Health);
    }

    [Fact]
    public void A_snapshot_that_verifies_but_failed_validation_is_suspect()
    {
        var (probe, _, snapshot) = Rig(suspect: true);

        Assert.Equal(SnapshotHealth.Suspect, probe.Check(snapshot).Health);
    }

    [Fact]
    public void A_snapshot_whose_bytes_changed_is_damaged()
    {
        var (probe, fs, snapshot) = Rig();

        fs.WriteBytes(snapshot.SettingsPath, "tampered"u8.ToArray());

        Assert.Equal(SnapshotHealth.Damaged, probe.Check(snapshot).Health);
    }

    // "Contents are unknowable" beats "contents are not whole". A row cannot draw both, and the
    // louder claim is the one that is still true.
    [Fact]
    public void Damaged_outranks_suspect()
    {
        var (probe, fs, snapshot) = Rig(suspect: true);

        fs.WriteBytes(snapshot.SettingsPath, "tampered"u8.ToArray());

        Assert.Equal(SnapshotHealth.Damaged, probe.Check(snapshot).Health);
    }

    [Theory]
    [InlineData(true, false, SnapshotHealth.Whole)]
    [InlineData(true, true, SnapshotHealth.Suspect)]
    [InlineData(false, false, SnapshotHealth.Damaged)]
    [InlineData(false, true, SnapshotHealth.Damaged)]
    public void The_decision_is_a_table(bool verified, bool isSuspect, SnapshotHealth expected)
    {
        Assert.Equal(expected, HealthProbe.Decide(verified, isSuspect));
    }

    // 02's selected-damaged detail line is "MANIFEST SAYS 470 KB · FILE IS 12 KB · CHECKED
    // 23:09". Without both figures the row can only say something went wrong, which is what the
    // design deliberately refuses to settle for.
    [Fact]
    public void A_damaged_verdict_carries_both_sizes_and_the_time_it_was_checked()
    {
        var (probe, fs, snapshot) = Rig();

        fs.WriteBytes(snapshot.SettingsPath, "short"u8.ToArray());

        var verdict = probe.Check(snapshot);

        Assert.Equal(Settings.LongLength, verdict.ManifestBytes);
        Assert.Equal(5, verdict.ActualBytes);
        Assert.NotEqual(default, verdict.CheckedAt);
    }

    [Fact]
    public void A_missing_file_reports_a_null_actual_size_rather_than_zero()
    {
        var (probe, fs, snapshot) = Rig();

        fs.Delete(snapshot.SettingsPath);

        var verdict = probe.Check(snapshot);

        Assert.Equal(SnapshotHealth.Damaged, verdict.Health);
        Assert.Null(verdict.ActualBytes);
    }

    [Fact]
    public async Task Probing_reports_every_snapshot_by_id()
    {
        var (probe, _, snapshot) = Rig();

        var reported = new Dictionary<string, HealthVerdict>(StringComparer.Ordinal);

        await probe.ProbeAsync([snapshot], (id, verdict) => reported[id] = verdict, CancellationToken.None);

        Assert.Equal(SnapshotHealth.Whole, reported[snapshot.Id].Health);
    }

    // F5 while a probe is still running must not have the old run writing verdicts into the new
    // list. Cancellation is what makes a refresh cheap instead of racy.
    [Fact]
    public async Task A_cancelled_probe_reports_nothing()
    {
        var (probe, _, snapshot) = Rig();

        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        var reported = 0;

        try
        {
            await probe.ProbeAsync([snapshot], (_, _) => reported++, cancellation.Token);
        }
        catch (OperationCanceledException)
        {
            // Task.Run with an already-cancelled token faults rather than running. Either way
            // the assertion below is the point.
        }

        Assert.Equal(0, reported);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/WaveLinkBackup.App.Tests --filter FullyQualifiedName~HealthProbeTests`
Expected: FAIL — `HealthProbe` does not exist.

> `SettingsAnalysis.Analyse` and `SettingsAnalysisResult`'s shape are what the rig assumes. Open
> `src/WaveLinkBackup.Core/Analysis/SettingsAnalysis.cs` and correct the helper to the real
> types if they differ. **Do not change Core to fit the test** — nothing in this plan touches Core.

- [ ] **Step 3: Write `HealthProbe`**

Create `src/WaveLinkBackup.App/Hosting/HealthProbe.cs`:

```csharp
using WaveLinkBackup.Core.Abstractions;
using WaveLinkBackup.Core.Snapshots;

namespace WaveLinkBackup.App.Hosting;

/// <summary>The three row states screen 1 draws.</summary>
public enum SnapshotHealth
{
    /// <summary>Verified, and validation passed when it was taken.</summary>
    Whole,

    /// <summary>
    /// Validation failed WHEN THE BACKUP WAS TAKEN. Contents are readable, it is still
    /// restorable, and it may be the only copy that exists. A warning - amber.
    /// </summary>
    Suspect,

    /// <summary>
    /// The recorded checksums no longer match: corrupted AFTER writing. Contents are
    /// unknowable, so it cannot be restored. A refusal - and deliberately NOT amber, because
    /// amber is a claim about contents and a damaged backup has none. It loses its colour
    /// rather than gaining one (02).
    /// </summary>
    Damaged,
}

/// <param name="ManifestBytes">What the manifest says this snapshot's files weigh.</param>
/// <param name="ActualBytes">What the settings file weighs now, or null when it could not be read.</param>
public sealed record HealthVerdict(
    SnapshotHealth Health,
    long ManifestBytes,
    long? ActualBytes,
    DateTimeOffset CheckedAt);

/// <summary>
/// Hashes the store so the list can say DAMAGED.
///
/// SnapshotStore.List() reads manifests only and is right not to hash - its own comment says
/// verification is a restore-time concern, and hashing there would rehash every backup on every
/// window open. So the shell verifies on its OWN thread, on open and on F5, and rows flip from
/// WHOLE or SUSPECT to DAMAGED as answers come back.
///
/// Tier 1 is one small settings.json per snapshot, so this is milliseconds today. The cost
/// arrives in phase 6 with presets and plugins - on a background thread, where it can be
/// reported, rather than in a window that will not open.
/// </summary>
public sealed class HealthProbe(SnapshotStore store, IFileSystem fileSystem, IClock clock)
{
    /// <summary>
    /// Pure, and a table test. Damaged OUTRANKS suspect: "contents are unknowable" and
    /// "contents are not whole" cannot both be drawn, and the first is the one still true.
    /// </summary>
    public static SnapshotHealth Decide(bool verified, bool isSuspect) =>
        !verified ? SnapshotHealth.Damaged
        : isSuspect ? SnapshotHealth.Suspect
        : SnapshotHealth.Whole;

    public HealthVerdict Check(Snapshot snapshot)
    {
        var verified = store.Verify(snapshot).IsSuccess;
        var manifestBytes = snapshot.Manifest.Files.Values.Sum(f => f.SizeBytes);

        // Measured only when it matters: 02's damaged detail line needs both figures, and a
        // whole row needs neither.
        var actualBytes = verified ? manifestBytes : ActualSizeOf(snapshot);

        return new HealthVerdict(
            Decide(verified, snapshot.Manifest.IsSuspect), manifestBytes, actualBytes, clock.UtcNow);
    }

    /// <summary>
    /// Null, not zero, when the file has gone. "The file is 0 KB" and "there is no file" are
    /// different sentences and only one of them is true.
    /// </summary>
    private long? ActualSizeOf(Snapshot snapshot)
    {
        var path = snapshot.SettingsPath;

        if (!fileSystem.FileExists(path)) return null;

        try { return fileSystem.ReadSharedBytes(path).LongLength; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }

    /// <summary>
    /// Verifies every snapshot off the UI thread, reporting each as it lands rather than in one
    /// batch at the end - a store whose third backup is damaged should say so without waiting
    /// for the thirtieth.
    ///
    /// <paramref name="report"/> is invoked on the PROBE's thread. The caller marshals.
    /// </summary>
    public Task ProbeAsync(
        IReadOnlyList<Snapshot> snapshots,
        Action<string, HealthVerdict> report,
        CancellationToken token) => Task.Run(() =>
    {
        foreach (var snapshot in snapshots)
        {
            // Checked before the work AND before the report: an F5 mid-probe must not leave the
            // previous run writing verdicts into the list that replaced it.
            if (token.IsCancellationRequested) return;

            var verdict = Check(snapshot);

            if (token.IsCancellationRequested) return;

            report(snapshot.Id, verdict);
        }
    }, token);
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/WaveLinkBackup.App.Tests --filter FullyQualifiedName~HealthProbeTests`
Expected: PASS, 12 tests.

- [ ] **Step 5: Commit**

```bash
git add src/WaveLinkBackup.App/Hosting/HealthProbe.cs \
        tests/WaveLinkBackup.App.Tests/HealthProbeTests.cs
git commit -m "feat: hash the store off the UI thread, so a row can say DAMAGED

List() will not hash and is right not to. The shell does it on its own thread
and rows flip as answers arrive, which keeps the window opening instantly and
keeps the cost visible once tiers 2-4 make snapshots large.

Damaged outranks suspect: contents that are unknowable beat contents that are
merely not whole."
```

---

### Task 6: The pure rules — readable text, and the five slots

Everything screen 1 says about a backup in mono comes out of one of these two files. Both are
static and pure, so the design's own sample data can be asserted verbatim.

**The generic-slot rule, and why it is not a name heuristic.** README lists the healthy inputs
as `Wave Mic 1, Voice, Browser, Game, System` and the collapsed row's as `Elgato Wave:3, System`
— **`System` appears in both**, green in one row and amber in the other. So genericness is a
property of the ROW, not of the string, and no name-matching rule can ever be right.

What actually distinguishes them is that the collapsed row has fewer inputs than that user's rig
normally has. That is exactly `HealthFingerprint`'s argument — *"There is deliberately no
`IsHealthy` property. Five inputs and 43 KB is ONE user's rig; an absolute threshold is a bug
waiting for the first user with three inputs"* — so the rule is **a row whose input count is
below the store's own high-water mark renders its present slots generic**. It reproduces the
design's sample data exactly and hard-codes nothing.

**Files:**
- Create: `src/WaveLinkBackup.App/ViewModels/Readable.cs`
- Create: `src/WaveLinkBackup.App/ViewModels/InputSlots.cs`
- Create: `src/WaveLinkBackup.App/ViewModels/ObservableObject.cs`
- Create: `tests/WaveLinkBackup.App.Tests/ReadableTests.cs`
- Create: `tests/WaveLinkBackup.App.Tests/InputSlotsTests.cs`

**Interfaces:**
- Produces:
  - `static string Readable.Bytes(long bytes)` · `static string Readable.RelativeTime(DateTimeOffset at, DateTimeOffset now)` · `static string Readable.DayGroup(DateTimeOffset at, DateTimeOffset now)` · `static string Readable.TimeOfDay(DateTimeOffset at)` · `static string Readable.ShortDate(DateTimeOffset at)` · `static string Readable.SlotLabel(string inputName)`
  - `enum SlotKind { Named, Generic, Missing }`
  - `readonly record struct InputSlot(string Label, SlotKind Kind)`
  - `const int InputSlots.SlotCount = 5` · `static IReadOnlyList<InputSlot> InputSlots.Build(IReadOnlyList<string> inputNames, int peakInputCount)`
  - `abstract class ObservableObject : INotifyPropertyChanged` with `protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)` and `protected void Raise([CallerMemberName] string? name = null)`

- [ ] **Step 1: Write the failing tests for `Readable`**

Create `tests/WaveLinkBackup.App.Tests/ReadableTests.cs`:

```csharp
using WaveLinkBackup.App.ViewModels;

namespace WaveLinkBackup.App.Tests;

/// <summary>
/// Every mono readout on screen 1. The expected strings are README's and 02's own sample data,
/// so a change here is a change to the design rather than to a format string.
/// </summary>
public sealed class ReadableTests
{
    // README: "12.1 MB · 4 days ago", "471 KB", "118 GB FREE", "43 KB".
    [Theory]
    [InlineData(0, "0 B")]
    [InlineData(512, "512 B")]
    [InlineData(1024, "1 KB")]
    [InlineData(44032, "43 KB")]
    [InlineData(482304, "471 KB")]
    [InlineData(12687769, "12.1 MB")]
    [InlineData(75091968, "71.6 MB")]
    [InlineData(126701535232, "118 GB")]
    public void Bytes_read_the_way_the_design_writes_them(long bytes, string expected)
    {
        Assert.Equal(expected, Readable.Bytes(bytes));
    }

    // KB never carries a decimal; MB and GB carry one until they reach three digits, where the
    // decimal is noise. 118.0 GB in a status strip is a number pretending to be a measurement.
    [Fact]
    public void Three_digit_figures_drop_the_decimal()
    {
        Assert.Equal("118 GB", Readable.Bytes(126701535232));
        Assert.Equal("99.6 MB", Readable.Bytes(104438169));
    }

    [Theory]
    [InlineData(0, "just now")]
    [InlineData(45, "just now")]
    [InlineData(60 * 5, "5 minutes ago")]
    [InlineData(60 * 60, "an hour ago")]
    [InlineData(60 * 60 * 5, "5 hours ago")]
    [InlineData(60 * 60 * 24, "yesterday")]
    [InlineData(60 * 60 * 24 * 4, "4 days ago")]
    [InlineData(60 * 60 * 24 * 30, "a month ago")]
    public void Relative_time_is_a_sentence_fragment_not_a_timestamp(int secondsAgo, string expected)
    {
        var now = new DateTimeOffset(2026, 8, 15, 23, 7, 0, TimeSpan.Zero);

        Assert.Equal(expected, Readable.RelativeTime(now.AddSeconds(-secondsAgo), now));
    }

    // README's date-group headers: TODAY, TUE 11 AUG, TUE 4 AUG.
    [Fact]
    public void Day_groups_name_today_and_yesterday_and_otherwise_use_the_weekday()
    {
        var now = new DateTimeOffset(2026, 8, 15, 23, 7, 0, TimeSpan.Zero);

        Assert.Equal("TODAY", Readable.DayGroup(now, now));
        Assert.Equal("YESTERDAY", Readable.DayGroup(now.AddDays(-1), now));
        Assert.Equal("TUE 11 AUG", Readable.DayGroup(now.AddDays(-4), now));
        Assert.Equal("TUE 4 AUG", Readable.DayGroup(now.AddDays(-11), now));
    }

    [Fact]
    public void The_taken_column_is_a_time_over_a_date()
    {
        var at = new DateTimeOffset(2026, 8, 11, 21, 36, 0, TimeSpan.Zero);

        Assert.Equal("21:36", Readable.TimeOfDay(at));
        Assert.Equal("11 AUG", Readable.ShortDate(at));
    }

    // "Label is the input name shortened to fit: MIC 1 · VOICE · BROWSER · GAME · SYSTEM" -
    // README, and "WAVE:3" for Elgato Wave:3 in the collapsed case. One leading brand word goes;
    // the rest is the user's.
    [Theory]
    [InlineData("Wave Mic 1", "MIC 1")]
    [InlineData("Voice", "VOICE")]
    [InlineData("Browser", "BROWSER")]
    [InlineData("Game", "GAME")]
    [InlineData("System", "SYSTEM")]
    [InlineData("Elgato Wave:3", "WAVE:3")]
    public void Slot_labels_are_the_design_s_own(string inputName, string expected)
    {
        Assert.Equal(expected, Readable.SlotLabel(inputName));
    }

    [Fact]
    public void A_name_that_is_only_a_brand_word_keeps_it()
    {
        Assert.Equal("WAVE", Readable.SlotLabel("Wave"));
        Assert.Equal("ELGATO", Readable.SlotLabel("Elgato"));
    }

    [Fact]
    public void Only_one_leading_brand_word_is_dropped()
    {
        Assert.Equal("WAVE MIC 1", Readable.SlotLabel("Elgato Wave Mic 1"));
    }

    [Fact]
    public void A_very_long_name_is_truncated_rather_than_overflowing_its_cell()
    {
        var label = Readable.SlotLabel("Podcast Guest Return Feed");

        Assert.True(label.Length <= 10, label);
        Assert.EndsWith("…", label, StringComparison.Ordinal);
    }

    [Fact]
    public void An_empty_name_reads_as_the_missing_dash()
    {
        Assert.Equal("—", Readable.SlotLabel(""));
        Assert.Equal("—", Readable.SlotLabel("   "));
    }
}
```

- [ ] **Step 2: Write the failing tests for `InputSlots`**

Create `tests/WaveLinkBackup.App.Tests/InputSlotsTests.cs`:

```csharp
using WaveLinkBackup.App.ViewModels;

namespace WaveLinkBackup.App.Tests;

/// <summary>
/// "Five equal flex cells, 4px apart, always in the same order and the same place, so a gap
/// breaks the pattern of the whole column before any text is read." - README.
///
/// Five always five is the information design, which is why it is asserted here rather than
/// left to a template that happens to be five wide.
/// </summary>
public sealed class InputSlotsTests
{
    private static readonly string[] Healthy =
        ["Wave Mic 1", "Voice", "Browser", "Game", "System"];

    private static readonly string[] Collapsed = ["Elgato Wave:3", "System"];

    [Fact]
    public void There_are_always_exactly_five()
    {
        Assert.Equal(5, InputSlots.Build(Healthy, 5).Count);
        Assert.Equal(5, InputSlots.Build(Collapsed, 5).Count);
        Assert.Equal(5, InputSlots.Build([], 5).Count);
        Assert.Equal(5, InputSlots.Build(Healthy, 2).Count);
    }

    [Fact]
    public void A_full_rig_reads_as_five_named_slots()
    {
        var slots = InputSlots.Build(Healthy, peakInputCount: 5);

        Assert.All(slots, s => Assert.Equal(SlotKind.Named, s.Kind));
        Assert.Equal(
            ["MIC 1", "VOICE", "BROWSER", "GAME", "SYSTEM"],
            slots.Select(s => s.Label));
    }

    // The whole reason genericness is a property of the ROW: SYSTEM is green above and amber
    // here, from the same string.
    [Fact]
    public void A_row_below_the_store_s_high_water_mark_renders_its_slots_generic()
    {
        var slots = InputSlots.Build(Collapsed, peakInputCount: 5);

        Assert.Equal(SlotKind.Generic, slots[0].Kind);
        Assert.Equal(SlotKind.Generic, slots[1].Kind);
        Assert.Equal(["WAVE:3", "SYSTEM"], slots.Take(2).Select(s => s.Label));
    }

    [Fact]
    public void The_missing_slots_come_last_and_carry_an_em_dash()
    {
        var slots = InputSlots.Build(Collapsed, peakInputCount: 5);

        Assert.All(slots.Skip(2), s =>
        {
            Assert.Equal(SlotKind.Missing, s.Kind);
            Assert.Equal("—", s.Label);
        });
    }

    // One backup in the store is its own high-water mark. It has not collapsed; there is just
    // nothing to have collapsed FROM.
    [Fact]
    public void A_row_at_the_high_water_mark_is_never_generic()
    {
        var slots = InputSlots.Build(Collapsed, peakInputCount: 2);

        Assert.All(slots.Take(2), s => Assert.Equal(SlotKind.Named, s.Kind));
    }

    [Fact]
    public void An_empty_rig_is_five_missing_slots()
    {
        var slots = InputSlots.Build([], peakInputCount: 5);

        Assert.All(slots, s => Assert.Equal(SlotKind.Missing, s.Kind));
    }

    // technical-debt section 5: "5 inputs is ONE user's rig". Six is not an error, and the
    // sixth must not push a slot out of alignment or crash the strip.
    [Fact]
    public void More_than_five_inputs_shows_the_first_five()
    {
        var slots = InputSlots.Build(
            ["Wave Mic 1", "Voice", "Browser", "Game", "System", "Return"], peakInputCount: 6);

        Assert.Equal(5, slots.Count);
        Assert.All(slots, s => Assert.Equal(SlotKind.Named, s.Kind));
        Assert.DoesNotContain("RETURN", slots.Select(s => s.Label));
    }
}
```

- [ ] **Step 3: Run both to verify they fail**

Run: `dotnet test tests/WaveLinkBackup.App.Tests --filter "FullyQualifiedName~ReadableTests|FullyQualifiedName~InputSlotsTests"`
Expected: FAIL — neither type exists.

- [ ] **Step 4: Write `Readable`**

Create `src/WaveLinkBackup.App/ViewModels/Readable.cs`:

```csharp
using System.Globalization;

namespace WaveLinkBackup.App.ViewModels;

/// <summary>
/// Every mono readout on screen 1, as pure functions.
///
/// Here rather than in a converter because these ARE the design - "12.1 MB · 4 days ago" is a
/// specified string, not a formatting preference - and a rule in a converter is a rule nobody
/// can assert.
/// </summary>
public static class Readable
{
    private static readonly string[] Units = ["B", "KB", "MB", "GB", "TB"];

    /// <summary>
    /// 471 KB · 12.1 MB · 118 GB, matching README and 02 exactly.
    ///
    /// Bytes and KB never carry a decimal, and a three-digit figure drops it too: "118.0 GB" in
    /// a status strip is a number pretending to be a measurement.
    /// </summary>
    public static string Bytes(long bytes)
    {
        if (bytes < 0) bytes = 0;

        var unit = 0;
        double value = bytes;

        while (value >= 1024 && unit < Units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        var decimals = unit >= 2 && value < 100 ? 1 : 0;

        return string.Create(
            CultureInfo.CurrentCulture, $"{Math.Round(value, decimals)} {Units[unit]}");
    }

    /// <summary>
    /// The row's meta line: "4 days ago". A fragment, lowercase, because it sits after a size in
    /// a sentence-shaped readout rather than standing alone as a label.
    /// </summary>
    public static string RelativeTime(DateTimeOffset at, DateTimeOffset now)
    {
        var elapsed = now - at;

        if (elapsed < TimeSpan.FromMinutes(1)) return "just now";

        if (elapsed < TimeSpan.FromHours(1))
        {
            var minutes = (int)elapsed.TotalMinutes;
            return minutes == 1 ? "a minute ago" : $"{minutes} minutes ago";
        }

        if (elapsed < TimeSpan.FromDays(1))
        {
            var hours = (int)elapsed.TotalHours;
            return hours == 1 ? "an hour ago" : $"{hours} hours ago";
        }

        var days = (int)elapsed.TotalDays;

        if (days == 1) return "yesterday";
        if (days < 30) return $"{days} days ago";

        var months = days / 30;

        if (months == 1) return "a month ago";
        if (months < 12) return $"{months} months ago";

        var years = days / 365;

        return years <= 1 ? "a year ago" : $"{years} years ago";
    }

    /// <summary>
    /// The date-group header: TODAY, YESTERDAY, TUE 11 AUG.
    ///
    /// README shows TODAY and the weekday form; YESTERDAY is this app's own, and it matches the
    /// tray readout's qualifier so the two never disagree about the same backup.
    /// </summary>
    public static string DayGroup(DateTimeOffset at, DateTimeOffset now)
    {
        var day = at.Date;
        var today = now.Date;

        if (day == today) return "TODAY";
        if (day == today.AddDays(-1)) return "YESTERDAY";

        return Upper(at.ToString("ddd d MMM", CultureInfo.CurrentCulture));
    }

    /// <summary>The TAKEN column's upper line.</summary>
    public static string TimeOfDay(DateTimeOffset at) => at.ToString("HH:mm", CultureInfo.CurrentCulture);

    /// <summary>The TAKEN column's lower line, and the bottom bar's selected readout.</summary>
    public static string ShortDate(DateTimeOffset at) =>
        Upper(at.ToString("d MMM", CultureInfo.CurrentCulture));

    /// <summary>
    /// An input name shortened to fit a 57px slot: MIC 1 · VOICE · BROWSER · GAME · SYSTEM, and
    /// WAVE:3 for "Elgato Wave:3".
    ///
    /// ONE leading brand word is dropped, never two - "Elgato Wave Mic 1" is still a Wave device
    /// and losing that would make two different inputs read identically.
    /// </summary>
    public static string SlotLabel(string inputName)
    {
        if (string.IsNullOrWhiteSpace(inputName)) return "—";

        var name = inputName.Trim();

        foreach (var brand in (string[])["Elgato ", "Wave "])
        {
            if (!name.StartsWith(brand, StringComparison.OrdinalIgnoreCase)) continue;

            var rest = name[brand.Length..].Trim();
            if (rest.Length > 0) name = rest;

            break;
        }

        name = Upper(name);

        return name.Length <= 10 ? name : name[..9] + "…";
    }

    private static string Upper(string value) => value.ToUpper(CultureInfo.CurrentCulture);
}
```

- [ ] **Step 5: Write `InputSlots`**

Create `src/WaveLinkBackup.App/ViewModels/InputSlots.cs`:

```csharp
namespace WaveLinkBackup.App.ViewModels;

/// <summary>How a slot in the five-slot health strip is drawn.</summary>
public enum SlotKind
{
    /// <summary>Present and named by the user. ok-soft fill, 2px solid ok rule, ok label.</summary>
    Named,

    /// <summary>
    /// Present but generic - the collapsed case, where Wave Link fell back to device-derived
    /// names. Transparent fill, 2px solid warn rule, warn label.
    /// </summary>
    Generic,

    /// <summary>Absent. Transparent fill, 2px DASHED line2 rule, an em dash at 45%.</summary>
    Missing,
}

public readonly record struct InputSlot(string Label, SlotKind Kind);

/// <summary>
/// The five-slot health strip: "five equal flex cells, 4px apart, always in the same order and
/// the same place, so a gap breaks the pattern of the whole column before any text is read."
///
/// Five ALWAYS five, padded with Missing. Design section C makes this structural in the view
/// model rather than an accident of a template, because it is the information design.
/// </summary>
public static class InputSlots
{
    /// <summary>
    /// Not a claim about how many inputs a rig has - technical-debt section 5 is explicit that
    /// five is one user's rig. It is the WIDTH OF THE STRIP, which is a layout constant.
    /// </summary>
    public const int SlotCount = 5;

    /// <param name="peakInputCount">
    /// The highest input count in the user's own store. A row below it has lost inputs relative
    /// to that user's own best, which is the collapsed case - so its present slots render
    /// generic.
    ///
    /// A property of the ROW, not of the name: README lists System as a healthy input AND as one
    /// of the two a collapsed configuration falls back to, so no name-matching rule could ever
    /// be right. It is also HealthFingerprint's own argument - health is decided against that
    /// user's previous snapshot, never against an absolute threshold.
    /// </param>
    public static IReadOnlyList<InputSlot> Build(IReadOnlyList<string> inputNames, int peakInputCount)
    {
        var collapsed = inputNames.Count < peakInputCount;
        var kind = collapsed ? SlotKind.Generic : SlotKind.Named;

        var slots = new InputSlot[SlotCount];

        for (var i = 0; i < SlotCount; i++)
        {
            slots[i] = i < inputNames.Count
                ? new InputSlot(Readable.SlotLabel(inputNames[i]), kind)
                : new InputSlot("—", SlotKind.Missing);
        }

        return slots;
    }
}
```

- [ ] **Step 6: Write `ObservableObject`**

Create `src/WaveLinkBackup.App/ViewModels/ObservableObject.cs`:

```csharp
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace WaveLinkBackup.App.ViewModels;

/// <summary>
/// The one INotifyPropertyChanged base. Hand-written rather than taken from a toolkit: the
/// shell has three view models, this is fifteen lines, and a source generator would be the
/// project's second production dependency for it.
/// </summary>
public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;

        field = value;
        Raise(name);

        return true;
    }

    protected void Raise([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
```

- [ ] **Step 7: Run the tests to verify they pass**

Run: `dotnet test tests/WaveLinkBackup.App.Tests --filter "FullyQualifiedName~ReadableTests|FullyQualifiedName~InputSlotsTests"`
Expected: PASS, 36 tests (29 `Readable`, counting every theory case, and 7 `InputSlots`).

- [ ] **Step 8: Commit**

```bash
git add src/WaveLinkBackup.App/ViewModels tests/WaveLinkBackup.App.Tests/ReadableTests.cs \
        tests/WaveLinkBackup.App.Tests/InputSlotsTests.cs
git commit -m "feat: the five-slot rule, and every mono readout, as pure functions

A slot is generic because its ROW lost inputs against the store's own high-water
mark, never because of what the input is called - README lists System as both a
healthy input and one of the two a collapsed config falls back to, so a
name-matching rule could not have been right. It is also the argument
HealthFingerprint already makes: compare against that user's own rig."
```

---

### Task 7: `SnapshotRowViewModel` — the row is the screen

Design §C: *"Screen 1 — the row is the screen."* Every cell, every state and every string a row
can show is decided here, so the whole of screen 1's information design is a table test with no
WPF in it.

**Files:**
- Create: `src/WaveLinkBackup.App/ViewModels/SnapshotRowViewModel.cs`
- Create: `tests/WaveLinkBackup.App.Tests/SnapshotRowViewModelTests.cs`

**Interfaces:**
- Consumes: `Snapshot`, `SnapshotManifest`, `SnapshotSearch.Segments`, `NameSegment`, `InputSlots`, `Readable`, `SnapshotHealth`, `HealthVerdict`, `ObservableObject`
- Produces:
  - `readonly record struct TierBadge(string Label, bool IsPresent)`
  - `sealed class SnapshotRowViewModel : ObservableObject`
    - ctor `(Snapshot snapshot, int peakInputCount, DateTimeOffset now, string? query = null)`
    - `string Id` · `string Name` · `IReadOnlyList<NameSegment> NameSegments`
    - `SnapshotHealth Health` · `bool IsSuspect` · `bool IsDamaged`
    - `string MetaLine` · `string VerdictLine` · `string? HealthBadge`
    - `string TakenTime` · `string TakenDate` · `string Why` · `bool WhyIsPrimary`
    - `IReadOnlyList<InputSlot> Slots` · `IReadOnlyList<TierBadge> Tiers`
    - `bool CanRename` · `bool CanDelete` · `bool CanRestore`
    - `string DetailLine` · `string DetailFileName` · `string? DamagedDetail` · `string? DamagedSentence`
    - `string AutomationName` · `string SlotsAutomationName`
    - `bool IsSelected { get; set; }`
    - `void ApplyVerdict(HealthVerdict verdict)`

- [ ] **Step 1: Write the failing tests**

Create `tests/WaveLinkBackup.App.Tests/SnapshotRowViewModelTests.cs`:

```csharp
using WaveLinkBackup.App.Hosting;
using WaveLinkBackup.App.ViewModels;
using WaveLinkBackup.Core.Snapshots;

namespace WaveLinkBackup.App.Tests;

/// <summary>
/// The row, which is the screen. Every string here is README's or 02's own.
/// </summary>
public sealed class SnapshotRowViewModelTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 15, 23, 7, 0, TimeSpan.Zero);

    private static readonly string[] Healthy =
        ["Wave Mic 1", "Voice", "Browser", "Game", "System"];

    private static Snapshot Snapshot(
        string name = "Before 3.3 beta",
        SnapshotTrigger trigger = SnapshotTrigger.Manual,
        string[]? inputs = null,
        bool suspect = false,
        long bytes = 12687769,
        DateTimeOffset? takenAt = null,
        string[]? tiers = null)
    {
        inputs ??= Healthy;

        return new Snapshot(
            Id: "2026-08-11_2136-abc12345",
            Directory: @"C:\store\2026-08-11_2136-abc12345",
            Manifest: new SnapshotManifest(
                SchemaVersion: SnapshotManifest.CurrentSchemaVersion,
                DisplayName: name,
                Notes: "",
                CreatedUtc: takenAt ?? new DateTimeOffset(2026, 8, 11, 21, 36, 0, TimeSpan.Zero),
                Trigger: trigger,
                SettingsSha256: "abc",
                WaveLinkVersion: "3.3.0.4108",
                InputCount: inputs.Length,
                InputNames: inputs,
                EffectCount: 17,
                EffectChannelCount: 4,
                HasDuplicateKeys: suspect,
                Tiers: tiers ?? ["settings", "presets"],
                Files: new Dictionary<string, SnapshotFile>(StringComparer.Ordinal)
                {
                    ["settings.json"] = new("abc", bytes),
                }));
    }

    private static SnapshotRowViewModel Row(
        Snapshot? snapshot = null, int peak = 5, string? query = null) =>
        new(snapshot ?? Snapshot(), peak, Now, query);

    // -- the five slots -------------------------------------------------------------------

    [Fact]
    public void There_are_always_five_slots()
    {
        Assert.Equal(5, Row().Slots.Count);
        Assert.Equal(5, Row(Snapshot(inputs: ["Elgato Wave:3", "System"])).Slots.Count);
    }

    [Fact]
    public void A_full_rig_reads_as_five_named_slots()
    {
        Assert.All(Row().Slots, s => Assert.Equal(SlotKind.Named, s.Kind));
    }

    // -- health ---------------------------------------------------------------------------

    [Fact]
    public void A_row_starts_whole_before_the_probe_answers()
    {
        Assert.Equal(SnapshotHealth.Whole, Row().Health);
    }

    // The manifest already knows this without hashing anything, so the amber row is right on
    // the first frame rather than a quarter second later.
    [Fact]
    public void A_snapshot_that_failed_validation_starts_suspect()
    {
        Assert.Equal(SnapshotHealth.Suspect, Row(Snapshot(suspect: true)).Health);
    }

    [Fact]
    public void A_verdict_can_turn_a_row_damaged()
    {
        var row = Row();

        row.ApplyVerdict(new HealthVerdict(SnapshotHealth.Damaged, 481280, 12288, Now));

        Assert.Equal(SnapshotHealth.Damaged, row.Health);
        Assert.True(row.IsDamaged);
    }

    // -- the meta line --------------------------------------------------------------------

    // README's row table: "12.1 MB · 4 days ago".
    [Fact]
    public void A_whole_row_reads_size_then_age()
    {
        Assert.Equal("12.1 MB · 4 days ago", Row().MetaLine);
    }

    // 02, verbatim: "471 KB · FAILED VALIDATION". Uppercase, unlike the whole row's - 02 is the
    // screen-specific spec and postdates README.
    [Fact]
    public void A_suspect_row_says_what_failed()
    {
        Assert.Equal("471 KB · FAILED VALIDATION", Row(Snapshot(suspect: true, bytes: 482304)).MetaLine);
    }

    [Fact]
    public void A_damaged_row_says_the_checksums_do_not_match()
    {
        var row = Row();
        row.ApplyVerdict(new HealthVerdict(SnapshotHealth.Damaged, 481280, 12288, Now));

        Assert.Equal("CHECKSUMS DON'T MATCH", row.MetaLine);
    }

    // 11-high-contrast: the meta line becomes a verdict word plus a glyph, in the NAME cell, on
    // the line where the size and relative time normally sit. No new column.
    [Theory]
    [InlineData(false, false, "WHOLE · 12.1 MB")]
    [InlineData(true, false, "SUSPECT · 12.1 MB · FAILED VALIDATION")]
    [InlineData(false, true, "DAMAGED · CHECKSUMS DON'T MATCH")]
    public void High_contrast_turns_the_meta_line_into_a_verdict(bool suspect, bool damaged, string expected)
    {
        var row = Row(Snapshot(suspect: suspect));

        if (damaged) row.ApplyVerdict(new HealthVerdict(SnapshotHealth.Damaged, 1, 1, Now));

        Assert.Equal(expected, row.VerdictLine);
    }

    // -- the badge ------------------------------------------------------------------------

    [Fact]
    public void Only_an_unhealthy_row_carries_a_badge()
    {
        Assert.Null(Row().HealthBadge);
        Assert.Equal("SUSPECT", Row(Snapshot(suspect: true)).HealthBadge);
    }

    [Fact]
    public void A_damaged_row_carries_the_damaged_badge()
    {
        var row = Row();
        row.ApplyVerdict(new HealthVerdict(SnapshotHealth.Damaged, 1, 1, Now));

        Assert.Equal("DAMAGED", row.HealthBadge);
    }

    // -- the other columns ----------------------------------------------------------------

    [Fact]
    public void Taken_is_a_time_over_a_date()
    {
        var row = Row();

        Assert.Equal("21:36", row.TakenTime);
        Assert.Equal("11 AUG", row.TakenDate);
    }

    // README: "MANUAL at --wl-text; AUTOMATIC and PRE-RESTORE at --wl-muted."
    [Theory]
    [InlineData(SnapshotTrigger.Manual, "MANUAL", true)]
    [InlineData(SnapshotTrigger.Automatic, "AUTOMATIC", false)]
    [InlineData(SnapshotTrigger.PreRestore, "PRE-RESTORE", false)]
    public void Why_is_a_pill_and_only_manual_is_primary(
        SnapshotTrigger trigger, string label, bool primary)
    {
        var row = Row(Snapshot(trigger: trigger));

        Assert.Equal(label, row.Why);
        Assert.Equal(primary, row.WhyIsPrimary);
    }

    // "Three fixed slots, always three wide, so the column is scannable."
    [Fact]
    public void There_are_always_three_tier_badges_in_a_fixed_order()
    {
        var tiers = Row().Tiers;

        Assert.Equal(["SETTINGS", "PRESETS", "PLUGINS"], tiers.Select(t => t.Label));
        Assert.Equal([true, true, false], tiers.Select(t => t.IsPresent));
    }

    [Fact]
    public void Settings_is_present_on_every_snapshot()
    {
        Assert.True(Row(Snapshot(tiers: ["settings"])).Tiers[0].IsPresent);
    }

    // -- actions --------------------------------------------------------------------------

    [Fact]
    public void A_healthy_row_allows_every_action()
    {
        var row = Row();

        Assert.True(row.CanRename);
        Assert.True(row.CanDelete);
        Assert.True(row.CanRestore);
    }

    // 02: "all enabled, including Restore". A suspect backup may be the only copy there is.
    [Fact]
    public void A_suspect_row_can_still_be_restored()
    {
        Assert.True(Row(Snapshot(suspect: true)).CanRestore);
    }

    // 02's bottom bar for a damaged selection: Rename and Restore disabled, Delete ENABLED,
    // because deleting is the only useful action.
    [Fact]
    public void A_damaged_row_can_only_be_deleted()
    {
        var row = Row();
        row.ApplyVerdict(new HealthVerdict(SnapshotHealth.Damaged, 1, 1, Now));

        Assert.False(row.CanRename);
        Assert.False(row.CanRestore);
        Assert.True(row.CanDelete);
    }

    // -- search ---------------------------------------------------------------------------

    [Fact]
    public void With_no_query_the_name_is_one_unmatched_segment()
    {
        var segments = Row().NameSegments;

        Assert.Single(segments);
        Assert.False(segments[0].IsMatch);
    }

    [Fact]
    public void A_query_marks_the_matched_run_of_the_name()
    {
        var segments = Row(query: "beta").NameSegments;

        Assert.Equal([("Before 3.3 ", false), ("beta", true)],
                     segments.Select(s => (s.Text, s.IsMatch)));
    }

    // -- the selected row's detail --------------------------------------------------------

    // README: "17 EFFECTS ON 4 CHANNELS · 3 PRESETS · STREAM + MONITOR MIXES". Presets and mixes
    // are phase 6 and are omitted rather than printed as zero.
    [Fact]
    public void The_detail_line_reports_what_the_manifest_actually_knows()
    {
        Assert.Equal("17 EFFECTS ON 4 CHANNELS", Row().DetailLine);
    }

    [Fact]
    public void The_detail_names_the_snapshot_on_disk()
    {
        Assert.Equal("2026-08-11_2136-abc12345", Row().DetailFileName);
    }

    // 02: "MANIFEST SAYS 470 KB · FILE IS 12 KB · CHECKED 23:09".
    [Fact]
    public void A_damaged_row_shows_both_sizes_and_when_it_was_checked()
    {
        var row = Row();
        row.ApplyVerdict(new HealthVerdict(SnapshotHealth.Damaged, 481280, 12288, Now));

        Assert.Equal("MANIFEST SAYS 470 KB · FILE IS 12 KB · CHECKED 23:07", row.DamagedDetail);
    }

    [Fact]
    public void An_unreadable_file_says_so_rather_than_claiming_zero_bytes()
    {
        var row = Row();
        row.ApplyVerdict(new HealthVerdict(SnapshotHealth.Damaged, 481280, null, Now));

        Assert.Contains("FILE CAN'T BE READ", row.DamagedDetail!, StringComparison.Ordinal);
        Assert.DoesNotContain("0 B", row.DamagedDetail!, StringComparison.Ordinal);
    }

    [Fact]
    public void A_healthy_row_has_no_damaged_detail_or_sentence()
    {
        Assert.Null(Row().DamagedDetail);
        Assert.Null(Row().DamagedSentence);
    }

    [Fact]
    public void The_damaged_sentence_says_it_cannot_be_restored_and_what_to_do()
    {
        var row = Row();
        row.ApplyVerdict(new HealthVerdict(SnapshotHealth.Damaged, 1, 1, Now));

        Assert.Contains("can't be restored", row.DamagedSentence!, StringComparison.Ordinal);
        Assert.Contains("take a fresh one", row.DamagedSentence!, StringComparison.Ordinal);
    }

    // -- screen readers -------------------------------------------------------------------

    // 7.4: "the five-slot strip reads as five unlabelled cells without an AutomationProperties
    // name." A strip whose whole meaning is which cells are filled is exactly the thing a reader
    // cannot infer.
    [Fact]
    public void The_strip_names_every_input_and_says_how_many_are_missing()
    {
        var name = Row(Snapshot(inputs: ["Elgato Wave:3", "System"])).SlotsAutomationName;

        Assert.Contains("2 of 5", name, StringComparison.Ordinal);
        Assert.Contains("Elgato Wave:3", name, StringComparison.Ordinal);
        Assert.Contains("System", name, StringComparison.Ordinal);
    }

    [Fact]
    public void The_row_reads_as_a_sentence_rather_than_six_cells()
    {
        var name = Row().AutomationName;

        Assert.Contains("Before 3.3 beta", name, StringComparison.Ordinal);
        Assert.Contains("manual", name, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("21:36", name, StringComparison.Ordinal);
    }

    [Fact]
    public void A_damaged_row_says_so_to_a_reader_first()
    {
        var row = Row();
        row.ApplyVerdict(new HealthVerdict(SnapshotHealth.Damaged, 1, 1, Now));

        Assert.StartsWith("Damaged", row.AutomationName, StringComparison.Ordinal);
    }

    // -- change notification --------------------------------------------------------------

    // The probe answers after the row is on screen. Without this the flip to DAMAGED happens in
    // the object and nowhere else.
    [Fact]
    public void Applying_a_verdict_raises_change_notification_for_everything_it_moves()
    {
        var row = Row();
        var raised = new List<string?>();

        row.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        row.ApplyVerdict(new HealthVerdict(SnapshotHealth.Damaged, 1, 1, Now));

        Assert.Contains(nameof(SnapshotRowViewModel.Health), raised);
        Assert.Contains(nameof(SnapshotRowViewModel.MetaLine), raised);
        Assert.Contains(nameof(SnapshotRowViewModel.CanRestore), raised);
        Assert.Contains(nameof(SnapshotRowViewModel.HealthBadge), raised);
    }

    [Fact]
    public void An_unchanged_verdict_is_still_safe_to_apply()
    {
        var row = Row();

        row.ApplyVerdict(new HealthVerdict(SnapshotHealth.Whole, 1, 1, Now));

        Assert.Equal(SnapshotHealth.Whole, row.Health);
        Assert.Equal("12.1 MB · 4 days ago", row.MetaLine);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/WaveLinkBackup.App.Tests --filter FullyQualifiedName~SnapshotRowViewModelTests`
Expected: FAIL — `SnapshotRowViewModel` does not exist.

- [ ] **Step 3: Write `SnapshotRowViewModel`**

Create `src/WaveLinkBackup.App/ViewModels/SnapshotRowViewModel.cs`:

```csharp
using System.Globalization;
using WaveLinkBackup.App.Hosting;
using WaveLinkBackup.Core.Snapshots;

namespace WaveLinkBackup.App.ViewModels;

/// <summary>One of the three fixed CONTENTS slots. Absent slots stay in place.</summary>
public readonly record struct TierBadge(string Label, bool IsPresent);

/// <summary>
/// One row. "The row is the screen" - design section C.
///
/// Everything the row can say is decided here rather than in a converter or a trigger, which is
/// what makes the whole of screen 1's information design a table test with no WPF in it. The
/// TEMPLATE decides only how each of these reads: what colour, what rule style, which of the
/// two meta lines.
/// </summary>
public sealed class SnapshotRowViewModel : ObservableObject
{
    private static readonly string[] TierOrder = ["settings", "presets", "plugins"];

    private readonly SnapshotManifest manifest;
    private readonly DateTimeOffset now;

    private SnapshotHealth health;
    private HealthVerdict? verdict;
    private bool isSelected;

    public SnapshotRowViewModel(
        Snapshot snapshot, int peakInputCount, DateTimeOffset now, string? query = null)
    {
        manifest = snapshot.Manifest;
        this.now = now;

        Id = snapshot.Id;
        Name = manifest.DisplayName;
        NameSegments = SnapshotSearch.Segments(manifest.DisplayName, query);

        // Suspect is known from the MANIFEST, without hashing anything - so an amber row is
        // right on the first frame rather than a quarter of a second later. Only DAMAGED has to
        // wait for the probe.
        health = manifest.IsSuspect ? SnapshotHealth.Suspect : SnapshotHealth.Whole;

        SizeBytes = manifest.Files.Values.Sum(f => f.SizeBytes);
        TakenAt = manifest.CreatedUtc.ToLocalTime();

        Slots = InputSlots.Build(manifest.InputNames, peakInputCount);
        Tiers = [.. TierOrder.Select(t => new TierBadge(
            t.ToUpper(CultureInfo.CurrentCulture),
            manifest.Tiers.Contains(t, StringComparer.OrdinalIgnoreCase)))];
    }

    public string Id { get; }

    public string Name { get; }

    /// <summary>
    /// Segments rather than a raw string, so the --wl-accent-soft highlight is a testable
    /// property of the view model instead of something hidden in a converter.
    /// </summary>
    public IReadOnlyList<NameSegment> NameSegments { get; }

    public long SizeBytes { get; }

    public DateTimeOffset TakenAt { get; }

    public IReadOnlyList<InputSlot> Slots { get; }

    public IReadOnlyList<TierBadge> Tiers { get; }

    public SnapshotHealth Health => health;

    public bool IsSuspect => health == SnapshotHealth.Suspect;

    public bool IsDamaged => health == SnapshotHealth.Damaged;

    public bool IsSelected
    {
        get => isSelected;
        set => Set(ref isSelected, value);
    }

    public string TakenTime => Readable.TimeOfDay(TakenAt);

    public string TakenDate => Readable.ShortDate(TakenAt);

    public string Why => manifest.Trigger switch
    {
        SnapshotTrigger.Manual => "MANUAL",
        SnapshotTrigger.Automatic => "AUTOMATIC",
        SnapshotTrigger.PreRestore => "PRE-RESTORE",
        _ => "UNKNOWN",
    };

    /// <summary>MANUAL is --wl-text; AUTOMATIC and PRE-RESTORE are --wl-muted (README).</summary>
    public bool WhyIsPrimary => manifest.Trigger == SnapshotTrigger.Manual;

    /// <summary>
    /// The sub-line under the name in the normal themes. Three states, three strings, each one
    /// README's or 02's own.
    /// </summary>
    public string MetaLine => health switch
    {
        SnapshotHealth.Damaged => "CHECKSUMS DON'T MATCH",
        SnapshotHealth.Suspect => $"{Readable.Bytes(SizeBytes)} · FAILED VALIDATION",
        _ => $"{Readable.Bytes(SizeBytes)} · {Readable.RelativeTime(TakenAt, now)}",
    };

    /// <summary>
    /// The same line in high contrast, where colour means nothing: a verdict WORD leads, and the
    /// rest follows. 11-high-contrast, and no new column - it renders in the NAME cell exactly
    /// where the meta line does.
    /// </summary>
    public string VerdictLine => health switch
    {
        SnapshotHealth.Damaged => "DAMAGED · CHECKSUMS DON'T MATCH",
        SnapshotHealth.Suspect => $"SUSPECT · {Readable.Bytes(SizeBytes)} · FAILED VALIDATION",
        _ => $"WHOLE · {Readable.Bytes(SizeBytes)}",
    };

    /// <summary>The pill beside the name, or null on a healthy row - which carries none.</summary>
    public string? HealthBadge => health switch
    {
        SnapshotHealth.Damaged => "DAMAGED",
        SnapshotHealth.Suspect => "SUSPECT",
        _ => null,
    };

    /// <summary>
    /// Rename and Restore are off for a damaged row; DELETE STAYS ON, because deleting it is
    /// the only useful thing left to do with it (02).
    ///
    /// A SUSPECT row keeps everything, Restore included - it is still restorable and may be the
    /// only copy that exists.
    /// </summary>
    public bool CanRename => !IsDamaged;

    public bool CanRestore => !IsDamaged;

    public bool CanDelete => true;

    /// <summary>
    /// README's selected-row readout. Presets and mixes are phase 6, so they are OMITTED rather
    /// than printed as zero - "3 PRESETS" when the tier does not exist yet would be a claim
    /// about contents nobody made.
    /// </summary>
    public string DetailLine
    {
        get
        {
            var effects = manifest.EffectCount == 1 ? "1 EFFECT" : $"{manifest.EffectCount} EFFECTS";
            var channels = manifest.EffectChannelCount == 1 ? "1 CHANNEL"
                : $"{manifest.EffectChannelCount} CHANNELS";

            return $"{effects} ON {channels}";
        }
    }

    /// <summary>
    /// What the backup is called on disk. README writes a ".wlbk" filename; this store keeps
    /// DIRECTORIES, and printing a filename that does not exist is worse than printing the name
    /// that does.
    /// </summary>
    public string DetailFileName => Id;

    /// <summary>02: "MANIFEST SAYS 470 KB · FILE IS 12 KB · CHECKED 23:09".</summary>
    public string? DamagedDetail
    {
        get
        {
            if (!IsDamaged || verdict is not { } v) return null;

            // Null, not zero. "The file is 0 B" is a measurement; "it can't be read" is what
            // actually happened, and they are different claims.
            var actual = v.ActualBytes is { } bytes
                ? $"FILE IS {Readable.Bytes(bytes)}"
                : "FILE CAN'T BE READ";

            return $"MANIFEST SAYS {Readable.Bytes(v.ManifestBytes)} · {actual} · "
                 + $"CHECKED {Readable.TimeOfDay(v.CheckedAt.ToLocalTime())}";
        }
    }

    /// <summary>02's sentence for the selected damaged row, verbatim.</summary>
    public string? DamagedSentence => IsDamaged
        ? "The files changed after this backup was written — a failed sync, a bad disk, or "
        + "something edited the folder. There is nothing left in here that can be trusted, so "
        + "it can't be restored. Delete it and take a fresh one."
        : null;

    /// <summary>
    /// What a screen reader hears instead of six unlabelled cells. Health leads, because a
    /// reader arriving at a row needs to know it cannot be restored before anything else.
    /// </summary>
    public string AutomationName
    {
        get
        {
            var state = health switch
            {
                SnapshotHealth.Damaged => "Damaged, cannot be restored. ",
                SnapshotHealth.Suspect => "Suspect, failed validation when it was taken. ",
                _ => string.Empty,
            };

            return $"{state}{Name}, {Why.ToLower(CultureInfo.CurrentCulture)}, taken "
                 + $"{Readable.ShortDate(TakenAt)} {Readable.TimeOfDay(TakenAt)}, "
                 + $"{Readable.Bytes(SizeBytes)}.";
        }
    }

    /// <summary>
    /// 7.4 is explicit that this is part of the work rather than a follow-up: the five-slot
    /// strip reads as five unlabelled cells without it, and which cells are filled is the entire
    /// meaning of the column.
    /// </summary>
    public string SlotsAutomationName =>
        manifest.InputNames.Count == 0
            ? $"No inputs, {InputSlots.SlotCount} slots empty."
            : $"{manifest.InputNames.Count} of {InputSlots.SlotCount} inputs: "
            + $"{string.Join(", ", manifest.InputNames)}.";

    /// <summary>
    /// The probe's answer, arriving after the row is already on screen. Raises every property it
    /// can move - without that the flip to DAMAGED happens in the object and nowhere else.
    /// </summary>
    public void ApplyVerdict(HealthVerdict verdict)
    {
        this.verdict = verdict;

        if (health == verdict.Health)
        {
            // Still raised: a damaged row re-probed after an F5 can have new sizes and a new
            // check time even though the verdict itself did not move.
            Raise(nameof(DamagedDetail));
            return;
        }

        health = verdict.Health;

        foreach (var property in (string[])
        [
            nameof(Health), nameof(IsSuspect), nameof(IsDamaged),
            nameof(MetaLine), nameof(VerdictLine), nameof(HealthBadge),
            nameof(CanRename), nameof(CanRestore), nameof(CanDelete),
            nameof(DamagedDetail), nameof(DamagedSentence), nameof(AutomationName),
        ])
        {
            Raise(property);
        }
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/WaveLinkBackup.App.Tests --filter FullyQualifiedName~SnapshotRowViewModelTests`
Expected: PASS, 35 tests (counting every theory case).

- [ ] **Step 5: Commit**

```bash
git add src/WaveLinkBackup.App/ViewModels/SnapshotRowViewModel.cs \
        tests/WaveLinkBackup.App.Tests/SnapshotRowViewModelTests.cs
git commit -m "feat: the row, which is the screen

Five slots always five, the damaged break, the amber suspect pill README still
gets wrong, and the high-contrast verdict word - all decided here so the whole
information design is a table test rather than a screenshot.

Suspect comes from the manifest and is right on the first frame; only DAMAGED
waits for the probe."
```

---

### Task 8: `SnapshotListViewModel` — groups, search, selection, and the four list states

**Files:**
- Create: `src/WaveLinkBackup.App/ViewModels/SnapshotListViewModel.cs`
- Create: `tests/WaveLinkBackup.App.Tests/SnapshotListViewModelTests.cs`

**Interfaces:**
- Consumes: `SnapshotStore.List`, `SnapshotStore.StorePath`, `SnapshotSearch.Filter`, `HealthProbe.ProbeAsync`, `SnapshotRowViewModel`, `Readable`, `IFileSystem.DirectoryExists`, `IClock`
- Produces:
  - `enum ListState { Loaded, NoResults, Empty, FolderMissing }`
  - `sealed record DateGroup(string Header, IReadOnlyList<SnapshotRowViewModel> Rows)`
  - `sealed class SnapshotListViewModel(SnapshotStore store, HealthProbe probe, IFileSystem fileSystem, IClock clock) : ObservableObject, IDisposable`
    - `ObservableCollection<DateGroup> Groups`
    - `string Query { get; set; }` · `SnapshotRowViewModel? Selected { get; set; }`
    - `ListState State` · `int TotalCount` · `int MatchCount` · `int HiddenCount` · `long TotalBytes`
    - `string MatchSummary` · `string? SearchFooter` · `string? ShowAllLabel`
    - `string NoResultsTitle` · `string NoResultsDetail`
    - `void Refresh()` · `Task RefreshAsync()` · `void ClearSearch()` · `void Select(string id)`
    - `Action<Action>? Marshal { get; set; }` — how a probe verdict gets back to the UI thread

- [ ] **Step 1: Write the failing tests**

Create `tests/WaveLinkBackup.App.Tests/SnapshotListViewModelTests.cs`:

```csharp
using System.Text;
using WaveLinkBackup.App.Hosting;
using WaveLinkBackup.App.ViewModels;
using WaveLinkBackup.Core.Analysis;
using WaveLinkBackup.Core.Snapshots;
using WaveLinkBackup.Core.Tests.Fakes;

namespace WaveLinkBackup.App.Tests;

/// <summary>
/// The list: grouping, search and the four things it can be showing. Every count and every
/// string is 07-search.md's or README's own.
/// </summary>
public sealed class SnapshotListViewModelTests
{
    private const string StorePath = @"C:\store";

    private static byte[] SettingsFor(params string[] inputs)
    {
        var entries = inputs.Select((n, i) =>
            $"\"K{i}\":{{\"InputName\":\"{n}\",\"AudioPluginConfigurations\":[]}}");

        return Encoding.UTF8.GetBytes(
            $"{{\"MixerConfiguration\":{{\"InputSettings\":{{{string.Join(",", entries)}}}}}}}");
    }

    private sealed class Rig
    {
        // Created up front, and empty: "no backups yet" and "the folder is gone" are different
        // states, and a rig that could not tell them apart would make the empty-store test pass
        // for the wrong reason.
        public FakeFileSystem Fs { get; } = Created();

        private static FakeFileSystem Created()
        {
            var fs = new FakeFileSystem();
            fs.CreateDirectory(StorePath);

            return fs;
        }

        public FakeClock Clock { get; } = new() { UtcNow = new DateTimeOffset(2026, 8, 15, 23, 7, 0, TimeSpan.Zero) };

        public SnapshotStore Store => new(Fs, Clock, StorePath);

        public SnapshotListViewModel List() =>
            new(Store, new HealthProbe(Store, Fs, Clock), Fs, Clock) { Marshal = action => action() };

        public void Add(string name, DateTimeOffset at, params string[] inputs)
        {
            Clock.UtcNow = at;

            var bytes = SettingsFor(inputs.Length == 0
                ? ["Wave Mic 1", "Voice", "Browser", "Game", "System"]
                : inputs);

            Store.Write(bytes, SettingsAnalysis.Analyse(bytes).Value, SnapshotTrigger.Manual, name);
        }
    }

    private static Rig Store14()
    {
        var rig = new Rig();
        var start = new DateTimeOffset(2026, 8, 15, 22, 0, 0, TimeSpan.Zero);

        rig.Add("Auto", start);
        rig.Add("Before restore", start.AddDays(-4).AddMinutes(3), "Elgato Wave:3", "System");
        rig.Add("Before 3.3 beta", start.AddDays(-4));
        rig.Add("Full rig + plugins", start.AddDays(-11));

        for (var i = 0; i < 10; i++) rig.Add($"Spare {i}", start.AddDays(-20 - i));

        rig.Clock.UtcNow = new DateTimeOffset(2026, 8, 15, 23, 7, 0, TimeSpan.Zero);

        return rig;
    }

    // -- loading and grouping ---------------------------------------------------------------

    [Fact]
    public void Refreshing_loads_every_snapshot()
    {
        var list = Store14().List();

        list.Refresh();

        Assert.Equal(14, list.TotalCount);
        Assert.Equal(ListState.Loaded, list.State);
    }

    // "Newest group first, newest row first inside a group." - README.
    [Fact]
    public void Groups_run_newest_first_and_so_do_the_rows_inside_them()
    {
        var list = Store14().List();

        list.Refresh();

        Assert.Equal("TODAY", list.Groups[0].Header);
        Assert.Equal("Auto", list.Groups[0].Rows[0].Name);

        var second = list.Groups[1];
        Assert.Equal("TUE 11 AUG", second.Header);
        Assert.Equal(["Before restore", "Before 3.3 beta"], second.Rows.Select(r => r.Name));
    }

    // 02: "Damaged rows stay in date order. Do not sort them to the bottom without asking; a
    // user looking for a specific date needs to find it where they expect."
    [Fact]
    public void A_damaged_row_stays_where_its_date_puts_it()
    {
        var rig = Store14();
        var list = rig.List();

        list.Refresh();

        var damaged = list.Groups[1].Rows[1];
        damaged.ApplyVerdict(new HealthVerdict(SnapshotHealth.Damaged, 1, 1, rig.Clock.UtcNow));

        list.Refresh();

        Assert.Equal("Before 3.3 beta", list.Groups[1].Rows[1].Name);
    }

    // The collapsed row's slots are amber because the store's own high-water mark is five.
    [Fact]
    public void The_high_water_mark_comes_from_the_store_not_from_a_constant()
    {
        var list = Store14().List();

        list.Refresh();

        var collapsed = list.Groups[1].Rows.Single(r => r.Name == "Before restore");
        var full = list.Groups[1].Rows.Single(r => r.Name == "Before 3.3 beta");

        Assert.Equal(SlotKind.Generic, collapsed.Slots[0].Kind);
        Assert.Equal(SlotKind.Named, full.Slots[0].Kind);
    }

    // -- search -----------------------------------------------------------------------------

    [Fact]
    public void A_query_filters_the_list_and_keeps_the_groups()
    {
        var list = Store14().List();
        list.Refresh();

        list.Query = "beta";

        Assert.Equal(1, list.MatchCount);
        Assert.Equal(14, list.TotalCount);
        Assert.Equal("Before 3.3 beta", list.Groups.Single().Rows.Single().Name);
    }

    [Fact]
    public void A_match_is_marked_in_the_row_s_name()
    {
        var list = Store14().List();
        list.Refresh();

        list.Query = "beta";

        var segments = list.Groups[0].Rows[0].NameSegments;

        Assert.Contains(segments, s => s.IsMatch && s.Text == "beta");
    }

    // 07: status strip left reads "3 OF 14 MATCH \"BETA\"".
    [Fact]
    public void The_status_strip_reports_the_match_count()
    {
        var list = Store14().List();
        list.Refresh();

        list.Query = "spare";

        Assert.Equal("10 OF 14 MATCH \"SPARE\"", list.MatchSummary);
    }

    // 07: footer "SHOWING 3 OF 14 · 11 HIDDEN BY THE SEARCH", right "Show all 14".
    [Fact]
    public void The_footer_says_what_is_hidden_and_offers_to_show_it()
    {
        var list = Store14().List();
        list.Refresh();

        list.Query = "beta";

        Assert.Equal("SHOWING 1 OF 14 · 13 HIDDEN BY THE SEARCH", list.SearchFooter);
        Assert.Equal("Show all 14", list.ShowAllLabel);
    }

    [Fact]
    public void With_no_query_there_is_no_footer_and_no_summary()
    {
        var list = Store14().List();

        list.Refresh();

        Assert.Null(list.SearchFooter);
        Assert.Equal(string.Empty, list.MatchSummary);
    }

    [Fact]
    public void Clearing_the_search_returns_the_full_list()
    {
        var list = Store14().List();
        list.Refresh();
        list.Query = "beta";

        list.ClearSearch();

        Assert.Equal(string.Empty, list.Query);
        Assert.Equal(14, list.MatchCount);
        Assert.Equal(ListState.Loaded, list.State);
    }

    // -- no results, which is NOT the empty state ------------------------------------------

    [Fact]
    public void A_query_that_matches_nothing_is_its_own_state()
    {
        var list = Store14().List();
        list.Refresh();

        list.Query = "wave:3";

        Assert.Equal(ListState.NoResults, list.State);
        Assert.Equal("0 OF 14 MATCH \"WAVE:3\"", list.MatchSummary);
    }

    // 07's body copy, and the sentence that makes the promise the search must keep.
    [Fact]
    public void No_results_names_the_query_and_says_search_looks_at_names_only()
    {
        var list = Store14().List();
        list.Refresh();

        list.Query = "wave:3";

        Assert.Equal("No backup is called \"wave:3\".", list.NoResultsTitle);
        Assert.Equal("14 BACKUPS ARE HERE · SEARCH LOOKS AT NAMES ONLY", list.NoResultsDetail);
    }

    [Fact]
    public void An_empty_store_is_empty_and_not_no_results()
    {
        var list = new Rig().List();

        list.Refresh();

        Assert.Equal(ListState.Empty, list.State);
        Assert.Equal(0, list.TotalCount);
    }

    // 08's error 12 is a later session; the strip saying so is 10-decisions section 6, which is
    // pinned now.
    [Fact]
    public void A_store_folder_that_is_not_there_is_its_own_state()
    {
        var fs = new FakeFileSystem();
        var clock = new FakeClock();
        var gone = new SnapshotStore(fs, clock, @"E:\gone");

        var missing = new SnapshotListViewModel(gone, new HealthProbe(gone, fs, clock), fs, clock)
        {
            Marshal = action => action(),
        };

        missing.Refresh();

        Assert.Equal(ListState.FolderMissing, missing.State);
    }

    // -- selection --------------------------------------------------------------------------

    [Fact]
    public void Nothing_is_selected_after_a_load()
    {
        var list = Store14().List();

        list.Refresh();

        Assert.Null(list.Selected);
    }

    [Fact]
    public void Selecting_a_row_marks_it_and_unmarks_the_last_one()
    {
        var list = Store14().List();
        list.Refresh();

        var first = list.Groups[0].Rows[0];
        var second = list.Groups[1].Rows[0];

        list.Selected = first;
        list.Selected = second;

        Assert.False(first.IsSelected);
        Assert.True(second.IsSelected);
    }

    // Back up now inserts a row at the top of TODAY and selects it (README). Selection is by id
    // because the row objects are rebuilt by the refresh that follows.
    [Fact]
    public void A_selection_survives_a_refresh_by_id()
    {
        var list = Store14().List();
        list.Refresh();

        var id = list.Groups[1].Rows[0].Id;
        list.Select(id);

        list.Refresh();

        Assert.Equal(id, list.Selected?.Id);
    }

    [Fact]
    public void Selecting_an_id_that_is_gone_clears_the_selection()
    {
        var list = Store14().List();
        list.Refresh();

        list.Select("no-such-snapshot");

        Assert.Null(list.Selected);
    }

    // -- the probe --------------------------------------------------------------------------

    [Fact]
    public async Task Refreshing_asynchronously_verifies_every_row()
    {
        var rig = Store14();
        var list = rig.List();

        await list.RefreshAsync();

        Assert.All(list.Groups.SelectMany(g => g.Rows), r => Assert.NotEqual(SnapshotHealth.Damaged, r.Health));
    }

    [Fact]
    public async Task A_tampered_snapshot_turns_its_row_damaged()
    {
        var rig = Store14();
        var list = rig.List();

        list.Refresh();

        var victim = rig.Store.List().Single(s => s.Manifest.DisplayName == "Auto");
        rig.Fs.WriteBytes(victim.SettingsPath, "tampered"u8.ToArray());

        await list.RefreshAsync();

        Assert.Equal(SnapshotHealth.Damaged, list.Groups[0].Rows[0].Health);
    }

    [Fact]
    public void Total_bytes_is_the_whole_store_not_the_filtered_view()
    {
        var list = Store14().List();
        list.Refresh();

        var total = list.TotalBytes;

        list.Query = "beta";

        Assert.Equal(total, list.TotalBytes);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/WaveLinkBackup.App.Tests --filter FullyQualifiedName~SnapshotListViewModelTests`
Expected: FAIL — `SnapshotListViewModel` does not exist.

- [ ] **Step 3: Write `SnapshotListViewModel`**

Create `src/WaveLinkBackup.App/ViewModels/SnapshotListViewModel.cs`:

```csharp
using System.Collections.ObjectModel;
using System.Globalization;
using WaveLinkBackup.App.Hosting;
using WaveLinkBackup.Core.Abstractions;
using WaveLinkBackup.Core.Snapshots;

namespace WaveLinkBackup.App.ViewModels;

/// <summary>What the list area is showing. Four states, and they are not interchangeable.</summary>
public enum ListState
{
    Loaded,

    /// <summary>
    /// A search matched nothing. NOT the empty state - 07 is explicit that the status strip,
    /// the column header and the count stay on screen precisely so an empty RESULT never looks
    /// like an empty APP.
    /// </summary>
    NoResults,

    /// <summary>No backups at all. Screen 4's first run belongs here and is a later session.</summary>
    Empty,

    /// <summary>The store folder is not there. Error 12's full screen is a later session.</summary>
    FolderMissing,
}

/// <summary>A date-group header and the rows under it.</summary>
public sealed record DateGroup(string Header, IReadOnlyList<SnapshotRowViewModel> Rows);

/// <summary>
/// The list: what is in the store, grouped by day, filtered by the search field, with one row
/// selected.
///
/// The store is read on Refresh and NOT held open - SnapshotStore.List() re-reads the manifests
/// each time, which is what makes F5 mean something. Health arrives separately, from the probe,
/// on a background thread.
/// </summary>
public sealed class SnapshotListViewModel(
    SnapshotStore store, HealthProbe probe, IFileSystem fileSystem, IClock clock)
    : ObservableObject, IDisposable
{
    private readonly List<Snapshot> all = [];

    private CancellationTokenSource? probing;
    private string query = string.Empty;
    private SnapshotRowViewModel? selected;
    private ListState state = ListState.Empty;

    /// <summary>
    /// How a verdict gets from the probe's thread back to the UI thread. Set by the window to
    /// Dispatcher.Invoke; the tests set it to run inline, which is what makes the probe
    /// assertable without a dispatcher.
    /// </summary>
    public Action<Action>? Marshal { get; set; }

    public ObservableCollection<DateGroup> Groups { get; } = [];

    public int TotalCount => all.Count;

    public int MatchCount { get; private set; }

    public int HiddenCount => TotalCount - MatchCount;

    /// <summary>The whole store, never the filtered view - the bottom bar counts backups, not results.</summary>
    public long TotalBytes => all.Sum(s => s.Manifest.Files.Values.Sum(f => f.SizeBytes));

    public ListState State
    {
        get => state;
        private set => Set(ref state, value);
    }

    public string Query
    {
        get => query;
        set
        {
            if (!Set(ref query, value ?? string.Empty)) return;

            Rebuild();
        }
    }

    public SnapshotRowViewModel? Selected
    {
        get => selected;
        set
        {
            if (ReferenceEquals(selected, value)) return;

            if (selected is not null) selected.IsSelected = false;
            selected = value;
            if (selected is not null) selected.IsSelected = true;

            Raise();
        }
    }

    /// <summary>07: `3 OF 14 MATCH "BETA"`. Empty with no query - the strip says other things then.</summary>
    public string MatchSummary => query.Length == 0
        ? string.Empty
        : $"{MatchCount} OF {TotalCount} MATCH \"{query.ToUpper(CultureInfo.CurrentCulture)}\"";

    /// <summary>07: `SHOWING 3 OF 14 · 11 HIDDEN BY THE SEARCH`.</summary>
    public string? SearchFooter => query.Length == 0 || State != ListState.Loaded
        ? null
        : $"SHOWING {MatchCount} OF {TotalCount} · {HiddenCount} HIDDEN BY THE SEARCH";

    public string? ShowAllLabel => SearchFooter is null ? null : $"Show all {TotalCount}";

    /// <summary>07's line 1. Lower case and in quotes, because it echoes what the user typed.</summary>
    public string NoResultsTitle => $"No backup is called \"{query}\".";

    /// <summary>
    /// 07's line 2. "SEARCH LOOKS AT NAMES ONLY" is a promise, and SnapshotSearch keeps it -
    /// widening the filter later would make this copy a lie.
    /// </summary>
    public string NoResultsDetail =>
        $"{TotalCount} BACKUP{(TotalCount == 1 ? "" : "S")} ARE HERE · SEARCH LOOKS AT NAMES ONLY";

    /// <summary>Reads the store and rebuilds the rows. F5, and every load.</summary>
    public void Refresh()
    {
        var selectedId = selected?.Id;

        all.Clear();
        all.AddRange(store.List());

        Rebuild();

        if (selectedId is not null) Select(selectedId);
    }

    /// <summary>
    /// Refresh, then verify everything off the UI thread. The rows are on screen before the
    /// first hash starts, which is the whole point of splitting them.
    /// </summary>
    public async Task RefreshAsync()
    {
        Refresh();

        // An F5 while a probe is running must not leave the old run writing verdicts into the
        // rows that replaced them.
        probing?.Cancel();
        probing?.Dispose();
        probing = new CancellationTokenSource();

        var rows = Groups.SelectMany(g => g.Rows).ToDictionary(r => r.Id, StringComparer.Ordinal);

        await probe.ProbeAsync(
            all,
            (id, verdict) => (Marshal ?? (a => a()))(() =>
            {
                if (rows.TryGetValue(id, out var row)) row.ApplyVerdict(verdict);
            }),
            probing.Token);
    }

    public void ClearSearch() => Query = string.Empty;

    /// <summary>
    /// Selection is by ID, not by object: Refresh builds new rows, and "Back up now inserts a
    /// row at the top of TODAY and selects it" needs a name for the thing to select.
    /// </summary>
    public void Select(string id) =>
        Selected = Groups
            .SelectMany(g => g.Rows)
            .FirstOrDefault(r => string.Equals(r.Id, id, StringComparison.Ordinal));

    private void Rebuild()
    {
        var now = clock.UtcNow.ToLocalTime();

        // The high-water mark is the STORE's, not the filtered view's - hiding the full rig
        // behind a search must not make a collapsed row look whole.
        var peak = all.Count == 0 ? 0 : all.Max(s => s.Manifest.InputCount);

        var matched = SnapshotSearch.Filter(all, query);
        MatchCount = matched.Count;

        Groups.Clear();

        foreach (var group in matched
            .OrderByDescending(s => s.Manifest.CreatedUtc)
            .GroupBy(s => s.Manifest.CreatedUtc.ToLocalTime().Date))
        {
            Groups.Add(new DateGroup(
                Readable.DayGroup(new DateTimeOffset(group.Key, now.Offset), now),
                [.. group.Select(s => new SnapshotRowViewModel(s, peak, now, NullIfEmpty(query)))]));
        }

        // Asked here, on every refresh, rather than of the store: "is the folder there" is a
        // question about a MOMENT, and a stored answer would be stale before anyone read it.
        // It is also why this needs no Core change - nothing in this plan touches Core.
        State = !fileSystem.DirectoryExists(store.StorePath) ? ListState.FolderMissing
            : all.Count == 0 ? ListState.Empty
            : matched.Count == 0 ? ListState.NoResults
            : ListState.Loaded;

        foreach (var property in (string[])
        [
            nameof(TotalCount), nameof(MatchCount), nameof(HiddenCount), nameof(TotalBytes),
            nameof(MatchSummary), nameof(SearchFooter), nameof(ShowAllLabel),
            nameof(NoResultsTitle), nameof(NoResultsDetail),
        ])
        {
            Raise(property);
        }
    }

    private static string? NullIfEmpty(string value) => value.Length == 0 ? null : value;

    public void Dispose()
    {
        probing?.Cancel();
        probing?.Dispose();
        probing = null;
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/WaveLinkBackup.App.Tests --filter FullyQualifiedName~SnapshotListViewModelTests`
Expected: PASS, 21 tests.

- [ ] **Step 5: Commit**

```bash
git add src/WaveLinkBackup.App/ViewModels/SnapshotListViewModel.cs \
        tests/WaveLinkBackup.App.Tests/SnapshotListViewModelTests.cs
git commit -m "feat: the list - date groups, search, selection, four states

No results is its own state and not the empty one: 07 keeps the strip, the
header and the count on screen precisely so an empty result never reads as an
empty app.

The high-water mark that decides a generic slot comes from the whole store, not
the filtered view - hiding the full rig behind a search must not make a
collapsed row look whole."
```

---

### Task 9: `ShellViewModel` — the two strips, and what the buttons may do

**Files:**
- Create: `src/WaveLinkBackup.App/ViewModels/ShellViewModel.cs`
- Create: `tests/WaveLinkBackup.App.Tests/ShellViewModelTests.cs`
- Modify: `src/WaveLinkBackup.App/App.xaml.cs` — compose `WaveLinkProcess` and pass it in

**Interfaces:**
- Consumes: `SnapshotListViewModel`, `IWaveLinkProcess`, `IFileSystem`, `SettingsInspector`, `BackupSettings`, `BackupHost.AutoBackupEnabled`, `Readable`
- Produces:
  - `enum StripTone { Ok, Warn, Neutral }`
  - `sealed record ShellFacts(bool WaveLinkFound, bool WaveLinkRunning, DateTimeOffset? SettingsLastSavedLocal, bool AutoBackupEnabled, bool FolderMissing, string StorePath, long? FreeBytes)`
  - `sealed class ShellViewModel(SnapshotListViewModel list) : ObservableObject` with
    `SnapshotListViewModel List`, `bool IsHighContrast { get; set; }`, `string StatusStrip`, `StripTone StatusTone`,
    `string? SelectedLine`, `string SummaryLine`,
    `bool CanRename` · `bool CanDelete` · `bool CanRestore` · `bool CanBackUpNow`,
    `void Apply(ShellFacts facts)`

- [ ] **Step 1: Write the failing tests**

Create `tests/WaveLinkBackup.App.Tests/ShellViewModelTests.cs`:

```csharp
using WaveLinkBackup.App.Hosting;
using WaveLinkBackup.App.ViewModels;
using WaveLinkBackup.Core.Automation;

namespace WaveLinkBackup.App.Tests;

/// <summary>
/// The status strip and the bottom bar - the two places the app states a standing fact about
/// the machine, and the place it says what may be done to the selection.
/// </summary>
public sealed class ShellViewModelTests
{
    private static readonly DateTimeOffset SavedAt = new(2026, 8, 15, 23, 7, 0, TimeSpan.Zero);

    private static ShellViewModel Shell(
        bool waveLinkRunning = true,
        bool waveLinkFound = true,
        bool folderMissing = false,
        bool autoBackup = true,
        long? freeBytes = 126701535232,
        string storePath = @"C:\Users\t\AppData\Local\WaveLinkBackup")
    {
        // The harness the plan builds in Step 3; it wraps the five facts the strip reports so a
        // test does not have to stand up a store, a process and an inspector to assert a string.
        return ShellViewModelHarness.Build(
            waveLinkRunning, waveLinkFound, folderMissing, autoBackup, freeBytes, storePath, SavedAt);
    }

    // -- the status strip -------------------------------------------------------------------

    // README section Screen 1 item 2, verbatim.
    [Fact]
    public void The_strip_reports_wave_link_the_save_time_and_the_switch()
    {
        Assert.Equal(
            "WAVE LINK RUNNING · SETTINGS LAST SAVED 23:07 · AUTOMATIC BACKUP ON",
            Shell().StatusStrip);
    }

    [Fact]
    public void The_strip_is_green_when_everything_is_as_it_should_be()
    {
        Assert.Equal(StripTone.Ok, Shell().StatusTone);
    }

    [Fact]
    public void Wave_link_not_running_is_stated_rather_than_hidden()
    {
        Assert.StartsWith("WAVE LINK NOT RUNNING", Shell(waveLinkRunning: false).StatusStrip,
            StringComparison.Ordinal);
    }

    [Fact]
    public void The_switch_being_off_is_stated_too()
    {
        Assert.EndsWith("AUTOMATIC BACKUP OFF", Shell(autoBackup: false).StatusStrip,
            StringComparison.Ordinal);
    }

    // 06's status strip (1). The "Choose the settings file…" button beside it is error 1 and is
    // a later session; the sentence is not.
    [Fact]
    public void Wave_link_not_found_is_amber_and_says_so()
    {
        var shell = Shell(waveLinkFound: false);

        Assert.Equal("WAVE LINK NOT FOUND ON THIS COMPUTER", shell.StatusStrip);
        Assert.Equal(StripTone.Warn, shell.StatusTone);
    }

    // 08: "status strip ok dot: WAVE LINK RUNNING · 5 INPUTS · BACKUP FOLDER UNAVAILABLE", and
    // "Neutral, not amber: nothing is broken and nothing is lost - a location is missing."
    [Fact]
    public void A_missing_folder_replaces_the_last_segment_and_is_neutral()
    {
        var shell = Shell(folderMissing: true);

        Assert.EndsWith("BACKUP FOLDER UNAVAILABLE", shell.StatusStrip, StringComparison.Ordinal);
        Assert.DoesNotContain("AUTOMATIC BACKUP", shell.StatusStrip, StringComparison.Ordinal);
        Assert.Equal(StripTone.Neutral, shell.StatusTone);
    }

    // 10-decisions section 6: "Automatic backup while the folder is missing does nothing at all,
    // and the status strip says so. It must not fail silently every hour and it must not queue."
    [Fact]
    public void A_missing_folder_outranks_the_automatic_backup_switch_in_the_strip()
    {
        Assert.Equal(Shell(folderMissing: true).StatusStrip, Shell(folderMissing: true, autoBackup: false).StatusStrip);
    }

    // -- the bottom bar ---------------------------------------------------------------------

    // README: "4 BACKUPS · 12.4 MB IN %LOCALAPPDATA%\WaveLinkBackup · 118 GB FREE".
    [Fact]
    public void The_summary_counts_backups_names_the_folder_and_reports_free_space()
    {
        var shell = Shell();
        shell.List.Refresh();

        Assert.Matches(
            @"^\d+ BACKUPS · [\d.]+ [KMG]?B IN %LOCALAPPDATA%\\WaveLinkBackup · 118 GB FREE$",
            shell.SummaryLine);
    }

    // "Null rather than 0 or a throw ... omitting the figure is honest where printing 0 would
    // quietly claim a full disk." - IFileSystem.GetAvailableFreeBytes.
    [Fact]
    public void Unknown_free_space_is_omitted_rather_than_printed_as_zero()
    {
        var shell = Shell(freeBytes: null);
        shell.List.Refresh();

        Assert.DoesNotContain("FREE", shell.SummaryLine, StringComparison.Ordinal);
        Assert.DoesNotContain("0 B", shell.SummaryLine, StringComparison.Ordinal);
    }

    [Fact]
    public void A_store_outside_localappdata_is_printed_in_full()
    {
        var shell = Shell(storePath: @"\\NAS\streaming\WaveLinkBackup");
        shell.List.Refresh();

        Assert.Contains(@"\\NAS\streaming\WaveLinkBackup", shell.SummaryLine, StringComparison.Ordinal);
        Assert.DoesNotContain("%LOCALAPPDATA%", shell.SummaryLine, StringComparison.Ordinal);
    }

    [Fact]
    public void With_nothing_selected_there_is_no_selected_line()
    {
        var shell = Shell();
        shell.List.Refresh();

        Assert.Null(shell.SelectedLine);
    }

    // README: "SELECTED · BEFORE 3.3 BETA · 11 AUG 21:36".
    [Fact]
    public void The_selected_line_names_the_backup_and_when_it_was_taken()
    {
        var shell = Shell();
        shell.List.Refresh();
        shell.List.Selected = shell.List.Groups[0].Rows[0];

        Assert.StartsWith("SELECTED · ", shell.SelectedLine!, StringComparison.Ordinal);
        Assert.Equal(shell.SelectedLine, shell.SelectedLine!.ToUpperInvariant());
    }

    // 02's bottom bar for a damaged selection, line 2.
    [Fact]
    public void A_damaged_selection_says_restore_is_off_before_it_counts_anything()
    {
        var shell = Shell();
        shell.List.Refresh();

        var row = shell.List.Groups[0].Rows[0];
        row.ApplyVerdict(new HealthVerdict(SnapshotHealth.Damaged, 1, 1, SavedAt));
        shell.List.Selected = row;

        Assert.StartsWith("DAMAGED — RESTORE IS OFF FOR THIS ONE · ", shell.SummaryLine,
            StringComparison.Ordinal);
    }

    // -- what the buttons may do ------------------------------------------------------------

    [Fact]
    public void With_no_selection_only_back_up_now_is_live()
    {
        var shell = Shell();
        shell.List.Refresh();

        Assert.False(shell.CanRename);
        Assert.False(shell.CanDelete);
        Assert.False(shell.CanRestore);
        Assert.True(shell.CanBackUpNow);
    }

    [Fact]
    public void A_healthy_selection_lights_all_four()
    {
        var shell = Shell();
        shell.List.Refresh();
        shell.List.Selected = shell.List.Groups[0].Rows[0];

        Assert.True(shell.CanRename);
        Assert.True(shell.CanDelete);
        Assert.True(shell.CanRestore);
        Assert.True(shell.CanBackUpNow);
    }

    [Fact]
    public void A_damaged_selection_leaves_only_delete_and_back_up_now()
    {
        var shell = Shell();
        shell.List.Refresh();

        var row = shell.List.Groups[0].Rows[0];
        row.ApplyVerdict(new HealthVerdict(SnapshotHealth.Damaged, 1, 1, SavedAt));
        shell.List.Selected = row;

        Assert.False(shell.CanRename);
        Assert.False(shell.CanRestore);
        Assert.True(shell.CanDelete);
        Assert.True(shell.CanBackUpNow);
    }

    // 08: "all four action buttons at 40% opacity, INCLUDING Back up now" - there is nowhere to
    // put a backup.
    [Fact]
    public void A_missing_folder_turns_every_action_off_including_back_up_now()
    {
        var shell = Shell(folderMissing: true);
        shell.List.Refresh();

        Assert.False(shell.CanBackUpNow);
        Assert.False(shell.CanDelete);
    }

    // -- high contrast ----------------------------------------------------------------------

    // Design section C: structural differences are "template switches driven by a flag on the
    // shell view model". This is that flag, and it is the only place high contrast lives outside
    // the theme dictionaries.
    [Fact]
    public void High_contrast_is_a_flag_the_templates_can_switch_on()
    {
        var shell = Shell();

        Assert.False(shell.IsHighContrast);

        shell.IsHighContrast = true;

        Assert.True(shell.IsHighContrast);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/WaveLinkBackup.App.Tests --filter FullyQualifiedName~ShellViewModelTests`
Expected: FAIL — neither `ShellViewModel` nor the harness exists.

- [ ] **Step 3: Write the test harness**

Create `tests/WaveLinkBackup.App.Tests/ShellViewModelHarness.cs`. It stands up a `FakeFileSystem`
with a store, a `FakeWaveLinkProcess`, and a `SettingsInspector` over a live settings file, then
returns a `ShellViewModel`. Model it on `SnapshotListViewModelTests.Rig` — same
`FakeFileSystem` + `FakeClock` + `SnapshotStore` construction, plus:

- `waveLinkFound: false` → do not add the live `Settings.json` to the fake, so `Inspect` fails
- `folderMissing: true` → point the store at `E:\gone` and never create it
- `freeBytes` → `FakeFileSystem.FreeBytes`
- the saved-at time → `FakeFileSystem`'s last-write time for the live settings path
- three backups written into the store, so `SummaryLine` has something to count

- [ ] **Step 4: Write `ShellViewModel`**

Create `src/WaveLinkBackup.App/ViewModels/ShellViewModel.cs`:

```csharp
using System.Globalization;
using WaveLinkBackup.Core.Abstractions;

namespace WaveLinkBackup.App.ViewModels;

/// <summary>
/// How loud the status strip is. 06's weight rule: "Neutral if nothing happened. Amber only if
/// the configuration - live or restorable - is not whole."
/// </summary>
public enum StripTone
{
    /// <summary>Green dot. Everything is as it should be.</summary>
    Ok,

    /// <summary>Amber dot. Wave Link cannot be read, so the live configuration is not whole.</summary>
    Warn,

    /// <summary>Muted dot. A location is missing; nothing is broken and nothing is lost (08).</summary>
    Neutral,
}

/// <param name="WaveLinkFound">False means no settings file in any of the usual places.</param>
/// <param name="SettingsLastSavedLocal">Null when the file could not be read at all.</param>
public sealed record ShellFacts(
    bool WaveLinkFound,
    bool WaveLinkRunning,
    DateTimeOffset? SettingsLastSavedLocal,
    bool AutoBackupEnabled,
    bool FolderMissing,
    string StorePath,
    long? FreeBytes);

/// <summary>
/// The status strip, the bottom bar, and what the four action buttons may do.
///
/// Nothing here reaches for the store or the process directly: the window hands it a
/// <see cref="ShellFacts"/> on every refresh, which is what lets every one of these strings be
/// asserted from a table.
/// </summary>
public sealed class ShellViewModel(SnapshotListViewModel list) : ObservableObject
{
    private ShellFacts facts = new(true, false, null, true, false, string.Empty, null);
    private bool isHighContrast;

    public SnapshotListViewModel List { get; } = list;

    /// <summary>
    /// The flag every structural high-contrast difference switches on: the 3px left edge, the
    /// verdict word in place of the meta line, disabled as GrayText at full opacity rather than
    /// 40%. Design section C keeps these as TEMPLATE switches rather than a fourth palette.
    /// </summary>
    public bool IsHighContrast
    {
        get => isHighContrast;
        set => Set(ref isHighContrast, value);
    }

    /// <summary>
    /// README: "WAVE LINK RUNNING · SETTINGS LAST SAVED 23:07 · AUTOMATIC BACKUP ON".
    ///
    /// A missing folder REPLACES the third segment rather than joining it: 10-decisions section
    /// 6 says the automatic backup does nothing at all while the folder is gone, so printing
    /// "AUTOMATIC BACKUP ON" beside it would be the exact silent lie that rule forbids.
    /// </summary>
    public string StatusStrip
    {
        get
        {
            // 06's status strip (1). Everything else on the strip is a fact about a
            // configuration we could not read, so there is nothing else to say.
            if (!facts.WaveLinkFound) return "WAVE LINK NOT FOUND ON THIS COMPUTER";

            var running = facts.WaveLinkRunning ? "WAVE LINK RUNNING" : "WAVE LINK NOT RUNNING";

            var saved = facts.SettingsLastSavedLocal is { } at
                ? $"SETTINGS LAST SAVED {Readable.TimeOfDay(at)}"
                : "SETTINGS NEVER SAVED";

            var last = facts.FolderMissing
                ? "BACKUP FOLDER UNAVAILABLE"
                : $"AUTOMATIC BACKUP {(facts.AutoBackupEnabled ? "ON" : "OFF")}";

            return $"{running} · {saved} · {last}";
        }
    }

    public StripTone StatusTone =>
        !facts.WaveLinkFound ? StripTone.Warn
        : facts.FolderMissing ? StripTone.Neutral
        : StripTone.Ok;

    /// <summary>README: "SELECTED · BEFORE 3.3 BETA · 11 AUG 21:36". Absent with no selection.</summary>
    public string? SelectedLine => List.Selected is not { } row
        ? null
        : $"SELECTED · {row.Name.ToUpper(CultureInfo.CurrentCulture)} · "
        + $"{row.TakenDate} {row.TakenTime}";

    /// <summary>
    /// README: "4 BACKUPS · 12.4 MB IN %LOCALAPPDATA%\WaveLinkBackup · 118 GB FREE", and 02's
    /// damaged variant, which leads with the refusal because that is what the user needs first.
    /// </summary>
    public string SummaryLine
    {
        get
        {
            var count = List.TotalCount;
            var backups = $"{count} BACKUP{(count == 1 ? "" : "S")}";
            var size = $"{Readable.Bytes(List.TotalBytes)} IN {ShortStorePath}";

            // Omitted, never zero: "0 GB free" is a claim about the disk that we did not make.
            var free = facts.FreeBytes is { } bytes ? $" · {Readable.Bytes(bytes)} FREE" : string.Empty;

            var summary = $"{backups} · {size}{free}";

            return List.Selected?.IsDamaged == true
                ? $"DAMAGED — RESTORE IS OFF FOR THIS ONE · {summary}"
                : summary;
        }
    }

    /// <summary>
    /// %LOCALAPPDATA% back where it came from, exactly as README prints it. A literal
    /// C:\Users\<name>\AppData\Local is longer, less recognisable, and puts the user's name on
    /// screen for no reason (technical-debt section 6).
    /// </summary>
    private string ShortStorePath
    {
        get
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

            return localAppData.Length > 0
                && facts.StorePath.StartsWith(localAppData, StringComparison.OrdinalIgnoreCase)
                    ? "%LOCALAPPDATA%" + facts.StorePath[localAppData.Length..]
                    : facts.StorePath;
        }
    }

    public bool CanRename => !facts.FolderMissing && List.Selected?.CanRename == true;

    public bool CanDelete => !facts.FolderMissing && List.Selected?.CanDelete == true;

    public bool CanRestore => !facts.FolderMissing && List.Selected?.CanRestore == true;

    /// <summary>
    /// Always live EXCEPT when the folder is gone - 08 puts all four buttons at 40% there,
    /// "including Back up now", because there is nowhere to put a backup.
    /// </summary>
    public bool CanBackUpNow => !facts.FolderMissing;

    /// <summary>Called by the window on load, on F5, after a capture, and on every host tick.</summary>
    public void Apply(ShellFacts facts)
    {
        this.facts = facts;

        foreach (var property in (string[])
        [
            nameof(StatusStrip), nameof(StatusTone), nameof(SelectedLine), nameof(SummaryLine),
            nameof(CanRename), nameof(CanDelete), nameof(CanRestore), nameof(CanBackUpNow),
        ])
        {
            Raise(property);
        }
    }
}
```

`ShellViewModel` must also re-raise the selection-dependent properties when `List.Selected`
changes — subscribe to `List.PropertyChanged` in the constructor and call `Apply(facts)` when
`e.PropertyName` is `nameof(SnapshotListViewModel.Selected)`. Do the same for the selected row's
own `PropertyChanged`, so a row flipping to DAMAGED while selected turns Restore off without
waiting for the next tick.

- [ ] **Step 5: Compose the Wave Link process in `App`**

`App.Compose` builds an inspector, a store, a service, a watcher and a coordinator, but no
`IWaveLinkProcess` — the status strip is the first thing that needs one. Add
`new WaveLinkProcess()` to `Compose`'s return tuple and hold it on `App`. Open
`src/WaveLinkBackup.Core/Process/WaveLinkProcess.cs` for its constructor; the CLI already builds
one in `Program.cs` and that is the call to copy.

The saved-at time is `fileSystem.GetLastWriteTimeUtc(inspection.Location.SettingsPath)`, and
`WaveLinkFound` is `inspector.Inspect(settings.ChosenWaveLinkPath).IsSuccess`.

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test tests/WaveLinkBackup.App.Tests --filter FullyQualifiedName~ShellViewModelTests`
Expected: PASS, 18 tests.

- [ ] **Step 7: Commit**

```bash
git add src/WaveLinkBackup.App/ViewModels/ShellViewModel.cs \
        src/WaveLinkBackup.App/App.xaml.cs \
        tests/WaveLinkBackup.App.Tests/ShellViewModelTests.cs \
        tests/WaveLinkBackup.App.Tests/ShellViewModelHarness.cs
git commit -m "feat: the status strip and the bottom bar, as facts rather than lookups

A missing folder replaces the automatic-backup segment instead of joining it:
the backup does nothing at all while the folder is gone, so printing AUTOMATIC
BACKUP ON beside BACKUP FOLDER UNAVAILABLE would be the silent failure
10-decisions section 6 forbids.

Free space is omitted when unknown rather than printed as zero, which is what
GetAvailableFreeBytes returns null for."
```

---

### Task 10: The templates — the strip, the header, the row, and the two empty bodies

Every decision is already made in a view model; this task decides only how each one *reads*.

**Files:**
- Create: `src/WaveLinkBackup.App/Views/RowStyles.xaml`
- Modify: `src/WaveLinkBackup.App/Views/ControlStyles.xaml`
- Modify: `src/WaveLinkBackup.App/Views/MainWindow.xaml` · `MainWindow.xaml.cs`
- Modify: `src/WaveLinkBackup.App/App.xaml` · `App.xaml.cs`
- Create: `tests/WaveLinkBackup.App.Tests/RowTemplateTests.cs`

**The grid, which is not negotiable.** README §Screen 1 and `11` both pin it, and `11` spells
out why it survives high contrast unchanged:

```
grid-template-columns: minmax(200px,1fr) 120px 124px 300px 200px 40px    gap: 20px
NAME · TAKEN · WHY · INPUTS · CONTENTS · overflow
```

In WPF that is a shared-size `Grid` — the column header and every row must line up, so both use
`Grid.IsSharedSizeScope` on the list container with matching `SharedSizeGroup` names. The `20px`
gap is `Margin`, not a seventh column.

- [ ] **Step 1: Write the failing tests**

Create `tests/WaveLinkBackup.App.Tests/RowTemplateTests.cs`.

**These are source-text assertions, not template walks, and that is deliberate.** Walking a
`ControlTemplate` for a `BorderThickness` means resolving a `Style`, instantiating a container,
applying it and hunting a named part — a great deal of machinery to assert something that is one
attribute in one file. The failure mode being guarded against is *someone editing this XAML*, and
reading the XAML catches it directly. `ThemeTests` already reads `.xaml` from the `AppSourceRoot`
assembly metadata; this reuses that.

```csharp
using System.Reflection;
using System.Text.RegularExpressions;

namespace WaveLinkBackup.App.Tests;

/// <summary>
/// The design encoded health in SHAPE - solid rule = present, dashed = missing, dotted =
/// unknowable - precisely so that high contrast works without inventing anything. If the rules
/// stop being 2px solid / 2px solid / 2px dashed / 2px dotted, that argument quietly stops being
/// true and 11-high-contrast becomes a claim nobody is keeping.
/// </summary>
public sealed class RowTemplateTests
{
    private static readonly string SourceRoot = Assembly.GetExecutingAssembly()
        .GetCustomAttributes<AssemblyMetadataAttribute>()
        .Single(a => a.Key == "AppSourceRoot").Value!;

    private static string RowStyles() =>
        File.ReadAllText(Path.Combine(SourceRoot, "Views", "RowStyles.xaml"));

    private static string Style(string key)
    {
        var match = Regex.Match(
            RowStyles(),
            $"<(?:Style|DataTemplate)[^>]*x:Key=\"{Regex.Escape(key)}\".*?</(?:Style|DataTemplate)>",
            RegexOptions.Singleline);

        Assert.True(match.Success, $"{key} is gone or has been renamed.");

        return match.Value;
    }

    [Theory]
    [InlineData("WlSlotNamed", "WlOk")]
    [InlineData("WlSlotGeneric", "WlWarn")]
    public void A_present_slot_has_a_2px_solid_bottom_rule(string key, string brush)
    {
        var style = Style(key);

        Assert.Contains("BorderThickness=\"0,0,0,2\"", style, StringComparison.Ordinal);
        Assert.Contains(brush, style, StringComparison.Ordinal);
    }

    [Fact]
    public void A_missing_slot_has_a_dashed_rule_and_an_em_dash()
    {
        var style = Style("WlSlotMissing");

        Assert.Contains("BorderThickness=\"0,0,0,2\"", style, StringComparison.Ordinal);
        Assert.Contains("StrokeDashArray", style, StringComparison.Ordinal);
        Assert.Contains("WlLine2", style, StringComparison.Ordinal);
    }

    // "Deliberately breaking the five-slot pattern is the signal: the row stops being data." - 02
    [Fact]
    public void The_damaged_inputs_cell_is_one_dotted_full_width_cell()
    {
        var template = Style("WlContentsUnknown");

        Assert.Contains("CONTENTS UNKNOWN", template, StringComparison.Ordinal);
        Assert.Contains("StrokeDashArray", template, StringComparison.Ordinal);
    }

    // THE trap this whole plan is built around. README still specifies this pill in
    // --wl-accent-soft / --wl-accent: a red pill inside an amber row, which is both the second
    // red the rules forbid and a health state dressed up as an action.
    [Fact]
    public void The_suspect_pill_is_amber_and_never_mentions_the_accent()
    {
        var style = Style("WlSuspectPill");

        Assert.Contains("WlWarn", style, StringComparison.Ordinal);
        Assert.DoesNotContain("WlAccent", style, StringComparison.Ordinal);
    }

    [Fact]
    public void The_damaged_pill_is_neutral_and_takes_no_colour_at_all()
    {
        var style = Style("WlDamagedPill");

        Assert.Contains("WlLine2", style, StringComparison.Ordinal);
        Assert.DoesNotContain("WlWarn", style, StringComparison.Ordinal);
        Assert.DoesNotContain("WlAccent", style, StringComparison.Ordinal);
    }

    // 10-decisions section 5: "No element in this app has a 2px border on all four sides." The
    // health slots use a 2px bottom RULE, which is a different thing. The focus ring is the one
    // legitimate 2px rectangle and it lives in ControlStyles.xaml, not here.
    [Fact]
    public void Nothing_in_the_row_has_a_2px_border_on_all_four_sides()
    {
        var offenders = Regex.Matches(RowStyles(), "BorderThickness=\"([^\"]+)\"")
            .Select(m => m.Groups[1].Value)
            .Where(v => v is "2" || v.Split(',').Distinct().SequenceEqual(["2"]))
            .ToArray();

        Assert.True(offenders.Length == 0,
            $"2px on all four sides is a rule the design does not use: {string.Join(", ", offenders)}");
    }

    // Every brush in the row must be one of the 22 theme keys. The colour-literal guard catches
    // a #RRGGBB; this catches a slot bound to WlLine instead of WlOk, which is a perfectly legal
    // colour and completely wrong.
    [Fact]
    public void Every_brush_the_row_uses_is_a_theme_key()
    {
        var known = Theming.ThemeManager.BrushKeys.ToHashSet(StringComparer.Ordinal);

        var used = Regex.Matches(RowStyles(), @"(?:Dynamic|Static)Resource\s+(Wl[A-Za-z]+)")
            .Select(m => m.Groups[1].Value)
            .Where(name => name.StartsWith("Wl", StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal);

        var unknown = used
            .Where(name => !known.Contains(name))
            // Type styles, geometries and the row's own templates are keys too, and are not brushes.
            .Where(name => !name.EndsWith("Text", StringComparison.Ordinal)
                        && !name.EndsWith("Geometry", StringComparison.Ordinal)
                        && !name.EndsWith("Font", StringComparison.Ordinal)
                        && !name.EndsWith("Pill", StringComparison.Ordinal)
                        && !name.StartsWith("WlSlot", StringComparison.Ordinal))
            .ToArray();

        Assert.True(unknown.Length == 0,
            $"Not theme brushes: {string.Join(", ", unknown)}");
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/WaveLinkBackup.App.Tests --filter FullyQualifiedName~RowTemplateTests`
Expected: FAIL — `Views/RowStyles.xaml` does not exist.

- [ ] **Step 3: Write the row template**

Create `src/WaveLinkBackup.App/Views/RowStyles.xaml`. **The resource keys are fixed**, because
Task 12's guards look them up by name:

| Key | What |
|---|---|
| `WlRowTemplate` | The `ListBoxItem` template — the six-column grid and every state trigger |
| `WlSlotTemplate` | One cell of the strip; picks between the three styles below on `SlotKind` |
| `WlSlotNamed` · `WlSlotGeneric` · `WlSlotMissing` | The three slot treatments |
| `WlSlotStrip` | The five-cell `ItemsControl`, for the INPUTS `ContentControl` |
| `WlContentsUnknown` | The damaged row's single dotted cell |
| `WlSuspectPill` · `WlDamagedPill` · `WlWhyPill` | The three pills |
| `WlTierPresent` · `WlTierAbsent` | The two tier-badge treatments |

The measurements, from README §Screen 1 and `02`:

| Element | Normal themes | High contrast (`11`) |
|---|---|---|
| Row | padding `15,20`, 3px left edge, `align-items: center` | same padding, **no left edge** |
| Rest | transparent background, transparent 3px edge | transparent |
| Hover | `WlHover` background | 1px `HotTrack` outline, no fill |
| Selected | `WlCard` background, 3px `WlAccent` edge | full `Highlight` fill, `HighlightText` throughout |
| Suspect | `WlWarnSoft` background, 3px `WlWarn` edge — **survives selection**, because health outranks selection | no tint; the verdict word does the work |
| Damaged | `WlSunken` background, 3px `WlLine2` edge | no tint |
| Name | `WlRowNameText`, `WlStrong` — `WlMuted` when damaged | inherits |
| Search match | `WlAccentSoft` background, 2px radius, `0,2` padding, name stays `WlStrong` | `Highlight` / `HighlightText` |
| SUSPECT pill | **transparent fill, 1px `WlWarn`, 999px radius, `3,8` padding, `WlTierBadgeText` + `Tracking .14`, `WlWarn` text, 10px warning triangle** | transparent, 1px `WindowText` |
| DAMAGED pill | transparent fill, 1px `WlLine2`, `WlMuted` text, 10px circle-slash | transparent, 1px `WindowText` |
| Meta line | `WlMonoMetaText`, `WlMuted` (80% when damaged) | bound to `VerdictLine` instead, with a 12px glyph |
| TAKEN | time `WlMonoReadoutText` 13px `WlText` over date 11px `WlMuted`, 3px apart; both `WlMuted` when damaged (date at 70%) | inherits |
| WHY pill | `WlCard` fill, 1px `WlLine`, 999px radius, `5,9`, `WlTierBadgeText` 10.5px + `Tracking .14`; `WlText` when `WhyIsPrimary`, else `WlMuted` | transparent, 1px `WindowText`, no wrap |
| Slot — Named | `WlOkSoft` fill, **2px bottom rule** `WlOk`, `WlSlotLabelText` + `Tracking .06`, `WlOk` | transparent, 2px `WindowText` rule |
| Slot — Generic | transparent fill, 2px bottom rule `WlWarn`, `WlWarn` label | transparent, 2px `WindowText` rule |
| Slot — Missing | transparent, **2px dashed** `WlLine2`, `—`, `WlMuted` at 45% | 1px dashed `WindowText`, **full opacity** |
| Damaged INPUTS | ONE full-width cell, transparent, **2px dotted** `WlLine2`, `WlSlotLabelText` + `Tracking .10`, `WlMuted` at 75%, centred, `CONTENTS UNKNOWN` | 2px dotted `WindowText` |
| Tier badge — present | `WlCard` fill, 1px `WlLine`, 4px radius, `4,8`, `WlText` | transparent, 1px `WindowText` |
| Tier badge — absent | transparent, **dashed** 1px `WlLine`, `WlMuted` at 50% | 1px dashed `WindowText`, full opacity |
| Overflow `···` | `WlMuted`, `WlStrong` on the selected row, `justify-self: center` | `WindowText` |

**The INPUTS cell is a template-switched `ContentControl`, not an `ItemsControl` in an odd
state.** Design §C: the damaged row's single dotted cell is *"the one deliberate break"*, and
the break is the signal — the row stops being data. A style trigger on `IsDamaged` swaps
`ContentTemplate` between `WlSlotStrip` and `WlContentsUnknown`.

```xml
<!--
  The damaged row's single dotted cell is the one deliberate break in the five-slot pattern,
  and the break IS the signal: the row stops being data. So it is a template switch rather
  than an ItemsControl rendering five copies of nothing, which would say the opposite.
-->
<ContentControl Grid.Column="3" Content="{Binding}">
    <ContentControl.Style>
        <Style TargetType="ContentControl">
            <Setter Property="ContentTemplate" Value="{StaticResource WlSlotStrip}" />
            <Style.Triggers>
                <DataTrigger Binding="{Binding IsDamaged}" Value="True">
                    <Setter Property="ContentTemplate" Value="{StaticResource WlContentsUnknown}" />
                </DataTrigger>
            </Style.Triggers>
        </Style>
    </ContentControl.Style>
</ContentControl>
```

Every high-contrast difference is a `DataTrigger` on the shell's `IsHighContrast`, reached with
`{Binding DataContext.IsHighContrast, RelativeSource={RelativeSource AncestorType=Window}}`.
Colours come from the theme dictionary either way — `HighContrast.xaml` already maps all 21
keys onto `SystemColors.*Brush`, so **only the structural differences** (the edge, the verdict
line, the disabled treatment) need a trigger. Do not add a second set of colour triggers.

- [ ] **Step 4: Build the rest of the window**

In `MainWindow.xaml`, fill rows 1–4:

**Status strip** (row 1) — `10,20` padding, `WlChrome`, hairline bottom, space-between. Left: a
7px dot bound to `StatusTone` (`WlOk` / `WlWarn` / `WlMuted`) plus a `TrackedText`
`Tracking=".14"` on `StatusStrip`, or on `List.MatchSummary` when a search is active. Right: the
216px search field, then a 36 × 36 ghost gear button.

**Search field** — `WlBg`, hairline, 8px radius, `8,11` padding, 14px search icon, placeholder
`Search backups` at `WlMuted`, border steps to `WlLine2` when active, and an 18px round clear
button (`WlLine` fill, 8px `×` glyph in `WlText`) once there is a query.

**Column header** (row 2) — `11,20,9,20` padding, hairline bottom, the same shared-size grid,
`TrackedText` `Tracking=".18"` on `WlColumnHeaderText`: `NAME` `TAKEN` `WHY` `INPUTS` `CONTENTS`
and an empty 40px cell. **It stays during a search** (`07`) and goes with the list when the
folder is missing (`08`).

**The list** (row 3) — a `ListBox` over `Groups` with `Grid.IsSharedSizeScope="True"`, its
`ItemsPanel` a `VirtualizingStackPanel`, group headers in `WlColumnHeaderText` +
`Tracking=".18"` at `14,20,20,7` with **no separator line**, and an `ItemContainerStyle` whose
template is the row above. `SelectedItem` two-way to `List.Selected`. `ScrollViewer.CanContentScroll`
stays true so virtualization survives; the list is the only vertically flexible region.

**Selected-row expansion** — a second `Grid.Row` inside the same `WlCard` block, visible only
when `IsSelected`, padding `0,0,20,16`: `DetailLine` in `WlMonoMetaText` `WlMuted`, a 1px
hairline filling the middle, then `DetailFileName` right-aligned at 75%. A damaged selected row
shows `DamagedSentence` in `WlBodyText` (max 660px) above `DamagedDetail`. One row expands at a
time, and the actions stay in the bottom bar rather than moving into the row.

**Bottom bar** (row 4) — `12,20`, `WlChrome`, hairline top, space-between. Left, stacked 4px
apart: `SelectedLine` (`TrackedText` `.14`, `WlText`) over `SummaryLine` (mono 400 11px,
`WlMuted`). Right, 7px apart: **Rename** (ghost + pencil), **Delete** (ghost + trash),
**Restore this backup** (secondary, hairline-24% border, rotate-ccw), **Back up now** (primary
`WlAccent`, download-to-tray). Each `IsEnabled` bound to its `Can*`.

**The two empty bodies** (row 3, swapped by `ListState`):

- `NoResults` — centred, `52` top / `56` bottom padding: `NoResultsTitle` in `WlBodyText` 15px
  `WlStrong`, `NoResultsDetail` in `WlMonoMetaText` `WlMuted`, and a secondary
  `Clear the search` 6px below. **No measure-rule frame** — `07` is explicit that the frame
  belongs to first run, and reusing it would make an empty result look like an empty app.
- `Empty` and `FolderMissing` — a **stand-in**, not the designed screen. Both of those are later
  sessions (screen 4 and error 12). Render the column header, a centred `WlBodyText` line
  (`No backups yet.` / `The backup folder can't be used.`) and nothing else. Flagged in
  *What this plan does not do*.

- [ ] **Step 5: Wire the window to the view models**

In `MainWindow.xaml.cs`, take a `ShellViewModel` in the constructor, set it as `DataContext`,
and:

- on `Loaded`, `await shell.List.RefreshAsync()`
- set `shell.List.Marshal = action => Dispatcher.Invoke(action);` **before** the first refresh —
  without it a verdict lands on the probe's thread and the first `PropertyChanged` throws
- subscribe to `ISystemTheme.Changed` and set `shell.IsHighContrast`, so the structural
  switches follow the OS without a restart (`11` requires this at runtime, not on restart)
- wire the four buttons: Back up now calls the existing `App.BackUpNow` then
  `RefreshAsync` and selects the new row by id; Rename, Delete and Restore each open a
  `MessageBox` naming the session that builds them, the same answer plan 3 gave Settings
- wire the gear button to the existing `App.OpenSettings` placeholder

In `App.xaml.cs`, build the `HealthProbe`, the `SnapshotListViewModel` and the `ShellViewModel`
in `Compose`, hold them, and refresh the shell's facts on the same 15-second tick that already
calls `RefreshTray`.

- [ ] **Step 6: Run everything**

Run: `dotnet test WaveLinkBackup.slnx`
Then: `dotnet build WaveLinkBackup.slnx -c Release`
Expected: PASS, Release zero warnings. **If the colour-literal guard fails, the fix is a theme
key, never an exclusion.**

- [ ] **Step 7: Commit**

```bash
git add src/WaveLinkBackup.App tests/WaveLinkBackup.App.Tests
git commit -m "feat: screen 1 - the strip, the header, the row and its three health states

The suspect pill is AMBER. README still specifies it in accent-soft/accent -
a red pill inside an amber row, the second red the rules forbid - and
10-decisions section 1 overturned that. Built to the correction.

The damaged row's single dotted cell is a template switch rather than an
ItemsControl in an odd state, because breaking the five-slot pattern IS the
signal that the row has stopped being data."
```

---

### Task 11: Keyboard, focus and screen readers

Design §C: *"Retrofitting it across a list and a nine-section dialog costs more than doing it
inline."* `10-decisions.md` §6 pins four of these; §7.4 adds the Windows conventions.

**Files:**
- Create: `src/WaveLinkBackup.App/ViewModels/ShellCommands.cs`
- Modify: `src/WaveLinkBackup.App/Views/MainWindow.xaml` · `MainWindow.xaml.cs`
- Modify: `src/WaveLinkBackup.App/Views/ControlStyles.xaml` — the focus ring
- Create: `tests/WaveLinkBackup.App.Tests/ShellCommandTests.cs`

**Interfaces:**
- Produces: `static class ShellCommands` holding `RoutedUICommand Refresh` (F5), `Search`
  (Ctrl+F), `ClearSearch` (Escape), `BackUpNow` (Ctrl+B), `Rename` (F2), `Delete` (Delete),
  `Restore` (Enter)

**The map:**

| Key | Does | Source |
|---|---|---|
| `Escape` | Clears the search when the list has focus; cancels a dialog otherwise | `10-decisions` §6 |
| `Enter` | Fires the primary — **except Delete and Restore, where focus starts on Cancel and the destructive button must be reached deliberately** | `10-decisions` §6 |
| `F5` | Re-reads the backup folder | `10-decisions` §6 |
| `Ctrl+F` | Focuses the search field | §7.4 |
| `Delete` | Deletes the selection (a confirmation is a later session) | §7.4 |
| `F2` | Renames in place | README §Interactions |
| `↑` / `↓` | Move selection | README §Interactions |
| `Home` / `End` | First / last row | §7.4 |
| `Space` | Activates the focused button | §7.4 |
| `Shift+F10` | Context menu | §7.4 |
| Focus ring | **2px, 2px offset, always visible, including on the list rows** | `10-decisions` §6 |

- [ ] **Step 1: Write the failing tests**

Create `tests/WaveLinkBackup.App.Tests/ShellCommandTests.cs`. Gestures are data, so they are
assertable without a window:

```csharp
using System.Windows.Input;
using WaveLinkBackup.App.ViewModels;

namespace WaveLinkBackup.App.Tests;

/// <summary>
/// 10-decisions section 6 pins four of these and 7.4 adds the rest. A key map is exactly the
/// kind of thing that drifts silently, so it is asserted rather than trusted.
/// </summary>
public sealed class ShellCommandTests
{
    private static KeyGesture Gesture(RoutedUICommand command) =>
        (KeyGesture)command.InputGestures[0]!;

    [Fact]
    public void F5_re_reads_the_backup_folder()
    {
        Assert.Equal(Key.F5, Gesture(ShellCommands.Refresh).Key);
        Assert.Equal(ModifierKeys.None, Gesture(ShellCommands.Refresh).Modifiers);
    }

    [Fact]
    public void Escape_clears_the_search()
    {
        Assert.Equal(Key.Escape, Gesture(ShellCommands.ClearSearch).Key);
    }

    [Fact]
    public void Ctrl_f_reaches_the_search_field()
    {
        Assert.Equal(Key.F, Gesture(ShellCommands.Search).Key);
        Assert.Equal(ModifierKeys.Control, Gesture(ShellCommands.Search).Modifiers);
    }

    [Fact]
    public void Delete_deletes_and_f2_renames()
    {
        Assert.Equal(Key.Delete, Gesture(ShellCommands.Delete).Key);
        Assert.Equal(Key.F2, Gesture(ShellCommands.Rename).Key);
    }

    // Enter fires the primary, and on screen 1 the primary is Restore. In the RESTORE DIALOG it
    // must not - focus starts on Cancel there and the destructive button is reached
    // deliberately - but that dialog is a later session, and this is the list.
    [Fact]
    public void Enter_restores_from_the_list()
    {
        Assert.Equal(Key.Enter, Gesture(ShellCommands.Restore).Key);
    }

    [Fact]
    public void Every_command_has_a_name_a_screen_reader_can_announce()
    {
        foreach (var command in ShellCommands.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(command.Text), command.Name);
        }
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/WaveLinkBackup.App.Tests --filter FullyQualifiedName~ShellCommandTests`
Expected: FAIL — `ShellCommands` does not exist.

- [ ] **Step 3: Write `ShellCommands`**

```csharp
using System.Windows.Input;

namespace WaveLinkBackup.App.ViewModels;

/// <summary>
/// Screen 1's key map, as data.
///
/// RoutedUICommand rather than an ICommand per action: the Text property is what a screen
/// reader announces and what a context menu would show, and it comes free. 7.4 is explicit that
/// reader labels are part of this work rather than a follow-up.
/// </summary>
public static class ShellCommands
{
    public static RoutedUICommand Refresh { get; } =
        New("Re-read the backup folder", nameof(Refresh), Key.F5, ModifierKeys.None);

    public static RoutedUICommand Search { get; } =
        New("Search backups", nameof(Search), Key.F, ModifierKeys.Control);

    public static RoutedUICommand ClearSearch { get; } =
        New("Clear the search", nameof(ClearSearch), Key.Escape, ModifierKeys.None);

    public static RoutedUICommand BackUpNow { get; } =
        New("Back up now", nameof(BackUpNow), Key.B, ModifierKeys.Control);

    public static RoutedUICommand Rename { get; } =
        New("Rename this backup", nameof(Rename), Key.F2, ModifierKeys.None);

    public static RoutedUICommand Delete { get; } =
        New("Delete this backup", nameof(Delete), Key.Delete, ModifierKeys.None);

    public static RoutedUICommand Restore { get; } =
        New("Restore this backup", nameof(Restore), Key.Enter, ModifierKeys.None);

    public static IReadOnlyList<RoutedUICommand> All { get; } =
        [Refresh, Search, ClearSearch, BackUpNow, Rename, Delete, Restore];

    private static RoutedUICommand New(string text, string name, Key key, ModifierKeys modifiers) =>
        new(text, name, typeof(ShellCommands), [new KeyGesture(key, modifiers)]);
}
```

- [ ] **Step 4: Bind them, and give the focus ring its 2px**

In `MainWindow.xaml`, add a `Window.CommandBindings` entry per command. Two behaviours need
care:

- **`Escape` only clears the search when the list has focus** (`10-decisions` §6). Bind it on
  the list and the search field, not on the window, or it will swallow Escape in the dialogs a
  later session adds.
- **`Enter` restores only when a row is selected and `CanRestore`.** `CanExecute` handles it;
  do not guard it in the handler and leave the button looking live.

In `ControlStyles.xaml`, one shared `FocusVisualStyle`:

```xml
<!--
  2px, 2px offset, ALWAYS VISIBLE - including on the list rows, which is the one WPF makes you
  ask for, since a ListBoxItem's default focus visual is a dotted 1px rectangle that vanishes
  on a filled row. In high contrast it is WindowText, and HighlightText on a Highlight-filled
  row so it never disappears into the fill (11).
-->
<Style x:Key="WlFocusVisual">
    <Setter Property="Control.Template">
        <Setter.Value>
            <ControlTemplate>
                <Rectangle Margin="-2" StrokeThickness="2" SnapsToDevicePixels="True"
                           Stroke="{DynamicResource WlAccent}" />
            </ControlTemplate>
        </Setter.Value>
    </Setter>
</Style>
```

> This is the **one** place a 2px stroke on all four sides is correct, and it does not violate
> `10-decisions` §5: that rule is about borders on elements, and a focus ring is neither a
> border nor part of the element it surrounds.

- [ ] **Step 5: Automation names on everything that is not a sentence**

- The list: `AutomationProperties.Name="Backups"`, and each row's
  `AutomationProperties.Name="{Binding AutomationName}"`
- The five-slot strip: `AutomationProperties.Name="{Binding SlotsAutomationName}"` on the
  container, and `AutomationProperties.IsOffscreenBehavior` left alone — the individual cells get
  `AutomationProperties.AccessibilityView="Raw"` so a reader hears one strip, not five fragments
- Every icon-only button — the three caption buttons, the gear, the search clear button — gets an
  `AutomationProperties.Name`
- The status strip gets a sentence-cased name distinct from its uppercase text, via the override
  `TrackedText` already supports
- Every `TrackedText` micro-label whose text is an abbreviation (`WHY`, `INPUTS`) gets a spelt-out
  `AutomationProperties.Name`

- [ ] **Step 6: Run the tests**

Run: `dotnet test tests/WaveLinkBackup.App.Tests --filter FullyQualifiedName~ShellCommandTests`
Expected: PASS, 6 tests.

- [ ] **Step 7: Drive it from the keyboard only**

Unplug the mouse, or resolve not to touch it:

- [ ] Tab reaches the search field, the gear, the list and all four buttons, in that order
- [ ] The focus ring is visible on every one of them, including on a list row
- [ ] `↑`/`↓` move the selection and the bottom bar follows
- [ ] `Home`/`End` reach the first and last rows
- [ ] `Ctrl+F` focuses the search; typing filters; `Escape` clears it and restores the full list
- [ ] `F5` re-reads the folder
- [ ] With Narrator on, arrowing through the list announces the row as a sentence, and the
      INPUTS strip announces as one thing naming its inputs — not five unlabelled cells

- [ ] **Step 8: Commit**

```bash
git add src/WaveLinkBackup.App tests/WaveLinkBackup.App.Tests
git commit -m "feat: the key map, the focus ring, and names for the screen reader

Escape is bound on the list and the search field rather than the window, so the
dialogs a later session adds still get their own cancel.

The five-slot strip announces as one thing naming its inputs. 7.4 is explicit
that this is part of the work: without it the strip reads as five unlabelled
cells, and which cells are filled is the entire meaning of the column."
```

---

### Task 12: The guards, and the whole thing running

- [ ] **Step 1: The guard that keeps five slots five, all the way to the screen**

Task 10's guards read the XAML. This one renders it, because five-always-five is the one claim
that has to survive the round trip from view model to visual tree — a template with a `Take(4)`
or an `ItemsControl` that drops empty items would pass every source scan.

Append to `tests/WaveLinkBackup.App.Tests/RowTemplateTests.cs`:

```csharp
    // Design section C makes five-always-five structural in the view model precisely so it
    // cannot become an accident of a template. This asserts the template agrees, on a row that
    // only has two inputs - the case where a template that skips empties would look fine.
    [Fact]
    public void The_slot_strip_renders_five_containers_for_a_two_input_row()
    {
        var rendered = Wpf.Run(() =>
        {
            var dictionary = new System.Windows.ResourceDictionary
            {
                Source = new Uri("pack://application:,,,/WaveLinkBackup;component/Views/RowStyles.xaml"),
            };

            var items = new System.Windows.Controls.ItemsControl
            {
                ItemsSource = ViewModels.InputSlots.Build(["Elgato Wave:3", "System"], peakInputCount: 5),
                ItemTemplate = (System.Windows.DataTemplate)dictionary["WlSlotTemplate"],
            };

            // A container per item only exists once the control has been through a layout pass.
            items.Measure(new System.Windows.Size(300, 40));
            items.Arrange(new System.Windows.Rect(0, 0, 300, 40));
            items.UpdateLayout();

            return items.Items.Count;
        });

        Assert.Equal(ViewModels.InputSlots.SlotCount, rendered);
    }
```

- [ ] **Step 2: Full suite, Release, and a look at the coverage**

```bash
dotnet test WaveLinkBackup.slnx
dotnet build WaveLinkBackup.slnx -c Release
```

Expected: green, zero warnings, **≥ 610** tests (473 baseline + ~140 new).

- [ ] **Step 3: Run it against a real store**

```bash
dotnet run --project src/WaveLinkBackup.App
```

with a store holding at least: one manual backup with five inputs, one automatic, one
pre-restore, one with two inputs, and one whose `settings.json` has been edited by hand after
the fact (that last one is the DAMAGED row, and it is the only way to see one).

- [ ] Rows group under TODAY and weekday headers, newest first in both
- [ ] The five-slot strip is five cells wide on every row, including the two-input one
- [ ] The two-input row's slots are **amber**, and the five-input rows' are **green**
- [ ] The suspect row's pill is **amber, transparent-filled, with an amber border** — not red
- [ ] The damaged row is sunken, has a neutral left edge, and its INPUTS cell is one dotted
      `CONTENTS UNKNOWN`
- [ ] Selecting the damaged row disables Rename and Restore and leaves Delete live
- [ ] The damaged row appears in **date order**, not at the bottom
- [ ] Typing in the search filters, highlights the match, and the footer counts what is hidden
- [ ] A query matching nothing keeps the strip, the header and the count on screen
- [ ] The bottom bar's free-space figure matches what Explorer says about the volume

- [ ] **Step 4: Look at it in the other three themes**

- [ ] Light mode: every surface, rule and pill still legible; the amber tint sits on an opaque
      base rather than compositing to mud
- [ ] High contrast (both HC Black and HC White): no tints, no left edges, the meta line reads
      `WHOLE ·` / `SUSPECT ·` / `DAMAGED ·`, disabled buttons are `GrayText` at full opacity,
      and the focus ring is visible on a selected row
- [ ] Switching theme, accent or high contrast **while the window is open** restyles it without
      a restart — and nothing that was red stops being red

- [ ] **Step 5: Commit**

```bash
git add tests/WaveLinkBackup.App.Tests
git commit -m "test: guard the amber pill and the five-slot strip at the template

The pill guard is a source scan rather than a template walk because the failure
mode is someone editing RowStyles.xaml with README open beside them - and
README is still wrong about it."
```

---

## Done when

- [ ] `dotnet build WaveLinkBackup.slnx -c Release` — zero warnings
- [ ] `dotnet test WaveLinkBackup.slnx` — all green, **≥ 610** tests
- [ ] The window has a 34px caption bar on Mica and remembers where it was
- [ ] Every row has exactly five input slots, always, including a row with two inputs and a row
      with six
- [ ] The SUSPECT pill is amber — by test, not by eye
- [ ] A snapshot edited on disk turns its row DAMAGED without the window freezing, and that row
      stays in date order
- [ ] A damaged selection disables Rename and Restore and leaves Delete enabled — by test
- [ ] Search filters, highlights, counts, and Escape clears it
- [ ] High contrast removes every tint and the left edge, and replaces the meta line with a
      verdict word — by test
- [ ] The whole screen is reachable and readable from the keyboard alone, with Narrator
      announcing rows as sentences
- [ ] `technical-debt.md` §4.8 item 2 is closed and item 4's wording updated (Settings is still
      a placeholder; it is now reachable from the window's gear as well as the tray)

## What this plan does not do

| Deferred | To | Why |
|---|---|---|
| Restore confirmation (screen 2), the delete dialogs (`05`), the twelve errors (`06`), restore outcomes (`03`), in-progress states (`04`) | A later phase 5 session | Design §Out of scope. Rename, Delete and Restore render live with correct enablement and open a placeholder `MessageBox` |
| **First run / empty state (screen 4)** — the measure-rule frame, the two actions, the checkbox, the found/not-found line | A later phase 5 session | An empty store gets a one-line stand-in. Recorded as debt rather than smuggled in half-built |
| **Error 12's full screen** — the frame, `Choose a folder…` / `Look again` / `Use the default folder`, the bottom-bar sentence | A later phase 5 session | The **status strip** says `BACKUP FOLDER UNAVAILABLE` and every action goes to 40%, because `10-decisions` §6 pins that much |
| **Error 1's `Choose the settings file…` button** | The same session | The strip's sentence is built; the button beside it is error 1 |
| The Settings dialog, including the autostart toggle | Plan 5 | The gear button opens plan 3's placeholder |
| Inline rename on the row | The session that brings the delete dialog | README specs it under Interactions, not under screen 1's anatomy, and it shares that session's editing affordances |
| Tier 2–4 capture, so PRESETS and PLUGINS badges are always absent | Phase 6 | The slots are drawn in their absent treatment, which is the design's answer |
| The two toast notifications | Phase 7 | Design §Out of scope |

## Related, not scheduled

- **The tray icon is still rendered at a fixed 32px** (technical-debt §4.8 item 1). Untouched here.
- **There is still no icon set** (§4.7). The eleven Lucide glyphs this screen needs — `search`,
  `settings`, `pencil`, `trash-2`, `rotate-ccw`, `download`, `alert-triangle`, `chevron-down` —
  are drawn to the same 24px grid as `TrayIconRenderer`'s four and are stand-ins for the same reason.
- **Row density.** README gives a 15px comfortable / 9px compact pair (≈54px / ≈42px rows).
  Only comfortable is built; nothing chooses between them yet, and the switch belongs with the
  Settings dialog that would hold it.
- **Motion.** README's 140ms hover / 220ms state change with `cubic-bezier(.2,0,0,1)` is specified
  and not implemented here. Worth its own pass across the whole shell rather than one screen's.

## Risks

| Risk | Signal | Response |
|---|---|---|
| Building the row from README alone | A red SUSPECT pill | The source-scan guard in Task 12 Step 1 fails the build |
| The variable-font trap | Every weight renders identically; the screen looks flat rather than broken | `TypographyTests` asserts three distinct Rubik faces |
| Mica applied and invisible | The DWM call succeeds and nothing changes | Task 4's four traps, and the `WlChrome` fallback the return value drives |
| The probe writing into a replaced list | A row flips to DAMAGED after an F5 that removed it | Cancellation, asserted in `HealthProbeTests` |
| Colour literals creeping into the new XAML | Any `#RRGGBB` outside `Theming/` | The existing scan, which is why `Typography.xaml` lives in `Views/` |
| Logic migrating into the templates | A trigger computing what a row means | Every state is a view-model property; the templates only choose how it reads |
| The plan is eleven tasks in one session | Losing the thread mid-build | Twelve staged checkpoints, each with its own commit — design §Risks says execute in stages, not one pass |
