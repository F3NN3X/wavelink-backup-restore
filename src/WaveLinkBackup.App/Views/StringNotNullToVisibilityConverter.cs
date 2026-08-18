using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace WaveLinkBackup.App.Views;

/// <summary>
/// Non-null, non-empty string → Visible; null or empty → Collapsed.
///
/// The rename cue's only consumer: a DataTrigger can say "when RenameError is {x:Null} collapse
/// the cue" but cannot say "when it is NOT null show it", because there is no single Value to match
/// against every non-null string. A converter expresses the predicate directly - "is there an error
/// to show?" - which is what the cue's Visibility actually means. Lives in Views (not on the view
/// model) for the same reason SentenceCaseConverter does: it is a READING of a fact the row already
/// owns (RenameError), not a new fact.
/// </summary>
public sealed class StringNotNullToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is string { Length: > 0 } ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
