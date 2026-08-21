using WaveLinkBackup.Core.Automation;

namespace WaveLinkBackup.Core.Tests;

/// <summary>
/// The two timings a user can set — operations/design/screens/14-backup-timing.md.
///
/// Both are pure decisions, so every case here is arithmetic and none of them wait. The offsets are
/// explicit on purpose: "every day at 03:00" is a wall clock, and a test that leaned on the
/// machine's timezone would pass in Oslo and fail in CI.
/// </summary>
public sealed class DailyBackupTests
{
    private static readonly TimeSpan Oslo = TimeSpan.FromHours(2);

    private static DateTimeOffset At(int day, int hour, int minute = 0) =>
        new(2026, 8, day, hour, minute, 0, Oslo);

    private static AutoBackupPolicy Daily(TimeOnly at, int intervalMinutes = 60) =>
        new(TimeSpan.FromSeconds(60), TimeSpan.FromMinutes(intervalMinutes), at);

    // -------------------------------------------------------------------- the interval is a setting

    [Fact]
    public void The_interval_comes_from_the_settings_rather_than_a_constant()
    {
        // It used to be an hour, hard-coded, while the Settings dialog said "at most one an hour"
        // as though that were a fact about the world.
        var policy = AutoBackupPolicy.For(
            BackupSettings.Default with { AutoBackupIntervalMinutes = 15 });

        Assert.Equal(TimeSpan.FromMinutes(15), policy.MinimumInterval);

        // Twelve minutes after the last automatic capture, with a settled write pending: rate
        // limited at an hour, due at fifteen minutes.
        Assert.Equal(
            CaptureDecision.RateLimited,
            policy.Decide(At(19, 12, 0), At(19, 12, 3), At(19, 12, 10)));

        Assert.Equal(
            CaptureDecision.Capture,
            policy.Decide(At(19, 12, 0), At(19, 11, 40), At(19, 12, 10)));
    }

    [Fact]
    public void Settings_with_no_timings_recorded_behave_exactly_as_before()
    {
        var policy = AutoBackupPolicy.For(BackupSettings.Default);

        Assert.Equal(AutoBackupPolicy.Default.MinimumInterval, policy.MinimumInterval);
        Assert.Null(policy.DailyAt);
    }

    // ------------------------------------------------------------------------- the daily copy

    [Fact]
    public void Nothing_is_scheduled_before_the_time_arrives()
    {
        Assert.Equal(
            CaptureDecision.NothingPending,
            Daily(new TimeOnly(3, 0)).Decide(null, null, At(19, 2, 59)));
    }

    [Fact]
    public void The_daily_copy_is_due_once_the_time_has_passed_with_nothing_captured_since()
    {
        Assert.Equal(
            CaptureDecision.Scheduled,
            Daily(new TimeOnly(3, 0)).Decide(null, At(18, 22, 0), At(19, 3, 0)));
    }

    [Fact]
    public void A_backup_taken_after_the_set_time_does_not_suppress_the_daily_copy()
    {
        // The daily backup is a GUARANTEED copy at its set time, independent of change-driven
        // captures. The old rule suppressed it when any automatic capture had landed after the set
        // time - which made it silently do nothing on any machine where Wave Link writes settings
        // during the day. A change-driven capture no longer vetoes the schedule; only today's own
        // daily copy (lastDailyAt) does.
        Assert.Equal(
            CaptureDecision.Scheduled,
            Daily(new TimeOnly(3, 0)).Decide(null, At(19, 3, 30), At(19, 9, 0)));
    }

    [Fact]
    public void It_does_not_fire_twice_in_one_day()
    {
        var policy = Daily(new TimeOnly(3, 0));

        Assert.Equal(CaptureDecision.Scheduled, policy.Decide(null, null, At(19, 3, 0)));

        // Having run at 03:00, it is done until tomorrow - even though lastAutoCaptureAt stayed
        // null, which is what a deduped daily copy looks like.
        Assert.Equal(
            CaptureDecision.NothingPending,
            policy.Decide(null, null, At(19, 23, 59), lastDailyAt: At(19, 3, 0)));

        Assert.Equal(
            CaptureDecision.Scheduled,
            policy.Decide(null, null, At(20, 3, 0), lastDailyAt: At(19, 3, 0)));
    }

    [Fact]
    public void A_machine_that_was_asleep_at_the_set_time_captures_when_it_wakes()
    {
        // Late is the useful direction to be wrong. The alternative is a schedule that silently
        // does nothing for anyone who does not leave their computer on overnight.
        Assert.Equal(
            CaptureDecision.Scheduled,
            Daily(new TimeOnly(3, 0)).Decide(null, At(18, 20, 0), At(19, 9, 30), At(18, 3, 0)));
    }

    [Fact]
    public void The_rate_limit_never_suppresses_the_daily_copy()
    {
        // A daily backup is an instruction with a time on it; the interval is a cap on
        // change-driven captures. A cap that could veto an explicit schedule would make the daily
        // setting silently do nothing for anyone who edits their mixer at 02:55.
        Assert.Equal(
            CaptureDecision.Scheduled,
            Daily(new TimeOnly(3, 0), intervalMinutes: 1440)
                .Decide(At(19, 2, 55), At(19, 2, 56), At(19, 3, 0), At(18, 3, 0)));
    }

    [Fact]
    public void With_no_daily_time_set_nothing_is_ever_scheduled()
    {
        var policy = new AutoBackupPolicy(TimeSpan.FromSeconds(60), TimeSpan.FromHours(1));

        Assert.Equal(CaptureDecision.NothingPending, policy.Decide(null, null, At(19, 3, 0)));
        Assert.Equal(CaptureDecision.NothingPending, policy.Decide(null, null, At(19, 23, 59)));
    }

    [Fact]
    public void Midnight_is_a_valid_time_and_not_read_as_off()
    {
        // 00:00 is zero minutes past midnight, and zero is exactly the value an int? nullable makes
        // easy to confuse with "not set".
        var settings = BackupSettings.Default with { DailyBackupMinutes = 0 };

        Assert.Equal(new TimeOnly(0, 0), settings.DailyBackupAt);
        Assert.Equal(
            CaptureDecision.Scheduled,
            AutoBackupPolicy.For(settings).Decide(null, null, At(19, 0, 1)));
    }

    // ---------------------------------------------------------------------------- round-tripping

    [Fact]
    public void Both_timings_survive_a_write_and_a_read()
    {
        var settings = BackupSettings.Default with
        {
            AutoBackupIntervalMinutes = 240,
            DailyBackupMinutes = 4 * 60 + 30,
        };

        var read = SettingsSerializer.Read(SettingsSerializer.Write(settings));

        Assert.Equal(240, read.AutoBackupIntervalMinutes);
        Assert.Equal(new TimeOnly(4, 30), read.DailyBackupAt);
    }

    [Fact]
    public void A_settings_file_written_before_these_existed_reads_as_the_old_behaviour()
    {
        // No schema bump, for the same reason phase 6's tier toggles had none: a field whose
        // absence means its default is exactly what the tolerant read already handles.
        var read = SettingsSerializer.Read("""
            {"schemaVersion":1,"storePath":"D:\\B","autoBackupEnabled":true,"autoBackupKeepCount":30}
            """u8);

        Assert.Equal(60, read.AutoBackupIntervalMinutes);
        Assert.Null(read.DailyBackupAt);
    }
}
