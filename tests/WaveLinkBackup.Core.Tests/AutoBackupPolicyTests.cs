using WaveLinkBackup.Core.Automation;

namespace WaveLinkBackup.Core.Tests;

/// <summary>
/// The debounce and rate limit, as a pure function of three timestamps. No test here waits
/// for anything - a suite that takes 60 seconds to prove a 60-second debounce is a suite
/// nobody runs.
///
/// The behaviour is described to users in the Settings dialog: "Wave Link writes its file the
/// moment you touch a channel. This notices, waits a minute, then keeps a copy - at most one
/// an hour." That copy is a specification. If these constants change, it changes too.
/// </summary>
public sealed class AutoBackupPolicyTests
{
    private static readonly DateTimeOffset T0 = new(2026, 8, 16, 12, 0, 0, TimeSpan.Zero);
    private static readonly AutoBackupPolicy Policy = AutoBackupPolicy.Default;

    [Fact]
    public void The_defaults_match_the_copy_shown_to_users()
    {
        Assert.Equal(TimeSpan.FromSeconds(60), Policy.Debounce);
        Assert.Equal(TimeSpan.FromHours(1), Policy.MinimumInterval);
    }

    [Fact]
    public void Nothing_pending_means_nothing_to_do()
    {
        Assert.Equal(CaptureDecision.NothingPending,
            Policy.Decide(lastWriteAt: null, lastAutoCaptureAt: null, now: T0));
    }

    [Fact]
    public void A_write_is_not_captured_immediately()
    {
        // Wave Link writes on every touch of a channel; capturing each one would store every
        // intermediate state of a fader drag.
        Assert.Equal(CaptureDecision.Waiting,
            Policy.Decide(lastWriteAt: T0, lastAutoCaptureAt: null, now: T0.AddSeconds(1)));
    }

    [Fact]
    public void A_write_is_captured_once_the_debounce_has_passed()
    {
        Assert.Equal(CaptureDecision.Capture,
            Policy.Decide(lastWriteAt: T0, lastAutoCaptureAt: null, now: T0.AddSeconds(61)));
    }

    [Fact]
    public void The_debounce_measures_from_the_LAST_write_not_the_first()
    {
        // A burst restarts the clock. Five writes over ten seconds is one configuration
        // change, not five.
        var lastOfBurst = T0.AddSeconds(10);

        Assert.Equal(CaptureDecision.Waiting,
            Policy.Decide(lastWriteAt: lastOfBurst, lastAutoCaptureAt: null, now: T0.AddSeconds(65)));

        Assert.Equal(CaptureDecision.Capture,
            Policy.Decide(lastWriteAt: lastOfBurst, lastAutoCaptureAt: null, now: T0.AddSeconds(71)));
    }

    [Fact]
    public void Exactly_at_the_debounce_boundary_counts_as_elapsed()
    {
        Assert.Equal(CaptureDecision.Capture,
            Policy.Decide(lastWriteAt: T0, lastAutoCaptureAt: null, now: T0.AddSeconds(60)));
    }

    [Fact]
    public void A_second_change_within_the_hour_is_rate_limited()
    {
        // Debounce satisfied, but an automatic snapshot was taken 30 minutes ago.
        var decision = Policy.Decide(
            lastWriteAt: T0.AddMinutes(30),
            lastAutoCaptureAt: T0,
            now: T0.AddMinutes(31));

        Assert.Equal(CaptureDecision.RateLimited, decision);
    }

    [Fact]
    public void A_change_after_the_hour_is_captured()
    {
        Assert.Equal(CaptureDecision.Capture, Policy.Decide(
            lastWriteAt: T0.AddMinutes(61),
            lastAutoCaptureAt: T0,
            now: T0.AddMinutes(62)));
    }

    [Fact]
    public void The_debounce_is_checked_before_the_rate_limit()
    {
        // Both unsatisfied. Reporting Waiting is more accurate than RateLimited, because the
        // write is what has not settled yet - and the distinction is what a diagnostic log
        // needs to be useful.
        Assert.Equal(CaptureDecision.Waiting, Policy.Decide(
            lastWriteAt: T0.AddMinutes(30),
            lastAutoCaptureAt: T0,
            now: T0.AddMinutes(30).AddSeconds(5)));
    }

    [Fact]
    public void A_clock_that_goes_backwards_does_not_capture_early()
    {
        // NTP correction, or a user changing the system clock. Negative elapsed time must not
        // read as "long enough ago".
        Assert.Equal(CaptureDecision.Waiting,
            Policy.Decide(lastWriteAt: T0, lastAutoCaptureAt: null, now: T0.AddMinutes(-5)));
    }

    [Fact]
    public void A_clock_that_goes_backwards_does_not_bypass_the_rate_limit()
    {
        Assert.Equal(CaptureDecision.RateLimited, Policy.Decide(
            lastWriteAt: T0.AddMinutes(-10),
            lastAutoCaptureAt: T0,
            now: T0.AddMinutes(-5)));
    }

    [Fact]
    public void A_custom_policy_uses_its_own_intervals()
    {
        var eager = new AutoBackupPolicy(TimeSpan.FromSeconds(5), TimeSpan.FromMinutes(1));

        Assert.Equal(CaptureDecision.Capture,
            eager.Decide(lastWriteAt: T0, lastAutoCaptureAt: null, now: T0.AddSeconds(6)));
    }

    [Fact]
    public void A_zero_debounce_captures_on_the_next_evaluation()
    {
        var immediate = new AutoBackupPolicy(TimeSpan.Zero, TimeSpan.Zero);

        Assert.Equal(CaptureDecision.Capture,
            immediate.Decide(lastWriteAt: T0, lastAutoCaptureAt: T0, now: T0));
    }
}
