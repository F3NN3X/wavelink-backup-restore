---
title: "Phase 5 Plan 2 — The tray shell"
status: published
created: 2026-08-17
updated: 2026-08-17
related_adrs: [ADR-004, ADR-005]
tags: [plan, implementation, app, tray, phase-5]
---

# Phase 5 Plan 2 — Tray Shell Implementation Plan


**Goal:** Turn `WaveLinkBackup.App` from a two-file stub into a tray-resident process that watches Wave Link, survives its window closing, refuses to run twice, and can put itself in `HKCU\...\Run`.

**Architecture:** `App` owns the lifetime and three long-lived things — settings, a backup host, a tray presenter. Windows are transient views. Every Windows dependency sits behind an interface with a fake, and the tray's four states are a pure function so the whole behaviour is a table test with no WPF in it.

**Tech Stack:** C# / .NET 10, WPF, H.NotifyIcon.Wpf, xunit.v3, `Microsoft.Win32.Registry`.

**Spec:** [2026-08-17-phase-5-shell-design.md](2026-08-17-phase-5-shell-design.md) sections A, B and the tray half of C.

## Global Constraints

- `WaveLinkBackup.Core` stays **`net10.0`**. Nothing in this plan touches it.
- `TreatWarningsAsErrors` is on, repo-wide.
- **No colour literals outside a theme dictionary.** Every `--wl-*` value is a brush resource key, referenced with `DynamicResource`. A guard test enforces this from Task 2 onward.
- **`SUSPECT` is amber, not red** (`10-decisions.md` §1) — relevant here only because the tray's `NEEDS YOU` uses the same `WlWarn`.
- Single instance is **mandatory**: two watchers race on one settings file.
- Autostart is **per-user, never per-machine, never a scheduled task** (`12-tray-autostart-update.md`).
- Build: `dotnet build WaveLinkBackup.slnx` · Test: `dotnet test WaveLinkBackup.slnx`
- Baseline: **386 tests green** (295 Core, 91 CLI), Release zero warnings, NativeAOT 3.23 MB.

## File Structure

| File | Responsibility |
|---|---|
| `src/WaveLinkBackup.App/WaveLinkBackup.App.csproj` | *Modify* — TFM, H.NotifyIcon, InternalsVisibleTo |
| `src/WaveLinkBackup.App/Startup/ShellArguments.cs` | *Create* — parse `--tray`/`--store`/`--settings`/`--keep` |
| `src/WaveLinkBackup.App/Startup/SingleInstance.cs` | *Create* — mutex to detect, events to activate |
| `src/WaveLinkBackup.App/Theming/Dark.xaml` · `Light.xaml` · `HighContrast.xaml` | *Create* — the 20 `Wl*` brushes |
| `src/WaveLinkBackup.App/Theming/ThemeManager.cs` | *Create* — pick and swap the dictionary |
| `src/WaveLinkBackup.App/Hosting/TrayState.cs` | *Create* — the four states, as a pure function |
| `src/WaveLinkBackup.App/Hosting/BackupHost.cs` | *Create* — owns the coordinator, the timer and pause |
| `src/WaveLinkBackup.App/Windows/IAutostart.cs` · `RunKeyAutostart.cs` | *Create* — Run key + Task Manager veto |
| `src/WaveLinkBackup.App/Windows/IRegistryKeys.cs` · `WindowsRegistryKeys.cs` | *Create* — the seam that makes autostart testable |
| `src/WaveLinkBackup.App/Views/TrayIcon.xaml` | *Create* — `TaskbarIcon` + the designed context menu |
| `src/WaveLinkBackup.App/Views/TrayIconRenderer.cs` | *Create* — geometry + colour → `ImageSource` |
| `src/WaveLinkBackup.App/App.xaml` · `App.xaml.cs` | *Modify* — the lifetime |
| `src/WaveLinkBackup.App/MainWindow.xaml` | *Modify* — drop the two colour literals |
| `tests/WaveLinkBackup.App.Tests/` | *Create* — the new test project |

---

### Task 1: The App project, and its arguments

Brings the test project into existence with something real in it, and moves the App project onto the TFM the rest of phase 5 needs.

**Files:**
- Modify: `src/WaveLinkBackup.App/WaveLinkBackup.App.csproj`
- Create: `src/WaveLinkBackup.App/Startup/ShellArguments.cs`
- Create: `tests/WaveLinkBackup.App.Tests/WaveLinkBackup.App.Tests.csproj`
- Create: `tests/WaveLinkBackup.App.Tests/ShellArgumentsTests.cs`
- Modify: `WaveLinkBackup.slnx`

**Interfaces:**
- Produces:
  - `sealed record ShellArguments(bool StartInTray, string? StorePath, string? SettingsPath, int? KeepCount, string? Error)`
  - `static ShellArguments ShellArguments.Parse(string[] args)`
  - `bool ShellArguments.IsValid => Error is null`
  - `BackupSettings ShellArguments.ApplyTo(BackupSettings settings)`

- [ ] **Step 1: Update the App csproj**

Replace `src/WaveLinkBackup.App/WaveLinkBackup.App.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <RootNamespace>WaveLinkBackup.App</RootNamespace>
    <AssemblyName>WaveLinkBackup</AssemblyName>
    <UseWPF>true</UseWPF>
    <!--
      OS-versioned, unlike every other project here. UISettings.GetColorValue is the API the
      design names for the OS accent (screens/01-tokens-and-mapping.md), and
      UISettings.ColorValuesChanged is what makes live theme following an event rather than a
      poll. That WinRT projection needs a Windows SDK TFM.

      Core stays net10.0 and GuardNoDesktopFramework is untouched.
    -->
    <TargetFramework>net10.0-windows10.0.19041.0</TargetFramework>
    <SupportedOSPlatformVersion>10.0.19041.0</SupportedOSPlatformVersion>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="H.NotifyIcon.Wpf" Version="2.3.0" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="../WaveLinkBackup.Core/WaveLinkBackup.Core.csproj" />
  </ItemGroup>

  <ItemGroup>
    <InternalsVisibleTo Include="WaveLinkBackup.App.Tests" />
  </ItemGroup>

</Project>
```

> If `2.3.0` does not resolve, run `dotnet add src/WaveLinkBackup.App package H.NotifyIcon.Wpf` and take whatever version it picks. Record the version you landed on in the commit message.

- [ ] **Step 2: Create the test project**

Create `tests/WaveLinkBackup.App.Tests/WaveLinkBackup.App.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <RootNamespace>WaveLinkBackup.App.Tests</RootNamespace>
    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
    <!-- Must match the App project: a test project cannot reference a higher TFM. -->
    <TargetFramework>net10.0-windows10.0.19041.0</TargetFramework>
    <!-- Needed to reference WPF types (brushes, resource dictionaries) from tests. -->
    <UseWPF>true</UseWPF>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="18.0.1" />
    <PackageReference Include="xunit.v3" Version="3.2.2" />
    <PackageReference Include="xunit.runner.visualstudio" Version="3.1.5" />
    <PackageReference Include="coverlet.collector" Version="6.0.4" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="../../src/WaveLinkBackup.App/WaveLinkBackup.App.csproj" />
  </ItemGroup>

  <!--
    The XAML colour-literal guard is a source scan, like Core's SourceGuardTests. It needs the
    App source directory at test time; passing it as assembly metadata beats walking up from
    the output directory and guessing.
  -->
  <ItemGroup>
    <AssemblyMetadata Include="AppSourceRoot"
                      Value="$([System.IO.Path]::GetFullPath('$(MSBuildThisFileDirectory)../../src/WaveLinkBackup.App'))" />
  </ItemGroup>

</Project>
```

Create `tests/WaveLinkBackup.App.Tests/Usings.cs`:

```csharp
global using Xunit;
```

> Check `tests/WaveLinkBackup.Core.Tests/Usings.cs` and copy whatever it contains instead, if it differs.

- [ ] **Step 3: Register the test project in the solution**

In `WaveLinkBackup.slnx`, add inside the `/tests/` folder:

```xml
    <Project Path="tests/WaveLinkBackup.App.Tests/WaveLinkBackup.App.Tests.csproj" />
```

- [ ] **Step 4: Write the failing tests**

Create `tests/WaveLinkBackup.App.Tests/ShellArgumentsTests.cs`:

```csharp
using WaveLinkBackup.App.Startup;
using WaveLinkBackup.Core.Automation;

namespace WaveLinkBackup.App.Tests;

/// <summary>
/// Flags win for one run and are never written back
/// (operations/design/screens/08-settings-persistence.md), so these produce an overlay over
/// BackupSettings rather than something that gets saved.
/// </summary>
public sealed class ShellArgumentsTests
{
    [Fact]
    public void No_arguments_means_show_the_window()
    {
        var args = ShellArguments.Parse([]);

        Assert.True(args.IsValid);
        Assert.False(args.StartInTray);
    }

    [Fact]
    public void Tray_starts_windowless()
    {
        Assert.True(ShellArguments.Parse(["--tray"]).StartInTray);
    }

    [Fact]
    public void Every_value_flag_is_captured()
    {
        var args = ShellArguments.Parse(
            ["--store", @"D:\B", "--settings", @"C:\WL\Settings.json", "--keep", "12"]);

        Assert.True(args.IsValid);
        Assert.Equal(@"D:\B", args.StorePath);
        Assert.Equal(@"C:\WL\Settings.json", args.SettingsPath);
        Assert.Equal(12, args.KeepCount);
    }

    [Fact]
    public void An_unknown_flag_is_an_error_rather_than_being_ignored()
    {
        var args = ShellArguments.Parse(["--destroy-everything"]);

        Assert.False(args.IsValid);
        Assert.Contains("--destroy-everything", args.Error!, StringComparison.Ordinal);
    }

    [Fact]
    public void A_value_flag_with_no_value_is_an_error()
    {
        Assert.False(ShellArguments.Parse(["--store"]).IsValid);
        Assert.False(ShellArguments.Parse(["--keep"]).IsValid);
    }

    [Fact]
    public void A_non_numeric_keep_count_is_an_error()
    {
        Assert.False(ShellArguments.Parse(["--keep", "loads"]).IsValid);
    }

    [Fact]
    public void Flags_overlay_the_settings_they_are_given()
    {
        var settings = new BackupSettings(@"D:\from-file", AutoBackupKeepCount: 30);

        var overlaid = ShellArguments.Parse(["--store", @"D:\from-flag"]).ApplyTo(settings);

        Assert.Equal(@"D:\from-flag", overlaid.StorePath);
        Assert.Equal(30, overlaid.AutoBackupKeepCount); // untouched
    }

    [Fact]
    public void Absent_flags_leave_the_settings_alone()
    {
        var settings = new BackupSettings(@"D:\from-file", AutoBackupKeepCount: 30,
                                          ChosenWaveLinkPath: @"C:\WL\Settings.json");

        Assert.Equal(settings, ShellArguments.Parse([]).ApplyTo(settings));
    }
}
```

