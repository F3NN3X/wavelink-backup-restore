using System.Windows.Media;
using WaveLinkBackup.App.Theming;

namespace WaveLinkBackup.App.Windows;

/// <summary>
/// Everything Windows can tell us about the palette, behind one event.
///
/// One <see cref="Changed"/> rather than three, because every source underneath means the same
/// thing to the app — the palette moved, re-apply — and a consumer that has to subscribe to
/// three is a consumer that will subscribe to two.
/// </summary>
public interface ISystemTheme : IDisposable
{
    /// <summary>
    /// Already accounts for high contrast outranking dark/light, so callers never have to
    /// remember the precedence. High contrast is not a third preference sitting alongside the
    /// other two: it is Windows saying the palette is no longer ours.
    /// </summary>
    AppTheme Theme { get; }

    /// <summary>The user's accent. <see cref="AppTheme.HighContrast"/> ignores it entirely.</summary>
    Color Accent { get; }

    bool IsHighContrast { get; }

    event EventHandler? Changed;

    /// <summary>Begins watching. Separate from construction so nothing fires mid-startup.</summary>
    void Start();
}
