using WaveLinkBackup.App.Hosting;

namespace WaveLinkBackup.App.Tests;

/// <summary>
/// screens/12's "Notifications — exactly two", which the app had none of in any form
/// (technical-debt.md §4.21 item 6).
///
/// The rule with teeth is the one about restraint: "A successful backup NEVER notifies. A safety
/// net that congratulates itself weekly gets muted, and then it is not a safety net." That is
/// satisfied by construction here — nothing in <see cref="TrayNotifications"/> takes a success as
/// an input — so what these tests pin is the other half: the nine-day notice fires ONCE, and
/// re-arms only when the condition it describes has actually gone away.
/// </summary>
public sealed class TrayNotificationTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 20, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_recent_backup_notifies_nothing()
    {
        var notifications = new TrayNotifications();

        Assert.Null(notifications.NothingBackedUp(Now.AddDays(-1), Now));
    }

    [Fact]
    public void Eight_days_is_still_within_the_window()
    {
        var notifications = new TrayNotifications();

        Assert.Null(notifications.NothingBackedUp(Now.AddDays(-8), Now));
    }

    [Fact]
    public void Nine_days_notifies_with_the_designed_copy()
    {
        var notifications = new TrayNotifications();

        var notice = notifications.NothingBackedUp(Now.AddDays(-9), Now);

        Assert.NotNull(notice);
        Assert.Equal(TrayNotificationKind.NothingBackedUp, notice.Kind);
        Assert.Equal("Nothing has been backed up for 9 days.", notice.Title);
        Assert.Equal("Choose a folder…", notice.ActionLabel);
    }

    /// <summary>A store that has never held a backup is exactly what this exists to catch.</summary>
    [Fact]
    public void Never_having_backed_up_at_all_notifies()
    {
        Assert.NotNull(new TrayNotifications().NothingBackedUp(null, Now));
    }

    /// <summary>"The nine-day notice fires once, not daily."</summary>
    [Fact]
    public void The_nine_day_notice_fires_once_however_often_it_is_asked()
    {
        var notifications = new TrayNotifications();

        Assert.NotNull(notifications.NothingBackedUp(Now.AddDays(-9), Now));
        Assert.Null(notifications.NothingBackedUp(Now.AddDays(-10), Now.AddDays(1)));
        Assert.Null(notifications.NothingBackedUp(Now.AddDays(-40), Now.AddDays(31)));
    }

    /// <summary>
    /// Once per EPISODE, not once per process. A machine left running for months that recovers and
    /// then falls behind again is describing a second, real problem.
    /// </summary>
    [Fact]
    public void A_backup_re_arms_the_notice_for_the_next_time_it_goes_quiet()
    {
        var notifications = new TrayNotifications();

        Assert.NotNull(notifications.NothingBackedUp(Now.AddDays(-9), Now));

        // A backup happens: the condition clears.
        Assert.Null(notifications.NothingBackedUp(Now, Now));

        // And nine days later it is a new episode, not a repeat of the old one.
        Assert.NotNull(notifications.NothingBackedUp(Now, Now.AddDays(9)));
    }

    [Fact]
    public void The_reset_notice_names_the_backup_that_puts_you_back()
    {
        var notice = TrayNotifications.WaveLinkReset("Before restore");

        Assert.Equal(TrayNotificationKind.WaveLinkReset, notice.Kind);
        Assert.Equal("Wave Link reset your settings.", notice.Title);
        Assert.Contains("\"Before restore\" will put you back.", notice.Body, StringComparison.Ordinal);
        Assert.Equal("Restore \"Before restore\"", notice.ActionLabel);
    }
}