- [ ] **Step 5: Run the tests to verify they fail**

Run: `dotnet test tests/WaveLinkBackup.App.Tests`
Expected: FAIL to compile — `ShellArguments` does not exist.

- [ ] **Step 6: Write ShellArguments**

Create `src/WaveLinkBackup.App/Startup/ShellArguments.cs`:

```csharp
using WaveLinkBackup.Core.Automation;

namespace WaveLinkBackup.App.Startup;

/// <summary>
/// The shell's command line. Deliberately NOT shared with the CLI's parser: the two have
/// almost nothing in common — no verbs here, and the CLI has no --tray — and coupling them
/// would mean every future CLI verb widened the shell's surface (ADR-009 took the same view of
/// hand-rolled parsing over a library).
///
/// Flags apply to THIS RUN and are never written back
/// (operations/design/screens/08-settings-persistence.md).
/// </summary>
/// <param name="Error">Non-null when parsing failed. The shell shows it and exits.</param>
public sealed record ShellArguments(
    bool StartInTray = false,
    string? StorePath = null,
    string? SettingsPath = null,
    int? KeepCount = null,
    string? Error = null)
{
    public bool IsValid => Error is null;

    private static ShellArguments Failed(string error) => new(Error: error);

    public static ShellArguments Parse(string[] args)
    {
        var result = new ShellArguments();

        for (var i = 0; i < args.Length; i++)
        {
            var flag = args[i];

            switch (flag)
            {
                case "--tray":
                    result = result with { StartInTray = true };
                    break;

                case "--store":
                    if (!TryValue(args, ref i, out var store)) return Failed("--store needs a folder.");
                    result = result with { StorePath = store };
                    break;

                case "--settings":
                    if (!TryValue(args, ref i, out var settings)) return Failed("--settings needs a path.");
                    result = result with { SettingsPath = settings };
                    break;

                case "--keep":
                    if (!TryValue(args, ref i, out var keep)) return Failed("--keep needs a number.");
                    if (!int.TryParse(keep, out var count)) return Failed($"'{keep}' is not a number.");
                    result = result with { KeepCount = count };
                    break;

                default:
                    // Ignoring an unknown flag is how a typo silently becomes "watch the
                    // default folder instead of the one you meant".
                    return Failed($"'{flag}' is not something this app understands.");
            }
        }

        return result;
    }

    /// <summary>Layers the flags over what the settings file said. Produces a value; saves nothing.</summary>
    public BackupSettings ApplyTo(BackupSettings settings) => settings with
    {
        StorePath = StorePath ?? settings.StorePath,
        AutoBackupKeepCount = KeepCount ?? settings.AutoBackupKeepCount,
        ChosenWaveLinkPath = SettingsPath ?? settings.ChosenWaveLinkPath,
    };

    private static bool TryValue(string[] args, ref int index, out string value)
    {
        if (index + 1 >= args.Length)
        {
            value = string.Empty;
            return false;
        }

        value = args[++index];
        return true;
    }
}
```

- [ ] **Step 7: Run the tests to verify they pass**

Run: `dotnet test tests/WaveLinkBackup.App.Tests`
Expected: PASS, 8 tests.

- [ ] **Step 8: Run the whole suite**

Run: `dotnet test WaveLinkBackup.slnx`
Expected: PASS, 394 total.

- [ ] **Step 9: Commit**

```bash
git add src/WaveLinkBackup.App/ tests/WaveLinkBackup.App.Tests/ WaveLinkBackup.slnx
git commit -m "feat: the shell's command line, and a test project to hold it

App moves to an OS-versioned TFM for the WinRT UISettings projection the design
names for the OS accent. Core is untouched and stays net10.0."
```

---

### Task 2: The theme dictionaries, and the guard that keeps them honest

Three dictionaries, the same 20 keys in each, and a source scan that fails the build if a colour ever appears outside them.

**Values are copied verbatim from `01-tokens-and-mapping.md`. Do not re-sample or re-derive them.**

**Files:**
- Create: `src/WaveLinkBackup.App/Theming/Dark.xaml`, `Light.xaml`, `HighContrast.xaml`
- Create: `src/WaveLinkBackup.App/Theming/ThemeManager.cs`
- Modify: `src/WaveLinkBackup.App/App.xaml`, `MainWindow.xaml`
- Create: `tests/WaveLinkBackup.App.Tests/ThemeTests.cs`

**Interfaces:**
- Produces:
  - `enum AppTheme { Dark, Light, HighContrast }`
  - `static ResourceDictionary ThemeManager.Load(AppTheme theme)`
  - `static AppTheme ThemeManager.DetectFromSystem()`
  - `static void ThemeManager.Apply(AppTheme theme)`
  - `static IReadOnlyList<string> ThemeManager.BrushKeys` — the 20 names

- [ ] **Step 1: Write Dark.xaml**

Create `src/WaveLinkBackup.App/Theming/Dark.xaml`. Nine of these are literal F3NN3X tokens; `WlChrome` is app-owned. See `01-tokens-and-mapping.md`.

```xml
<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">

    <!-- Surfaces -->
    <SolidColorBrush x:Key="WlBg"        Color="#111419" />
    <SolidColorBrush x:Key="WlChrome"    Color="#1A1E20" />
    <SolidColorBrush x:Key="WlCard"      Color="#16191A" />
    <SolidColorBrush x:Key="WlRaised"    Color="#1B1D1C" />
    <SolidColorBrush x:Key="WlSunken"    Color="#0B0D0C" />

    <!-- Text -->
    <SolidColorBrush x:Key="WlText"      Color="#C8CBD2" />
    <SolidColorBrush x:Key="WlStrong"    Color="#FFFFFF" />
    <SolidColorBrush x:Key="WlMuted"     Color="#8B9096" />

    <!-- Lines -->
    <SolidColorBrush x:Key="WlLine"      Color="#FFFFFF" Opacity="0.11" />
    <SolidColorBrush x:Key="WlLine2"     Color="#FFFFFF" Opacity="0.20" />
    <SolidColorBrush x:Key="WlHover"     Color="#FFFFFF" Opacity="0.04" />

    <!-- Accent. WlAccent follows the OS accent (plan 3); WlDanger never does. -->
    <SolidColorBrush x:Key="WlAccent"     Color="#F01616" />
    <SolidColorBrush x:Key="WlAccentInk"  Color="#FFFFFF" />
    <SolidColorBrush x:Key="WlAccentSoft" Color="#F01616" Opacity="0.12" />
    <SolidColorBrush x:Key="WlAccentLine" Color="#F01616" Opacity="0.32" />
    <SolidColorBrush x:Key="WlDanger"     Color="#F01616" />

    <!-- Health -->
    <SolidColorBrush x:Key="WlOk"        Color="#5FD3A6" />
    <SolidColorBrush x:Key="WlOkSoft"    Color="#5FD3A6" Opacity="0.13" />
    <SolidColorBrush x:Key="WlWarn"      Color="#F5B843" />
    <SolidColorBrush x:Key="WlWarnSoft"  Color="#F5B843" Opacity="0.18" />

    <SolidColorBrush x:Key="WlScrim"     Color="#0B0D0C" Opacity="0.22" />

</ResourceDictionary>
```

- [ ] **Step 2: Write Light.xaml**

Create `src/WaveLinkBackup.App/Theming/Light.xaml`. **App-owned in full — F3NN3X has no light mode. Do not substitute F3NN3X values here.**

```xml
<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">

    <SolidColorBrush x:Key="WlBg"        Color="#F5F5F3" />
    <SolidColorBrush x:Key="WlChrome"    Color="#EAEAE6" />
    <SolidColorBrush x:Key="WlCard"      Color="#FFFFFF" />
    <SolidColorBrush x:Key="WlRaised"    Color="#FBFBF9" />
    <SolidColorBrush x:Key="WlSunken"    Color="#EDEDE9" />

    <SolidColorBrush x:Key="WlText"      Color="#2A3033" />
    <SolidColorBrush x:Key="WlStrong"    Color="#0B0D0C" />
    <SolidColorBrush x:Key="WlMuted"     Color="#61686C" />

    <SolidColorBrush x:Key="WlLine"      Color="#0B0D0C" Opacity="0.11" />
    <SolidColorBrush x:Key="WlLine2"     Color="#0B0D0C" Opacity="0.20" />
    <SolidColorBrush x:Key="WlHover"     Color="#0B0D0C" Opacity="0.04" />

    <SolidColorBrush x:Key="WlAccent"     Color="#AA0000" />
    <SolidColorBrush x:Key="WlAccentInk"  Color="#FFFFFF" />
    <SolidColorBrush x:Key="WlAccentSoft" Color="#AA0000" Opacity="0.07" />
    <SolidColorBrush x:Key="WlAccentLine" Color="#AA0000" Opacity="0.24" />
    <SolidColorBrush x:Key="WlDanger"     Color="#AA0000" />

    <!-- Darkened for contrast on #FFFFFF; NOT --green-400. -->
    <SolidColorBrush x:Key="WlOk"        Color="#0F6B4A" />
    <SolidColorBrush x:Key="WlOkSoft"    Color="#2F8A67" Opacity="0.13" />
    <!-- amber-400 is unreadable on white, so the TEXT darkens; the TINT stays amber-400. -->
    <SolidColorBrush x:Key="WlWarn"      Color="#8A5A05" />
    <SolidColorBrush x:Key="WlWarnSoft"  Color="#F5B843" Opacity="0.18" />

    <SolidColorBrush x:Key="WlScrim"     Color="#0B0D0C" Opacity="0.22" />

</ResourceDictionary>
```

