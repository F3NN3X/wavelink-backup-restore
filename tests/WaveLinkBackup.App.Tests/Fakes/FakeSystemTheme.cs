using System.Windows.Media;
using WaveLinkBackup.App.Theming;
using WaveLinkBackup.App.Windows;

namespace WaveLinkBackup.App.Tests.Fakes;

/// <summary>
/// The seam that makes theme following testable without changing the developer's own Windows
/// settings mid-test. Same shape as FakeSettingsWatcher: set the state, raise the event.
/// </summary>
public sealed class FakeSystemTheme : ISystemTheme
{
    private AppTheme theme = AppTheme.Dark;

    public AppTheme Theme
    {
        get => IsHighContrast ? AppTheme.HighContrast : theme;
        set => theme = value;
    }

    public Color Accent { get; set; } = Colors.DodgerBlue;

    public bool IsHighContrast { get; set; }

    public bool Started { get; private set; }

    public bool Disposed { get; private set; }

    public event EventHandler? Changed;

    public void Start() => Started = true;

    public void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);

    public void Dispose() => Disposed = true;
}
