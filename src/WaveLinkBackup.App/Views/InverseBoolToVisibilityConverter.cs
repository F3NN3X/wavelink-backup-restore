using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace WaveLinkBackup.App.Views;

/// <summary>
/// True → Collapsed, false (or null) → Visible. The WHAT GOES IN A BACKUP rows use it to hide the
/// toggle on the locked tiers (which carry the NOT BUILT YET badge instead). The inverse of
/// BoolToVisibilityConverter - a DataTrigger cannot say "when Locked is true collapse the sibling",
/// so the converter carries the predicate. Lives in Views, not on the view model: it is a READING
/// of a fact the row already owns (Locked), not a new fact.
/// </summary>
public sealed class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