- [ ] **Step 3: Write HighContrast.xaml**

Create `src/WaveLinkBackup.App/Theming/HighContrast.xaml`. **Binds to `SystemColors.*Key`, never to a hard-coded HC hex** (`11-high-contrast.md`). Every tint and fill becomes transparent; surfaces are told apart by borders.

```xml
<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">

    <!--
      In a high-contrast theme the palette is not ours. Windows forces four or five colours and
      we do not get to mean anything with them: green-is-whole and amber-is-not are gone. This
      works because health was never encoded only in colour — it is encoded in shape.

      Every fill goes transparent. Nothing here is a literal.
    -->
    <SolidColorBrush x:Key="WlBg"     Color="{DynamicResource {x:Static SystemColors.WindowColor}}" />
    <SolidColorBrush x:Key="WlChrome" Color="{DynamicResource {x:Static SystemColors.WindowColor}}" />
    <SolidColorBrush x:Key="WlCard"   Color="Transparent" />
    <SolidColorBrush x:Key="WlRaised" Color="Transparent" />
    <SolidColorBrush x:Key="WlSunken" Color="Transparent" />

    <SolidColorBrush x:Key="WlText"   Color="{DynamicResource {x:Static SystemColors.WindowTextColor}}" />
    <SolidColorBrush x:Key="WlStrong" Color="{DynamicResource {x:Static SystemColors.WindowTextColor}}" />
    <SolidColorBrush x:Key="WlMuted"  Color="{DynamicResource {x:Static SystemColors.GrayTextColor}}" />

    <SolidColorBrush x:Key="WlLine"   Color="{DynamicResource {x:Static SystemColors.WindowTextColor}}" />
    <SolidColorBrush x:Key="WlLine2"  Color="{DynamicResource {x:Static SystemColors.WindowTextColor}}" />
    <SolidColorBrush x:Key="WlHover"  Color="Transparent" />

    <!-- Red and the accent are gone. Primary = Highlight; nothing here is red. -->
    <SolidColorBrush x:Key="WlAccent"     Color="{DynamicResource {x:Static SystemColors.HighlightColor}}" />
    <SolidColorBrush x:Key="WlAccentInk"  Color="{DynamicResource {x:Static SystemColors.HighlightTextColor}}" />
    <SolidColorBrush x:Key="WlAccentSoft" Color="Transparent" />
    <SolidColorBrush x:Key="WlAccentLine" Color="{DynamicResource {x:Static SystemColors.WindowTextColor}}" />
    <SolidColorBrush x:Key="WlDanger"     Color="{DynamicResource {x:Static SystemColors.WindowTextColor}}" />

    <SolidColorBrush x:Key="WlOk"       Color="{DynamicResource {x:Static SystemColors.WindowTextColor}}" />
    <SolidColorBrush x:Key="WlOkSoft"   Color="Transparent" />
    <SolidColorBrush x:Key="WlWarn"     Color="{DynamicResource {x:Static SystemColors.WindowTextColor}}" />
    <SolidColorBrush x:Key="WlWarnSoft" Color="Transparent" />

    <!-- A dialog is separated by a border, not by dimming: dimming is opacity. -->
    <SolidColorBrush x:Key="WlScrim"    Color="Transparent" />

</ResourceDictionary>
```

- [ ] **Step 4: Write the failing tests**

Create `tests/WaveLinkBackup.App.Tests/ThemeTests.cs`:

```csharp
using System.Reflection;
using System.Text.RegularExpressions;
using System.Windows.Media;
using WaveLinkBackup.App.Theming;

namespace WaveLinkBackup.App.Tests;

/// <summary>
/// Theme switching is a resource swap, which is what makes it testable at all. Pixels are what
/// the handoff and a human eye are for; these are the two rules that can rot silently.
/// </summary>
public sealed class ThemeTests
{
    [Theory]
    [InlineData(AppTheme.Dark)]
    [InlineData(AppTheme.Light)]
    [InlineData(AppTheme.HighContrast)]
    public void Every_theme_declares_every_brush(AppTheme theme)
    {
        var dictionary = ThemeManager.Load(theme);

        var missing = ThemeManager.BrushKeys.Where(k => !dictionary.Contains(k)).ToList();

        Assert.True(missing.Count == 0,
            $"{theme} is missing: {string.Join(", ", missing)}. A missing key is a control that " +
            $"renders with WPF's default colours in one theme only.");
    }

    /// <summary>
    /// "--wl-danger must not follow the OS accent. Two different reds in one window is a bug the
    /// design calls out explicitly." Dark and light both pin it to the red they specify.
    /// </summary>
    [Theory]
    [InlineData(AppTheme.Dark, "#FFF01616")]
    [InlineData(AppTheme.Light, "#FFAA0000")]
    public void Danger_is_the_specified_red_and_not_the_accent(AppTheme theme, string expected)
    {
        var danger = (SolidColorBrush)ThemeManager.Load(theme)["WlDanger"]!;

        Assert.Equal(expected, danger.Color.ToString());
    }

    /// <summary>
    /// The guard that keeps "no colour literals in XAML" true in six months rather than only
    /// today. Mirrors Core's SourceGuardTests.
    /// </summary>
    [Fact]
    public void No_xaml_outside_the_theme_dictionaries_contains_a_colour_literal()
    {
        var root = Assembly.GetExecutingAssembly()
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .Single(a => a.Key == "AppSourceRoot").Value!;

        var themingFolder = $"{Path.DirectorySeparatorChar}Theming{Path.DirectorySeparatorChar}";
        var literal = new Regex("#[0-9A-Fa-f]{3,8}\\b");

        var offenders = new List<string>();

        foreach (var file in Directory.EnumerateFiles(root, "*.xaml", SearchOption.AllDirectories))
        {
            if (file.Contains(themingFolder, StringComparison.OrdinalIgnoreCase)) continue;
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")) continue;
            if (file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")) continue;

            foreach (Match match in literal.Matches(File.ReadAllText(file)))
            {
                offenders.Add($"  {Path.GetFileName(file)}: {match.Value}");
            }
        }

        Assert.True(offenders.Count == 0,
            $"Colours belong in Theming/*.xaml and are referenced by key, which is what makes " +
            $"a theme switch a resource swap rather than a window rebuild. Found:" +
            $"{Environment.NewLine}{string.Join(Environment.NewLine, offenders)}");
    }
}
```

- [ ] **Step 5: Run the tests to verify they fail**

Run: `dotnet test tests/WaveLinkBackup.App.Tests`
Expected: FAIL to compile — `ThemeManager` and `AppTheme` do not exist.

- [ ] **Step 6: Write ThemeManager**

Create `src/WaveLinkBackup.App/Theming/ThemeManager.cs`:

```csharp
using System.Windows;

namespace WaveLinkBackup.App.Theming;

public enum AppTheme
{
    Dark,
    Light,
    HighContrast,
}

/// <summary>
/// Every --wl-* value is a brush resource key, declared once per theme and referenced with
/// DynamicResource. That is what makes switching a resource swap rather than a window rebuild.
///
/// Live following of the OS — UISettings.ColorValuesChanged, SystemEvents.UserPreferenceChanged
/// and the accent derivation — arrives in plan 3. This picks a theme once, at startup.
/// </summary>
public static class ThemeManager
{
    /// <summary>
    /// The 20 roles from screens/01-tokens-and-mapping.md. Named here so a missing key in one
    /// theme is a failing test rather than a control that renders wrong in light mode only.
    /// </summary>
    public static IReadOnlyList<string> BrushKeys { get; } =
    [
        "WlBg", "WlChrome", "WlCard", "WlRaised", "WlSunken",
        "WlText", "WlStrong", "WlMuted",
        "WlLine", "WlLine2", "WlHover",
        "WlAccent", "WlAccentInk", "WlAccentSoft", "WlAccentLine", "WlDanger",
        "WlOk", "WlOkSoft", "WlWarn", "WlWarnSoft",
        "WlScrim",
    ];

    public static ResourceDictionary Load(AppTheme theme) => new()
    {
        Source = new Uri($"pack://application:,,,/Theming/{theme}.xaml", UriKind.Absolute),
    };

    /// <summary>
    /// High contrast wins over dark/light: it is not a preference sitting alongside them, it is
    /// Windows saying the palette is no longer ours.
    /// </summary>
    public static AppTheme DetectFromSystem()
    {
        if (SystemParameters.HighContrast) return AppTheme.HighContrast;

        return IsSystemInLightMode() ? AppTheme.Light : AppTheme.Dark;
    }

    public static void Apply(AppTheme theme)
    {
        var dictionaries = Application.Current.Resources.MergedDictionaries;

        // Slot 0 is the theme by convention; everything merged after it may reference these
        // keys. Replacing in place keeps that ordering.
        if (dictionaries.Count == 0) dictionaries.Add(Load(theme));
        else dictionaries[0] = Load(theme);
    }

    private static bool IsSystemInLightMode()
    {
        // Registry rather than UISettings for now: UISettings arrives with the live-following
        // work in plan 3, and this keeps the WinRT surface out of the startup path until then.
        using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
            @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");

        return key?.GetValue("AppsUseLightTheme") is int light && light != 0;
    }
}
```

> `BrushKeys` has 21 entries, not 20 — the design's list of 20 omits `WlRaised`, which `Dark.xaml` needs for the raised surface. Keep all 21; the test asserts every theme declares every key it lists.

- [ ] **Step 7: Wire the dictionary into App.xaml**

Replace `src/WaveLinkBackup.App/App.xaml`:

```xml
<Application x:Class="WaveLinkBackup.App.App"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <Application.Resources>
        <ResourceDictionary>
            <ResourceDictionary.MergedDictionaries>
                <!-- Slot 0. Replaced at runtime by ThemeManager.Apply. -->
                <ResourceDictionary Source="Theming/Dark.xaml" />
            </ResourceDictionary.MergedDictionaries>
        </ResourceDictionary>
    </Application.Resources>
</Application>
```

`StartupUri` is deliberately gone — `App` decides whether a window appears (Task 6).

- [ ] **Step 8: Take the colour literals out of MainWindow**

Replace `src/WaveLinkBackup.App/MainWindow.xaml`:

