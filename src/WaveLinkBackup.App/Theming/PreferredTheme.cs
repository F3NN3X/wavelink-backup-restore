using System.Windows.Media;
using WaveLinkBackup.App.Windows;

namespace WaveLinkBackup.App.Theming;

/// <summary>
/// The user's choice, wrapped around the OS.
///
/// A DECORATOR rather than a second source of truth, because every consumer in the app already
/// reads the palette through <see cref="ISystemTheme"/>: ThemeManager.Follow, the main window's
/// chrome and its ShellViewModel.IsHighContrast, the tray menu's material, the tray icon. Adding a
/// preference beside that interface would mean finding all of them and teaching each one the
/// precedence rule; putting it behind the same interface means none of them changes and none of
/// them can disagree.
///
/// <see cref="Refresh"/> is why the preference needs no second notification path either: a change
/// to the preference raises the SAME <see cref="Changed"/> event an OS change does, so the app
/// re-themes through the one route it already had.
/// </summary>
public sealed class PreferredTheme : ISystemTheme
{
    private readonly ISystemTheme system;
    private readonly Func<ThemePreference> preference;

    public PreferredTheme(ISystemTheme system, Func<ThemePreference> preference)
    {
        this.system = system;
        this.preference = preference;

        system.Changed += OnSystemChanged;
    }

    /// <inheritdoc />
    public AppTheme Theme => ThemeChoice.Resolve(preference(), system.Theme, system.IsHighContrast);

    /// <inheritdoc />
    public Color Accent => system.Accent;

    /// <summary>
    /// The EFFECTIVE answer, not Windows' own: the high-contrast rendering rules
    /// (screens/11: no fills, shape-first health, disabled at full opacity) belong to the palette
    /// being drawn, not to the setting that usually turns it on. A user who picked High contrast
    /// here needs them exactly as much.
    /// </summary>
    public bool IsHighContrast => Theme == AppTheme.HighContrast;

    public event EventHandler? Changed;

    public void Start() => system.Start();

    /// <summary>Re-raise <see cref="Changed"/> because the PREFERENCE moved rather than the OS.</summary>
    public void Refresh() => Changed?.Invoke(this, EventArgs.Empty);

    public void Dispose()
    {
        system.Changed -= OnSystemChanged;
        system.Dispose();
    }

    // Re-raised with THIS as the sender: a subscriber that reads the sender must not be handed the
    // inner theme, which does not know about the preference.
    private void OnSystemChanged(object? sender, EventArgs e) => Changed?.Invoke(this, EventArgs.Empty);
}
