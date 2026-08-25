using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WaveLinkBackup.App.Hosting;

namespace WaveLinkBackup.App.Views;

/// <summary>
/// The tray icon is GENERATED, not shipped as four .ico files.
///
/// The high-contrast spec: "The tray icon follows the system icon contrast." A static icon
/// cannot do that. The taskbar's theme is the system's, which is not necessarily the app's, and
/// in high contrast the colours are not ours at all. Drawing it means the glyph is always
/// rendered against whatever the taskbar currently is.
///
/// No count badges: "a tray icon that says 3 invites a stressed user to guess what three of".
/// </summary>
public static class TrayIconRenderer
{
    /// <summary>
    /// LUCIDE, verbatim: the substitution point this class was written to have
    /// (technical-debt.md §4.7). Every path is copied unchanged from the lucide-icons repository
    /// on the same 24px grid; see THIRD-PARTY-NOTICES.md for the ISC licence, and
    /// ControlStyles.xaml's own note for the two mechanical differences from the .svg files.
    ///
    /// The four states compose a shield with one of four marks inside it, which is the design's
    /// own construction (the tray and updates spec) rather than four whole Lucide icons, so the shield body is
    /// lucide/shield-check's outline, and each mark is the glyph Lucide draws for that meaning.
    /// </summary>
    private const string ShieldPath =
        "M20 13c0 5-3.5 7.5-7.66 8.95a1 1 0 0 1-.67-.01C7.5 20.5 4 18 4 13V6a1 1 0 0 1 1-1"
        + "c2 0 4.5-1.2 6.24-2.72a1.17 1.17 0 0 1 1.52 0C14.51 3.81 17 5 19 5a1 1 0 0 1 1 1z";

    /// <summary>lucide/shield-check's own check, which is why it needs no rescaling to sit in it.</summary>
    private const string CheckPath = "M9 12l2 2 4-4";

    /// <summary>
    /// lucide/download's arrow, shortened to the shield's interior. The tray icon renders at 16px
    /// and Lucide's full-height arrow would touch the shield's own outline at that size.
    /// </summary>
    private const string ArrowPath = "M12 8v6 M9 11.5l3 3 3-3";

    /// <summary>lucide/triangle-alert's stem and dot, without its triangle. The shield IS the frame.</summary>
    private const string BangPath = "M12 8v5 M12 16h.01";

    /// <summary>lucide/circle-slash's slash, without its circle, for the same reason.</summary>
    private const string SlashPath = "M8.5 15.5 15.5 8.5";

    /// <summary>
    /// The pixel size to render at for a given DPI, from the 16px logical size Windows asks the
    /// notification area for.
    ///
    /// Snapped to the sizes an .ico is normally cut at, 16, 20, 24, 32, 48, 64, rather than
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

    /// <summary>
    /// Produces a System.Drawing.Icon rather than an ImageSource, and TaskbarIcon.Icon is set
    /// rather than TaskbarIcon.IconSource.
    ///
    /// IconSource is the obvious property and it cannot work here: H.NotifyIcon converts one by
    /// calling new Uri(source.ToString()), so it only accepts images that CAME FROM a URI. A
    /// generated glyph has no URI, and no amount of wrapping gives it one. Both failures are
    /// runtime-only, the compiler is happy either way, so this was found by launching the app.
    /// </summary>
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
    /// rather than a trick, and it avoids GetHicon, whose handle we would then have to
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
    /// WindowText and PAUSED becomes GrayText at FULL opacity: never the 55% used in the normal
    /// themes, because transparency is not a contrast guarantee (the high-contrast spec).
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
