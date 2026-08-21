using System.Windows.Threading;

namespace WaveLinkBackup.App.Hosting;

/// <summary>
/// The one-line guard that lets a method touching WPF objects be called from anywhere.
///
/// It exists because of a real crash, not as a precaution. <c>App.BackUpNow</c> refreshes the tray
/// icon and the shell's facts after a capture, and every caller ran on the UI thread until the
/// backing-up strip moved the capture into a <c>Task.Run</c> so the progress bar could animate.
/// From that thread, <c>TaskbarIcon.Icon = …</c> is a <c>DependencyObject.SetValue</c> on an object
/// the UI thread owns, which throws — and an exception on a thread-pool thread, rethrown into an
/// async void event handler with no handler above it, ends the process. Pressing "Back up now"
/// killed the app, tray and all.
///
/// The guard lives at the METHOD rather than at the call site on purpose. Marshalling one caller
/// fixes one caller; a method that marshals itself is safe for the next one, which is how this
/// arrived — the same hazard was spotted and handled for <c>SystemEvents</c> a phase earlier and
/// missed here.
/// </summary>
public static class UiThread
{
    /// <summary>
    /// True if <paramref name="work"/> has been run on <paramref name="dispatcher"/>'s thread and
    /// the caller should return; false if the caller is already there and should carry on.
    ///
    /// Blocking <see cref="Dispatcher.Invoke(Action)"/> rather than <c>BeginInvoke</c>, so a caller
    /// that goes on to read what the refresh produced sees it. That is safe for every caller here —
    /// they are inside an <c>await</c>, so the UI thread is pumping. It would deadlock a caller
    /// that blocked the UI thread waiting for the background work, but that caller has already
    /// frozen the window it is refreshing.
    /// </summary>
    public static bool Marshal(Dispatcher dispatcher, Action work)
    {
        if (dispatcher.CheckAccess()) return false;

        dispatcher.Invoke(work);
        return true;
    }
}
