using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace WaveLinkBackup.App.Views;

/// <summary>
/// True → Visible, false (or null) → Collapsed. The WHAT GOES IN A BACKUP rows use it to show the
/// toggle on the two built tiers and hide it on the locked ones (which carry the NOT BUILT YET
/// badge instead). A DataTrigger cannot say "when Locked is true collapse the sibling", so the
/// converter carries the predicate - the same reason NotNullToVisibilityConverter exists for the
/// trash row. Lives in Views, not on the view model: it is a READING of a fact the row already
/// owns (Locked), not a new fact.
/// </summary>
public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
