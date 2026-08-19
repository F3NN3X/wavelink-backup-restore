using System.IO;
using WaveLinkBackup.App.Views;

namespace WaveLinkBackup.App.Tests;

/// <summary>
/// Guards the app icon asset's PRESENCE and VALIDITY, not its pixels. A deleted or corrupt
/// app.ico would otherwise fail a user's first launch (or ship an exe with the WPF default glyph)
/// rather than a test run. The colours are deliberately not asserted: the mark is neutral grey on
/// transparent by design (it must read on both a light and a dark taskbar), and that is a decision
/// about taste, not a property a test can defend.
/// </summary>
public class AppIconAssetTests
{
    /// <summary>
    /// The .ico lives in the App project so a deleted asset fails the build's test run. Resolved
    /// relative to the test assembly's location (bin/Debug|Release/net...), walking up to the
    /// source tree — robust to configuration and runtime subfolders.
    /// </summary>
    private static string LocateAppIco()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "src", "WaveLinkBackup.App", "app.ico");
            if (File.Exists(candidate)) return candidate;

            dir = dir.Parent;
        }

        throw new FileNotFoundException(
            "app.ico was not found anywhere up the source tree from the test assembly. " +
            "The asset must live at src/WaveLinkBackup.App/app.ico.");
    }

    [Fact]
    public void The_app_icon_asset_exists()
    {
        var path = LocateAppIco();

        Assert.True(File.Exists(path), $"expected app.ico at {path}");
        Assert.True(new FileInfo(path).Length > 0, "app.ico must not be empty");
    }

    [Fact]
    public void The_app_icon_asset_is_a_valid_multi_size_icon()
    {
        var path = LocateAppIco();

        // Loading through System.Drawing.Icon proves the container parses and yields at least one
        // usable bitmap. A truncated or mis-framed file throws here, which is exactly the failure
        // we want to catch before a user's first launch.
        using var icon = new System.Drawing.Icon(path);

        Assert.True(icon.Width > 0 && icon.Height > 0, "icon must report a usable size");
    }
}
