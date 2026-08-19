using System.Windows;
using System.Windows.Media;

namespace WaveLinkBackup.App.Views;

/// <summary>
/// Renders the window's caption glyph from the SAME geometry TrayIconRenderer draws for WATCHING
/// (ShieldPath + CheckPath), so the static asset and the live tray icon read as one object.
///
/// Why render rather than ship a file: a XAML Icon="app.ico" attribute fails at runtime with
/// "Cannot locate resource 'app.ico'" (dotnet/wpf#209). The .ico IS embedded in the assembly's
/// WPF resource blob, but neither the XAML type-converter path nor a pack://application:,,,/ URI
/// through BitmapImage can locate it by name - WPF's pack-URI resolution does not index .ico
/// entries the way it does .png/.jpg. Rendering the mark from geometry sidesteps the file
/// entirely, and the exe's own icon (taskbar, Alt-Tab, file properties) is separate and comes
/// from &lt;ApplicationIcon&gt; in the csproj.
///
/// The colour is a neutral grey that reads on both a light and a dark caption bar. It is not tied
/// to a theme brush because the caption bar's background is set by ApplyChrome (WlChrome with Mica,
/// or WlTransparent without), and a single fixed grey survives either. In high contrast the OS
/// tints the caption; this glyph is the fallback that must survive any theme.
/// </summary>
public static class AppCaptionGlyph
{
    /// <summary>
    /// Lucide idiom - 24px grid, monoline. Identical to TrayIconRenderer's ShieldPath and
    /// CheckPath so the caption glyph and the WATCHING tray icon are the same mark.
    /// </summary>
    private const string ShieldPath =
        "M12 2 L20 5 V11 C20 16 16.5 19.5 12 21 C7.5 19.5 4 16 4 11 V5 Z";

    private const string CheckPath = "M8.5 12 L11 14.5 L15.5 9.5";

    /// <summary>
    /// Neutral grey: reads on both a light and a dark caption bar. Not pure white (vanishes on
    /// light), not pure black (vanishes on dark). The OS tints where it can; this is the fallback
    /// that must survive any theme.
    /// </summary>
    private static readonly Color GlyphColour = Color.FromRgb(0x8A, 0x8F, 0x98);

    /// <summary>
    /// Renders the shield-check mark at 32px (the standard caption glyph size) and returns a
    /// frozen BitmapSource suitable for Window.Icon. Returns null on any rendering failure so a
    /// cosmetic glyph problem never takes down the window - the exe's own icon is separate.
    /// </summary>
    public static ImageSource? Render()
    {
        try
        {
            const int pixelSize = 32;

            var pen = new Pen(new SolidColorBrush(GlyphColour), 1.75)
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
                context.DrawGeometry(null, pen, Geometry.Parse(CheckPath));
                context.Pop();
            }

            var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(
                pixelSize, pixelSize, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(visual);
            bitmap.Freeze();

            return bitmap;
        }
        catch
        {
            // A rendering failure is cosmetic - the window must still open. The exe's own icon
            // (taskbar, Alt-Tab) is separate and unaffected.
            return null;
        }
    }
}
