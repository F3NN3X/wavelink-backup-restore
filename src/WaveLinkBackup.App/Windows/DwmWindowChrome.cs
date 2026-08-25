using System.Runtime.InteropServices;

namespace WaveLinkBackup.App.Windows;

/// <summary>
/// The real one. Three attributes, each HRESULT-checked and each allowed to fail on its own.
///
/// Everything here is version-gated at RUNTIME rather than by the project's TFM. The App project
/// targets 10.0.19041.0 because that is what the WinRT UISettings projection needs, and that is
/// years older than any of these attributes, so "the SDK knows about it" says nothing about
/// whether the machine does. An unstyled window on Windows 10 is fine; a crash is not.
/// </summary>
public sealed class DwmWindowChrome : IWindowChrome
{
    // Windows 10 2004 renumbered the dark-mode attribute. 19 was the 1809/1903 value and is
    // still worth trying, because the app runs on 19041 and up but the two overlap awkwardly.
    private const int DwmwaUseImmersiveDarkModeLegacy = 19;
    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaWindowCornerPreference = 33;
    private const int DwmwaSystemBackdropType = 38;

    private const int DwmwcpDefault = 0;
    private const int DwmwcpRound = 2;

    private const int DwmsbtNone = 1;
    private const int DwmsbtMainWindow = 2;
    private const int DwmsbtTransientWindow = 3;

    /// <summary>
    /// DWMWA_SYSTEMBACKDROP_TYPE needs Windows 11 22H2. Build 22000 had only the undocumented
    /// DWMWA_MICA_EFFECT (1029), which is deliberately NOT used here: an undocumented attribute
    /// that Microsoft already replaced is a worse bet than no backdrop at all.
    /// </summary>
    private static bool SupportsBackdrop => Environment.OSVersion.Version.Build >= 22621;

    private static bool SupportsRoundedCorners => Environment.OSVersion.Version.Build >= 22000;

    public bool Apply(IntPtr hwnd, Backdrop backdrop, Corners corners, bool dark)
    {
        if (hwnd == IntPtr.Zero) return false;

        SetDarkMode(hwnd, dark);
        SetCorners(hwnd, corners);

        return SetBackdrop(hwnd, backdrop);
    }

    private static void SetDarkMode(IntPtr hwnd, bool dark)
    {
        var value = dark ? 1 : 0;

        if (!Succeeded(Set(hwnd, DwmwaUseImmersiveDarkMode, value)))
        {
            Set(hwnd, DwmwaUseImmersiveDarkModeLegacy, value);
        }
    }

    private static void SetCorners(IntPtr hwnd, Corners corners)
    {
        if (!SupportsRoundedCorners) return;

        Set(hwnd, DwmwaWindowCornerPreference, corners == Corners.Rounded ? DwmwcpRound : DwmwcpDefault);
    }

    private static bool SetBackdrop(IntPtr hwnd, Backdrop backdrop)
    {
        if (!SupportsBackdrop) return false;

        var value = backdrop switch
        {
            Backdrop.Mica => DwmsbtMainWindow,
            Backdrop.Acrylic => DwmsbtTransientWindow,
            _ => DwmsbtNone,
        };

        return Succeeded(Set(hwnd, DwmwaSystemBackdropType, value)) && backdrop != Backdrop.None;
    }

    private static int Set(IntPtr hwnd, int attribute, int value)
    {
        // The attribute is read as a DWORD, so the length is 4 and the value must outlive the
        // call, hence a local rather than an expression.
        var payload = value;

        return DwmSetWindowAttribute(hwnd, attribute, ref payload, sizeof(int));
    }

    private static bool Succeeded(int hresult) => hresult >= 0;

    [DllImport("dwmapi.dll", ExactSpelling = true)]
    private static extern int DwmSetWindowAttribute(
        IntPtr hwnd, int attribute, ref int value, int valueLength);
}
