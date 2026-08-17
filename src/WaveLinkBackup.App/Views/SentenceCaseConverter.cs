using System.Globalization;
using System.Windows.Data;

namespace WaveLinkBackup.App.Views;

/// <summary>
/// The status strip's own text is upper-case mono by design (README: "WAVE LINK RUNNING ·
/// SETTINGS LAST SAVED 23:07 · AUTOMATIC BACKUP ON"), which a screen reader can read as shouted,
/// or spell out letter-by-letter for the short all-caps runs inside it. AutomationProperties.Name
/// gets the SAME words, sentence-cased, via TrackedText's own explicit-name override - 7.4 is
/// explicit that reader labels are part of this work rather than a follow-up.
///
/// Lives here rather than on ShellViewModel: the brief is explicit that no view model changes in
/// this task, and this is a READING of a string the view model already owns, not a new fact.
/// </summary>
public sealed class SentenceCaseConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string { Length: > 0 } text) return value;

        var lower = text.ToLower(culture);

        return char.ToUpper(lower[0], culture) + lower[1..];
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
