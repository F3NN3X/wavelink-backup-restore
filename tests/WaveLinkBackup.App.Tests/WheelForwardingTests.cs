using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using WaveLinkBackup.App.Views;

namespace WaveLinkBackup.App.Tests;

/// <summary>
/// The list would not scroll with the wheel. The scroll bar worked, Page Up and the arrow keys
/// worked, and six notches of the wheel moved it nothing at all.
///
/// The shape is the main window's, reproduced here without it: a ListBox whose own ScrollViewer is
/// DISABLED, inside the ScrollViewer that really scrolls. That arrangement is deliberate - it is
/// what gives the column header and the rows one scroll position - and it is also what swallows
/// the wheel, because <c>ScrollViewer.OnMouseWheel</c> marks the event handled whether or not it
/// scrolled anything.
/// </summary>
public sealed class WheelForwardingTests
{
    /// <summary>
    /// The window has to be SHOWN. A ScrollViewer's extent, and therefore whether it can scroll at
    /// all, does not exist until layout has run against a real presentation source - offscreen and
    /// out of the taskbar, the same idiom the dialog view tests use.
    /// </summary>
    /// <summary>
    /// The deepest realised element under where a cursor would be - the first row's container.
    ///
    /// **Raising the wheel on the ListBox itself does not reproduce anything**, and the first
    /// version of this file did exactly that and "passed" by scrolling: a routed event raised on
    /// the ListBox bubbles from the ListBox UPWARDS, and the ScrollViewer that swallows the wheel
    /// is INSIDE the ListBox's own template. A real notch starts on the row under the cursor and
    /// passes through it on the way out. So the row is where the test has to start too.
    /// </summary>
    private static UIElement Row(ListBox list) =>
        (UIElement)list.ItemContainerGenerator.ContainerFromIndex(0);

    private static (Window Window, ScrollViewer Outer, ListBox Inner) Build()
    {
        var inner = new ListBox
        {
            ItemsSource = Enumerable.Range(1, 60).Select(i => $"row {i}").ToList(),
            BorderThickness = new Thickness(0),
        };

        // The main window's own settings: the ListBox does not scroll, the outer ScrollViewer does.
        ScrollViewer.SetVerticalScrollBarVisibility(inner, ScrollBarVisibility.Disabled);
        ScrollViewer.SetHorizontalScrollBarVisibility(inner, ScrollBarVisibility.Disabled);

        var outer = new ScrollViewer
        {
            Content = inner,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            CanContentScroll = true,
        };

        var window = new Window
        {
            Content = outer,
            Width = 400,
            Height = 200,
            Left = -3000,
            Top = -3000,
            ShowInTaskbar = false,
        };

        window.Show();
        window.UpdateLayout();

        return (window, outer, inner);
    }

    private static MouseWheelEventArgs Notch() =>
        new(Mouse.PrimaryDevice, Environment.TickCount, -120)
        {
            RoutedEvent = UIElement.MouseWheelEvent,
        };

    /// <summary>
    /// One notch of the wheel, the way the input system delivers it: the TUNNELLING preview first,
    /// and then - only if nothing answered it - the bubbling event.
    ///
    /// <c>RaiseEvent</c> raises the one event it is handed and nothing else, so a test that raises
    /// only <c>MouseWheelEvent</c> never runs a <c>PreviewMouseWheel</c> handler and would report
    /// the fix as broken. Modelling both halves is what makes this test say something about the
    /// app rather than about <c>RaiseEvent</c>.
    /// </summary>
    private static void Wheel(UIElement from)
    {
        var preview = Notch();
        preview.RoutedEvent = UIElement.PreviewMouseWheelEvent;
        from.RaiseEvent(preview);

        if (preview.Handled) return;

        from.RaiseEvent(Notch());
    }

    /// <summary>
    /// The defect itself, and the shipped behaviour until this was wired: a notch over a row
    /// bubbles into the ListBox's own disabled ScrollViewer, which marks it handled and scrolls
    /// nothing, and the outer one - the only thing that could have moved - never sees it.
    /// </summary>
    [Fact]
    public void A_wheel_notch_over_a_row_scrolls_nothing_on_its_own()
    {
        var offset = Wpf.Run(() =>
        {
            var (window, outer, inner) = Build();

            try
            {
                Assert.True(outer.ScrollableHeight > 0, "Nothing to scroll - the fixture is wrong.");

                Wheel(Row(inner));
                window.UpdateLayout();

                return outer.VerticalOffset;
            }
            finally
            {
                window.Close();
            }
        });

        Assert.Equal(0, offset);
    }

    /// <summary>
    /// The window's own wiring, through the real event route: PreviewMouseWheel on the ListBox
    /// TUNNELS - it runs on the way down, before the inner ScrollViewer gets its turn to bubble -
    /// which is the whole reason the fix works at all.
    /// </summary>
    [Fact]
    public void The_windows_wiring_scrolls_the_list()
    {
        var offset = Wpf.Run(() =>
        {
            var (window, outer, inner) = Build();

            try
            {
                inner.PreviewMouseWheel += (s, e) => WheelForwarding.Redirect(outer, s, e);

                Wheel(Row(inner));
                window.UpdateLayout();

                return outer.VerticalOffset;
            }
            finally
            {
                window.Close();
            }
        });

        Assert.True(offset > 0, $"The wheel moved the list by {offset}px.");
    }

    /// <summary>
    /// The original is marked handled, so the inner ScrollViewer does not also answer it. It would
    /// not scroll - but a wheel notch that two ScrollViewers both act on is how a list ends up
    /// jumping twice as far as the mouse asked for.
    /// </summary>
    [Fact]
    public void The_original_notch_is_marked_handled()
    {
        var handled = Wpf.Run(() =>
        {
            var (window, outer, inner) = Build();

            try
            {
                var notch = Notch();
                WheelForwarding.Redirect(outer, inner, notch);

                return notch.Handled;
            }
            finally
            {
                window.Close();
            }
        });

        Assert.True(handled);
    }

    /// <summary>
    /// An event something else has already answered is left alone. Nothing in the window does that
    /// today; it is the guard that keeps this helper from being the reason a future handler stops
    /// working.
    /// </summary>
    [Fact]
    public void An_already_handled_notch_is_left_alone()
    {
        var offset = Wpf.Run(() =>
        {
            var (window, outer, inner) = Build();

            try
            {
                var notch = Notch();
                notch.Handled = true;

                WheelForwarding.Redirect(outer, inner, notch);
                window.UpdateLayout();

                return outer.VerticalOffset;
            }
            finally
            {
                window.Close();
            }
        });

        Assert.Equal(0, offset);
    }
}
