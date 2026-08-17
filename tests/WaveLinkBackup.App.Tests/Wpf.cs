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
                _ = new Application();
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
