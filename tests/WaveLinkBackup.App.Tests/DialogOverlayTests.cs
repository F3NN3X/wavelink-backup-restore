using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using WaveLinkBackup.App.Windows;

namespace WaveLinkBackup.App.Tests;

/// <summary>
/// The geometry behind the frosted modal, and the three XAML facts it depends on.
///
/// What this replaced was a real defect: the dialogs were borderless windows with
/// Background="Transparent" and AllowsTransparency left FALSE, and a non-layered WPF window cannot
/// be transparent - so "Transparent" resolved to opaque black. With no size set they also took
/// WPF's default window dimensions. On screen that was a large black rectangle with a card floating
/// in the middle, and a WlScrim fill compositing onto black rather than onto the app.
/// </summary>
public sealed class DialogOverlayTests
{
    /// <summary>Every dialog that covers its owner. SettingsDialog joined them in this pass.</summary>
    private static readonly string[] Dialogs =
    [
        "DeleteDialog.xaml", "EmptyTrashDialog.xaml", "ErrorDialog.xaml",
        "RestoreDialog.xaml", "SettingsDialog.xaml",
    ];

    private static string Xaml(string name) =>
        File.ReadAllText(Path.Combine(AppResources.SourceRoot, "Views", name));

    private static string WindowTag(string name) =>
        Regex.Match(Xaml(name), "<Window\\b.*?>", RegexOptions.Singleline).Value;

    // ------------------------------------------------------------------ the arithmetic

    [Fact]
    public void The_overlay_spans_its_owner_below_the_caption_bar()
    {
        // A 1180x760 window at the origin, at 100% scaling.
        var bounds = DialogOverlay.Cover(new Rect(0, 0, 1180, 760), 1.0, DialogOverlay.CaptionInset);

        Assert.Equal(0, bounds.Left);
        Assert.Equal(34, bounds.Top);
        Assert.Equal(1180, bounds.Width);
        Assert.Equal(760 - 34, bounds.Height);
    }

    [Fact]
    public void The_overlay_follows_its_owner_across_the_desktop()
    {
        var bounds = DialogOverlay.Cover(new Rect(400, 220, 1180, 760), 1.0, DialogOverlay.CaptionInset);

        Assert.Equal(400, bounds.Left);
        Assert.Equal(254, bounds.Top);
    }

    /// <summary>
    /// The bug this is really here for. GetWindowRect returns PIXELS and Window.Left/Width are
    /// DIPs; mixing them looks perfect at 100% and mis-sizes the overlay on every scaled display,
    /// which is most laptops. At 150% a 1770x1140 pixel window is 1180x760 DIPs.
    /// </summary>
    [Fact]
    public void Pixels_become_device_independent_units()
    {
        var bounds = DialogOverlay.Cover(new Rect(0, 0, 1770, 1140), 2 / 3d, DialogOverlay.CaptionInset);

        Assert.Equal(1180, bounds.Width, 3);
        Assert.Equal(760 - 34, bounds.Height, 3);
    }

    [Fact]
    public void An_owner_shorter_than_the_caption_inset_still_yields_a_sane_rect()
    {
        // Nothing produces this today, but a negative Height would throw inside Rect rather than
        // simply looking wrong, and a modal is not where a crash belongs.
        var bounds = DialogOverlay.Cover(new Rect(0, 0, 200, 20), 1.0, DialogOverlay.CaptionInset);

        Assert.True(bounds.Height >= 0);
        Assert.True(bounds.Width >= 0);
    }

    // ------------------------------------------------------------------ the XAML it relies on

    [Theory]
    [InlineData("DeleteDialog.xaml")]
    [InlineData("EmptyTrashDialog.xaml")]
    [InlineData("ErrorDialog.xaml")]
    [InlineData("RestoreDialog.xaml")]
    [InlineData("SettingsDialog.xaml")]
    public void Every_dialog_is_layered_so_its_scrim_is_really_transparent(string dialog)
    {
        var tag = WindowTag(dialog);

        Assert.Contains("AllowsTransparency=\"True\"", tag, StringComparison.Ordinal);
        Assert.Contains("Background=\"Transparent\"", tag, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("DeleteDialog.xaml")]
    [InlineData("EmptyTrashDialog.xaml")]
    [InlineData("ErrorDialog.xaml")]
    [InlineData("RestoreDialog.xaml")]
    [InlineData("SettingsDialog.xaml")]
    public void Every_dialog_places_itself_rather_than_centring_on_its_owner(string dialog)
    {
        // CenterOwner would re-centre an overlay that is already owner-SIZED, pushing it off by
        // half the owner - so DialogOverlay's own positioning is the only one allowed to run.
        var tag = WindowTag(dialog);

        Assert.Contains("WindowStartupLocation=\"Manual\"", tag, StringComparison.Ordinal);
        Assert.DoesNotContain("CenterOwner", tag, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("DeleteDialog.xaml")]
    [InlineData("EmptyTrashDialog.xaml")]
    [InlineData("ErrorDialog.xaml")]
    [InlineData("RestoreDialog.xaml")]
    [InlineData("SettingsDialog.xaml")]
    public void Every_dialog_paints_a_scrim_and_gives_its_card_the_designs_shadow(string dialog)
    {
        var xaml = Xaml(dialog);

        // The scrim is what guarantees a dimmed owner even where the blur is refused.
        Assert.Contains("{DynamicResource WlScrim}", xaml, StringComparison.Ordinal);
        // A layered window gets no DWM shadow, so the card draws README's own.
        Assert.Contains("{StaticResource WlDialogShadow}", xaml, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("DeleteDialog.xaml.cs")]
    [InlineData("EmptyTrashDialog.xaml.cs")]
    [InlineData("ErrorDialog.xaml.cs")]
    [InlineData("RestoreDialog.xaml.cs")]
    [InlineData("SettingsDialog.xaml.cs")]
    public void Every_dialog_attaches_the_overlay(string codeBehind)
    {
        // Layered and owner-sized are separate facts: a dialog that is layered but never positioned
        // is a transparent window at (0,0), which is worse than the black box it replaced.
        Assert.Contains("DialogOverlay.Attach(this)",
            File.ReadAllText(Path.Combine(AppResources.SourceRoot, "Views", codeBehind)),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// MainWindow must NOT join them. AllowsTransparency makes a window layered, and DWM silently
    /// ignores the Mica backdrop on a layered window - the call still succeeds, so nothing short of
    /// reading the XAML catches it. MainWindowTemplateTests guards the same fact from its own side;
    /// this states it here too, where the temptation to "make them all consistent" lives.
    /// </summary>
    [Fact]
    public void The_main_window_is_not_a_dialog_and_stays_unlayered()
    {
        var withoutComments = Regex.Replace(
            Xaml("MainWindow.xaml"), "<!--.*?-->", string.Empty, RegexOptions.Singleline);

        Assert.DoesNotContain("AllowsTransparency", withoutComments, StringComparison.Ordinal);
        Assert.DoesNotContain("DialogOverlay", withoutComments, StringComparison.Ordinal);
    }
}
