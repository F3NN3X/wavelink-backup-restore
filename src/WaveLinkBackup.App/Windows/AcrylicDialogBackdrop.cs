using System.Runtime.InteropServices;

namespace WaveLinkBackup.App.Windows;

/// <summary>
/// The real one: SetWindowCompositionAttribute with an accent policy, which is the only route that
/// blurs the WINDOW BEHIND rather than the desktop material.
///
/// Undocumented, and knowingly so. Microsoft ships no public API for "frost the app under my
/// modal" - DwmEnableBlurBehind was retired after Windows 7 and the DWM system-backdrop attributes
/// (see <see cref="DwmWindowChrome"/>) composite the wallpaper, not the owner. Every shipping app
/// with this effect uses this call. It is safe to depend on ONLY because it is allowed to fail:
/// nothing here throws, the return value is advisory, and the dialog's own WlScrim fill is what
/// actually guarantees the owner reads as dimmed.
///
/// Two states are tried, newest first. ACCENT_ENABLE_ACRYLICBLURBEHIND is the Windows 10 1803+
/// material; ACCENT_ENABLE_BLURBEHIND is the older, cheaper plain blur, kept as the fallback
/// because the acrylic state has been reported to no-op on some builds.
/// </summary>
public sealed class AcrylicDialogBackdrop : IDialogBackdrop
{
    private const int WcaAccentPolicy = 19;

    private const int AccentEnableBlurBehind = 3;
    private const int AccentEnableAcrylicBlurBehind = 4;

    /// <summary>
    /// Draw the whole surface, not just the client area, so the blur reaches the window's edges.
    /// </summary>
    private const int DrawAllBorders = 0x20 | 0x40 | 0x80 | 0x100;

    /// <summary>
    /// The tint, as AABBGGRR. Alpha 1 is deliberate: this class contributes BLUR ONLY and the
    /// dimming belongs to the dialog's own WlScrim Border, which is a theme resource and therefore
    /// follows light/dark/high-contrast for free. A zero alpha is rejected as "no material" by some
    /// builds, so this is the smallest value that still reads as a request for acrylic.
    /// </summary>
    private const uint AlmostClearTint = 0x01000000;

    /// <summary>Acrylic needs Windows 10 1803. Below that, neither state does anything useful.</summary>
    private static bool Supported => Environment.OSVersion.Version.Build >= 17134;

    public bool Apply(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero || !Supported) return false;

        return TrySet(hwnd, AccentEnableAcrylicBlurBehind)
            || TrySet(hwnd, AccentEnableBlurBehind);
    }

    private static bool TrySet(IntPtr hwnd, int state)
    {
        var policy = new AccentPolicy
        {
            AccentState = state,
            AccentFlags = DrawAllBorders,
            GradientColor = AlmostClearTint,
            AnimationId = 0,
        };

        var size = Marshal.SizeOf<AccentPolicy>();
        var buffer = Marshal.AllocHGlobal(size);

        try
        {
            Marshal.StructureToPtr(policy, buffer, fDeleteOld: false);

            var data = new WindowCompositionAttributeData
            {
                Attribute = WcaAccentPolicy,
                Data = buffer,
                SizeOfData = size,
            };

            // Documented nowhere, so treat any non-success as "this machine does not do it".
            return SetWindowCompositionAttribute(hwnd, ref data) != 0;
        }
        catch (EntryPointNotFoundException)
        {
            // user32 without the export. Older or trimmed Windows; the scrim still works.
            return false;
        }
        catch (DllNotFoundException)
        {
            return false;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AccentPolicy
    {
        public int AccentState;
        public int AccentFlags;
        public uint GradientColor;
        public int AnimationId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowCompositionAttributeData
    {
        public int Attribute;
        public IntPtr Data;
        public int SizeOfData;
    }

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern int SetWindowCompositionAttribute(
        IntPtr hwnd, ref WindowCompositionAttributeData data);
}
