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
}

public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
