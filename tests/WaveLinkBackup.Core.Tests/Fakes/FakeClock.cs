using WaveLinkBackup.Core.Abstractions;

namespace WaveLinkBackup.Core.Tests.Fakes;

/// <summary>
/// Turns "two snapshots in the same second" from a race into a test. The seam exists for
/// this and nothing else, which is why phase 1 did without it.
/// </summary>
public sealed class FakeClock(DateTimeOffset start) : IClock
{
    public FakeClock() : this(new DateTimeOffset(2026, 8, 15, 23, 7, 11, TimeSpan.Zero)) { }

    public DateTimeOffset UtcNow { get; set; } = start;

    /// <summary>
    /// The fake's own offset IS the local one, so a test that sets the clock to 03:00+02:00 gets a
    /// local 03:00 whatever timezone the machine running the suite is in. Converting through
    /// ToLocalTime here would make every daily-backup test pass or fail depending on where it ran.
    /// </summary>
    public DateTimeOffset Now => UtcNow;

    public void Advance(TimeSpan by) => UtcNow += by;
}
