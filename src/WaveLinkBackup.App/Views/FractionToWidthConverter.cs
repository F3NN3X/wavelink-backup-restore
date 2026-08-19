using System.Globalization;
using System.Windows.Data;

namespace WaveLinkBackup.App.Views;

/// <summary>
/// A 0..1 fraction and a track width, in; that fraction OF that width, out.
///
/// The settings dialog's proportion bar is a horizontal StackPanel of Borders, one per enabled
/// tier. <c>WhatGoesInModel</c> computes each segment's Fraction and SettingsViewModelTests pins
/// the arithmetic - but nothing bound a WIDTH to it, so every segment measured to zero and the bar
/// rendered as an empty sunken track. The computation was right and invisible for a whole phase.
///
/// A MultiBinding rather than a Grid of star columns because the segment count is data-driven: a
/// tier toggled off leaves the bar entirely, and star columns cannot be generated from an
/// ItemsSource without building the ColumnDefinitions in code.
/// </summary>
public sealed class FractionToWidthConverter : IMultiValueConverter
{
    /// <summary>The 1px hairline between segments, which each one gives back.</summary>
    private const double Gap = 1;

    /// <summary>
    /// A tier that is IN the backup gets at least this much bar, however little it weighs.
    ///
    /// The effects list is 4 KB against a 10 MB total - 0.04%, which is a quarter of a pixel on a
    /// 630px track and rounds away to nothing. A segment that disappears says the tier is not
    /// included, which is the opposite of true. The design's own bar draws a 0.6% band for the
    /// same reason, so a floor is what it already assumes; this states it.
    ///
    /// It costs proportional honesty at the very bottom of the range, and that is the right trade:
    /// the bar answers "what is in a backup, roughly", and the exact figures are printed beside
    /// every row anyway.
    /// </summary>
    private const double MinimumVisible = 2;

    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values is not [double fraction, double trackWidth]) return 0d;
        if (double.IsNaN(fraction) || double.IsNaN(trackWidth)) return 0d;

        // Zero is different from small: a tier that is not in the backup has no segment at all.
        if (fraction <= 0 || trackWidth <= 0) return 0d;

        return Math.Max(MinimumVisible, (Math.Min(1, fraction) * trackWidth) - Gap);
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        throw new NotSupportedException("The proportion bar is a readout; nothing writes a width back.");
}
