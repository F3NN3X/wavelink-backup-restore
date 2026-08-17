using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using Windows.UI.ViewManagement;
using WaveLinkBackup.App.Theming;

namespace WaveLinkBackup.App.Windows;

/// <summary>
/// The real one, over three sources that each tell us something the others do not:
///
///   UISettings.ColorValuesChanged      the accent, and dark/light
///   UISettings.GetColorValue           the accent itself, and the background to judge dark/light
///   SystemEvents.UserPreferenceChanged high contrast going on or off (screens/11 names this)
///
/// screens/11 is explicit that high contrast must be reacted to at runtime and not require a
/// restart, which is why SystemEvents is here and not just a startup read.
/// </summary>
public sealed class UiSettingsTheme : ISystemTheme
{
    /// <summary>
    /// Held in a FIELD deliberately. UISettings unsubscribes itself when the instance is
    /// collected, and the symptom is theme following that works for a minute and then quietly
    /// stops — which looks like a Windows bug rather than ours.
    /// </summary>
    private readonly UISettings? settings;

    private readonly Dispatcher dispatcher;
    private bool started;
    private bool disposed;

    /// <summary>Construct on the UI thread: the dispatcher captured here is where Changed is raised.</summary>
    public UiSettingsTheme()
    {
        dispatcher = Dispatcher.CurrentDispatcher;

        try
        {
            settings = new UISettings();
        }
        catch (Exception ex) when (ex is TypeLoadException or PlatformNotSupportedException or COMException)
        {
            // WinRT activation can fail in stripped-down or sandboxed environments. Dark/light
            // then comes from the registry, as it did before this seam existed, and the accent
            // falls back to the authored one. Losing the accent is a cosmetic degradation;
            // refusing to start over it would not be.
            settings = null;
        }
    }

    public bool IsHighContrast => SystemParameters.HighContrast;

    public AppTheme Theme
    {
        get
        {
            if (IsHighContrast) return AppTheme.HighContrast;

            return IsLight() ? AppTheme.Light : AppTheme.Dark;
        }
    }

    public Color Accent
    {
        get
        {
            if (settings is null) return AccentFallback;

            var accent = settings.GetColorValue(UIColorType.Accent);
            return Color.FromArgb(accent.A, accent.R, accent.G, accent.B);
        }
    }

    /// <summary>
    /// What the dictionaries author. Used only when UISettings is unavailable, so an app with no
    /// accent to read looks exactly like plan 2's app rather than looking broken.
    /// </summary>
    private static Color AccentFallback => Color.FromRgb(0xF0, 0x16, 0x16);

    public event EventHandler? Changed;

    public void Start()
    {
        if (started || disposed) return;
        started = true;

        if (settings is not null) settings.ColorValuesChanged += OnColorValuesChanged;

        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
    }

    /// <summary>
    /// ColorValuesChanged arrives on a WinRT thread pool thread, NOT the UI thread. Raising
    /// straight through would put a ResourceDictionary swap on the wrong thread, and the throw
    /// would surface somewhere unrelated.
    /// </summary>
    private void OnColorValuesChanged(UISettings sender, object args) => RaiseOnUiThread();

    private void OnUserPreferenceChanged(object? sender, UserPreferenceChangedEventArgs e)
    {
        // Category.Color covers high contrast turning on and off. The other categories fire
        // often and mean nothing to us.
        if (e.Category == UserPreferenceCategory.Color) RaiseOnUiThread();
    }

    private void RaiseOnUiThread()
    {
        if (disposed) return;

        // BeginInvoke rather than Invoke: this is called from a thread we do not own, and
        // blocking it on our own dispatcher is a deadlock waiting for a slow tick.
        dispatcher.BeginInvoke(() =>
        {
            if (!disposed) Changed?.Invoke(this, EventArgs.Empty);
        });
    }

    private bool IsLight()
    {
        if (settings is null) return RegistrySaysLight();

        // The WinUI luminance heuristic: a light background is what "light mode" actually means,
        // and it is more reliable than AppsUseLightTheme, which some setups never write.
        var background = settings.GetColorValue(UIColorType.Background);

        return (5 * background.G) + (2 * background.R) + background.B > 8 * 128;
    }

    private static bool RegistrySaysLight()
    {
        using var key = Registry.CurrentUser.OpenSubKey(
            @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");

        return key?.GetValue("AppsUseLightTheme") is int light && light != 0;
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;

        if (started)
        {
            if (settings is not null) settings.ColorValuesChanged -= OnColorValuesChanged;

            // SystemEvents holds a STATIC subscription. Leaving it attached keeps the process
            // alive past Shutdown, which on a tray app is indistinguishable from a leak.
            SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        }
    }
}
