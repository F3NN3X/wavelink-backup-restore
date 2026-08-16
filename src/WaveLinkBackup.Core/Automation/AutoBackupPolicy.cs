namespace WaveLinkBackup.Core.Automation;

public enum CaptureDecision
{
    /// <summary>No write has been seen since the last capture.</summary>
    NothingPending,

    /// <summary>A write was seen, but the debounce has not elapsed. Check again later.</summary>
    Waiting,

    /// <summary>Debounced, but an automatic snapshot was taken too recently.</summary>
    RateLimited,

    /// <summary>Take an automatic snapshot now.</summary>
    Capture,
}

/// <summary>
/// When an automatic snapshot is due. PURE - three timestamps in, a decision out. No clock,
/// no timer, no IO, so every case is a two-line test and none of them wait.
///
/// The behaviour is described to users in the Settings dialog: "Wave Link writes its file the
/// moment you touch a channel. This notices, waits a minute, then keeps a copy - at most one
/// an hour." That copy is a specification, not decoration: if these constants change, it
/// changes with them.
/// </summary>
public sealed class AutoBackupPolicy(TimeSpan debounce, TimeSpan minimumInterval)
{
    /// <summary>~60s debounce, at most one automatic snapshot an hour. ADR-007.</summary>
    public static AutoBackupPolicy Default { get; } =
        new(TimeSpan.FromSeconds(60), TimeSpan.FromHours(1));

    public TimeSpan Debounce { get; } = debounce;

    public TimeSpan MinimumInterval { get; } = minimumInterval;

    /// <param name="lastWriteAt">When Wave Link last wrote the file. Null if never seen.</param>
    /// <param name="lastAutoCaptureAt">When we last took an AUTOMATIC snapshot. Manual and
    /// pre-restore snapshots do not count - the user asking for one is not a reason to
    /// suppress the watcher.</param>
    public CaptureDecision Decide(
        DateTimeOffset? lastWriteAt,
        DateTimeOffset? lastAutoCaptureAt,
        DateTimeOffset now)
    {
        if (lastWriteAt is null) return CaptureDecision.NothingPending;

        // Elapsed times are compared as they are, so a clock that jumps backwards - an NTP
        // correction, or a user changing the system time - yields a negative span and reads
        // as "not long enough", never as "long ago". Waiting is the safe direction to fail.
        if (now - lastWriteAt.Value < Debounce) return CaptureDecision.Waiting;

        // Debounce first, deliberately: when neither is satisfied, "the write has not settled"
        // is the more accurate report, and a diagnostic log needs the distinction.
        if (lastAutoCaptureAt is not null && now - lastAutoCaptureAt.Value < MinimumInterval)
        {
            return CaptureDecision.RateLimited;
        }

        return CaptureDecision.Capture;
    }
}
