namespace WaveLinkBackup.App.Windows;

/// <summary>
/// Which surface gets which material. The interop is not unit-tested — design §E lists Mica
/// under "not tested" — but this DECISION is, because it is the part that gets quietly lost in a
/// refactor and the part that looks wrong to a user when it is.
///
/// It is also the contract between this plan and plan 4: when the 34px caption bar exists, it
/// asks <see cref="ForMainWindow"/> rather than deciding for itself.
/// </summary>
public static class ChromeChoice
{
    /// <summary>
    /// A transient surface. Windows uses Acrylic for menus and flyouts, and rounds them whether
    /// or not their host window is rounded.
    /// </summary>
    public static (Backdrop Backdrop, Corners Corners) ForTrayMenu(bool highContrast) =>
        (highContrast ? Backdrop.None : Backdrop.Acrylic, Corners.Rounded);

    /// <summary>
    /// A long-lived background. Mica, and whatever corners Windows would give a resizable window
    /// unasked — overriding those is how you get a window that does not match its neighbours.
    /// </summary>
    public static (Backdrop Backdrop, Corners Corners) ForMainWindow(bool highContrast) =>
        (highContrast ? Backdrop.None : Backdrop.Mica, Corners.Default);
}
