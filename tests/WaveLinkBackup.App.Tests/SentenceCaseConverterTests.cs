using System.Globalization;
using WaveLinkBackup.App.Views;

namespace WaveLinkBackup.App.Tests;

/// <summary>
/// SentenceCaseConverter is a plain IValueConverter.Convert - culture-aware ToLower/ToUpper,
/// slicing, an empty/null guard - and shipped with no coverage of its own (Task 11 review). No
/// window or WPF harness is needed to exercise it, so these are plain pure-function tests.
///
/// Convert lower-cases the whole string, then upper-cases only the first character, regardless of
/// the input's own casing: "WAVE LINK RUNNING" and "wave link running" both land on
/// "Wave link running". Non-string values, null, and the empty string all pass through unchanged
/// (the guard `value is not string { Length: > 0 } text` returns the original value).
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

    // Pins the culture-sensitivity the report flags as a possible instance of the same
    // fixed-string-should-use-InvariantCulture bug this plan has already fixed three times
    // elsewhere. Under tr-TR, ToLower/ToUpper treat 'I'/'i' differently from every other Latin
    // culture (the "Turkish I problem") - the design's own English strings ("WAVE LINK RUNNING")
    // come out with a dotless i instead of the expected one wherever the OS locale is Turkish.
    // This is NOT a claim about what SHOULD happen - only proof of what DOES happen today with
    // whatever CultureInfo the binding infrastructure hands the converter (WPF passes
    // CultureInfo.CurrentCulture here since MainWindow.xaml sets no ConverterCulture).
    [Fact]
    public void Under_Turkish_culture_the_dotless_i_leaks_into_a_fixed_design_string()
    {
        var turkish = CultureInfo.GetCultureInfo("tr-TR");

        var result = Converter.Convert("WAVE LINK RUNNING", typeof(string), null, turkish);

        Assert.Equal("Wave lınk runnıng", result);
    }
}
