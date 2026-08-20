using System.Windows;
using System.Windows.Media;
using WaveLinkBackup.App.Hosting;
using WaveLinkBackup.App.Theming;
using WaveLinkBackup.App.Views;

namespace WaveLinkBackup.App.Tests;

/// <summary>
/// The icon set (technical-debt.md §4.7). Every glyph in the app is now a Lucide path copied
/// verbatim onto the same 24px grid, rather than the hand-drawn stand-in "in the Lucide idiom"
/// each one was — README §icons asked for exactly that substitution.
///
/// **The failure this guards against is silent.** WPF's path mini-language differs from SVG's in
/// small ways — no <c>&lt;circle&gt;</c> element, and a few commands it will not take — and a path
/// it cannot parse, or one that parses to nothing, renders as an empty box with no error anywhere.
/// A mistyped digit in a 200-character path is exactly the kind of thing that ships.
/// </summary>
public sealed class IconSetTests
{
    /// <summary>
    /// Every geometry the app defines. Named individually rather than discovered, so deleting one
    /// fails this rather than quietly shrinking the set under test.
    /// </summary>
    public static TheoryData<string> Keys =>
    [
        "WlShieldCheckGeometry",
        "WlSearchGeometry",
        "WlGearGeometry",
        "WlCloseGeometry",
        "WlPencilGeometry",
        "WlTrashGeometry",
        "WlRotateCcwGeometry",
        "WlDownloadTrayGeometry",
        "WlFolderGeometry",
        "WlCheckGeometry",
        "WlWarningTriangleGeometry",
        "WlCircleSlashGeometry",
    ];

    [Theory]
    [MemberData(nameof(Keys))]
    public void Every_glyph_parses_and_draws_something(string key)
    {
        var bounds = Wpf.Run(() =>
        {
            AppResources.Load(AppTheme.Dark);

            var geometry = Application.Current.Resources[key] as Geometry;
            Assert.NotNull(geometry);

            return geometry.Bounds;
        });

        Assert.False(bounds.IsEmpty, $"{key} parses but draws nothing.");
        Assert.True(bounds.Width > 0 && bounds.Height > 0, $"{key} has no extent.");
    }

    /// <summary>
    /// Lucide's grid is 24×24 and the app's Path elements all stretch uniformly into a box sized in
    /// those terms. A glyph drawn to a different grid would render at the wrong optical weight
    /// beside its neighbours — the one way a correct-looking path can still be wrong.
    ///
    /// The bounds are the STROKE PATH's, so a glyph that legitimately reaches the edges measures
    /// close to 24; the tolerance is for round caps, which sit outside it.
    /// </summary>
    [Theory]
    [MemberData(nameof(Keys))]
    public void Every_glyph_is_drawn_to_the_24px_grid(string key)
    {
        var bounds = Wpf.Run(() =>
        {
            AppResources.Load(AppTheme.Dark);
            return ((Geometry)Application.Current.Resources[key]).Bounds;
        });

        Assert.True(bounds.Left >= -1, $"{key} starts at {bounds.Left}, left of the grid.");
        Assert.True(bounds.Top >= -1, $"{key} starts at {bounds.Top}, above the grid.");
        Assert.True(bounds.Right <= 25, $"{key} reaches {bounds.Right}, past the 24px grid.");
        Assert.True(bounds.Bottom <= 25, $"{key} reaches {bounds.Bottom}, past the 24px grid.");
    }

    /// <summary>
    /// The tray icon's own four glyph constants are the substitution point §4.7 named, and they are
    /// not resources — they are strings in <c>TrayIconRenderer</c>, so they need their own pass.
    /// Rendering each state is the check: the renderer parses all of them on the way to a bitmap.
    /// </summary>
    [Theory]
    [InlineData(TrayStatus.Watching)]
    [InlineData(TrayStatus.BackingUp)]
    [InlineData(TrayStatus.NeedsYou)]
    [InlineData(TrayStatus.Paused)]
    public void Every_tray_state_still_renders_after_the_substitution(TrayStatus status)
    {
        using var icon = Wpf.Run(() =>
            TrayIconRenderer.Render(status, Colors.White, 32));

        Assert.Equal(32, icon.Width);
    }
}
