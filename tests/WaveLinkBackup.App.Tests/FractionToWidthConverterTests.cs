using System.Globalization;
using WaveLinkBackup.App.Views;

namespace WaveLinkBackup.App.Tests;

/// <summary>
/// The proportion bar's widths. `WhatGoesInModel` computes the fractions and SettingsViewModelTests
/// pins that arithmetic; this pins what the view does with them - which for a whole phase was
/// nothing, because no width was bound at all and every segment measured to zero.
/// </summary>
public sealed class FractionToWidthConverterTests
{
    private static double Width(double fraction, double trackWidth) =>
        (double)new FractionToWidthConverter().Convert(
            [fraction, trackWidth], typeof(double), null!, CultureInfo.InvariantCulture);

    [Fact]
    public void A_segment_takes_its_share_of_the_track()
    {
        // Half of 600, less the 1px hairline it gives back to its neighbour.
        Assert.Equal(299, Width(0.5, 600));
    }

    /// <summary>
    /// The case that made this a converter rather than a one-liner: the effects list is 4 KB
    /// against a ~10 MB backup, which is a quarter of a pixel. A tier that IS included must not
    /// vanish - a missing segment reads as an excluded tier.
    /// </summary>
    [Fact]
    public void A_tier_too_small_to_measure_still_draws()
    {
        Assert.True(Width(0.0004, 630) >= 2);
    }

    [Fact]
    public void A_tier_that_is_not_included_draws_nothing()
    {
        // Zero is categorically different from small, and the floor must not apply to it.
        Assert.Equal(0, Width(0, 630));
    }

    [Theory]
    [InlineData(double.NaN, 600)]
    [InlineData(0.5, double.NaN)]
    [InlineData(0.5, 0)]
    [InlineData(-1, 600)]
    public void Nothing_unmeasurable_produces_a_width(double fraction, double trackWidth)
    {
        // ActualWidth is NaN before the first arrange, and a binding fires then too.
        Assert.Equal(0, Width(fraction, trackWidth));
    }

    [Fact]
    public void A_fraction_over_one_cannot_overflow_the_track()
    {
        Assert.Equal(599, Width(1.5, 600));
    }
}
