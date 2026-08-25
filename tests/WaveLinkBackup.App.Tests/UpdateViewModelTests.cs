using WaveLinkBackup.App.Updates;
using WaveLinkBackup.App.ViewModels;

namespace WaveLinkBackup.App.Tests;

/// <summary>
/// Settings' UPDATES section (screens/12).
///
/// **The design's hardest rule here is a rule about restraint**, and it is what most of these
/// tests are for: "An available update is NEVER a notification, a badge or a banner", and "It
/// never installs anything without you." So the model can be asked to check and asked to install,
/// and the only thing it ever does unprompted is look.
/// </summary>
public sealed class UpdateViewModelTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 20, 12, 0, 0, TimeSpan.Zero);

    private static UpdateRelease Release(string version = "1.4.0") => new(
        Version.Parse(version + ".0"),
        new DateTimeOffset(2026, 8, 12, 9, 14, 0, TimeSpan.Zero),
        "https://example.invalid/app.zip",
        4_300_000,
        "https://example.invalid/app.zip.sha256",
        "https://example.invalid/notes");

    private sealed class Harness
    {
        public UpdateCheck Answer { get; set; } = UpdateCheck.UpToDate;

        public int Checks { get; private set; }

        public int Installs { get; private set; }

        public string? InstallFailure { get; set; }

        public List<(bool AutoCheck, DateTimeOffset? At)> Persisted { get; } = [];

        public UpdateViewModel Build(bool autoCheck = true, DateTimeOffset? lastChecked = null) =>
            new(
                check: _ => { Checks++; return Task.FromResult(Answer); },
                install: (_, _, _) => { Installs++; return Task.FromResult(InstallFailure); },
                persist: (auto, at) => { Persisted.Add((auto, at)); return true; },
                autoCheckEnabled: autoCheck,
                lastCheckedAt: lastChecked);
    }

    // ------------------------------------------------------------ nothing happens on its own

    [Fact]
    public void Building_the_model_checks_nothing_and_installs_nothing()
    {
        var h = new Harness();
        h.Build();

        Assert.Equal(0, h.Checks);
        Assert.Equal(0, h.Installs);
    }

    /// <summary>
    /// A found update must not install itself. This is the one behaviour in the section that
    /// could not be undone by the user.
    /// </summary>
    [Fact]
    public async Task Finding_an_update_installs_nothing()
    {
        var h = new Harness { Answer = UpdateCheck.Available(Release()) };
        var model = h.Build();

        await model.CheckAsync(Now, TestContext.Current.CancellationToken);

        Assert.True(model.HasUpdate);
        Assert.Equal(0, h.Installs);
    }

    [Fact]
    public async Task Installing_happens_only_when_asked()
    {
        var h = new Harness { Answer = UpdateCheck.Available(Release()) };
        var model = h.Build();

        await model.CheckAsync(Now, TestContext.Current.CancellationToken);
        await model.InstallAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, h.Installs);
    }

    [Fact]
    public async Task Installing_with_nothing_found_does_nothing()
    {
        var h = new Harness();
        var model = h.Build();

        await model.InstallAsync(TestContext.Current.CancellationToken);

        Assert.Equal(0, h.Installs);
    }

    // ------------------------------------------------------------ the weekly check

    [Fact]
    public void A_check_is_due_when_one_has_never_run()
    {
        Assert.True(new Harness().Build().ShouldAutoCheck(Now));
    }

    /// <summary>
    /// **This pair encoded the WEEKLY interval and was correct when written.** ADR-018 moved the
    /// interval to a day, so the first of them now asserts the opposite of the shipped behaviour.
    /// Rewritten rather than deleted: the boundary is still the thing worth pinning, and the two
    /// cases either side of it are still the useful ones.
    ///
    /// Weekly was right while the check only ran on the way into the Settings dialog - a rare,
    /// deliberate visit where a stale answer costs nothing. It stopped being right when the check
    /// began running on its own and saying something when it found one.
    /// </summary>
    [Fact]
    public void A_check_is_not_due_an_hour_after_the_last_one()
    {
        Assert.False(new Harness().Build(lastChecked: Now.AddHours(-1)).ShouldAutoCheck(Now));
    }

    [Fact]
    public void A_check_is_due_a_day_after_the_last_one()
    {
        Assert.True(new Harness().Build(lastChecked: Now.AddDays(-1)).ShouldAutoCheck(Now));
    }

    [Fact]
    public void Switching_the_weekly_check_off_stops_it_being_due_at_all()
    {
        Assert.False(new Harness().Build(autoCheck: false).ShouldAutoCheck(Now));
    }

    /// <summary>
    /// A FAILED look still records that we looked. Otherwise a machine that is offline for a
    /// fortnight re-checks on every tick.
    /// </summary>
    [Fact]
    public async Task A_failed_check_still_stamps_the_time_so_it_does_not_retry_forever()
    {
        var h = new Harness { Answer = UpdateCheck.Failed("NO CONNECTION") };
        var model = h.Build();

        await model.CheckAsync(Now, TestContext.Current.CancellationToken);

        Assert.Equal(UpdateCheckResult.CheckFailed, model.Result);
        Assert.Contains(h.Persisted, p => p.At == Now);
        Assert.False(model.ShouldAutoCheck(Now));
    }

    [Fact]
    public void Toggling_the_switch_commits_immediately()
    {
        var h = new Harness();
        var model = h.Build();

        model.AutoCheck = false;

        Assert.Contains(h.Persisted, p => !p.AutoCheck);
    }

    // ------------------------------------------------------------ what the rows say

    [Fact]
    public async Task An_available_update_prints_the_line_the_design_gives_it()
    {
        var h = new Harness { Answer = UpdateCheck.Available(Release()) };
        var model = h.Build();

        await model.CheckAsync(Now, TestContext.Current.CancellationToken);

        Assert.Equal("1.4.0 is available", model.Headline);
        Assert.Contains($"YOU HAVE {model.CurrentVersion}", model.Meta, StringComparison.Ordinal);
        Assert.Contains("RELEASED", model.Meta, StringComparison.Ordinal);
        Assert.NotNull(model.NotesUrl);
    }

    [Fact]
    public async Task Up_to_date_prints_the_version_and_when_it_last_looked()
    {
        var h = new Harness();
        var model = h.Build();

        await model.CheckAsync(Now, TestContext.Current.CancellationToken);

        Assert.Equal("Up to date", model.Headline);
        Assert.Contains("CHECKED", model.Meta, StringComparison.Ordinal);
        Assert.False(model.HasUpdate);
    }

    [Fact]
    public void Never_having_checked_says_so_rather_than_claiming_a_time()
    {
        Assert.Contains("NEVER CHECKED", new Harness().Build().Meta, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------ the failed-update block

    [Fact]
    public async Task A_failed_install_shows_the_neutral_block_with_its_reason()
    {
        var h = new Harness
        {
            Answer = UpdateCheck.Available(Release()),
            InstallFailure = "COULDN'T WRITE TO C:\\PROGRAM FILES\\WAVELINKBACKUP\\ · ACCESS DENIED",
        };
        var model = h.Build();

        await model.CheckAsync(Now, TestContext.Current.CancellationToken);
        await model.InstallAsync(TestContext.Current.CancellationToken);

        Assert.True(model.HasFailed);
        Assert.Equal("The update didn't install. Nothing changed.", model.FailureLead);
        Assert.Contains("still running and still watching", model.FailureBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Try_again_clears_the_block_and_installs_again()
    {
        var h = new Harness
        {
            Answer = UpdateCheck.Available(Release()),
            InstallFailure = "ACCESS DENIED",
        };
        var model = h.Build();

        await model.CheckAsync(Now, TestContext.Current.CancellationToken);
        await model.InstallAsync(TestContext.Current.CancellationToken);

        h.InstallFailure = null;
        await model.RetryAsync(TestContext.Current.CancellationToken);

        Assert.False(model.HasFailed);
        Assert.Equal(2, h.Installs);
    }

    [Fact]
    public async Task A_successful_install_reports_no_failure()
    {
        var h = new Harness { Answer = UpdateCheck.Available(Release()) };
        var model = h.Build();

        await model.CheckAsync(Now, TestContext.Current.CancellationToken);
        await model.InstallAsync(TestContext.Current.CancellationToken);

        Assert.False(model.HasFailed);
    }

    // ------------------------------------------------------------ no feed configured

    [Fact]
    public void With_no_release_feed_the_section_hides_and_nothing_can_be_pressed()
    {
        var model = new UpdateViewModel(
            check: _ => Task.FromResult(UpdateCheck.Unknown),
            install: (_, _, _) => Task.FromResult<string?>(null),
            persist: (_, _) => true,
            autoCheckEnabled: true,
            lastCheckedAt: null,
            isConfigured: false);

        Assert.False(model.IsConfigured);
        Assert.False(model.CanAct);
        Assert.False(model.ShouldAutoCheck(Now));
    }
}
