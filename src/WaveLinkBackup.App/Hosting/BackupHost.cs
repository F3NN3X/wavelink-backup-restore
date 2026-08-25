using WaveLinkBackup.Core.Abstractions;
using WaveLinkBackup.Core.Automation;
using WaveLinkBackup.Core.Discovery;
using WaveLinkBackup.Core.Results;
using WaveLinkBackup.Core.Snapshots;

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
public sealed class BackupHost : IDisposable
{
    private readonly IClock clock;

    // Mutable on purpose: error 12's "Choose a folder…" re-points the store and service at a new
    // path while the app is running. The watcher watches Wave Link's own settings file - not our
    // backup folder - so it survives the move; only the service (and therefore the destination)
    // changes. A pending write is cleared on the swap, but the next real settings write re-arms
    // the coordinator and lands in the new folder.
    private AutoBackupCoordinator currentCoordinator;

    private DateTimeOffset? pausedUntil;
    private bool disposed;

    public BackupHost(AutoBackupCoordinator coordinator, IClock clock)
    {
        currentCoordinator = coordinator;
        this.clock = clock;
    }

    public bool AutoBackupEnabled { get; set; } = true;

    public DateTimeOffset? LastBackupAt { get; private set; }

    public CoreError? LastError { get; private set; }

    public bool IsCapturing { get; private set; }

    public bool IsPaused => pausedUntil is { } until && clock.UtcNow < until;

    public TrayConditions Conditions =>
        new(AutoBackupEnabled, IsPaused, IsCapturing, LastError);

    /// <summary>
    /// The live coordinator, exposed so App can build a replacement that reuses the SAME watcher
    /// when error 12's "Choose a folder…" moves the store.
    /// </summary>
    public AutoBackupCoordinator Coordinator => currentCoordinator;

    /// <summary>
    /// The timings a rebuilt coordinator inherits. Set by App from the user's settings, and re-set
    /// when they change one in the Settings dialog - a stepper that only took effect on the next
    /// launch would be a control that appears not to work.
    /// </summary>
    public AutoBackupPolicy Policy
    {
        get => currentCoordinator.Policy;
        set => currentCoordinator.Policy = value;
    }

    /// <summary>
    /// Swaps the store and service the coordinator calls, keeping the SAME watcher (which watches
    /// Wave Link's settings file, not our backup folder). Called from App.SetStorePath when error
    /// 12's "Choose a folder…" re-points the backup folder. The old coordinator is disposed; the
    /// new one starts immediately if the host was running.
    /// </summary>
    public void SetStore(SnapshotStore newStore, BackupService newService)
    {
        var wasRunning = currentCoordinator.IsRunning;

        // Build a fresh watcher on the same path (Wave Link's settings file). The old coordinator
        // is disposed after the new one is constructed so there is no gap where ticks are lost.
        var watchPath = SettingsLocator.SystemLocalAppData;
        var newWatcher = new FileSystemSettingsWatcher(watchPath);
        // Carry the timings across. Moving the backup folder must not silently reset the
        // interval the user chose or switch off their daily copy.
        var newCoordinator = new AutoBackupCoordinator(newWatcher, newService, clock, currentCoordinator.Policy);

        currentCoordinator.Dispose();
        currentCoordinator = newCoordinator;

        if (wasRunning) newCoordinator.Start();
    }

    public void Start() => currentCoordinator.Start();

    public void Stop() => currentCoordinator.Stop();

    public void PauseFor(TimeSpan duration) => pausedUntil = clock.UtcNow + duration;

    public void Resume() => pausedUntil = null;

    /// <summary>
    /// Called by the host timer. Cheap when nothing is due, only a Capture decision touches
    /// disk, so the shell can call it as often as it likes.
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
            var result = currentCoordinator.Tick();
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
    /// update, while the machine was restarting. A strategy that only captures during
    /// steady-state operation misses the exact moment that matters.
    /// </summary>
    public TickResult CaptureOnShutdown()
    {
        var result = currentCoordinator.CaptureOnShutdown();
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

        currentCoordinator.Dispose();
    }
}
