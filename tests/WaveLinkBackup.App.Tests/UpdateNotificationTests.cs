using WaveLinkBackup.App.Hosting;

namespace WaveLinkBackup.App.Tests;

/// <summary>
/// The third tray notification, and the cadence that keeps it from becoming a nag.
///
/// <para>
/// "Once" is the whole difference between a warning and a nag — the same argument the nine-day
/// notice already makes. But an update is not an episode: it stays available until it is
/// installed, so "once per episode" would mean once ever, and "once per process" would nag on
/// every launch until the user gave in. Per-version is the honest cadence, and these tests are
/// what hold it there.
/// </para>
/// </summary>
public sealed class UpdateNotificationTests
{
    [Fact]
    public void An_available_version_notifies_once()
    {
        var notifications = new TrayNotifications();

        var first = notifications.UpdateAvailable("0.7.5");
        var second = notifications.UpdateAvailable("0.7.5");
        var third = notifications.UpdateAvailable("0.7.5");

        Assert.NotNull(first);
        Assert.Equal(TrayNotificationKind.UpdateAvailable, first.Kind);
        Assert.Null(second);
        Assert.Null(third);
    }

    [Fact]
    public void A_newer_version_notifies_again()
    {
        // The check runs weekly forever. Being told about 0.7.5 must not silence 0.7.6.
        var notifications = new TrayNotifications();

        Assert.NotNull(notifications.UpdateAvailable("0.7.5"));
        Assert.NotNull(notifications.UpdateAvailable("0.7.6"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void No_update_is_silence_rather_than_a_notification(string? nothing)
    {
        Assert.Null(new TrayNotifications().UpdateAvailable(nothing));
    }

    [Fact]
    public void Losing_the_update_re_arms_it()
    {
        // Installed, or the release withdrawn. If the same version comes back it is news again -
        // the same re-arming the nine-day notice does when the condition clears.
        var notifications = new TrayNotifications();

        Assert.NotNull(notifications.UpdateAvailable("0.7.5"));
        Assert.Null(notifications.UpdateAvailable(null));
        Assert.NotNull(notifications.UpdateAvailable("0.7.5"));
    }

    [Fact]
    public void The_body_says_the_backups_are_safe()
    {
        // The one question a person actually has before letting a backup tool replace itself.
        // Saying it in the notice beats making them open Settings to find out.
        var notice = new TrayNotifications().UpdateAvailable("0.7.5")!;

        Assert.Contains("0.7.5", notice.Title, StringComparison.Ordinal);
        Assert.Contains("backups are unaffected", notice.Body, StringComparison.OrdinalIgnoreCase);
        Assert.NotEmpty(notice.ActionLabel);
    }

    [Fact]
    public void Nothing_here_can_congratulate_itself_on_a_backup()
    {
        // The rule the design was actually protecting: "A successful backup NEVER notifies."
        // It is enforced by the type - no method takes a completed backup as an input - and this
        // is what says so when someone adds a fourth kind.
        var takesABackup = typeof(TrayNotifications)
            .GetMethods()
            .Where(m => m.DeclaringType == typeof(TrayNotifications))
            .SelectMany(m => m.GetParameters())
            .Any(p => p.ParameterType.Name.Contains("Snapshot", StringComparison.Ordinal)
                   || p.Name?.Contains("success", StringComparison.OrdinalIgnoreCase) == true);

        Assert.False(takesABackup,
            "A successful backup never notifies. Nothing here may take one as an input.");
    }
}