```xml
<Window x:Class="WaveLinkBackup.App.MainWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="Wave Link Backup" Height="760" Width="1180"
        MinHeight="560" MinWidth="980"
        Background="{DynamicResource WlBg}">
    <TextBlock Foreground="{DynamicResource WlMuted}"
               FontFamily="Segoe UI Variable"
               HorizontalAlignment="Center" VerticalAlignment="Center"
               TextAlignment="Center"
               Text="Wave Link Backup&#x0a;&#x0a;Shell stub — the list arrives in plan 4.&#x0a;See _docs/operations/design/README.md" />
</Window>
```

Sizes are the design's: default 1180 × 760, minimum 980 × 560.

- [ ] **Step 9: Run the tests to verify they pass**

Run: `dotnet test tests/WaveLinkBackup.App.Tests`
Expected: PASS, 12 tests (8 from task 1 + 4 here).

> If the guard test fails on a `pack://` URI or similar false positive, tighten the regex to require the `#` be inside a quoted attribute value rather than loosening the rule.

- [ ] **Step 10: Commit**

```bash
git add src/WaveLinkBackup.App/ tests/WaveLinkBackup.App.Tests/ThemeTests.cs
git commit -m "feat: the three theme dictionaries, and a guard against colour literals

High contrast binds to SystemColors and never to a hard-coded HC hex; every
tint goes transparent, because in a high-contrast theme the palette is not ours.

WlDanger is pinned by test in both themes. Two reds in one window is a bug the
design calls out explicitly, so it earns an assertion rather than a note."
```

---

### Task 3: Tray state, as a pure function

The tray's whole behaviour, with no WPF in it.

**Files:**
- Create: `src/WaveLinkBackup.App/Hosting/TrayState.cs`
- Create: `tests/WaveLinkBackup.App.Tests/TrayStateTests.cs`

**Interfaces:**
- Produces:
  - `enum TrayStatus { Watching, BackingUp, NeedsYou, Paused }`
  - `readonly record struct TrayConditions(bool AutoBackupEnabled, bool IsPaused, bool IsCapturing, CoreError? LastError)`
  - `static TrayStatus TrayState.From(TrayConditions conditions)`
  - `static string TrayState.Tooltip(TrayConditions conditions, DateTimeOffset? lastBackupAt, IFormatProvider? culture = null)`

- [ ] **Step 1: Write the failing tests**

Create `tests/WaveLinkBackup.App.Tests/TrayStateTests.cs`:

```csharp
using WaveLinkBackup.App.Hosting;
using WaveLinkBackup.Core.Results;

namespace WaveLinkBackup.App.Tests;

/// <summary>
/// Four states, one bit of state each (screens/12-tray-autostart-update.md). NEEDS YOU is
/// reachable only because technical-debt 7.3 put the CoreError on TickResult.
/// </summary>
public sealed class TrayStateTests
{
    private static readonly TrayConditions Healthy = new(
        AutoBackupEnabled: true, IsPaused: false, IsCapturing: false, LastError: null);

    [Fact]
    public void Watching_is_the_resting_state()
    {
        Assert.Equal(TrayStatus.Watching, TrayState.From(Healthy));
    }

    [Fact]
    public void Capturing_shows_backing_up()
    {
        Assert.Equal(TrayStatus.BackingUp, TrayState.From(Healthy with { IsCapturing = true }));
    }

    [Fact]
    public void An_error_shows_needs_you()
    {
        var conditions = Healthy with { LastError = new StoreUnavailable(@"D:\gone", "not there") };

        Assert.Equal(TrayStatus.NeedsYou, TrayState.From(conditions));
    }

    [Fact]
    public void Pausing_shows_paused()
    {
        Assert.Equal(TrayStatus.Paused, TrayState.From(Healthy with { IsPaused = true }));
    }

    /// <summary>
    /// Automatic backup switched off and "pause for an hour" both leave nothing watching, and
    /// the design gives them one icon state between them — with different tooltips.
    /// </summary>
    [Fact]
    public void Automatic_backup_switched_off_also_shows_paused()
    {
        Assert.Equal(TrayStatus.Paused, TrayState.From(Healthy with { AutoBackupEnabled = false }));
    }

    /// <summary>
    /// Amber outranks the rest. A failing watcher that also happens to be mid-capture is still
    /// something the user has to act on, and the quiet states must not hide it.
    /// </summary>
    [Fact]
    public void Needs_you_outranks_every_other_state()
    {
        var broken = new TrayConditions(
            AutoBackupEnabled: false,
            IsPaused: true,
            IsCapturing: true,
            LastError: new StoreUnavailable(@"D:\gone", "not there"));

        Assert.Equal(TrayStatus.NeedsYou, TrayState.From(broken));
    }

    [Fact]
    public void Capturing_outranks_paused_because_something_is_actually_happening()
    {
        var conditions = Healthy with { IsCapturing = true, IsPaused = true };

        Assert.Equal(TrayStatus.BackingUp, TrayState.From(conditions));
    }

    [Fact]
    public void The_tooltip_names_the_last_backup()
    {
        var at = new DateTimeOffset(2026, 8, 15, 23, 7, 0, TimeSpan.Zero);

        var tooltip = TrayState.Tooltip(Healthy, at, System.Globalization.CultureInfo.InvariantCulture);

        Assert.StartsWith("Wave Link Backup — ", tooltip, StringComparison.Ordinal);
        Assert.Contains("23:07", tooltip, StringComparison.Ordinal);
    }

    /// <summary>
    /// "In the NEEDS YOU state the tooltip names the problem." A tray icon that only says
    /// something is wrong makes the user open the app to find out what.
    /// </summary>
    [Fact]
    public void The_tooltip_names_the_problem_when_something_is_wrong()
    {
        var conditions = Healthy with { LastError = new StoreUnavailable(@"D:\gone", "not there") };

        var tooltip = TrayState.Tooltip(conditions, null);

        Assert.Contains("backup folder", tooltip, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_tooltip_copes_with_never_having_backed_up()
    {
        Assert.Contains("No backup yet", TrayState.Tooltip(Healthy, null), StringComparison.Ordinal);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/WaveLinkBackup.App.Tests --filter FullyQualifiedName~TrayStateTests`
Expected: FAIL to compile.

- [ ] **Step 3: Write TrayState**

Create `src/WaveLinkBackup.App/Hosting/TrayState.cs`:

```csharp
using System.Globalization;
using WaveLinkBackup.Core.Results;

namespace WaveLinkBackup.App.Hosting;

/// <summary>The four icon states from screens/12-tray-autostart-update.md.</summary>
public enum TrayStatus
{
    /// <summary>shield + check, --wl-text.</summary>
    Watching,

    /// <summary>shield + down arrow, --wl-text.</summary>
    BackingUp,

    /// <summary>shield + exclamation, --wl-warn. The only colour the icon ever takes.</summary>
    NeedsYou,

    /// <summary>shield + slash, --wl-muted at 55%.</summary>
    Paused,
}

/// <param name="LastError">
/// From TickResult. Non-null means the watcher tried and failed — which is what makes NEEDS YOU
/// reachable at all (technical-debt.md 7.3).
/// </param>
public readonly record struct TrayConditions(
    bool AutoBackupEnabled,
    bool IsPaused,
    bool IsCapturing,
    CoreError? LastError);

/// <summary>
/// The tray's entire behaviour, as a pure function. Deliberately not a stored field that
/// something has to remember to update: a derived state cannot go stale.
/// </summary>
public static class TrayState
{
    public static TrayStatus From(TrayConditions conditions)
    {
        // Amber outranks everything. Something the user must act on must not be hidden by a
        // quieter state that also happens to be true.
        if (conditions.LastError is not null) return TrayStatus.NeedsYou;

        // Then whatever is actually happening right now.
        if (conditions.IsCapturing) return TrayStatus.BackingUp;

        // Paused and switched-off both leave nothing watching, and share one icon.
        if (conditions.IsPaused || !conditions.AutoBackupEnabled) return TrayStatus.Paused;

        return TrayStatus.Watching;
    }

    public static string Tooltip(
        TrayConditions conditions,
        DateTimeOffset? lastBackupAt,
        IFormatProvider? culture = null)
    {
        const string Name = "Wave Link Backup";

        if (conditions.LastError is not null) return $"{Name} — {Explain(conditions.LastError)}";

        var when = lastBackupAt is null
            ? "No backup yet"
            : $"last backup {lastBackupAt.Value.ToLocalTime().ToString("HH:mm", culture ?? CultureInfo.CurrentCulture)}";

        return conditions.IsPaused || !conditions.AutoBackupEnabled
            ? $"{Name} — paused · {when}"
            : $"{Name} — {when}";
    }

    /// <summary>
    /// Core's message is written for a log and a CLI; the tray needs the design's shorter
    /// phrasing. Translating here rather than changing CoreError keeps Core's wording intact
    /// for the CLI, which is where the longer form belongs.
    /// </summary>
    private static string Explain(CoreError error) => error switch
    {
        StoreUnavailable => "the backup folder can't be used",
        WaveLinkNotInstalled => "Wave Link wasn't found",
        MultiplePackagesFound => "choose which Wave Link to watch",
        SettingsUnreadable or MalformedSettings => "Wave Link's settings can't be read",
        _ => error.Message,
    };
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/WaveLinkBackup.App.Tests --filter FullyQualifiedName~TrayStateTests`
Expected: PASS, 10 tests.

- [ ] **Step 5: Commit**

```bash
git add src/WaveLinkBackup.App/Hosting/TrayState.cs tests/WaveLinkBackup.App.Tests/TrayStateTests.cs
git commit -m "feat: the tray's four states, as a pure function

Derived rather than stored, so it cannot go stale. NEEDS YOU outranks every
other state: a watcher that is failing must not be hidden by a quieter state
that also happens to be true."
```

---

### Task 4: Autostart, and the veto it must not fight

**Files:**
- Create: `src/WaveLinkBackup.App/Windows/IRegistryKeys.cs`
- Create: `src/WaveLinkBackup.App/Windows/WindowsRegistryKeys.cs`
- Create: `src/WaveLinkBackup.App/Windows/IAutostart.cs`
- Create: `src/WaveLinkBackup.App/Windows/RunKeyAutostart.cs`
- Create: `tests/WaveLinkBackup.App.Tests/Fakes/FakeRegistryKeys.cs`
- Create: `tests/WaveLinkBackup.App.Tests/AutostartTests.cs`

