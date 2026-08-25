---
title: "The tray menu keeps the theme it started with"
status: published
created: 2026-08-17
updated: 2026-08-17
tags: [gotcha, wpf, theming, tray]
---

# The tray menu keeps the theme it started with

**Provenance:** **Experienced.** Hit while building phase 5 plan 3. Caught by
`TrayMenuStyleTests.The_menus_colours_follow_the_theme_rather_than_being_baked_in`, which was
written before the fix and failed on two plausible workarounds before the real one was found.

## Symptom

The app follows Windows dark/light correctly, the window recolours the moment the OS changes,
with no restart. **The tray's context menu does not.** It keeps whichever theme was current when
the app started, for the life of the process.

Nothing errors. `ThemeManager.Apply` runs, `Application.Current.Resources["WlBg"]` returns the new
value, and the menu is still wrong.

## Cause

`DynamicResource` is not a subscription to a dictionary. It is resolved through the **element
tree**: when `Application.Resources` changes, WPF walks the loaded windows and invalidates the
`DynamicResource` references it finds on the way down.

A tray icon's `ContextMenu` is not on that path. It is created from a `ResourceDictionary` and
handed to `TaskbarIcon.ContextMenu`, and the `TaskbarIcon` itself is never loaded into a visual
tree. That is why it needs `ForceCreate()` at all. So the menu has **no parent in any tree**, the
invalidation never reaches it, and its `DynamicResource`s resolve exactly once: when the dictionary
is loaded.

This is not specific to tray menus. Anything reachable only from `Application.Resources`, a
detached popup, a control built in code and never parented, an object held in a static, has the
same problem.

## What does not fix it

Both of these look right and neither works. They were tried in that order:

| Attempt | Why it fails |
|---|---|
| Close the menu and reopen it | Reopening realises the popup again but reuses the same `ContextMenu` instance. Its resource references were already resolved and are not re-evaluated. |
| `menu.UpdateLayout()` after the swap | Layout is not resource resolution. There is nothing marked dirty to recompute. |

`SetResourceReference` on individual properties does work, but only for the properties you
remember to list, and not for anything inside a `ControlTemplate`. That is a fix that decays.

## The fix

**Rebuild the menu from its XAML on every theme change.** A freshly loaded `ResourceDictionary`
resolves against whatever `Application.Resources` currently holds, so a new instance is simply
correct.

```csharp
var dictionary = new ResourceDictionary
{
    Source = new Uri(
        "pack://application:,,,/WaveLinkBackup;component/Views/TrayIcon.xaml",
        UriKind.Absolute),
};

trayMenu = (ContextMenu)dictionary["TrayMenu"];
WireMenu(trayMenu);                  // handlers belong to the new instance
tray.ContextMenu = trayMenu;
```

See `App.RebuildTrayMenu`. Three things follow from it:

- **Re-wire the click handlers.** They were attached to the discarded instance.
- **Rebuild *before* refreshing state.** Anything that writes into the menu's items, a checked
  toggle, a header readout, must run against the new instance, or it lands on the one being
  thrown away. `App.OnThemeChanged` orders it that way deliberately.
- **Do not also merge the dictionary into `App.xaml`.** Two paths to the same menu, one of them a
  cached instance that is never rebuilt, is the bug again with extra steps.

## Why the test is worth keeping

The failure is invisible to the compiler, invisible in a single-theme session, and needs someone
to change their OS theme *while the app is running* to notice. Asserting that a freshly loaded
menu carries the current theme's colours costs three lines and is the only thing standing between
this and a silent regression.

## See also

- `src/WaveLinkBackup.App/App.xaml.cs`, `RebuildTrayMenu`, `OnThemeChanged`
- [patterns/named-method-seams.md](../patterns/named-method-seams.md)
