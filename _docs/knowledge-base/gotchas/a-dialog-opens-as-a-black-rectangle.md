---
title: "A dialog opens as a black rectangle"
status: published
created: 2026-08-19
updated: 2026-08-19
tags: [gotcha, wpf, dialogs, theming]
---

# A dialog opens as a black rectangle

**Provenance:** **Experienced.** Reported by the user against the shipped 0.5.0 shell, "the
delete dialog brings up a big black background". Reproduced immediately; all five dialogs had it.
Now pinned by `DialogOverlayTests.Every_dialog_is_layered_so_its_scrim_is_really_transparent`.

## Symptom

A modal opens as a large, opaque black rectangle with the dialog card floating in the middle of
it. The card itself is correct, right size, right colours, right copy. Everything around it is
black, and the app behind is not visible at all.

No exception, no binding error, no warning. It looks like a deliberate, very heavy scrim.

## Cause

Two things, and only together do they produce this:

**1. `Background="Transparent"` on a Window does nothing unless the window is layered.** WPF only
honours per-pixel transparency when `AllowsTransparency="True"`, which sets `WS_EX_LAYERED`.
Without it the window is an ordinary opaque HWND and "Transparent" resolves to black. The dialogs
set `WindowStyle="None"` and `Background="Transparent"` and left `AllowsTransparency` at its
default of false, so the `--wl-scrim` fill inside was compositing onto **black**, not onto the
app.

**2. No `Width`/`Height` and `SizeToContent="Manual"`.** With neither set, the window takes
Windows' default size. So the black area was not even card-sized; it was an arbitrary rectangle
with a card centred in it.

The comment in every one of those files asserted the opposite, that `AllowsTransparency` was
deliberately false "so DWM gives it a drop shadow instead of the card drawing one". That is a true
statement about drop shadows and an irrelevant one about scrims, and it is why the setting went
unquestioned for a whole phase.

## What does not fix it

| Attempt | Why it fails |
|---|---|
| Lower the scrim's opacity | The scrim is not the problem; what it composites onto is. A lighter scrim over black is grey, not translucent. |
| Set `Background="{x:Null}"` instead of `Transparent` | Same layer question. A non-layered window has no alpha channel to write into. |
| Give the window `SizeToContent="WidthAndHeight"` | Fixes the *size* so the black is only card-sized, which hides the symptom and leaves the app undimmed. It also throws away the scrim entirely. |

## The fix

Three things, and each is load-bearing:

1. **`AllowsTransparency="True"`** on the dialog, so its transparent regions really are
   transparent.
2. **Size and position it over its owner**, `DialogOverlay.Cover`. A scrim that does not reach
   the owner's edges reads as a panel, not a modal.
3. **Draw the card's shadow yourself.** A layered window gets no DWM shadow. README specifies
   `0 30px 70px rgba(0,0,0,.5)` anyway, which the system shadow never matched.

```xml
<Window WindowStyle="None" ResizeMode="NoResize"
        AllowsTransparency="True"
        WindowStartupLocation="Manual"
        Background="Transparent">
```

**`WindowStartupLocation` must become `Manual`.** `CenterOwner` re-centres a window that is
already owner-*sized*, pushing it off by half the owner.

## The one that does not follow this

**`MainWindow` must stay unlayered.** `AllowsTransparency="True"` makes DWM silently ignore the
Mica backdrop, the call still succeeds and nothing looks different, which is
[its own trap](../../technical-debt.md). `DialogOverlayTests.The_main_window_is_not_a_dialog_and_stays_unlayered`
states it from the dialog side; `MainWindowTemplateTests` states it from the window's own.

## Frosting, and its honest limits

Blurring what shows through needs `SetWindowCompositionAttribute`, undocumented, and the only
route that blurs the *window behind* rather than the desktop material. `DwmSetWindowAttribute`'s
system backdrops (Mica, Acrylic) composite the **wallpaper**, which is not what a modal wants.

It is safe to depend on only because it is allowed to fail: nothing throws, the return value is
advisory, and the scrim guarantees a dimmed owner regardless. See `AcrylicDialogBackdrop`.

## See also

- `src/WaveLinkBackup.App/Windows/DialogOverlay.cs` · `AcrylicDialogBackdrop.cs`
- [the-window-never-opens-and-nothing-says-why.md](the-window-never-opens-and-nothing-says-why.md),
the other class of dialog failure found in the same audit
- The 0.5.1 design audit, 2026-08-19