**Interfaces:**
- Produces:
  - `enum AutostartState { Off, On, BlockedByTaskManager }`
  - `interface IAutostart { AutostartState Read(); bool Enable(); void Disable(); }`
  - `interface IRegistryKeys { string? GetString(string keyPath, string name); byte[]? GetBinary(string keyPath, string name); void SetString(string keyPath, string name, string value); void DeleteValue(string keyPath, string name); }`
  - `new RunKeyAutostart(IRegistryKeys registry, string executablePath)`

- [ ] **Step 1: Write the failing tests**

Create `tests/WaveLinkBackup.App.Tests/Fakes/FakeRegistryKeys.cs`:

```csharp
using WaveLinkBackup.App.Windows;

namespace WaveLinkBackup.App.Tests.Fakes;

public sealed class FakeRegistryKeys : IRegistryKeys
{
    private readonly Dictionary<string, object> values = new(StringComparer.OrdinalIgnoreCase);

    private static string Key(string keyPath, string name) => $"{keyPath}::{name}";

    public FakeRegistryKeys WithString(string keyPath, string name, string value)
    {
        values[Key(keyPath, name)] = value;
        return this;
    }

    public FakeRegistryKeys WithBinary(string keyPath, string name, byte[] value)
    {
        values[Key(keyPath, name)] = value;
        return this;
    }

    public string? GetString(string keyPath, string name) =>
        values.TryGetValue(Key(keyPath, name), out var value) ? value as string : null;

    public byte[]? GetBinary(string keyPath, string name) =>
        values.TryGetValue(Key(keyPath, name), out var value) ? value as byte[] : null;

    public void SetString(string keyPath, string name, string value) =>
        values[Key(keyPath, name)] = value;

    public void DeleteValue(string keyPath, string name) => values.Remove(Key(keyPath, name));
}
```

Create `tests/WaveLinkBackup.App.Tests/AutostartTests.cs`:

```csharp
using WaveLinkBackup.App.Tests.Fakes;
using WaveLinkBackup.App.Windows;

namespace WaveLinkBackup.App.Tests;

/// <summary>
/// "If Task Manager has disabled the entry, the toggle reads off and cannot be switched on
/// here. Task Manager wins; the note says so rather than fighting it."
/// </summary>
public sealed class AutostartTests
{
    private const string Exe = @"C:\Program Files\WaveLinkBackup\WaveLinkBackup.exe";

    private static RunKeyAutostart Autostart(FakeRegistryKeys registry) => new(registry, Exe);

    [Fact]
    public void Reads_off_when_there_is_no_run_entry()
    {
        Assert.Equal(AutostartState.Off, Autostart(new FakeRegistryKeys()).Read());
    }

    [Fact]
    public void Reads_on_when_the_run_entry_exists_and_nothing_vetoed_it()
    {
        var registry = new FakeRegistryKeys()
            .WithString(RunKeyAutostart.RunKeyPath, RunKeyAutostart.ValueName, $"\"{Exe}\" --tray");

        Assert.Equal(AutostartState.On, Autostart(registry).Read());
    }

    [Fact]
    public void Enabling_writes_the_exe_with_the_tray_flag()
    {
        var registry = new FakeRegistryKeys();

        Assert.True(Autostart(registry).Enable());

        var written = registry.GetString(RunKeyAutostart.RunKeyPath, RunKeyAutostart.ValueName);
        Assert.Equal($"\"{Exe}\" --tray", written);
    }

    [Fact]
    public void Disabling_removes_the_entry()
    {
        var registry = new FakeRegistryKeys()
            .WithString(RunKeyAutostart.RunKeyPath, RunKeyAutostart.ValueName, $"\"{Exe}\" --tray");

        Autostart(registry).Disable();

        Assert.Null(registry.GetString(RunKeyAutostart.RunKeyPath, RunKeyAutostart.ValueName));
        Assert.Equal(AutostartState.Off, Autostart(registry).Read());
    }

    // 0x03 in the first DWORD is Task Manager's disable; the remaining 8 bytes are a FILETIME.
    private static readonly byte[] Disabled = [0x03, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0];
    private static readonly byte[] Enabled = [0x02, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0];
    private static readonly byte[] AlsoEnabled = [0x06, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0];

    [Fact]
    public void Reads_blocked_when_task_manager_disabled_it()
    {
        var registry = new FakeRegistryKeys()
            .WithString(RunKeyAutostart.RunKeyPath, RunKeyAutostart.ValueName, $"\"{Exe}\" --tray")
            .WithBinary(RunKeyAutostart.ApprovedKeyPath, RunKeyAutostart.ValueName, Disabled);

        Assert.Equal(AutostartState.BlockedByTaskManager, Autostart(registry).Read());
    }

    [Theory]
    [InlineData(0x02)]
    [InlineData(0x06)]
    public void An_approval_record_that_permits_it_still_reads_on(byte first)
    {
        byte[] approval = [first, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0];

        var registry = new FakeRegistryKeys()
            .WithString(RunKeyAutostart.RunKeyPath, RunKeyAutostart.ValueName, $"\"{Exe}\" --tray")
            .WithBinary(RunKeyAutostart.ApprovedKeyPath, RunKeyAutostart.ValueName, approval);

        Assert.Equal(AutostartState.On, Autostart(registry).Read());
    }

    /// <summary>
    /// The heart of it. Writing the Run key while Task Manager holds a veto would produce a
    /// toggle that flips on, does nothing at next login, and flips back — which is worse than
    /// a toggle that honestly refuses.
    /// </summary>
    [Fact]
    public void Enabling_refuses_while_task_manager_holds_the_veto()
    {
        var registry = new FakeRegistryKeys()
            .WithBinary(RunKeyAutostart.ApprovedKeyPath, RunKeyAutostart.ValueName, Disabled);

        Assert.False(Autostart(registry).Enable());
        Assert.Null(registry.GetString(RunKeyAutostart.RunKeyPath, RunKeyAutostart.ValueName));
    }

    [Fact]
    public void A_blocked_entry_reads_blocked_even_with_no_run_entry()
    {
        var registry = new FakeRegistryKeys()
            .WithBinary(RunKeyAutostart.ApprovedKeyPath, RunKeyAutostart.ValueName, Disabled);

        Assert.Equal(AutostartState.BlockedByTaskManager, Autostart(registry).Read());
    }

    /// <summary>A short or empty approval record tells us nothing; do not read it as a veto.</summary>
    [Fact]
    public void A_malformed_approval_record_is_ignored()
    {
        var registry = new FakeRegistryKeys()
            .WithString(RunKeyAutostart.RunKeyPath, RunKeyAutostart.ValueName, $"\"{Exe}\" --tray")
            .WithBinary(RunKeyAutostart.ApprovedKeyPath, RunKeyAutostart.ValueName, []);

        Assert.Equal(AutostartState.On, Autostart(registry).Read());
    }

    [Fact]
    public void Enabling_is_idempotent()
    {
        var registry = new FakeRegistryKeys();
        var autostart = Autostart(registry);

        Assert.True(autostart.Enable());
        Assert.True(autostart.Enable());

        Assert.Equal(AutostartState.On, autostart.Read());
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/WaveLinkBackup.App.Tests --filter FullyQualifiedName~AutostartTests`
Expected: FAIL to compile.

- [ ] **Step 3: Write the registry seam**

Create `src/WaveLinkBackup.App/Windows/IRegistryKeys.cs`:

```csharp
namespace WaveLinkBackup.App.Windows;

/// <summary>
/// The registry, narrowed to what autostart needs. A seam rather than direct Microsoft.Win32
/// calls because the interesting behaviour — the Task Manager veto — is otherwise only testable
/// by writing to the developer's real HKCU and hoping.
/// </summary>
public interface IRegistryKeys
{
    string? GetString(string keyPath, string name);

    byte[]? GetBinary(string keyPath, string name);

    void SetString(string keyPath, string name, string value);

    void DeleteValue(string keyPath, string name);
}
```

Create `src/WaveLinkBackup.App/Windows/WindowsRegistryKeys.cs`:

```csharp
using Microsoft.Win32;

namespace WaveLinkBackup.App.Windows;

/// <summary>HKEY_CURRENT_USER only. Per-user, never per-machine (screens/12).</summary>
public sealed class WindowsRegistryKeys : IRegistryKeys
{
    public string? GetString(string keyPath, string name)
    {
        using var key = Registry.CurrentUser.OpenSubKey(keyPath);
        return key?.GetValue(name) as string;
    }

    public byte[]? GetBinary(string keyPath, string name)
    {
        using var key = Registry.CurrentUser.OpenSubKey(keyPath);
        return key?.GetValue(name) as byte[];
    }

    public void SetString(string keyPath, string name, string value)
    {
        using var key = Registry.CurrentUser.CreateSubKey(keyPath, writable: true);
        key?.SetValue(name, value, RegistryValueKind.String);
    }

    public void DeleteValue(string keyPath, string name)
    {
        using var key = Registry.CurrentUser.OpenSubKey(keyPath, writable: true);
        key?.DeleteValue(name, throwOnMissingValue: false);
    }
}
```

- [ ] **Step 4: Write the autostart**

Create `src/WaveLinkBackup.App/Windows/IAutostart.cs`:

```csharp
namespace WaveLinkBackup.App.Windows;

/// <summary>
/// Three states, not two. The design requires the toggle to READ BACK what Task Manager did
/// rather than fight it, and a boolean cannot express "off, and you may not turn it on".
/// </summary>
public enum AutostartState
{
    Off,
    On,
    BlockedByTaskManager,
}

public interface IAutostart
{
    AutostartState Read();

    /// <summary>Returns false when Task Manager holds a veto. Nothing is written in that case.</summary>
    bool Enable();

    void Disable();
}
```

Create `src/WaveLinkBackup.App/Windows/RunKeyAutostart.cs`:

