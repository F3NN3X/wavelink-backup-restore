using WaveLinkBackup.App.Hosting;
using WaveLinkBackup.Core.Results;

namespace WaveLinkBackup.App.Tests;

/// <summary>
/// Four states, one bit of state each (screens/12-tray-autostart-update.md). NEEDS YOU is
/// reachable only because technical-debt 7.3 put the CoreError on TickResult.
/// </summary>
public sealed class TrayStateTests
{
    private static readonly TrayConditions Healthy = new(
        AutoBackupEnabled: true, IsPaused: false, IsCapturing: false, LastError: null);

    [Fact]
    public void Watching_is_the_resting_state()
    {
        Assert.Equal(TrayStatus.Watching, TrayState.From(Healthy));
    }

    [Fact]
    public void Capturing_shows_backing_up()
    {
        Assert.Equal(TrayStatus.BackingUp, TrayState.From(Healthy with { IsCapturing = true }));
    }

    [Fact]
    public void An_error_shows_needs_you()
    {
        var conditions = Healthy with { LastError = new StoreUnavailable(@"D:\gone", "not there") };

        Assert.Equal(TrayStatus.NeedsYou, TrayState.From(conditions));
    }

    [Fact]
    public void Pausing_shows_paused()
    {
        Assert.Equal(TrayStatus.Paused, TrayState.From(Healthy with { IsPaused = true }));
    }

    /// <summary>
    /// Automatic backup switched off and "pause for an hour" both leave nothing watching, and
    /// the design gives them one icon state between them — with different tooltips.
    /// </summary>
    [Fact]
    public void Automatic_backup_switched_off_also_shows_paused()
    {
        Assert.Equal(TrayStatus.Paused, TrayState.From(Healthy with { AutoBackupEnabled = false }));
    }

    /// <summary>
    /// Amber outranks the rest. A failing watcher that also happens to be mid-capture is still
    /// something the user has to act on, and the quiet states must not hide it.
    /// </summary>
    [Fact]
    public void Needs_you_outranks_every_other_state()
    {
        var broken = new TrayConditions(
            AutoBackupEnabled: false,
            IsPaused: true,
            IsCapturing: true,
            LastError: new StoreUnavailable(@"D:\gone", "not there"));

        Assert.Equal(TrayStatus.NeedsYou, TrayState.From(broken));
    }

    [Fact]
    public void Capturing_outranks_paused_because_something_is_actually_happening()
    {
        var conditions = Healthy with { IsCapturing = true, IsPaused = true };

        Assert.Equal(TrayStatus.BackingUp, TrayState.From(conditions));
    }

    /// <summary>
    /// The instant is built with the LOCAL offset rather than the plan's TimeSpan.Zero: the
    /// tooltip renders local time, so a UTC instant asserted as "23:07" only passes on a machine
    /// in UTC. This one reads 23:07 locally in every timezone, which is the behaviour meant.
    /// </summary>
    [Fact]
    public void The_tooltip_names_the_last_backup()
    {
        var at = new DateTimeOffset(new DateTime(2026, 8, 15, 23, 7, 0, DateTimeKind.Local));

        var tooltip = TrayState.Tooltip(Healthy, at, System.Globalization.CultureInfo.InvariantCulture);

        Assert.StartsWith("Wave Link Backup — ", tooltip, StringComparison.Ordinal);
        Assert.Contains("23:07", tooltip, StringComparison.Ordinal);
    }

    /// <summary>
    /// "In the NEEDS YOU state the tooltip names the problem." A tray icon that only says
    /// something is wrong makes the user open the app to find out what.
    /// </summary>
    [Fact]
    public void The_tooltip_names_the_problem_when_something_is_wrong()
    {
        var conditions = Healthy with { LastError = new StoreUnavailable(@"D:\gone", "not there") };

        var tooltip = TrayState.Tooltip(conditions, null);

        Assert.Contains("backup folder", tooltip, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_tooltip_copes_with_never_having_backed_up()
    {
        Assert.Contains("No backup yet", TrayState.Tooltip(Healthy, null), StringComparison.Ordinal);
    }
}
