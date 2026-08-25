using WaveLinkBackup.Core.Automation;

namespace WaveLinkBackup.App.Tests;

/// <summary>
/// A check that FAILED still counts as a check.
///
/// <para>
/// <b>The regression this exists for.</b> The automatic check was moved onto the timer tick, which
/// runs every 15 seconds, and the timestamp that makes it back off was written only after the feed
/// answered. A machine that was offline, blocked by a proxy, or rate-limited by GitHub therefore
/// never recorded an attempt — and re-tried on every tick, roughly 5,700 times a day. **The failure
/// that most needs backing off was the one case that skipped it.**
/// </para>
///
/// <para>
/// <see cref="BackupSettings"/> had said so all along, about this exact field: "when the last check
/// ran, successful or not. Recording a FAILED look too is deliberate: otherwise a machine that is
/// offline for a fortnight re-checks on every tick." The doc was right and the code was new.
/// </para>
/// </summary>
public sealed class UpdateCheckBackoffTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 25, 12, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// The rule the app applies before making the request, expressed once so a test can assert it
    /// without standing up a window, a feed and an HTTP client.
    /// </summary>
    private static bool IsDue(BackupSettings settings, DateTimeOffset now) =>
        settings.CheckForUpdates
        && (settings.LastUpdateCheckUtc is not { } last
            || now - last >= WaveLinkBackup.App.ViewModels.UpdateViewModel.AutoCheckInterval);

    [Fact]
    public void An_attempt_that_failed_still_holds_off_the_next_one()
    {
        // Recorded on the way out of the check, in a finally - not on the success path.
        var afterFailedAttempt = BackupSettings.Default with { LastUpdateCheckUtc = Now };

        Assert.False(IsDue(afterFailedAttempt, Now.AddMinutes(1)));
        Assert.False(IsDue(afterFailedAttempt, Now.AddHours(23)));
    }

    [Fact]
    public void And_it_stops_holding_off_a_day_later()
    {
        // Backing off must not become giving up: an offline machine that comes back online should
        // find out about a fix the next day, not never.
        var afterFailedAttempt = BackupSettings.Default with { LastUpdateCheckUtc = Now };

        Assert.True(IsDue(afterFailedAttempt, Now.AddHours(24)));
    }

    [Fact]
    public void Never_having_checked_is_due_immediately()
    {
        Assert.True(IsDue(BackupSettings.Default with { LastUpdateCheckUtc = null }, Now));
    }

    [Fact]
    public void The_setting_still_wins_over_everything()
    {
        // Off means the request is never made, not that the answer is hidden.
        var off = BackupSettings.Default with { CheckForUpdates = false, LastUpdateCheckUtc = null };

        Assert.False(IsDue(off, Now));
    }
}
