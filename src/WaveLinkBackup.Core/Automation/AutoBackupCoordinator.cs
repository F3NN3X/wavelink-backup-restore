using WaveLinkBackup.Core.Abstractions;

namespace WaveLinkBackup.Core.Automation;

/// <param name="Decision">Why the tick did or did not capture. Useful in a diagnostic log.</param>
public sealed record TickResult(CaptureDecision Decision, CaptureResult? Capture)
{
    public bool Captured => Capture?.Captured == true;
}

/// <summary>
/// Wires the watcher to the policy to the service. The only stateful piece of the automation,
/// and it holds exactly two timestamps.
///
/// It does NOT own a timer. <see cref="Tick"/> is called by whatever is hosting it - a shell's
/// timer in production, a test directly. That is what keeps every timing test instantaneous:
/// there is no delay to wait through, only a clock to move.
/// </summary>
public sealed class AutoBackupCoordinator : IDisposable
{
    private readonly ISettingsWatcher watcher;
    private readonly BackupService service;
    private readonly AutoBackupPolicy policy;
    private readonly IClock clock;
    private readonly Lock gate = new();

    private DateTimeOffset? lastWriteAt;
    private DateTimeOffset? lastAutoCaptureAt;
    private bool disposed;

    public AutoBackupCoordinator(
        ISettingsWatcher watcher,
        BackupService service,
        IClock clock,
        AutoBackupPolicy? policy = null)
    {
        this.watcher = watcher;
        this.service = service;
        this.clock = clock;
        this.policy = policy ?? AutoBackupPolicy.Default;

        watcher.SettingsChanged += OnSettingsChanged;
    }

    public bool IsRunning { get; private set; }

    /// <summary>Exposed for diagnostics and tests; nothing in production reads it.</summary>
    public DateTimeOffset? PendingSince
    {
        get { lock (gate) return lastWriteAt; }
    }

    public void Start()
    {
        watcher.Start();
        IsRunning = true;
    }

    public void Stop()
    {
        watcher.Stop();
        IsRunning = false;
    }

    /// <summary>
    /// Evaluates the policy and captures if due. Safe to call as often as the host likes -
    /// deciding is cheap and only a Capture decision touches disk.
    /// </summary>
    public TickResult Tick()
    {
        DateTimeOffset? write;
        DateTimeOffset? lastCapture;
        var now = clock.UtcNow;

        lock (gate)
        {
            write = lastWriteAt;
            lastCapture = lastAutoCaptureAt;
        }

        var decision = policy.Decide(write, lastCapture, now);
        if (decision != CaptureDecision.Capture) return new TickResult(decision, null);

        var result = service.CaptureAutomatic();
        if (!result.IsSuccess) return new TickResult(decision, null);

        lock (gate)
        {
            // Clear the pending write whether or not bytes were stored. A duplicate means the
            // change is accounted for - leaving it pending would re-evaluate it forever.
            lastWriteAt = null;

            // But only a real write restarts the rate limit. A skipped duplicate must not
            // suppress the next hour, or a launch-time rewrite would mask a genuine edit
            // made moments later.
            if (result.Value.Captured) lastAutoCaptureAt = now;
        }

        return new TickResult(decision, result.Value);
    }

    /// <summary>
    /// Captures on the way out, ignoring the debounce and the rate limit.
    ///
    /// The original incident happened during an update, while the app was restarting. A
    /// strategy that only captures during steady-state operation misses the exact moment that
    /// matters. Dedup still applies, so a quiet shutdown costs nothing.
    /// </summary>
    public TickResult CaptureOnShutdown()
    {
        var result = service.CaptureAutomatic();
        if (!result.IsSuccess) return new TickResult(CaptureDecision.Capture, null);

        lock (gate)
        {
            lastWriteAt = null;
            if (result.Value.Captured) lastAutoCaptureAt = clock.UtcNow;
        }

        return new TickResult(CaptureDecision.Capture, result.Value);
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;

        watcher.SettingsChanged -= OnSettingsChanged;
        watcher.Dispose();
    }

    private void OnSettingsChanged(object? sender, EventArgs e)
    {
        // Every write restarts the debounce, so a burst is one capture rather than five.
        lock (gate) lastWriteAt = clock.UtcNow;
    }
}
