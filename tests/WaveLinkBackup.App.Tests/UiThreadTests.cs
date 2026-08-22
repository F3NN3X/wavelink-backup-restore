using System.Windows.Controls;
using System.Windows.Threading;
using WaveLinkBackup.App.Hosting;

namespace WaveLinkBackup.App.Tests;

/// <summary>
/// The guard behind a crash that ended the process: "Back up now" in the window ran the capture in
/// a Task.Run so the progress bar could animate, and the refresh that follows a capture set
/// TaskbarIcon.Icon from that thread. A DependencyObject the UI thread owns throws when another
/// thread writes it, and an exception on a thread-pool thread rethrown into an async void handler
/// has nothing above it to catch it.
///
/// These run the work from the TEST's thread against a dispatcher owned by another - which is the
/// shape of the bug, and cannot be faked with a same-thread stand-in.
/// </summary>
public sealed class UiThreadTests
{
    /// <summary>The dispatcher Wpf.Run's shared STA loop owns, and the id of its thread.</summary>
    private static (Dispatcher Dispatcher, int ThreadId) Loop() => Wpf.Run(() =>
        (Dispatcher.CurrentDispatcher, Environment.CurrentManagedThreadId));

    [Fact]
    public void Work_from_another_thread_runs_on_the_dispatchers_thread()
    {
        var (dispatcher, uiThreadId) = Loop();

        var ranOn = 0;
        var marshalled = UiThread.Marshal(dispatcher, () => ranOn = Environment.CurrentManagedThreadId);

        Assert.True(marshalled, "Marshal reported that the caller was already on the UI thread.");
        Assert.Equal(uiThreadId, ranOn);
        Assert.NotEqual(Environment.CurrentManagedThreadId, ranOn);
    }

    /// <summary>
    /// The return value is what lets a method guard itself in one line — <c>if (Marshal(…)) return;</c>
    /// re-enters through the dispatcher and the second pass must fall through, or the method
    /// marshals itself for ever and does nothing.
    /// </summary>
    [Fact]
    public void Work_already_on_the_dispatchers_thread_is_not_marshalled_again()
    {
        var (dispatcher, uiThreadId) = Loop();

        var (marshalled, ranOn) = Wpf.Run(() =>
        {
            var ran = 0;
            var wasMarshalled = UiThread.Marshal(dispatcher, () => ran = Environment.CurrentManagedThreadId);

            return (wasMarshalled, ran);
        });

        Assert.False(marshalled);
        Assert.Equal(0, ranOn);
        Assert.NotEqual(0, uiThreadId);
    }

    /// <summary>
    /// The failure itself, reproduced: writing a DependencyProperty on an object another thread
    /// owns throws, and going through the guard is what stops it. TaskbarIcon.Icon in the real
    /// crash; a MenuItem here, because the tray refresh writes three of those on the next line and
    /// the rule is the same for every DependencyObject.
    /// </summary>
    [Fact]
    public void A_dependency_property_written_through_the_guard_does_not_throw()
    {
        var (dispatcher, _) = Loop();
        var item = Wpf.Run(() => new MenuItem());

        Assert.Throws<InvalidOperationException>(() => item.Header = "written from the wrong thread");

        UiThread.Marshal(dispatcher, () => item.Header = "written through the guard");

        Assert.Equal("written through the guard", Wpf.Run(() => item.Header));
    }
}
