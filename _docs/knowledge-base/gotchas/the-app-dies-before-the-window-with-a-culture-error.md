---
title: "The app dies before the window, with a culture error"
status: published
created: 2026-08-23
updated: 2026-08-23
tags: [gotcha, wpf, globalization, build-config, startup]
---

# The app dies before the window, with a culture error

**Provenance:** **Experienced**, once, on the dev machine, the tray app would not start after a
publish. The event log named the fault precisely; the plausible explanation (a font or locale
problem on the box) was wrong, and the real cause sat in our own `.csproj`. Now pinned by the
absence of `<InvariantGlobalization>` in `WaveLinkBackup.App.csproj`, reintroducing it brings the
crash back on any machine, which is why this is written down rather than left as a comment.

## Symptom

The app does not start. No window, no tray icon, no dialog. The only trace is in the Windows
Application event log:

```
System.TypeInitializationException: The type initializer for
'MS.Internal.FontCache.MajorLanguages' threw an exception.
 ---> System.Globalization.CultureNotFoundException: Only the invariant culture is supported in
globalization-invariant mode. (Parameter 'name')  en is an invalid culture identifier.
    at System.Globalization.CultureInfo..ctor(String name, Boolean useUserOverride)
    at MS.Internal.FontCache.MajorLanguages..cctor()
```

The stack runs up through `TextBlock.MeasureOverride` → `Window.Show()` → `App.ShowMainWindow()` →
`App.OnStartup`. It looks like a font or rendering failure, which is the wrong place to start
looking.

## Cause

`<InvariantGlobalization>true</InvariantGlobalization>` in the project file, which emits
`"System.Globalization.Invariant": true` into the published `runtimeconfig.json`.

It was added for a real reason. WPF ships localized strings for 13 cultures (~9 MB of satellite
resource assemblies) and the UI is English-only, so dropping them shrinks the publish. The mistake
was the *mechanism*. Invariant globalization is not "no satellite folders"; it restricts the **entire
process** to a single culture. `en`, `nb-NO`, every named culture becomes invalid.

WPF's font cache does not know or care about that policy. `MS.Internal.FontCache.MajorLanguages`
constructs `CultureInfo("en")` in its static constructor, and the static constructor runs the moment
the first `TextBlock` is measured, which is the first thing `Window.Show()` does. So the app dies
inside layout, before any of our code after `ShowMainWindow()` runs, and before a crash handler can
do anything useful with a window that never existed.

## What does not fix it

| Attempt | Why it fails |
|---|---|
| Reinstall fonts / check the machine's locale | The machine is fine. The runtime was told there is only one culture, so no font list will satisfy it. |
| Catch the exception around `Show()` | The throw is inside WPF's measure pass on the UI thread; by the time it surfaces the window is already gone, and a tray app has no window to fall back to. |
| Assume it is a .NET 10 regression | It reproduces identically on any runtime with invariant mode on. The variable is the config flag, not the framework. |

## The fix

Replace the process-wide switch with the targeted one that does only what was actually wanted,
drop the non-English satellite assemblies while leaving full globalization (and therefore working
text rendering) intact:

```xml
<!-- Before: kills WPF's font cache -->
<InvariantGlobalization>true</InvariantGlobalization>

<!-- After: English-only publish, real cultures at runtime -->
<SatelliteResourceLanguages>en</SatelliteResourceLanguages>
```

`SatelliteResourceLanguages` trims the *resources*; `InvariantGlobalization` rewrites how the
runtime resolves *cultures*. Same apparent goal (a smaller, English-only publish), one of them
breaks the app. The ~9 MB of satellite assemblies come back in a form that does not crash, and if
they must go entirely later, that is a publish-size conversation, not a reason to put the runtime in
invariant mode.

## Why the crash report still matters here

This particular fault fires inside WPF's layout pass, which is *after* `App.OnStartup` has installed
the dispatcher and AppDomain handlers but the window never completes, so the report that lands in
`%LOCALAPPDATA%\WaveLinkBackup\crash-report.txt` names the line and the environment even though no
UI ever appeared. That is the whole point of [[the-window-never-opens-and-nothing-says-why]]'s
sister case: a startup fault that leaves a file to read instead of a mystery to bisect.

## See also

- [the-window-never-opens-and-nothing-says-why.md](the-window-never-opens-and-nothing-says-why.md),
  the same "no window, no message" symptom from XAML value-application throws one layer up; this one
  is a build-config fault one layer down
- [technical-debt.md](../../technical-debt.md) §8.1 / §8.1a, the crash report this entry relies on
