using System.Windows;
using System.Windows.Threading;

namespace WaveLinkBackup.App.Tests;

/// <summary>
/// A WPF thread for the handful of tests that touch resource dictionaries.
///
/// The design left this open: "whether xunit.v3 ships an STA fact attribute is unconfirmed. If
/// it does not, the fallback is running those assertions on a manually created STA thread."
/// xunit.v3 3.2.2 ships no such attribute, so this is that fallback.
///
/// It needs more than an apartment. Loading pack://application:,,,/ requires the "pack" URI
/// scheme AND its WebRequest prefix to be registered, and both happen when a
/// System.Windows.Application is constructed — without one the Uri throws "Invalid port
/// specified", and with only the scheme registered the dictionary throws "The URI prefix is not
/// recognized". A real Application on a real STA thread is the honest way to get both.
///
/// One thread for the whole assembly: Application.Current is per-process, so a second would
/// throw. Everything a test wants to assert must be reduced to plain data INSIDE the callback —
/// a ResourceDictionary belongs to this thread and reading it from the test's thread would
/// throw.
///
/// ShutdownMode is forced to OnExplicitShutdown, away from the real default of
/// OnLastWindowClose. This Application is shared for the whole assembly's run, but several test
/// classes construct a Window and Close() it without ever calling Show(); a Window is added to
/// Application.Current.Windows on construction (confirmed empirically, not assumed - a throwaway
/// probe: Windows.Count went 0 -> 1 on `new Window()`, with no Show() involved). If that
/// constructed-and-closed window is ever the ONLY one currently registered - trivially true for
/// whichever window-touching class happens to run first - OnLastWindowClose's default behaviour
/// tears the Application down there and then. Confirmed empirically too: with the default mode,
/// a pack://application:,,,/ ResourceDictionary load immediately after that Close() throws "The
/// URI prefix is not recognized" (the exact failure this class's own doc comment above describes
/// for a MISSING Application) for every test that runs afterwards, for the rest of the process -
/// not the merged-dictionaries interleaving race this file was touched to fix, but the same
/// several-classes-fail-together shape, and it got reliably reproducible rather than rare once
/// execution order became deterministic. OnExplicitShutdown removes the implicit teardown so the
/// one shared Application survives every test class's own window lifecycle, exactly as a shared
/// fixture must.
/// </summary>
internal static class Wpf
{
    private static readonly Lock Gate = new();
    private static Dispatcher? loop;

    public static T Run<T>(Func<T> work) => Loop().Invoke(work);

    private static Dispatcher Loop()
    {
        lock (Gate)
        {
            if (loop is not null) return loop;

            using var ready = new ManualResetEventSlim();

            var thread = new Thread(() =>
            {
                _ = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                loop = Dispatcher.CurrentDispatcher;

                ready.Set();
                Dispatcher.Run();
            })
            {
                IsBackground = true,
                Name = "WaveLinkBackup test WPF thread",
            };

            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            ready.Wait();

            return loop!;
        }
    }
}