```csharp
namespace WaveLinkBackup.App.Windows;

/// <summary>
/// Autostart through HKCU\...\Run. Per-user, never per-machine, and never a scheduled task
/// (screens/12-tray-autostart-update.md).
///
/// The complication is that whether the entry actually runs is decided somewhere else. Task
/// Manager's Startup tab does not delete the Run value — it writes an approval record under
/// StartupApproved, and Windows honours that. An app that only looked at the Run key would show
/// a toggle that reads on, does nothing at login, and looks like a bug in this app rather than
/// a choice the user made in Task Manager.
/// </summary>
public sealed class RunKeyAutostart(IRegistryKeys registry, string executablePath) : IAutostart
{
    public const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public const string ApprovedKeyPath =
        @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";

    public const string ValueName = "WaveLinkBackup";

    private string CommandLine => $"\"{executablePath}\" --tray";

    public AutostartState Read()
    {
        // The veto is checked FIRST and independently of the Run entry: Task Manager can hold
        // an approval record for an entry that is not currently present, and the toggle must
        // still show it as blocked rather than as a fresh off.
        if (IsVetoed()) return AutostartState.BlockedByTaskManager;

        return registry.GetString(RunKeyPath, ValueName) is null
            ? AutostartState.Off
            : AutostartState.On;
    }

    public bool Enable()
    {
        if (IsVetoed()) return false;

        registry.SetString(RunKeyPath, ValueName, CommandLine);
        return true;
    }

    public void Disable() => registry.DeleteValue(RunKeyPath, ValueName);

    /// <summary>
    /// The approval record is 12 bytes: a leading DWORD, then a FILETIME of when it was
    /// disabled. 0x02 and 0x06 mean enabled; 0x03 means the user disabled it. The low bit is
    /// the disable flag, which is why this tests the bit rather than listing the values.
    ///
    /// Anything shorter than a byte tells us nothing, and is NOT read as a veto — failing
    /// toward "the user may still turn this on".
    /// </summary>
    private bool IsVetoed()
    {
        var approval = registry.GetBinary(ApprovedKeyPath, ValueName);

        return approval is { Length: > 0 } && (approval[0] & 1) == 1;
    }
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test tests/WaveLinkBackup.App.Tests --filter FullyQualifiedName~AutostartTests`
Expected: PASS, 11 tests.

- [ ] **Step 6: Commit**

```bash
git add src/WaveLinkBackup.App/Windows/ tests/WaveLinkBackup.App.Tests/
git commit -m "feat: autostart through HKCU Run, reading back Task Manager's veto

Three states, not two. Task Manager does not delete the Run value when a user
disables startup — it writes an approval record elsewhere, and Windows honours
that. A toggle that only read the Run key would read on, do nothing at login,
and look like our bug rather than their choice."
```

---

### Task 5: Single instance, and the backup host

Two small pieces that the lifetime in Task 6 needs.

**Files:**
- Create: `src/WaveLinkBackup.App/Startup/SingleInstance.cs`
- Create: `src/WaveLinkBackup.App/Hosting/BackupHost.cs`
- Create: `tests/WaveLinkBackup.App.Tests/SingleInstanceTests.cs`
- Create: `tests/WaveLinkBackup.App.Tests/BackupHostTests.cs`

**Interfaces:**
- Produces:
  - `sealed class SingleInstance : IDisposable` with `static SingleInstance TryAcquire(string name)`, `bool IsFirst`, `void SignalExistingInstance(bool wantsWindow)`, `event EventHandler? ActivationRequested`, `void StartListening()`
  - `sealed class BackupHost : IDisposable` with `TrayConditions Conditions`, `DateTimeOffset? LastBackupAt`, `TickResult Tick()`, `void PauseFor(TimeSpan)`, `void Resume()`, `bool IsPaused`, `TickResult CaptureOnShutdown()`

- [ ] **Step 1: Write the failing tests**

Create `tests/WaveLinkBackup.App.Tests/SingleInstanceTests.cs`:

```csharp
using WaveLinkBackup.App.Startup;

namespace WaveLinkBackup.App.Tests;

/// <summary>
/// Mandatory, not a nicety: two instances means two watchers racing on one settings file.
/// Every test uses a unique name so the suite never collides with itself or a running app.
/// </summary>
public sealed class SingleInstanceTests
{
    private static string UniqueName() => "WaveLinkBackupTests-" + Guid.NewGuid().ToString("N");

    [Fact]
    public void The_first_instance_wins()
    {
        var name = UniqueName();

        using var first = SingleInstance.TryAcquire(name);

        Assert.True(first.IsFirst);
    }

    [Fact]
    public void A_second_instance_knows_it_is_second()
    {
        var name = UniqueName();

        using var first = SingleInstance.TryAcquire(name);
        using var second = SingleInstance.TryAcquire(name);

        Assert.True(first.IsFirst);
        Assert.False(second.IsFirst);
    }

    [Fact]
    public void Releasing_the_first_lets_a_later_one_win()
    {
        var name = UniqueName();

        var first = SingleInstance.TryAcquire(name);
        Assert.True(first.IsFirst);
        first.Dispose();

        using var later = SingleInstance.TryAcquire(name);
        Assert.True(later.IsFirst);
    }

    /// <summary>
    /// The only message is "show yourself", so there is no payload — but a second launch
    /// carrying --tray must be able to exit silently instead of forcing a window open that
    /// nobody asked for.
    /// </summary>
    [Fact]
    public async Task Signalling_with_a_window_request_raises_activation_on_the_first()
    {
        var name = UniqueName();
        using var first = SingleInstance.TryAcquire(name);

        var activated = new TaskCompletionSource();
        first.ActivationRequested += (_, _) => activated.TrySetResult();
        first.StartListening();

        using (var second = SingleInstance.TryAcquire(name))
        {
            second.SignalExistingInstance(wantsWindow: true);
        }

        await activated.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Signalling_without_a_window_request_does_not_raise_activation()
    {
        var name = UniqueName();
        using var first = SingleInstance.TryAcquire(name);

        var activated = new TaskCompletionSource();
        first.ActivationRequested += (_, _) => activated.TrySetResult();
        first.StartListening();

        using (var second = SingleInstance.TryAcquire(name))
        {
            second.SignalExistingInstance(wantsWindow: false);
        }

        var raised = await Task.WhenAny(activated.Task, Task.Delay(TimeSpan.FromMilliseconds(600)));

        Assert.NotSame(activated.Task, raised);
    }
}
```

Create `tests/WaveLinkBackup.App.Tests/BackupHostTests.cs`:

```csharp
using WaveLinkBackup.App.Hosting;
using WaveLinkBackup.Core.Automation;
using WaveLinkBackup.Core.Snapshots;
using WaveLinkBackup.Core.Tests.Fakes;

namespace WaveLinkBackup.App.Tests;

/// <summary>
/// The host owns the coordinator, the timer and pause. Pause is deliberately the host's
/// business: AutoBackupCoordinator owns no timer and waits to be ticked, so pausing is simply
/// not ticking it — putting a pause concept into Core would move a UI affordance into a library
/// that has no UI.
/// </summary>
public sealed class BackupHostTests
{
    private sealed class Harness : IDisposable
    {
        public FakeClock Clock { get; } = new();
        public FakeSettingsWatcher Watcher { get; } = new();
        public BackupHost Host { get; }

        public Harness(BackupService service)
        {
            var coordinator = new AutoBackupCoordinator(Watcher, service, Clock);
            Host = new BackupHost(coordinator, Clock);
        }

        public void Dispose() => Host.Dispose();
    }

    // The plan does not prescribe how to build a BackupService here. Open
    // tests/WaveLinkBackup.Core.Tests/AutoBackupCoordinatorTests.cs, copy how it constructs one
    // against FakeFileSystem, and reuse that shape.

    [Fact]
    public void A_new_host_is_not_paused()
    {
        // Assert Host.IsPaused is false and Conditions.IsPaused is false.
    }

    [Fact]
    public void Pausing_stops_ticks_reaching_the_coordinator()
    {
        // PauseFor(1 hour), then Tick(). Assert nothing was captured and the coordinator's
        // PendingSince is untouched.
    }

    [Fact]
    public void The_pause_expires_on_its_own()
    {
        // PauseFor(1 hour); advance the clock 61 minutes; assert IsPaused is false.
    }

    [Fact]
    public void Resuming_ends_the_pause_early()
    {
        // PauseFor(1 hour); Resume(); assert IsPaused is false.
    }

    [Fact]
    public void A_failing_tick_surfaces_its_error_in_the_conditions()
    {
        // Make the store unwritable, Tick(), assert Conditions.LastError is not null — this is
        // what makes the tray's NEEDS YOU reachable.
    }

    [Fact]
    public void A_successful_tick_clears_a_previous_error()
    {
        // Fail once, then succeed, and assert LastError went back to null.
    }
}
```

> **Implementer:** the six `BackupHostTests` bodies are described rather than written because the plan has not read `AutoBackupCoordinatorTests.cs` in full and must not invent a `BackupService` construction that does not match. **Open that file first**, copy its harness shape, and write these six in that style. Everything else in this plan is complete code; this is the one place to look something up.

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/WaveLinkBackup.App.Tests`
Expected: FAIL to compile.

- [ ] **Step 3: Write SingleInstance**

Create `src/WaveLinkBackup.App/Startup/SingleInstance.cs`:

```csharp
using System.Threading;

namespace WaveLinkBackup.App.Startup;

/// <summary>
/// Mandatory rather than polite: two instances means two watchers racing on one settings file.
///
/// A Mutex detects; named events activate. There is no IPC payload because the only message is
/// "show yourself" — but there are TWO events, so a second launch carrying --tray can exit
/// silently rather than forcing open a window nobody asked for.
///
/// Local\ rather than Global\: settings and the store are per-user, so two people signed into
/// one machine should each get an instance. The race being prevented is two watchers over ONE
/// user's file.
/// </summary>
public sealed class SingleInstance : IDisposable
{
    private readonly Mutex mutex;
    private readonly EventWaitHandle showEvent;
    private readonly CancellationTokenSource listening = new();
    private bool disposed;

    private SingleInstance(Mutex mutex, bool isFirst, EventWaitHandle showEvent)
    {
        this.mutex = mutex;
        this.showEvent = showEvent;
        IsFirst = isFirst;
    }

    public bool IsFirst { get; }

    /// <summary>Raised on the FIRST instance when a later launch asks for the window.</summary>
    public event EventHandler? ActivationRequested;

