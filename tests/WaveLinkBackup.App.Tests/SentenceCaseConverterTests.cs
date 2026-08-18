using System.Globalization;
using WaveLinkBackup.App.Views;

namespace WaveLinkBackup.App.Tests;

/// <summary>
/// SentenceCaseConverter is a plain IValueConverter.Convert - InvariantCulture ToLower/ToUpper,
/// slicing, an empty/null guard - and shipped with no coverage of its own (Task 11 review). No
/// window or WPF harness is needed to exercise it, so these are plain pure-function tests.
///
/// Convert lower-cases the whole string, then upper-cases only the first character, regardless of
/// the input's own casing: "WAVE LINK RUNNING" and "wave link running" both land on
/// "Wave link running". Non-string values, null, and the empty string all pass through unchanged
/// (the guard `value is not string { Length: > 0 } text` returns the original value). It always
/// uses InvariantCulture internally regardless of the CultureInfo the binding infrastructure
/// passes in - the text it reads is always one of the design's own fixed strings, never
/// user-entered text.
/// </summary>
public sealed class SentenceCaseConverterTests
{
    private static readonly SentenceCaseConverter Converter = new();

    [Fact]
    public void An_uppercase_design_string_is_sentence_cased()
    {
        var result = Converter.Convert(
            "WAVE LINK RUNNING", typeof(string), null, CultureInfo.InvariantCulture);

        Assert.Equal("Wave link running", result);
    }

    [Fact]
    public void An_already_lowercase_string_gets_only_its_first_letter_capitalised()
    {
        var result = Converter.Convert(
            "wave link running", typeof(string), null, CultureInfo.InvariantCulture);

        Assert.Equal("Wave link running", result);
    }

    [Fact]
    public void A_single_character_input_is_upper_cased_with_nothing_left_to_slice()
    {
        var result = Converter.Convert("A", typeof(string), null, CultureInfo.InvariantCulture);

        Assert.Equal("A", result);
    }

    [Fact]
    public void An_empty_string_passes_through_unchanged()
    {
        var result = Converter.Convert(string.Empty, typeof(string), null, CultureInfo.InvariantCulture);

        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void Null_passes_through_unchanged()
    {
        var result = Converter.Convert(null, typeof(string), null, CultureInfo.InvariantCulture);

        Assert.Null(result);
    }

    // The guard is `is not string`, so a non-string value (e.g. a boxed int) is also passed
    // through untouched rather than throwing an InvalidCastException.
    [Fact]
    public void A_non_string_value_passes_through_unchanged()
    {
        object boxed = 42;

        var result = Converter.Convert(boxed, typeof(string), null, CultureInfo.InvariantCulture);

        Assert.Equal(boxed, result);
    }

    [Fact]
    public void ConvertBack_is_not_supported()
    {
        Assert.Throws<NotSupportedException>(() =>
            Converter.ConvertBack("Wave link running", typeof(string), null, CultureInfo.InvariantCulture));
    }

    // Guards the fix for the same fixed-string-should-use-InvariantCulture bug this plan has
    // already fixed three times elsewhere (Readable.Upper, the tier badges, MatchSummary). The
    // text this converter reads is always one of the design's own fixed strings, never
    // user-entered text, so it must ignore whatever CultureInfo the binding infrastructure hands
    // it and always use InvariantCulture internally. Proof this holds: even under tr-TR - where
    // ToLower/ToUpper treat 'I'/'i' differently from every other Latin culture (the "Turkish I
    // problem") and would otherwise turn "WAVE LINK RUNNING" into "Wave lınk runnıng" (dotless i)
    // - the result must still be the ordinary invariant casing. This fails if CurrentCulture (or
    // the passed-in `culture` parameter) is ever wired back into the ToLower/ToUpper calls.
    [Fact]
    public void The_culture_the_binding_passes_in_is_ignored_even_under_a_turkish_locale()
    {
        var turkish = CultureInfo.GetCultureInfo("tr-TR");

        var result = Converter.Convert("WAVE LINK RUNNING", typeof(string), null, turkish);

        Assert.Equal("Wave link running", result);
    }
}
