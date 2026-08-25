namespace WaveLinkBackup.App.Windows;

/// <summary>
/// Which surface gets which material. The interop is not unit-tested, design §E lists Mica
/// under "not tested", but this DECISION is, because it is the part that gets quietly lost in a
/// refactor and the part that looks wrong to a user when it is.
///
/// It is also the contract between this plan and plan 4: when the 34px caption bar exists, it
/// asks <see cref="ForMainWindow"/> rather than deciding for itself.
/// </summary>
public static class ChromeChoice
{
    /// <summary>
    /// No backdrop, and rounded regardless.
    ///
    /// Acrylic was the first answer here and it was wrong for this app. A translucent menu means
    /// the palette showing through is the DESKTOP's, and this menu's whole job is to look like
    /// Wave Link Backup, so it paints an opaque WlCard surface instead and the brand colour is
    /// what you actually see. The corner preference stays: a square menu on Windows 11 looks
    /// broken rather than plain, and rounding is not a colour decision.
    /// </summary>
    public static (Backdrop Backdrop, Corners Corners) ForTrayMenu(bool highContrast) =>
        (Backdrop.None, Corners.Rounded);

    /// <summary>
    /// A long-lived background. Mica, and whatever corners Windows would give a resizable window
    /// unasked: overriding those is how you get a window that does not match its neighbours.
    /// </summary>
    public static (Backdrop Backdrop, Corners Corners) ForMainWindow(bool highContrast) =>
        (highContrast ? Backdrop.None : Backdrop.Mica, Corners.Default);
}
