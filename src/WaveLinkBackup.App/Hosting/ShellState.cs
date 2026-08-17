using System.Windows;

namespace WaveLinkBackup.App.Hosting;

/// <summary>
/// What the SHELL remembers, as opposed to what the app is configured to do.
///
/// Separate from BackupSettings on purpose. settings.json describes itself in the Settings
/// dialog as "the folder, the automatic-backup switch, how many to keep and which Wave Link
/// you picked" (screens/08-settings-persistence.md) - a window rectangle in there would make
/// that sentence false. And Core has no window to hide and no tray to hide it in (ADR-004).
/// </summary>
/// <param name="ClosingHidesToTray">
/// On by default. Off routes a window close through the full shutdown path, INCLUDING the
/// shutdown capture - coherent rather than dangerous, because the user turned it off in
/// Settings, where the description says automatic backups only happen while the app runs.
/// </param>
public sealed record ShellState(
    double? Left,
    double? Top,
    double? Width,
    double? Height,
    bool IsMaximized,
    bool ClosingHidesToTray)
{
    public static ShellState Default { get; } = new(
        Left: null, Top: null, Width: null, Height: null,
        IsMaximized: false, ClosingHidesToTray: true);

    /// <summary>
    /// Whether a remembered rectangle still overlaps a screen that exists.
    ///
    /// Overlap, not containment: half off the edge is somewhere the user deliberately put it,
    /// while entirely off every screen is a monitor that has been unplugged since.
    /// </summary>
    public static bool IsOnScreen(ShellState state, IReadOnlyList<Rect> screens)
    {
        if (state.Left is not { } left || state.Top is not { } top
            || state.Width is not { } width || state.Height is not { } height)
        {
            return false;
        }

        var window = new Rect(left, top, width, height);

        return screens.Any(screen => screen.IntersectsWith(window));
    }
}
