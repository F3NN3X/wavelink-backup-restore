namespace WaveLinkBackup.Core.Abstractions;

/// <summary>
/// Wall-clock time. The third seam, introduced in phase 2 rather than phase 1 because
/// snapshot timestamps are the first thing that genuinely needs it - a seam with no test
/// exercising it is decoration.
///
/// What it buys: "two snapshots taken in the same second" becomes a test instead of a race.
/// </summary>
public interface IClock
{
    DateTimeOffset UtcNow { get; }

    /// <summary>
    /// The same instant, carrying the user's own offset.
    ///
    /// Needed by exactly one thing: the daily backup time (operations/design/screens/
    /// The backup-timing spec). "Every day at 03:00" means 03:00 where the person is, so the only
    /// place in this program that cares about a wall clock rather than an elapsed span has to be
    /// told what the wall clock says. Everything else compares DateTimeOffsets, whose arithmetic
    /// is absolute and does not care about offsets at all.
    /// </summary>
    DateTimeOffset Now => UtcNow.ToLocalTime();
}

public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
