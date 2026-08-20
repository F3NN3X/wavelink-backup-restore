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

    /// <summary>
    /// The daily time has passed and nothing has been captured since. Take one whether or not the
    /// file changed - dedup decides whether it costs anything
    /// (operations/design/screens/14-backup-timing.md).
    /// </summary>
    Scheduled,
}

/// <summary>
/// When an automatic snapshot is due. PURE - three timestamps in, a decision out. No clock,
/// no timer, no IO, so every case is a two-line test and none of them wait.
///
/// The behaviour is described to users in the Settings dialog, and that copy is a specification
/// rather than decoration. <see cref="MinimumInterval"/> in particular used to be a constant while
/// the dialog said "at most one an hour" as though it were a fact about the world; it is a setting
/// now, and the row's title is the value read back as a sentence so the two cannot drift
/// (operations/design/screens/14-backup-timing.md).
/// </summary>
/// <param name="dailyAt">
/// A wall-clock time to back up at each day, or null for "only when things change". Not a timer
/// and not a scheduled task: it means "if the day has reached this time and nothing has been
/// captured since, capture now".
/// </param>
public sealed class AutoBackupPolicy(TimeSpan debounce, TimeSpan minimumInterval, TimeOnly? dailyAt = null)
{
    /// <summary>~60s debounce, at most one automatic snapshot an hour, no daily copy. ADR-007.</summary>
    public static AutoBackupPolicy Default { get; } =
        new(TimeSpan.FromSeconds(60), TimeSpan.FromHours(1));

    /// <summary>The policy the user's settings describe. The one place those two fields are read.</summary>
    public static AutoBackupPolicy For(BackupSettings settings) =>
        new(TimeSpan.FromSeconds(60),
            TimeSpan.FromMinutes(settings.AutoBackupIntervalMinutes),
            settings.DailyBackupAt);

    public TimeSpan Debounce { get; } = debounce;

    public TimeSpan MinimumInterval { get; } = minimumInterval;

    public TimeOnly? DailyAt { get; } = dailyAt;

    /// <param name="lastWriteAt">When Wave Link last wrote the file. Null if never seen.</param>
    /// <param name="lastAutoCaptureAt">When we last took an AUTOMATIC snapshot. Manual and
    /// pre-restore snapshots do not count - the user asking for one is not a reason to
    /// suppress the watcher.</param>
    /// <param name="now">
    /// The current time carrying the USER'S offset, not UTC. Every comparison below is between
    /// DateTimeOffsets and so is absolute either way; the offset matters only to
    /// <see cref="DailyDue"/>, because "every day at 03:00" is a wall clock rather than an elapsed
    /// span. <see cref="Abstractions.IClock.Now"/> is what supplies it.
    /// </param>
    /// <param name="lastDailyAt">
    /// When the daily copy last ran, whether or not it stored anything. Separate from
    /// <paramref name="lastAutoCaptureAt"/> because a deduped daily copy still means the day is
    /// covered, while a deduped change-driven one deliberately does NOT restart the rate limit.
    /// Merging them would make one of those two rules wrong.
    /// </param>
    public CaptureDecision Decide(
        DateTimeOffset? lastWriteAt,
        DateTimeOffset? lastAutoCaptureAt,
        DateTimeOffset now,
        DateTimeOffset? lastDailyAt = null)
    {
        // The daily copy is checked FIRST and is not subject to the rate limit. It is an
        // instruction with a time on it; the interval is a cap on change-driven captures, and
        // letting a cap veto an explicit schedule would mean a setting that silently does nothing.
        if (DailyDue(lastAutoCaptureAt, lastDailyAt, now)) return CaptureDecision.Scheduled;

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

    /// <summary>
    /// Whether today has reached the set time with nothing captured since.
    ///
    /// The "nothing captured since" half is what stops the daily copy being redundant: a user who
    /// edits their mixer every evening has an automatic backup after 03:00 already, the day is
    /// covered, and no second copy is taken. A user who does not gets one every day. Both fall out
    /// of the same comparison.
    ///
    /// A machine that was asleep at 03:00 and wakes at 09:00 captures at 09:00 rather than skipping
    /// the day. Late is the useful direction to be wrong; the alternative is a schedule that
    /// silently does nothing for anyone who does not leave their computer on overnight.
    /// </summary>
    private bool DailyDue(DateTimeOffset? lastAutoCaptureAt, DateTimeOffset? lastDailyAt, DateTimeOffset now)
    {
        if (DailyAt is not { } at) return false;

        var due = new DateTimeOffset(
            now.Year, now.Month, now.Day, at.Hour, at.Minute, 0, now.Offset);

        // Today's moment has not arrived. Yesterday's is not chased: a day the app was not running
        // for is a day with no backup, and inventing one now would date it wrongly.
        if (due > now) return false;

        var covered = Later(lastAutoCaptureAt, lastDailyAt);
        return covered is null || covered < due;
    }

    private static DateTimeOffset? Later(DateTimeOffset? a, DateTimeOffset? b) => (a, b) switch
    {
        (null, null) => null,
        ({ } one, null) => one,
        (null, { } two) => two,
        var (one, two) => one > two ? one : two,
    };
}
