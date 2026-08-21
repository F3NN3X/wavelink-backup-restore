using WaveLinkBackup.App.Tests.Fakes;
using WaveLinkBackup.App.Theming;

namespace WaveLinkBackup.App.Tests;

/// <summary>
/// The preference and the OS reduced to one palette, and the wrapper that puts that answer behind
/// the interface the whole app already reads.
///
/// The precedence rule is the part worth pinning: Windows' own high-contrast scheme beats every
/// choice made in Settings, because it is Windows saying the palette is no longer ours
/// (screens/11). A preference that could paint over it would paint over the one scheme somebody
/// turned on because they cannot read ours.
/// </summary>
public sealed class ThemeChoiceTests
{
    [Theory]
    [InlineData(ThemePreference.Auto, AppTheme.Dark, AppTheme.Dark)]
    [InlineData(ThemePreference.Auto, AppTheme.Light, AppTheme.Light)]
    [InlineData(ThemePreference.Dark, AppTheme.Light, AppTheme.Dark)]
    [InlineData(ThemePreference.Light, AppTheme.Dark, AppTheme.Light)]
    [InlineData(ThemePreference.HighContrast, AppTheme.Dark, AppTheme.HighContrast)]
    public void The_preference_decides_while_windows_is_not_in_high_contrast(
        ThemePreference preference, AppTheme system, AppTheme expected)
    {
        Assert.Equal(expected, ThemeChoice.Resolve(preference, system, systemIsHighContrast: false));
    }

    [Theory]
    [InlineData(ThemePreference.Auto)]
    [InlineData(ThemePreference.Dark)]
    [InlineData(ThemePreference.Light)]
    [InlineData(ThemePreference.HighContrast)]
    public void Windows_high_contrast_outranks_every_preference(ThemePreference preference)
    {
        Assert.Equal(
            AppTheme.HighContrast,
            ThemeChoice.Resolve(preference, AppTheme.Dark, systemIsHighContrast: true));
    }

    [Theory]
    [InlineData(ThemePreference.Auto)]
    [InlineData(ThemePreference.Dark)]
    [InlineData(ThemePreference.Light)]
    [InlineData(ThemePreference.HighContrast)]
    public void Every_preference_survives_a_round_trip_through_shell_json(ThemePreference preference)
    {
        Assert.Equal(preference, ThemeChoice.FromStorageName(ThemeChoice.ToStorageName(preference)));
    }

    /// <summary>
    /// Tolerant per field, like every other value shell.json reads. A preference nobody can honour
    /// is one to defer to Windows over, not one to fail a launch over.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("sepia")]
    [InlineData("2")]
    public void An_unreadable_preference_falls_back_to_following_windows(string? stored)
    {
        Assert.Equal(ThemePreference.Auto, ThemeChoice.FromStorageName(stored));
    }

    /// <summary>Spelt names, not the enum's numbers - shell.json is a file a person can open.</summary>
    [Fact]
    public void The_stored_names_are_words()
    {
        Assert.Equal("auto", ThemeChoice.ToStorageName(ThemePreference.Auto));
        Assert.Equal("highContrast", ThemeChoice.ToStorageName(ThemePreference.HighContrast));
    }

    // ------------------------------------------------------------------ the wrapper

    [Fact]
    public void The_wrapper_answers_with_the_preference_not_the_os()
    {
        var system = new FakeSystemTheme { Theme = AppTheme.Dark };
        using var preferred = new PreferredTheme(system, () => ThemePreference.Light);

        Assert.Equal(AppTheme.Light, preferred.Theme);
    }

    /// <summary>
    /// The reason IsHighContrast belongs to the EFFECTIVE theme rather than to Windows: every
    /// high-contrast rendering rule in the app keys off this one bool, and a user who picked the
    /// palette needs all of them.
    /// </summary>
    [Fact]
    public void Choosing_high_contrast_turns_on_the_high_contrast_rendering_rules()
    {
        var system = new FakeSystemTheme { Theme = AppTheme.Dark, IsHighContrast = false };
        using var preferred = new PreferredTheme(system, () => ThemePreference.HighContrast);

        Assert.True(preferred.IsHighContrast);
    }

    [Fact]
    public void A_change_in_the_preference_raises_the_same_event_an_os_change_raises()
    {
        var system = new FakeSystemTheme();
        var choice = ThemePreference.Auto;
        using var preferred = new PreferredTheme(system, () => choice);

        var raised = 0;
        preferred.Changed += (_, _) => raised++;

        choice = ThemePreference.Light;
        preferred.Refresh();

        system.RaiseChanged();

        Assert.Equal(2, raised);
    }

    /// <summary>
    /// A subscriber that reads the sender must not be handed the inner theme, which knows nothing
    /// about the preference and would answer the OS's question rather than the app's.
    /// </summary>
    [Fact]
    public void The_re_raised_event_comes_from_the_wrapper()
    {
        var system = new FakeSystemTheme();
        using var preferred = new PreferredTheme(system, () => ThemePreference.Auto);

        object? sender = null;
        preferred.Changed += (s, _) => sender = s;

        system.RaiseChanged();

        Assert.Same(preferred, sender);
    }

    [Fact]
    public void Starting_and_disposing_reach_the_real_thing()
    {
        var system = new FakeSystemTheme();
        var preferred = new PreferredTheme(system, () => ThemePreference.Auto);

        preferred.Start();
        Assert.True(system.Started);

        preferred.Dispose();
        Assert.True(system.Disposed);
    }

    /// <summary>
    /// A disposed wrapper stops re-raising. It subscribes to the inner theme in its constructor,
    /// and a handler left behind on a live OS listener is how a closed window keeps being asked to
    /// re-theme itself.
    /// </summary>
    [Fact]
    public void A_disposed_wrapper_stops_listening()
    {
        var system = new FakeSystemTheme();
        var preferred = new PreferredTheme(system, () => ThemePreference.Auto);

        var raised = 0;
        preferred.Changed += (_, _) => raised++;

        preferred.Dispose();
        system.RaiseChanged();

        Assert.Equal(0, raised);
    }
}