    public static SingleInstance TryAcquire(string name)
    {
        var mutex = new Mutex(initiallyOwned: true, $@"Local\{name}.instance", out var createdNew);

        var showEvent = new EventWaitHandle(
            initialState: false, EventResetMode.AutoReset, $@"Local\{name}.show");

        return new SingleInstance(mutex, createdNew, showEvent);
    }

    /// <summary>Starts watching for later launches. Only the first instance should call this.</summary>
    public void StartListening()
    {
        var thread = new Thread(WaitLoop)
        {
            IsBackground = true,
            Name = "WaveLinkBackup single-instance listener",
        };

        thread.Start();
    }

    /// <param name="wantsWindow">
    /// False for a --tray launch. Signalling nothing at all is what lets a second --tray exit
    /// without disturbing the running instance.
    /// </param>
    public void SignalExistingInstance(bool wantsWindow)
    {
        if (wantsWindow) showEvent.Set();
    }

    private void WaitLoop()
    {
        var handles = new WaitHandle[] { showEvent, listening.Token.WaitHandle };

        while (!listening.IsCancellationRequested)
        {
            if (WaitHandle.WaitAny(handles) != 0) return;

            ActivationRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;

        listening.Cancel();

        if (IsFirst)
        {
            try { mutex.ReleaseMutex(); }
            catch (ApplicationException) { /* never owned it */ }
        }

        mutex.Dispose();
        showEvent.Dispose();
        listening.Dispose();
    }
}
```

- [ ] **Step 4: Write BackupHost**

Create `src/WaveLinkBackup.App/Hosting/BackupHost.cs`:

```csharp
using WaveLinkBackup.Core.Abstractions;
using WaveLinkBackup.Core.Automation;
using WaveLinkBackup.Core.Results;

namespace WaveLinkBackup.App.Hosting;

/// <summary>
/// The host the coordinator has been waiting for since phase 3. AutoBackupCoordinator owns no
/// timer and holds two timestamps; something has to call Tick(). In the CLI that was the watch
/// verb's loop. Here it is this, driven by a DispatcherTimer that App owns.
///
/// Pause lives HERE and not in Core. Pausing is simply not ticking, so it costs no Core change;
/// putting a pause concept into AutoBackupPolicy would move a UI affordance into a library that
/// has no UI (ADR-004).
/// </summary>
public sealed class BackupHost(AutoBackupCoordinator coordinator, IClock clock) : IDisposable
{
    private DateTimeOffset? pausedUntil;
    private bool disposed;

    public bool AutoBackupEnabled { get; set; } = true;

    public DateTimeOffset? LastBackupAt { get; private set; }

    public CoreError? LastError { get; private set; }

    public bool IsCapturing { get; private set; }

    public bool IsPaused => pausedUntil is { } until && clock.UtcNow < until;

    public TrayConditions Conditions =>
        new(AutoBackupEnabled, IsPaused, IsCapturing, LastError);

    public void Start() => coordinator.Start();

    public void Stop() => coordinator.Stop();

    public void PauseFor(TimeSpan duration) => pausedUntil = clock.UtcNow + duration;

    public void Resume() => pausedUntil = null;

    /// <summary>
    /// Called by the host timer. Cheap when nothing is due — only a Capture decision touches
    /// disk — so the shell can call it as often as it likes.
    /// </summary>
    public TickResult Tick()
    {
        if (IsPaused || !AutoBackupEnabled) return new TickResult(CaptureDecision.NotDue, null);

        IsCapturing = true;
        try
        {
            var result = coordinator.Tick();
            Record(result);
            return result;
        }
        finally
        {
            IsCapturing = false;
        }
    }

    /// <summary>
    /// Ignores the debounce and the rate limit. The original incident happened during an
    /// update, while the machine was restarting — a strategy that only captures during
    /// steady-state operation misses the exact moment that matters.
    /// </summary>
    public TickResult CaptureOnShutdown()
    {
        var result = coordinator.CaptureOnShutdown();
        Record(result);
        return result;
    }

    private void Record(TickResult result)
    {
        // A successful tick clears a stale error, so the tray leaves NEEDS YOU on its own once
        // the folder comes back. Requiring a restart to clear it would be its own bug report.
        LastError = result.Error;

        if (result.Captured) LastBackupAt = clock.UtcNow;
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;

        coordinator.Dispose();
    }
}
```

> `CaptureDecision.NotDue` may not be the real member name. Open `src/WaveLinkBackup.Core/Automation/AutoBackupPolicy.cs` and use whatever the enum actually calls "nothing to do".

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test tests/WaveLinkBackup.App.Tests`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/WaveLinkBackup.App/ tests/WaveLinkBackup.App.Tests/
git commit -m "feat: single instance, and a host for the coordinator

Two named events rather than one, so a second launch carrying --tray exits
silently instead of forcing open a window nobody asked for.

Pause lives in the host: the coordinator owns no timer and waits to be ticked,
so pausing is not ticking it, and Core gains no UI concept."
```

---

### Task 6: The lifetime, and the icon in the tray

The WPF task. Little of this is unit-testable; the pieces that were are already done.

**Files:**
- Create: `src/WaveLinkBackup.App/Views/TrayIconRenderer.cs`
- Create: `src/WaveLinkBackup.App/Views/TrayIcon.xaml` + `.cs`
- Modify: `src/WaveLinkBackup.App/App.xaml.cs`

- [ ] **Step 1: Write the icon renderer**

Create `src/WaveLinkBackup.App/Views/TrayIconRenderer.cs`. The four glyphs are a shield plus a state mark, drawn as geometry so they can be recoloured per state and per system contrast.

```csharp
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WaveLinkBackup.App.Hosting;

namespace WaveLinkBackup.App.Views;

/// <summary>
/// The tray icon is GENERATED, not shipped as four .ico files.
///
/// screens/11-high-contrast.md: "The tray icon follows the system icon contrast." A static icon
/// cannot do that — the taskbar's theme is the system's, which is not necessarily the app's, and
/// in high contrast the colours are not ours at all. Drawing it means the glyph is always
/// rendered against whatever the taskbar currently is.
///
/// No count badges: "a tray icon that says 3 invites a stressed user to guess what three of".
/// </summary>
public static class TrayIconRenderer
{
    /// <summary>
    /// Lucide idiom — 24px grid, monoline. README §icons says to substitute the codebase's real
    /// icon set at the same weight and size; there is no icon set yet, so these are drawn to the
    /// same grid and should be replaced with the real shield-check mark when one exists.
    /// </summary>
    private const string ShieldPath =
        "M12 2 L20 5 V11 C20 16 16.5 19.5 12 21 C7.5 19.5 4 16 4 11 V5 Z";

    private const string CheckPath = "M8.5 12 L11 14.5 L15.5 9.5";
    private const string ArrowPath = "M12 8 V14.5 M9 12 L12 15 L15 12";
    private const string BangPath = "M12 7.5 V13 M12 15.5 V16.5";
    private const string SlashPath = "M6 18 L18 6";

    public static ImageSource Render(TrayStatus status, Color colour, int pixelSize = 32)
    {
        var pen = new Pen(new SolidColorBrush(colour), 1.75) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
        pen.Freeze();

        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            // The 24px design grid scaled to the requested pixel size.
            context.PushTransform(new ScaleTransform(pixelSize / 24.0, pixelSize / 24.0));

            context.DrawGeometry(null, pen, Geometry.Parse(ShieldPath));
            context.DrawGeometry(null, pen, Geometry.Parse(MarkFor(status)));

            context.Pop();
        }

        var bitmap = new RenderTargetBitmap(pixelSize, pixelSize, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();

        return bitmap;
    }

    private static string MarkFor(TrayStatus status) => status switch
    {
        TrayStatus.Watching => CheckPath,
        TrayStatus.BackingUp => ArrowPath,
        TrayStatus.NeedsYou => BangPath,
        TrayStatus.Paused => SlashPath,
        _ => CheckPath,
    };

    /// <summary>
    /// Amber is the only colour the icon ever takes, and it means what it means everywhere else:
    /// something is not whole. In high contrast amber means nothing, so NEEDS YOU becomes
    /// WindowText and PAUSED becomes GrayText at FULL opacity — never the 55% used in the normal
    /// themes, because transparency is not a contrast guarantee (screens/11).
    /// </summary>
    public static Color ColourFor(TrayStatus status, bool highContrast)
    {
        if (highContrast)
        {
            return status == TrayStatus.Paused
                ? SystemColors.GrayTextColor
                : SystemColors.WindowTextColor;
        }

        var key = status switch
        {
            TrayStatus.NeedsYou => "WlWarn",
            TrayStatus.Paused => "WlMuted",
            _ => "WlText",
        };

        var brush = (SolidColorBrush)Application.Current.Resources[key];
        var colour = brush.Color;

        // The deliberate exception to the 40%-disabled rule: the icon is not a disabled
        // control, it is a state.
        if (status == TrayStatus.Paused) colour.A = (byte)(255 * 0.55);

        return colour;
    }
}
```

- [ ] **Step 2: Build the tray icon and its menu**

Create `src/WaveLinkBackup.App/Views/TrayIcon.xaml`. The menu is the design's, in order, with the readout header as a non-interactive item.

```xml
<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                    xmlns:tb="clr-namespace:H.NotifyIcon;assembly=H.NotifyIcon.Wpf">

