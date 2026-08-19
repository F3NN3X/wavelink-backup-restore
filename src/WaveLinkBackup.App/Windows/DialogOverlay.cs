using System.Windows;
using System.Windows.Interop;

namespace WaveLinkBackup.App.Windows;

/// <summary>
/// Turns a modal into an overlay: a layered window covering its owner, carrying the scrim and the
/// centred card, with the owner frosted behind it.
///
/// What this replaces was a real defect, not a polish gap. The dialogs were borderless windows with
/// Background="Transparent" and AllowsTransparency LEFT FALSE - and a non-layered WPF window cannot
/// be transparent, so "Transparent" resolved to opaque black. With no Width/Height set they also
/// took WPF's default window size. The result on screen was a large black rectangle with a card
/// floating in the middle of it, and a WlScrim fill compositing onto black instead of onto the app.
/// Raising the dark scrim to its specified 55% made it darker still.
///
/// The fix is three things together, and each is load-bearing:
///   - AllowsTransparency="True" in the XAML, so the window is layered and its transparent regions
///     really are transparent;
///   - <see cref="Cover"/>, so the window spans the owner instead of a default-sized box - a scrim
///     that does not reach the owner's edges reads as a panel, not a modal;
///   - <see cref="IDialogBackdrop"/>, so what shows through is frosted rather than merely dimmed.
///
/// The cost is the DWM drop shadow, which a layered window does not get. The card draws its own
/// instead, which is closer to the design anyway - README gives an exact shadow
/// (0 30px 70px rgba(0,0,0,.5)) that the system shadow never matched.
/// </summary>
public static class DialogOverlay
{
    /// <summary>
    /// MainWindow's caption height. README: dialogs are "centred over a full-window scrim BELOW the
    /// caption bar" - the app's own title bar and its close button stay unfrosted and legible, which
    /// is what keeps the window recognisable as the thing being covered.
    /// </summary>
    public const double CaptionInset = 34;

    /// <summary>
    /// The overlay's bounds in device-independent units, from the owner's bounds in physical pixels.
    ///
    /// Pure, and separate from <see cref="Attach"/>, because this is the part that can be wrong in a
    /// way nobody notices until a 150% display: Window.Left/Top/Width/Height are DIPs while
    /// GetWindowRect is pixels, and mixing them silently mis-sizes the overlay on every non-96-DPI
    /// monitor. Taking the owner's rect from the OS rather than from Window.Left/Width is also what
    /// makes a maximized owner work - a maximized window's Left/Top are stale.
    /// </summary>
    /// <param name="dipsPerPixel">
    /// 1.0 at 96 DPI, 0.667 at 150%. This is the scale of
    /// <c>CompositionTarget.TransformFromDevice</c>, not its inverse.
    /// </param>
    public static Rect Cover(Rect ownerPixels, double dipsPerPixel, double captionInset)
    {
        var inset = Math.Clamp(captionInset, 0, ownerPixels.Height * dipsPerPixel);

        return new Rect(
            ownerPixels.Left * dipsPerPixel,
            (ownerPixels.Top * dipsPerPixel) + inset,
            Math.Max(0, ownerPixels.Width * dipsPerPixel),
            Math.Max(0, (ownerPixels.Height * dipsPerPixel) - inset));
    }

    /// <summary>
    /// Wires a dialog up at construction. Everything happens on SourceInitialized because both
    /// halves need an HWND that does not exist until then.
    ///
    /// A dialog with no Owner - a standalone run, or a view test - keeps its own geometry and gets
    /// no blur. That is the honest behaviour: there is nothing to cover.
    /// </summary>
    public static void Attach(Window dialog, IDialogBackdrop? backdrop = null)
    {
        ArgumentNullException.ThrowIfNull(dialog);

        var frost = backdrop ?? new AcrylicDialogBackdrop();

        dialog.SourceInitialized += (_, _) =>
        {
            var handle = new WindowInteropHelper(dialog).Handle;

            frost.Apply(handle);

            if (dialog.Owner is not { } owner) return;

            var ownerHandle = new WindowInteropHelper(owner).Handle;
            if (ownerHandle == IntPtr.Zero || !TryGetWindowRect(ownerHandle, out var rect)) return;

            var bounds = Cover(rect, DipsPerPixel(dialog), CaptionInset);
            if (bounds.Width <= 0 || bounds.Height <= 0) return;

            dialog.Left = bounds.Left;
            dialog.Top = bounds.Top;
            dialog.Width = bounds.Width;
            dialog.Height = bounds.Height;
        };
    }

    /// <summary>
    /// The dialog's own DPI, not the primary monitor's - a per-monitor-DPI app can have the two
    /// differ, and the owner is by definition on the same monitor as its modal.
    /// </summary>
    private static double DipsPerPixel(Window dialog) =>
        PresentationSource.FromVisual(dialog)?.CompositionTarget?.TransformFromDevice.M11 ?? 1.0;

    private static bool TryGetWindowRect(IntPtr hwnd, out Rect rect)
    {
        rect = default;

        if (!GetWindowRect(hwnd, out var native)) return false;

        rect = new Rect(
            native.Left, native.Top,
            Math.Max(0, native.Right - native.Left),
            Math.Max(0, native.Bottom - native.Top));

        return true;
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", ExactSpelling = true)]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hwnd, out NativeRect rect);
}
