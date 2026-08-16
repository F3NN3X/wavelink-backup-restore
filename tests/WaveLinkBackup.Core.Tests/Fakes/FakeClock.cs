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

    public void Advance(TimeSpan by) => UtcNow += by;
}
