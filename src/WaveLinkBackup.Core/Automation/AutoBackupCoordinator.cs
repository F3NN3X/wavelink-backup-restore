using WaveLinkBackup.Core.Abstractions;
using WaveLinkBackup.Core.Results;

namespace WaveLinkBackup.Core.Automation;

/// <param name="Decision">
/// Why the tick did or did not capture. Useful in a diagnostic log, and the only thing that
/// distinguishes a change-driven capture from the daily one after the fact.
/// </param>
/// <param name="Error">
/// Non-null when a capture was due and failed. This is what the shell shows. The tray's
/// NEEDS YOU state and the status strip's folder-unavailable variant both read it; without it
/// the tray has a state it cannot enter, and a failing watcher is invisible.
///
/// A skipped duplicate is NOT an error. Nothing is wrong, the configuration simply has not
/// changed.
/// </param>
public sealed record TickResult(CaptureDecision Decision, CaptureResult? Capture, CoreError? Error = null)
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
    private readonly IClock clock;
    private readonly Lock gate = new();

    private DateTimeOffset? lastWriteAt;
    private DateTimeOffset? lastAutoCaptureAt;
    private DateTimeOffset? lastDailyAt;
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
        Policy = policy ?? AutoBackupPolicy.Default;

        watcher.SettingsChanged += OnSettingsChanged;
    }

    /// <summary>
    /// The timings. Settable, because the Settings dialog commits on change and a stepper whose
    /// new value only took effect on the next launch is a control that appears not to work. The two
    /// timestamps below are deliberately NOT reset when it changes: shortening the interval should
    /// let the next write through sooner, not re-run history.
    /// </summary>
    public AutoBackupPolicy Policy { get; set; }

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
        DateTimeOffset? lastDaily;

        // The user's own offset, because the daily backup is a wall-clock time and 03:00 means
        // 03:00 where they are. Everything else here compares DateTimeOffsets, whose arithmetic is
        // absolute, so passing the local one costs nothing and answers the one question that cares.
        var now = clock.Now;

        lock (gate)
        {
            write = lastWriteAt;
            lastCapture = lastAutoCaptureAt;
            lastDaily = lastDailyAt;
        }

        var decision = Policy.Decide(write, lastCapture, now, lastDaily);
        if (decision is not (CaptureDecision.Capture or CaptureDecision.Scheduled))
        {
            return new TickResult(decision, null);
        }

        var result = service.CaptureAutomatic();

        if (!result.IsSuccess)
        {
            // Clear the pending write on FAILURE too, and report the error once.
            //
            // Leaving it pending was the old behaviour and it was exactly what the design
            // forbids: every tick re-evaluated as Capture and tried again - every 15 seconds,
            // silently, forever. Nothing is lost by clearing it, because the next real write
            // re-arms the watcher; this is the same reconciliation-by-hash argument that makes
            // a dropped filesystem event a latency problem rather than data loss.
            lock (gate)
            {
                lastWriteAt = null;

                // A failed daily attempt still counts as today's attempt. Without this the daily
                // decision would come back true on every tick for the rest of the day and retry a
                // failing capture every fifteen seconds - the same runaway the paragraph above
                // exists to prevent, by the other route.
                if (decision == CaptureDecision.Scheduled) lastDailyAt = now;
            }

            return new TickResult(decision, null, result.Error);
        }

        lock (gate)
        {
            // Clear the pending write whether or not bytes were stored. A duplicate means the
            // change is accounted for - leaving it pending would re-evaluate it forever.
            lastWriteAt = null;

            // A daily copy is recorded whether or not it stored anything: "we looked at 03:00" is
            // the event the schedule cares about, and a deduped daily copy means the day is
            // genuinely covered. That is exactly the opposite of the rate limit below, which is
            // why the two timestamps are separate.
            if (decision == CaptureDecision.Scheduled) lastDailyAt = now;

            // Only a real write restarts the rate limit. A skipped duplicate must not
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
        if (!result.IsSuccess) return new TickResult(CaptureDecision.Capture, null, result.Error);

        lock (gate)
        {
            lastWriteAt = null;
            if (result.Value.Captured) lastAutoCaptureAt = clock.Now;
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
        lock (gate) lastWriteAt = clock.Now;
    }
}
