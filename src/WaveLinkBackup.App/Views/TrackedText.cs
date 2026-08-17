using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Documents;
using System.Windows.Media;

namespace WaveLinkBackup.App.Views;

/// <summary>
/// A single-line text element that honours LETTER-SPACING, which WPF does not have.
///
/// README's type scale gives four mono roles .18em, .14em, .12em and .06em tracking, and
/// TextBlock has no equivalent - CharacterSpacing exists in WinUI and nowhere else. Faking it
/// with per-character Runs is not possible either: Inline has no Margin. So the characters are
/// drawn one at a time at accumulated offsets, which is exact and costs one DrawText per
/// character on a label of ten or so.
///
/// USE IT ONLY for the tracked mono micro-labels. Anything that wraps, selects, trims or holds
/// mixed inline runs - the row name with its search highlight, every sentence - stays a
/// TextBlock. This element deliberately does not do any of that.
///
/// Per-character drawing discards kerning and shaping. For uppercase Latin in a MONOSPACED
/// face that is a non-issue, and tracking is additive spacing anyway.
/// </summary>
public sealed class TrackedText : FrameworkElement
{
    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text), typeof(string), typeof(TrackedText),
        new FrameworkPropertyMetadata(
            string.Empty,
            FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Extra space between characters, in EM - .18 means .18em, as the design writes it.</summary>
    public static readonly DependencyProperty TrackingProperty = DependencyProperty.Register(
        nameof(Tracking), typeof(double), typeof(TrackedText),
        new FrameworkPropertyMetadata(
            0d,
            FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    // AddOwner rather than new properties: these then INHERIT down the visual tree exactly like
    // a TextBlock's, so a style that sets FontFamily on a container still reaches this element.
    public static readonly DependencyProperty FontFamilyProperty =
        TextElement.FontFamilyProperty.AddOwner(typeof(TrackedText), Metadata(new FontFamily("Segoe UI")));

    public static readonly DependencyProperty FontSizeProperty =
        TextElement.FontSizeProperty.AddOwner(typeof(TrackedText), Metadata(12d));

    public static readonly DependencyProperty FontWeightProperty =
        TextElement.FontWeightProperty.AddOwner(typeof(TrackedText), Metadata(FontWeights.Normal));

    public static readonly DependencyProperty FontStyleProperty =
        TextElement.FontStyleProperty.AddOwner(typeof(TrackedText), Metadata(FontStyles.Normal));

    public static readonly DependencyProperty ForegroundProperty =
        TextElement.ForegroundProperty.AddOwner(typeof(TrackedText), Metadata(SystemColors.ControlTextBrush));

    private static FrameworkPropertyMetadata Metadata(object defaultValue) => new(
        defaultValue,
        FrameworkPropertyMetadataOptions.AffectsMeasure
        | FrameworkPropertyMetadataOptions.AffectsRender
        | FrameworkPropertyMetadataOptions.Inherits);

    public TrackedText()
    {
        // The design's micro-labels are 9.5px to 11px. Rounded layout is what keeps a 2px rule
        // under one of them from landing on a half pixel and going grey.
        UseLayoutRounding = true;
        SnapsToDevicePixels = true;
    }

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public double Tracking
    {
        get => (double)GetValue(TrackingProperty);
        set => SetValue(TrackingProperty, value);
    }

    public FontFamily FontFamily
    {
        get => (FontFamily)GetValue(FontFamilyProperty);
        set => SetValue(FontFamilyProperty, value);
    }

    public double FontSize
    {
        get => (double)GetValue(FontSizeProperty);
        set => SetValue(FontSizeProperty, value);
    }

    public FontWeight FontWeight
    {
        get => (FontWeight)GetValue(FontWeightProperty);
        set => SetValue(FontWeightProperty, value);
    }

    public FontStyle FontStyle
    {
        get => (FontStyle)GetValue(FontStyleProperty);
        set => SetValue(FontStyleProperty, value);
    }

    public Brush Foreground
    {
        get => (Brush)GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    private Typeface Typeface => new(FontFamily, FontStyle, FontWeight, FontStretches.Normal);

    /// <summary>
    /// The arithmetic, pure and static so it can be asserted without a visual tree: every
    /// character's own advance, plus one gap between each PAIR. There is no gap after the last
    /// character - trailing tracking would push a right-aligned label off its edge.
    /// </summary>
    public static double MeasureWidth(string text, Typeface typeface, double size, double trackingEm)
    {
        if (string.IsNullOrEmpty(text)) return 0;

        var width = Formatted(text, typeface, size, Brushes.Black).WidthIncludingTrailingWhitespace;

        return width + (Math.Max(0, text.Length - 1) * trackingEm * size);
    }

    private static FormattedText Formatted(string text, Typeface typeface, double size, Brush brush) => new(
        text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, typeface, size, brush, 1.0);

    protected override Size MeasureOverride(Size availableSize)
    {
        if (string.IsNullOrEmpty(Text)) return new Size(0, 0);

        var line = Formatted(Text, Typeface, FontSize, Foreground);

        return new Size(MeasureWidth(Text, Typeface, FontSize, Tracking), line.Height);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        if (string.IsNullOrEmpty(Text)) return;

        // One DrawText per character at an accumulated offset. Drawing the whole string once
        // when there is no tracking is not just an optimisation - it keeps kerning and shaping
        // intact for the untracked case, which is the one that might not be monospaced.
        if (Tracking == 0)
        {
            drawingContext.DrawText(Formatted(Text, Typeface, FontSize, Foreground), new Point(0, 0));
            return;
        }

        var gap = Tracking * FontSize;
        var x = 0d;

        foreach (var character in Text)
        {
            var glyph = Formatted(character.ToString(), Typeface, FontSize, Foreground);

            drawingContext.DrawText(glyph, new Point(x, 0));
            x += glyph.WidthIncludingTrailingWhitespace + gap;
        }
    }

    /// <summary>
    /// Without this the four tracked roles are invisible to a screen reader, and 7.4 is explicit
    /// that reader labels are part of this work rather than a follow-up.
    ///
    /// AutomationProperties.Name still wins where it is set, so a label that should be HEARD
    /// differently from how it reads - "3 OF 14 MATCH BETA" - can say so.
    /// </summary>
    protected override AutomationPeer OnCreateAutomationPeer() => new TrackedTextAutomationPeer(this);

    private sealed class TrackedTextAutomationPeer(TrackedText owner) : FrameworkElementAutomationPeer(owner)
    {
        protected override string GetNameCore() =>
            AutomationProperties.GetName(owner) is { Length: > 0 } explicitName
                ? explicitName
                : owner.Text ?? string.Empty;

        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Text;

        protected override bool IsControlElementCore() => true;
    }
}
