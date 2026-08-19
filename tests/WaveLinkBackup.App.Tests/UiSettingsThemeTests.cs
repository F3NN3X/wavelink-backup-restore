using Microsoft.Win32;
using WaveLinkBackup.App.Windows;

namespace WaveLinkBackup.App.Tests;

/// <summary>
/// screens/11 is explicit: high contrast must be reacted to at runtime, "do not require a
/// restart". ThemeFollowingTests pins the MANAGER side of that chain (FakeSystemTheme →
/// ThemeManager re-apply); this file pins the SOURCE side — that the real UiSettingsTheme
/// actually fires Changed when Windows says the colour preference changed, and only then.
///
/// The test cannot flip SystemParameters.HighContrast without changing the developer's own
/// session, so it drives the decision point directly: HandleUserPreference is the internal seam
/// the SystemEvents subscription funnels into. Firing it with a real UserPreferenceChangedEventArgs
/// exercises the same code path Start() wires up, minus the static event. Because the call lands
/// on the UI thread (Wpf.Run), RaiseOnUiThread raises straight through rather than BeginInvoke —
/// deterministic, no dispatcher pumping required.
/// </summary>
public sealed class UiSettingsThemeTests
{
    [Fact]
    public void A_color_preference_change_fires_changed_immediately()
    {
        var fired = Wpf.Run(() =>
        {
            var theme = new UiSettingsTheme();
            theme.Start();

            int firedCount = 0;
            theme.Changed += (_, _) => firedCount++;

            // The real event type the SystemEvents subscription delivers. Category.Color is what
            // covers high contrast going on or off.
            theme.HandleUserPreference(new UserPreferenceChangedEventArgs(UserPreferenceCategory.Color));

            return firedCount;
        });

        Assert.Equal(1, fired);
    }

    [Fact]
    public void A_non_color_preference_change_does_not_fire_changed()
    {
        var fired = Wpf.Run(() =>
        {
            var theme = new UiSettingsTheme();
            theme.Start();

            int firedCount = 0;
            theme.Changed += (_, _) => firedCount++;

            // Accessibility, Sounds, Location: they fire often and mean nothing to a theme.
            // Re-applying on them would swap the dictionary for no reason.
            theme.HandleUserPreference(new UserPreferenceChangedEventArgs(UserPreferenceCategory.Accessibility));

            return firedCount;
        });

        Assert.Equal(0, fired);
    }

    [Fact]
    public void A_second_color_change_fires_changed_again()
    {
        var fired = Wpf.Run(() =>
        {
            var theme = new UiSettingsTheme();
            theme.Start();

            int firedCount = 0;
            theme.Changed += (_, _) => firedCount++;

            // On, then off: two real user actions, two re-applies. Missing either would mean the
            // app is stuck in the wrong scheme until a restart — the exact failure screens/11 forbids.
            theme.HandleUserPreference(new UserPreferenceChangedEventArgs(UserPreferenceCategory.Color));
            theme.HandleUserPreference(new UserPreferenceChangedEventArgs(UserPreferenceCategory.Color));

            return firedCount;
        });

        Assert.Equal(2, fired);
    }

    [Fact]
    public void Dispose_stops_firing_changed_on_later_color_changes()
    {
        var fired = Wpf.Run(() =>
        {
            var theme = new UiSettingsTheme();
            theme.Start();

            int firedCount = 0;
            theme.Changed += (_, _) => firedCount++;

            theme.Dispose();

            // After Dispose the subscription is gone and the disposed flag short-circuits the
            // raise. Firing here would be a leak: a tray app that keeps raising after shutdown
            // is indistinguishable from one that never exits.
            theme.HandleUserPreference(new UserPreferenceChangedEventArgs(UserPreferenceCategory.Color));

            return firedCount;
        });

        Assert.Equal(0, fired);
    }
}
