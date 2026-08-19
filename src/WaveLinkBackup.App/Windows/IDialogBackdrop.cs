namespace WaveLinkBackup.App.Windows;

/// <summary>
/// Frosts whatever sits behind a dialog window.
///
/// Separate from <see cref="IWindowChrome"/> because it is a different mechanism answering a
/// different question. DWMWA_SYSTEMBACKDROP_TYPE asks Windows for a MATERIAL - Mica reads the
/// desktop wallpaper, not the window underneath - which is right for the main window's caption and
/// useless for a modal, where the thing that must show through is the app itself. This seam is the
/// composition-attribute route, which blurs the actual pixels behind the window.
///
/// Behind an interface for the same reason IWindowChrome is: the DECISION (a modal frosts its
/// owner; nothing else in the app does) is testable even though the interop is not.
/// </summary>
public interface IDialogBackdrop
{
    /// <returns>
    /// Whether the blur took. It is allowed to fail: the dialog paints a WlScrim fill of its own
    /// regardless, so a machine that refuses the effect gets a plain dimmed owner rather than a
    /// broken window. Callers must not treat false as an error.
    /// </returns>
    bool Apply(IntPtr hwnd);
}
