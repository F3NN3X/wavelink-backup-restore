using WaveLinkBackup.App.Windows;

namespace WaveLinkBackup.App.Tests.Fakes;

public sealed class FakeWindowChrome : IWindowChrome
{
    public List<(IntPtr Hwnd, Backdrop Backdrop, Corners Corners, bool Dark)> Calls { get; } = [];

    /// <summary>Models a Windows build that does not understand the backdrop attribute.</summary>
    public bool BackdropSucceeds { get; init; } = true;

    public bool Apply(IntPtr hwnd, Backdrop backdrop, Corners corners, bool dark)
    {
        Calls.Add((hwnd, backdrop, corners, dark));
        return BackdropSucceeds;
    }
}
