using WaveLinkBackup.App.Updates;
using WaveLinkBackup.App.ViewModels;

namespace WaveLinkBackup.App.Tests;

/// <summary>
/// When the app looks for an update, as opposed to what it says when it finds one.
///
/// <para>
/// The interval is the design's number changed deliberately — weekly to daily — and [[ADR-018]]
/// carries why. What makes it worth pinning here is that the number only became load-bearing when
/// the check started running on its own: while it fired on the way into the Settings dialog, a
/// stale answer cost nothing, because nobody was looking. Now it is how long a shipped fix can sit
/// unmentioned in front of somebody using the app.
/// </para>
/// </summary>
public sealed class UpdateCheckCadenceTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 25, 12, 0, 0, TimeSpan.Zero);

    private static UpdateViewModel Model(DateTimeOffset? lastCheckedAt, bool autoCheck = true) =>
        new(check: _ => Task.FromResult(new UpdateCheck(UpdateCheckResult.UpToDate, null, null)),
            install: (_, _, _) => Task.FromResult<string?>(null),
            persist: (_, _) => true,
            autoCheckEnabled: autoCheck,
            lastCheckedAt: lastCheckedAt,
            isConfigured: true);

    [Fact]
    public void The_interval_is_a_day()
    {
        Assert.Equal(TimeSpan.FromHours(24), UpdateViewModel.AutoCheckInterval);
    }

    [Fact]
    public void A_check_is_due_a_day_after_the_last_one()
    {
        Assert.True(Model(Now.AddHours(-24)).ShouldAutoCheck(Now));
        Assert.True(Model(Now.AddDays(-3)).ShouldAutoCheck(Now));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(12)]
    [InlineData(23)]
    public void Inside_the_day_nothing_is_asked(int hoursAgo)
    {
        // The tick runs every 15 seconds. If this were wrong the app would hammer the release
        // feed roughly 5,700 times a day, which is how an update check becomes a rate-limit.
        Assert.False(Model(Now.AddHours(-hoursAgo)).ShouldAutoCheck(Now));
    }

    [Fact]
    public void A_first_run_checks_immediately()
    {
        // Never checked is not "checked long ago" - it is the one case where waiting a day would
        // mean a fresh install never mentions a fix that already exists.
        Assert.True(Model(lastCheckedAt: null).ShouldAutoCheck(Now));
    }

    [Fact]
    public void Turning_the_setting_off_stops_the_asking_entirely()
    {
        // Not "check but stay quiet" - the network call itself is what the setting turns off.
        Assert.False(Model(lastCheckedAt: null, autoCheck: false).ShouldAutoCheck(Now));
    }

    [Fact]
    public void An_unconfigured_feed_is_never_due()
    {
        // No owner/repo means no releases to read. A check that cannot reach anything should not
        // burn a timestamp that then suppresses the next real one.
        var unconfigured = new UpdateViewModel(
            check: _ => Task.FromResult(new UpdateCheck(UpdateCheckResult.UpToDate, null, null)),
            install: (_, _, _) => Task.FromResult<string?>(null),
            persist: (_, _) => true,
            autoCheckEnabled: true,
            lastCheckedAt: null,
            isConfigured: false);

        Assert.False(unconfigured.ShouldAutoCheck(Now));
    }
}
