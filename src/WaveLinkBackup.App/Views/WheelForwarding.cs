using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace WaveLinkBackup.App.Views;

/// <summary>
/// Hands a wheel notch to the <see cref="ScrollViewer"/> that actually scrolls.
///
/// **A ScrollViewer marks every wheel event handled, whether or not it scrolled.**
/// <c>ScrollViewer.OnMouseWheel</c> sets <c>e.Handled = true</c> unconditionally, so a control
/// with its own (disabled) ScrollViewer inside another one swallows the wheel and nothing moves.
/// The list is exactly that shape: a ListBox with <c>ScrollViewer.VerticalScrollBarVisibility
/// ="Disabled"</c> inside <c>ListScrollViewer</c>, which is what makes the header and the rows
/// share one scroll position — and which left the list unscrollable by wheel while the scroll bar
/// and every key still worked.
///
/// Re-raising rather than calling <see cref="ScrollViewer.ScrollToVerticalOffset"/> with a number:
/// the outer ScrollViewer's own handler then does the scrolling, so the distance is Windows'
/// (<c>SystemParameters.WheelScrollLines</c>, and the user's own mouse settings) rather than
/// one this app invented and would have to keep in step.
/// </summary>
internal static class WheelForwarding
{
    /// <summary>
    /// Call from a <c>PreviewMouseWheel</c> handler on the inner control. Marks the original
    /// handled so the inner ScrollViewer does not also swallow it - it would not scroll anyway,
    /// but it would leave the event looking answered.
    /// </summary>
    public static void Redirect(ScrollViewer target, object source, MouseWheelEventArgs e)
    {
        if (e.Handled) return;

        e.Handled = true;

        target.RaiseEvent(new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta)
        {
            RoutedEvent = UIElement.MouseWheelEvent,
            Source = source,
        });
    }
}
