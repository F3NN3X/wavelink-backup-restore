using System.Globalization;
using System.Windows;
using System.Windows.Media;
using WaveLinkBackup.App.Views;

namespace WaveLinkBackup.App.Tests;

/// <summary>
/// Letter-spacing, which WPF does not have. The arithmetic is what is testable; that the
/// glyphs land where the arithmetic says is a by-eye check in Task 11.
/// </summary>
public sealed class TrackedTextTests
{
    private static Typeface Mono() => Wpf.Run(() =>
    {
        var dictionary = new ResourceDictionary
        {
            Source = new Uri("pack://application:,,,/WaveLinkBackup;component/Views/Typography.xaml"),
        };

        return new Typeface(
            (FontFamily)dictionary["WlMonoFont"],
            FontStyles.Normal, FontWeights.Medium, FontStretches.Normal);
    });

    [Fact]
    public void No_tracking_measures_the_same_as_the_plain_string()
    {
        var typeface = Mono();

        var tracked = Wpf.Run(() => TrackedText.MeasureWidth("INPUTS", typeface, 10.5, 0));
        var plain = Wpf.Run(() => new FormattedText(
            "INPUTS", CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            typeface, 10.5, Brushes.Black, 1.0).WidthIncludingTrailingWhitespace);

        Assert.Equal(plain, tracked, precision: 3);
    }

    // .18em at 10.5px is 1.89px per gap, and six characters have five gaps.
    [Fact]
    public void Tracking_adds_one_gap_per_pair_and_none_after_the_last()
    {
        var typeface = Mono();

        var untracked = Wpf.Run(() => TrackedText.MeasureWidth("INPUTS", typeface, 10.5, 0));
        var tracked = Wpf.Run(() => TrackedText.MeasureWidth("INPUTS", typeface, 10.5, 0.18));

        Assert.Equal(untracked + (5 * 0.18 * 10.5), tracked, precision: 3);
    }

    [Fact]
    public void A_single_character_gains_no_tracking()
    {
        var typeface = Mono();

        Assert.Equal(
            Wpf.Run(() => TrackedText.MeasureWidth("N", typeface, 10.5, 0)),
            Wpf.Run(() => TrackedText.MeasureWidth("N", typeface, 10.5, 0.18)),
            precision: 3);
    }

    [Fact]
    public void An_empty_string_measures_zero()
    {
        Assert.Equal(0, Wpf.Run(() => TrackedText.MeasureWidth("", Mono(), 10.5, 0.18)));
    }

    [Fact]
    public void The_element_measures_to_the_tracked_width()
    {
        var size = Wpf.Run(() =>
        {
            var element = new TrackedText
            {
                Text = "INPUTS",
                Tracking = 0.18,
                FontSize = 10.5,
                FontWeight = FontWeights.Medium,
            };

            element.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            return element.DesiredSize;
        });

        Assert.True(size.Width > 0);
        Assert.True(size.Height > 0);
    }

    // The five-slot strip reads as five unlabelled cells to a screen reader without this.
    [Fact]
    public void The_automation_name_is_the_text()
    {
        var name = Wpf.Run(() =>
        {
            var element = new TrackedText { Text = "5 INPUTS" };

            return System.Windows.Automation.Peers.UIElementAutomationPeer
                .CreatePeerForElement(element)!.GetName();
        });

        Assert.Equal("5 INPUTS", name);
    }

    // AutomationProperties.Name wins where a label needs to READ differently from how it looks -
    // "3 OF 14 MATCH BETA" is a mono strip, but a reader should hear a sentence.
    [Fact]
    public void An_explicit_automation_name_overrides_the_text()
    {
        var name = Wpf.Run(() =>
        {
            var element = new TrackedText { Text = "3 OF 14 MATCH \"BETA\"" };
            System.Windows.Automation.AutomationProperties.SetName(element, "3 of 14 backups match beta");

            return System.Windows.Automation.Peers.UIElementAutomationPeer
                .CreatePeerForElement(element)!.GetName();
        });

        Assert.Equal("3 of 14 backups match beta", name);
    }
}
