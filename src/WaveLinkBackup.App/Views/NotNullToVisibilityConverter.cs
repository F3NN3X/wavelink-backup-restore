using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace WaveLinkBackup.App.Views;

/// <summary>
/// Non-null value → Visible; null → Collapsed.
///
/// The settings dialog's trash row is the consumer: <see cref="ViewModels.TrashRowModel"/> is a
/// nullable record on the view model, and the row must hide itself when there is no store to read
/// (TrashRow is null) but stay visible for every real state - including "the trash is empty", which
/// is its own HasItems=false value, not null. A DataTrigger cannot say "when it is NOT null show it"
/// (there is no single Value to match against every non-null object), so the converter expresses the
/// predicate directly: "is there a row to show?" Lives in Views (not on the view model) for the same
/// reason <see cref="StringNotNullToVisibilityConverter"/> does - it is a READING of a fact the row
/// already owns, not a new fact.
/// </summary>
public sealed class NotNullToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is not null ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
