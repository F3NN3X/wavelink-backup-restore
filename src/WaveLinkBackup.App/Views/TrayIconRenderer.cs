using System.IO;
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

    /// <summary>
    /// Produces a System.Drawing.Icon rather than an ImageSource, and TaskbarIcon.Icon is set
    /// rather than TaskbarIcon.IconSource.
    ///
    /// IconSource is the obvious property and it cannot work here: H.NotifyIcon converts one by
    /// calling new Uri(source.ToString()), so it only accepts images that CAME FROM a URI. A
    /// generated glyph has no URI, and no amount of wrapping gives it one. Both failures are
    /// runtime-only — the compiler is happy either way — so this was found by launching the app.
    /// </summary>
    /// <summary>
    /// The pixel size to render at for a given DPI, from the 16px logical size Windows asks the
    /// notification area for.
    ///
    /// **Snapped to the sizes an .ico is normally cut at** — 16, 20, 24, 32, 48, 64 — rather than
    /// scaled continuously. The shell rescales whatever it is given, and a bitmap at 38px scaled
    /// to 40 is blurrier than one at 48 scaled down. The fixed 32 this replaced was right at 100%
    /// and 150% and soft above (technical-debt.md §4.8 minor 1).
    /// </summary>
    public static int PixelSizeFor(double dpiScale)
    {
        if (double.IsNaN(dpiScale) || dpiScale <= 0) return 32;

        var wanted = 16 * dpiScale;

        foreach (var size in (int[])[16, 20, 24, 32, 48, 64])
        {
            if (wanted <= size) return size;
        }

        return 64;
    }

    public static System.Drawing.Icon Render(TrayStatus status, Color colour, int pixelSize = 32)
    {
        var pen = new Pen(new SolidColorBrush(colour), 1.75)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round,
        };
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

        var png = new MemoryStream();
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        encoder.Save(png);

        return IconFrom(png.ToArray(), pixelSize);
    }

    /// <summary>
    /// Wraps PNG bytes in a one-entry ICO container. A PNG-compressed icon entry is what
    /// Windows has used for the large sizes since Vista, so this is the format's own idiom
    /// rather than a trick — and it avoids GetHicon, whose handle we would then have to
    /// remember to destroy.
    /// </summary>
    private static System.Drawing.Icon IconFrom(byte[] png, int pixelSize)
    {
        const int HeaderLength = 6 + 16;

        using var ico = new MemoryStream();
        using (var writer = new BinaryWriter(ico, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            writer.Write((short)0);                 // reserved
            writer.Write((short)1);                 // type: icon
            writer.Write((short)1);                 // one image in the file

            // 0 means 256 in this field, which is why it is a byte and why 256 wraps to 0.
            writer.Write((byte)(pixelSize >= 256 ? 0 : pixelSize));
            writer.Write((byte)(pixelSize >= 256 ? 0 : pixelSize));
            writer.Write((byte)0);                  // palette entries: none, it is 32bpp
            writer.Write((byte)0);                  // reserved
            writer.Write((short)1);                 // colour planes
            writer.Write((short)32);                // bits per pixel
            writer.Write(png.Length);
            writer.Write(HeaderLength);             // where the payload starts
            writer.Write(png);
        }

        ico.Position = 0;

        return new System.Drawing.Icon(ico);
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
