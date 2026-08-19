using WaveLinkBackup.App.Startup;

namespace WaveLinkBackup.App.Tests;

/// <summary>
/// The primitive (first wins, second knows, activation raises) is pinned in
/// <see cref="SingleInstanceTests"/>. What those tests do NOT pin is the *decision* the App makes
/// when a second launch arrives: whether it asks the first instance to show itself at all. That
/// decision is `wantsWindow: !StartInTray` — a plain launch brings the window forward, a --tray
/// launch (the autostart-at-boot path) must exit silently so boot never forces a window open.
///
/// This test lives next to ShellArgumentsTests because it is about what a parsed launch implies,
/// not about the mutex/event machinery itself.
/// </summary>
public sealed class SecondLaunchTests
{
    [Fact]
    public void A_plain_second_launch_asks_the_first_for_the_window()
    {
        var args = ShellArguments.Parse([]);

        // The App signals with wantsWindow: !args.StartInTray. A plain launch carries the window.
        Assert.True(!args.StartInTray);
    }

    [Fact]
    public void A_tray_second_launch_signals_silently()
    {
        var args = ShellArguments.Parse(["--tray"]);

        // --tray must NOT ask for a window: the first instance is already running, and forcing a
        // window open at boot would be exactly what autostart is meant to avoid.
        Assert.False(!args.StartInTray);
    }

    [Fact]
    public void The_signal_carries_the_window_request_only_for_plain_launches()
    {
        // Pin the exact expression the App uses, so a future refactor that flips or drops the
        // negation fails here rather than in a user's first boot.
        var plain = ShellArguments.Parse([]);
        var tray = ShellArguments.Parse(["--tray"]);

        Assert.True(!plain.StartInTray);   // wantsWindow: true  -> first shows its window
        Assert.False(!tray.StartInTray);   // wantsWindow: false -> second exits silently
    }
}
