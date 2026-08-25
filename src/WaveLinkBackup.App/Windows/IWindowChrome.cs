namespace WaveLinkBackup.App.Windows;

/// <summary>
/// Windows 11 uses Mica for long-lived window BACKGROUNDS and Acrylic for TRANSIENT surfaces:
/// menus, flyouts, context menus. The distinction is not cosmetic: Mica on a context menu looks
/// like someone applied an effect, which is exactly the impression a native-feeling app must
/// avoid. So the caption bar and the tray menu ask this same seam for different materials.
/// </summary>
public enum Backdrop
{
    None,

    /// <summary>DWMSBT_MAINWINDOW. The window behind the list, and the caption bar.</summary>
    Mica,

    /// <summary>DWMSBT_TRANSIENTWINDOW. The tray menu, and any flyout after it.</summary>
    Acrylic,
}

public enum Corners
{
    /// <summary>Whatever Windows would do unasked, right for an ordinary resizable window.</summary>
    Default,

    /// <summary>DWMWCP_ROUND. A menu is rounded whether or not its host window would be.</summary>
    Rounded,
}

/// <summary>
/// The three DwmSetWindowAttribute calls the Windows 11 look needs, behind an interface so the
/// DECISION, which surface gets which material, is testable even though the interop is not
/// (design §E lists Mica under "not tested").
/// </summary>
public interface IWindowChrome
{
    /// <returns>
    /// Whether the BACKDROP took. A caller that cares can paint a solid WlChrome instead; the
    /// dark frame and the corners are allowed to succeed or fail independently, so an older
    /// Windows that rejects the backdrop still gets everything else it understands.
    /// </returns>
    bool Apply(IntPtr hwnd, Backdrop backdrop, Corners corners, bool dark);
}
