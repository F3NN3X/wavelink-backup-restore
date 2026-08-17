namespace WaveLinkBackup.App.Startup;

/// <summary>
/// Mandatory rather than polite: two instances means two watchers racing on one settings file.
///
/// A Mutex detects; named events activate. There is no IPC payload because the only message is
/// "show yourself" — but there are TWO events, so a second launch carrying --tray can exit
/// silently rather than forcing open a window nobody asked for.
///
/// Local\ rather than Global\: settings and the store are per-user, so two people signed into
/// one machine should each get an instance. The race being prevented is two watchers over ONE
/// user's file.
/// </summary>
public sealed class SingleInstance : IDisposable
{
    private readonly Mutex mutex;
    private readonly EventWaitHandle showEvent;
    private readonly CancellationTokenSource listening = new();
    private bool disposed;

    private SingleInstance(Mutex mutex, bool isFirst, EventWaitHandle showEvent)
    {
        this.mutex = mutex;
        this.showEvent = showEvent;
        IsFirst = isFirst;
    }

    public bool IsFirst { get; }

    /// <summary>Raised on the FIRST instance when a later launch asks for the window.</summary>
    public event EventHandler? ActivationRequested;

    public static SingleInstance TryAcquire(string name)
    {
        var mutex = new Mutex(initiallyOwned: true, $@"Local\{name}.instance", out var createdNew);

        var showEvent = new EventWaitHandle(
            initialState: false, EventResetMode.AutoReset, $@"Local\{name}.show");

        return new SingleInstance(mutex, createdNew, showEvent);
    }

    /// <summary>Starts watching for later launches. Only the first instance should call this.</summary>
    public void StartListening()
    {
        var thread = new Thread(WaitLoop)
        {
            IsBackground = true,
            Name = "WaveLinkBackup single-instance listener",
        };

        thread.Start();
    }

    /// <param name="wantsWindow">
    /// False for a --tray launch. Signalling nothing at all is what lets a second --tray exit
    /// without disturbing the running instance.
    /// </param>
    public void SignalExistingInstance(bool wantsWindow)
    {
        if (wantsWindow) showEvent.Set();
    }

    private void WaitLoop()
    {
        var handles = new WaitHandle[] { showEvent, listening.Token.WaitHandle };

        while (!listening.IsCancellationRequested)
        {
            if (WaitHandle.WaitAny(handles) != 0) return;

            ActivationRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;

        listening.Cancel();

        if (IsFirst)
        {
            try { mutex.ReleaseMutex(); }
            catch (ApplicationException) { /* never owned it */ }
        }

        mutex.Dispose();
        showEvent.Dispose();
        listening.Dispose();
    }
}