    <ContextMenu x:Key="TrayMenu">
        <!-- A readout, not an item. -->
        <MenuItem x:Name="LastBackupHeader" Header="LAST BACKUP" IsEnabled="False" />
        <Separator />
        <MenuItem x:Name="BackUpNow" Header="Back up now" FontWeight="Medium" />
        <MenuItem x:Name="OpenApp" Header="Open Wave Link Backup" />
        <MenuItem x:Name="OpenFolder" Header="Open the backup folder" />
        <Separator />
        <MenuItem x:Name="AutoBackup" Header="Back up automatically" IsCheckable="True" />
        <MenuItem x:Name="PauseResume" Header="Pause for an hour" />
        <Separator />
        <MenuItem x:Name="OpenSettings" Header="Settings…" />
        <!-- Quitting stops the backups, and the item says so rather than a dialog afterwards. -->
        <MenuItem x:Name="Quit" Header="Quit — stops backing up" />
    </ContextMenu>

</ResourceDictionary>
```

> Wire the click handlers in `App.xaml.cs` rather than a code-behind class — the tray has no state of its own; it renders `BackupHost`.
>
> Create the `TaskbarIcon` in code in `App.xaml.cs` so its lifetime is explicitly `App`'s, and set a **fixed GUID** via `TaskbarIcon.Id` — H.NotifyIcon derives its default GUID from the executable path, so the icon's registered settings reset when the exe moves.

- [ ] **Step 3: Write the lifetime**

Replace `src/WaveLinkBackup.App/App.xaml.cs`. This is the shape; fill in the handler bodies as you go.

```csharp
using System.Windows;
using System.Windows.Threading;
using H.NotifyIcon;
using WaveLinkBackup.App.Hosting;
using WaveLinkBackup.App.Startup;
using WaveLinkBackup.App.Theming;
using WaveLinkBackup.App.Windows;
using WaveLinkBackup.Core.Abstractions;
using WaveLinkBackup.Core.Automation;
using WaveLinkBackup.Core.Discovery;
using WaveLinkBackup.Core.Io;
using WaveLinkBackup.Core.Snapshots;

namespace WaveLinkBackup.App;

/// <summary>
/// The app is the PROCESS, not the window.
///
/// "Configured once, then ignored — so it lives in the tray and the window is the exception."
/// If closing the window stopped the backups, the app would fail its own promise and become
/// upstream's tool with extra steps. So: OnExplicitShutdown, the coordinator lives here and
/// outlives every window, and closing hides.
/// </summary>
public partial class App : Application
{
    private const string InstanceName = "WaveLinkBackup";
    private static readonly Guid TrayIconId = new("2f8b6f4e-9d3a-4c17-9b52-6a1d4f0e7c38");

    private SingleInstance? instance;
    private BackupHost? host;
    private TaskbarIcon? tray;
    private DispatcherTimer? timer;
    private SettingsRepository? settingsRepository;
    private BackupSettings settings = BackupSettings.Default;
    private bool shuttingDown;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var arguments = ShellArguments.Parse(e.Args);
        if (!arguments.IsValid)
        {
            MessageBox.Show(arguments.Error, "Wave Link Backup", MessageBoxButton.OK, MessageBoxImage.Warning);
            Shutdown(1);
            return;
        }

        // BEFORE any Core object is built: a second instance must cost nothing.
        instance = SingleInstance.TryAcquire(InstanceName);
        if (!instance.IsFirst)
        {
            instance.SignalExistingInstance(wantsWindow: !arguments.StartInTray);
            Shutdown(0);
            return;
        }

        instance.ActivationRequested += (_, _) => Dispatcher.Invoke(ShowMainWindow);
        instance.StartListening();

        // Set before anything exists that could close.
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        ThemeManager.Apply(ThemeManager.DetectFromSystem());

        var fileSystem = new FileSystem();
        settingsRepository = new SettingsRepository(fileSystem, SettingsRepository.DefaultDirectory);
        settings = arguments.ApplyTo(settingsRepository.Read());

        host = BuildHost(fileSystem, settings);
        host.AutoBackupEnabled = settings.AutoBackupEnabled;
        host.Start();

        tray = BuildTray();

        timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(15),
        };
        timer.Tick += (_, _) => { host.Tick(); RefreshTray(); };
        timer.Start();

        // Windows shutting down is a shutdown path too — and the ORIGINAL INCIDENT happened
        // during an update, while the machine was restarting. A shell that only captures on a
        // deliberate Quit misses the exact case CaptureOnShutdown was written for.
        SessionEnding += (_, _) => ShutdownEverything();

        if (!arguments.StartInTray) ShowMainWindow();

        RefreshTray();
    }

    private static BackupHost BuildHost(IFileSystem fileSystem, BackupSettings settings)
    {
        var clock = new SystemClock();
        var inspector = SettingsInspector.For(fileSystem, SettingsLocator.SystemLocalAppData);
        var store = new SnapshotStore(fileSystem, clock, settings.StorePath);

        var service = new BackupService(
            inspector, store, settings.AutoBackupKeepCount, settings.ChosenWaveLinkPath);

        var live = inspector.Inspect(settings.ChosenWaveLinkPath);
        var watchPath = live.IsSuccess
            ? live.Value.Location.LocalStatePath
            : SettingsLocator.SystemLocalAppData;

        var coordinator = new AutoBackupCoordinator(
            new FileSystemSettingsWatcher(watchPath), service, clock);

        return new BackupHost(coordinator, clock);
    }

    private TaskbarIcon BuildTray()
    {
        var menu = (System.Windows.Controls.ContextMenu)Resources["TrayMenu"];

        var icon = new TaskbarIcon
        {
            Id = TrayIconId,          // fixed, so the icon survives the exe moving
            ContextMenu = menu,
            MenuActivation = PopupActivationMode.RightClick,
        };

        icon.TrayLeftMouseUp += (_, _) => ShowMainWindow();

        return icon;
    }

    private void RefreshTray()
    {
        if (tray is null || host is null) return;

        var status = TrayState.From(host.Conditions);
        var colour = TrayIconRenderer.ColourFor(status, SystemParameters.HighContrast);

        tray.IconSource = TrayIconRenderer.Render(status, colour);
        tray.ToolTipText = TrayState.Tooltip(host.Conditions, host.LastBackupAt);
    }

    private void ShowMainWindow()
    {
        MainWindow ??= new MainWindow();

        // Closing HIDES it, so a window that exists may simply be invisible.
        MainWindow.Show();
        if (MainWindow.WindowState == WindowState.Minimized) MainWindow.WindowState = WindowState.Normal;
        MainWindow.Activate();
    }

    /// <summary>
    /// The single exit. Three entrances reach it: the tray's Quit, closing the window when
    /// "closing hides it" is off, and Windows ending the session.
    /// </summary>
    internal void ShutdownEverything()
    {
        if (shuttingDown) return;
        shuttingDown = true;

        timer?.Stop();
        host?.Stop();
        host?.CaptureOnShutdown();

        tray?.Dispose();
        host?.Dispose();
        instance?.Dispose();

        Shutdown(0);
    }
}
```

- [ ] **Step 4: Make closing the window hide it**

In `src/WaveLinkBackup.App/MainWindow.xaml.cs`:

```csharp
using System.ComponentModel;

namespace WaveLinkBackup.App;

public partial class MainWindow : System.Windows.Window
{
    public MainWindow() => InitializeComponent();

    /// <summary>
    /// Closing hides. The app is the process, not this window — if closing it stopped the
    /// backups, the app would fail its own promise.
    ///
    /// The setting that turns this off lives in the shell's own file, not in BackupSettings:
    /// Core has no window to hide (plan 3 builds the Settings UI for it).
    /// </summary>
    protected override void OnClosing(CancelEventArgs e)
    {
        e.Cancel = true;
        Hide();

        base.OnClosing(e);
    }
}
```

- [ ] **Step 5: Wire the menu handlers**

In `App.xaml.cs`, after building the tray, attach handlers by walking the menu items. Each is a small body:

| Item | Behaviour |
|---|---|
| `LastBackupHeader` | Not clickable. Update its `Header` in `RefreshTray` to the design's readout |
| `Back up now` | Call the backup service's manual capture, then `RefreshTray()` |
| `Open Wave Link Backup` | `ShowMainWindow()` |
| `Open the backup folder` | `Process.Start("explorer.exe", settings.StorePath)` |
| `Back up automatically` | Toggle `host.AutoBackupEnabled`, persist through `settingsRepository.Save`, `RefreshTray()` |
| `Pause for an hour` | `host.PauseFor(TimeSpan.FromHours(1))`; the item's header becomes `Resume` while paused |
| `Settings…` | Open the (stub) settings window — plan 5 builds it |
| `Quit — stops backing up` | `ShutdownEverything()` |

- [ ] **Step 6: Build and run it**

Run: `dotnet build WaveLinkBackup.slnx`
Then: `dotnet run --project src/WaveLinkBackup.App`

Check by hand — none of this is unit-testable:

- [ ] The window appears, and an icon appears in the tray
- [ ] Closing the window leaves the process running with the icon still there
- [ ] Left-clicking the icon brings the window back
- [ ] Right-clicking shows the designed menu in the designed order
- [ ] `dotnet run --project src/WaveLinkBackup.App -- --tray` starts with **no** window
- [ ] Launching a second time while one runs shows the first one's window rather than starting again
- [ ] Launching a second time with `--tray` does nothing visible
- [ ] *Quit — stops backing up* ends the process and removes the icon
- [ ] **Restart Explorer from Task Manager — the icon comes back** (the thing naive implementations miss)

- [ ] **Step 7: Run the whole suite**

Run: `dotnet test WaveLinkBackup.slnx`
Then: `dotnet build WaveLinkBackup.slnx -c Release`
Expected: PASS, zero warnings.

- [ ] **Step 8: Commit**

```bash
git add src/WaveLinkBackup.App/
git commit -m "feat: the tray shell — the app is the process, not the window

ShutdownMode.OnExplicitShutdown, closing hides, and the coordinator lives in App
and outlives every window. Three shutdown entrances share one exit, including
SessionEnding: the original incident happened during an update while the machine
was restarting, and a shell that only captures on a deliberate Quit misses the
exact case CaptureOnShutdown exists for.

The icon is generated rather than shipped, because high contrast requires it to
follow the system icon contrast."
```

---

## Done when

- [ ] `dotnet build WaveLinkBackup.slnx -c Release` — zero warnings
- [ ] `dotnet test WaveLinkBackup.slnx` — all green, **≥ 425** tests
- [ ] Closing the window does not stop the backups
- [ ] Two instances cannot run at once
- [ ] The tray icon survives an Explorer restart
- [ ] `RunKeyAutostart.Enable()` refuses while Task Manager holds a veto

## What this plan does not do

Live OS theme following, the accent derivation, and Mica are **plan 3** — the dictionaries exist
here, but nothing reacts to the OS changing yet. The list is **plan 4**. The Settings dialog,
including the autostart toggle that `IAutostart` was built for, is **plan 5**; until then
autostart has no UI and is reachable only from tests.
