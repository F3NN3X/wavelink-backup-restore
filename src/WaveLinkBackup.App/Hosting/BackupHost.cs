using WaveLinkBackup.Core.Abstractions;
using WaveLinkBackup.Core.Automation;
using WaveLinkBackup.Core.Results;

namespace WaveLinkBackup.App.Hosting;

/// <summary>
/// The host the coordinator has been waiting for since phase 3. AutoBackupCoordinator owns no
/// timer and holds two timestamps; something has to call Tick(). In the CLI that was the watch
/// verb's loop. Here it is this, driven by a DispatcherTimer that App owns.
///
/// Pause lives HERE and not in Core. Pausing is simply not ticking, so it costs no Core change;
/// putting a pause concept into AutoBackupPolicy would move a UI affordance into a library that
/// has no UI (ADR-004).
/// </summary>
public sealed class BackupHost(AutoBackupCoordinator coordinator, IClock clock) : IDisposable
{
    private DateTimeOffset? pausedUntil;
    private bool disposed;

    public bool AutoBackupEnabled { get; set; } = true;

    public DateTimeOffset? LastBackupAt { get; private set; }

    public CoreError? LastError { get; private set; }

    public bool IsCapturing { get; private set; }

    public bool IsPaused => pausedUntil is { } until && clock.UtcNow < until;

    public TrayConditions Conditions =>
        new(AutoBackupEnabled, IsPaused, IsCapturing, LastError);

    public void Start() => coordinator.Start();

    public void Stop() => coordinator.Stop();

    public void PauseFor(TimeSpan duration) => pausedUntil = clock.UtcNow + duration;

    public void Resume() => pausedUntil = null;

    /// <summary>
    /// Called by the host timer. Cheap when nothing is due — only a Capture decision touches
    /// disk — so the shell can call it as often as it likes.
    /// </summary>
    public TickResult Tick()
    {
        // NothingPending is the coordinator's word for "no work was done". A pending write is
        // left exactly as it was, so the capture happens on the first tick after the pause
        // rather than being discarded by it.
        if (IsPaused || !AutoBackupEnabled) return new TickResult(CaptureDecision.NothingPending, null);

        IsCapturing = true;
        try
        {
            var result = coordinator.Tick();
            Record(result);
            return result;
        }
        finally
        {
            IsCapturing = false;
        }
    }

    /// <summary>
    /// Ignores the debounce and the rate limit. The original incident happened during an
    /// update, while the machine was restarting — a strategy that only captures during
    /// steady-state operation misses the exact moment that matters.
    /// </summary>
    public TickResult CaptureOnShutdown()
    {
        var result = coordinator.CaptureOnShutdown();
        Record(result);
        return result;
    }

    private void Record(TickResult result)
    {
        // A successful tick clears a stale error, so the tray leaves NEEDS YOU on its own once
        // the folder comes back. Requiring a restart to clear it would be its own bug report.
        LastError = result.Error;

        if (result.Captured) LastBackupAt = clock.UtcNow;
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;

        coordinator.Dispose();
    }
}
