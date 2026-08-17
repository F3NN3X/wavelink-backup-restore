---
title: "The tray icon refuses every image you draw"
status: published
created: 2026-08-17
updated: 2026-08-17
tags: [gotcha, wpf, tray, interop]
---

# The tray icon refuses every image you draw

**Provenance:** **Experienced.** Cost two failed launches while building phase 5 plan 2. The
design requires the tray glyph to be *generated* rather than shipped as `.ico` files, because
`screens/11-high-contrast.md` says the icon follows the system icon contrast — which no static
file can do.

## Symptom

The app builds cleanly and dies on its first tray refresh:

```
Unhandled exception. System.NotImplementedException:
  ImageSource type: System.Windows.Media.Imaging.RenderTargetBitmap is not supported
   at H.NotifyIcon.ImageExtensions.ToStreamAsync(ImageSource, CancellationToken)
```

Wrap the bitmap in a `BitmapFrame`, which the library's own metadata says it understands, and the
throw simply moves:

```
Unhandled exception. System.UriFormatException:
  Invalid URI: The format of the URI could not be determined.
   at H.NotifyIcon.ImageExtensions.ToStreamAsync(ImageSource, CancellationToken)
```

Both are `WinExe` crashes with no console, so they are only visible if stderr is redirected.

## Cause

`TaskbarIcon.IconSource` accepts an `ImageSource`, and the type says nothing about the real
constraint. H.NotifyIcon converts one by taking **`new Uri(imageSource.ToString())`** — so it only
supports images that *came from a URI*. A `BitmapImage` with a `UriSource`, or a `BitmapFrame`
loaded from a pack URI, stringifies back to that URI and works.

A generated glyph has no URI and never will. `RenderTargetBitmap` falls through to an explicit
`NotImplementedException`; a `BitmapFrame` wrapping one reaches the `Uri` constructor with
something that is not a URI. There is no wrapping that helps, because the missing thing is
provenance, not type.

## The fix

Bypass `IconSource` and set **`TaskbarIcon.Icon`**, which takes a `System.Drawing.Icon`. Encode
the drawing to PNG and wrap it in a one-entry ICO container:

```csharp
var png = new MemoryStream();
var encoder = new PngBitmapEncoder();
encoder.Frames.Add(BitmapFrame.Create(bitmap));   // bitmap is the RenderTargetBitmap
encoder.Save(png);

return IconFrom(png.ToArray(), pixelSize);        // 6-byte ICONDIR + 16-byte entry + payload
```

A PNG-compressed icon entry is the format's own idiom for the larger sizes on Vista and later, so
this is not a trick. It also avoids `Bitmap.GetHicon`, whose handle you then have to remember to
`DestroyIcon`.

See `TrayIconRenderer.Render` and `IconFrom`.

## Two things that go with it

**A `TaskbarIcon` built in code needs `ForceCreate()`.** Nothing loads it into a visual tree, so
the icon is never created and never appears. No error either.

**Dispose the previous icon *after* assigning the new one.** The shell has taken its copy by then;
freeing first flashes an empty slot. `App.RefreshTray` keeps that order.

## Why there is a test for the container

`TrayIconRendererTests` loads each rendered icon back and checks its dimensions. Hand-assembled
binary headers are exactly the kind of code that is wrong in a way nothing catches until a user
right-clicks — and the two failures above were both invisible to the compiler.

## See also

- `src/WaveLinkBackup.App/Views/TrayIconRenderer.cs`
- [tray-menu-keeps-the-theme-it-started-with.md](tray-menu-keeps-the-theme-it-started-with.md)
