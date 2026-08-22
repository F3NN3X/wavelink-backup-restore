using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace WaveLinkBackup.App.Views;

/// <summary>
/// Nullable URL string -> Uri for a Hyperlink's NavigateUri. A WPF Hyperlink only fires its
/// RequestNavigate handler when it has a NavigateUri, so the footer links in AboutDialog and
/// HelpDialog bind this to their model's nullable URL properties: without a target value the
/// binding would never produce a Uri and the click would do nothing.
///
/// The mapping is fail-soft on purpose. A null or blank string means "none configured" (the model
/// maps absent environment values to null), and an unparseable one should not take the whole
/// dialog down - both collapse to Binding.DoNothing, which leaves NavigateUri unset so the link
/// simply does not navigate. The view never throws on a bad URL: it is a convenience, and the
/// code-behind's Loaded handler already hides a link whose URL is null. Lives in Views (not on the
/// model) for the same reason StringNotNullToVisibilityConverter does - it is a READING of a fact
/// the model owns (the URL string), not a new fact.
/// </summary>
public sealed class StringToUriConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is string { Length: > 0 } url && Uri.TryCreate(url, UriKind.Absolute, out var uri)
            ? uri
            : Binding.DoNothing;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
