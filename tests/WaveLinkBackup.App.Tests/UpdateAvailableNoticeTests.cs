using WaveLinkBackup.App.ViewModels;

namespace WaveLinkBackup.App.Tests;

/// <summary>
/// The "an update is available" notice, on the one surface that is always in front of the user
/// while the window is open.
///
/// <para>
/// Before this, the only place an update was ever mentioned was the Settings dialog's UPDATES
/// section — and the weekly auto-check ran when that dialog OPENED, so "check weekly, on by
/// default" meant "check weekly, the next time you happen to open Settings". Someone who never
/// opens Settings was never told. These tests pin the notice; the startup check that feeds it is
/// pinned in <see cref="UpdateStartupCheckTests"/>.
/// </para>
/// </summary>
public sealed class UpdateAvailableNoticeTests
{
    private static readonly DateTimeOffset SavedAt =
        new(2026, 8, 25, 15, 22, 0, TimeSpan.Zero);

    /// <summary>
    /// The harness builds a shell whose strip already reads
    /// "WAVE LINK RUNNING · SETTINGS LAST SAVED 15:22 · AUTOMATIC BACKUP ON"; this layers the one
    /// fact under test on top of it rather than hand-rolling a second ShellFacts that would drift.
    /// </summary>
    private static ShellViewModel Shell(string? updateVersion)
    {
        var shell = ShellViewModelHarness.Build(
            waveLinkRunning: true,
            waveLinkFound: true,
            folderMissing: false,
            autoBackupEnabled: true,
            freeBytes: 100_000_000,
            storePath: @"C:\store",
            savedAt: SavedAt);

        shell.Apply(shell.Facts with { UpdateAvailableVersion = updateVersion });
        return shell;
    }

    private static string Line(string? updateVersion) => Shell(updateVersion).StatusStrip;

    [Fact]
    public void An_available_update_becomes_a_fourth_segment_on_the_strip()
    {
        Assert.EndsWith("· UPDATE 0.7.5 AVAILABLE", Line("0.7.5"), StringComparison.Ordinal);
    }

    [Fact]
    public void It_comes_last_so_the_facts_about_the_backup_keep_their_order()
    {
        // The three before it are about the thing the user came here to protect. An update is
        // about the tool, and it does not get to push them along.
        var line = Line("0.7.5");

        Assert.StartsWith(
            "WAVE LINK RUNNING · SETTINGS LAST SAVED", line, StringComparison.Ordinal);
        Assert.Contains("· AUTOMATIC BACKUP ON ·", line, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void With_no_update_the_strip_is_exactly_what_it_was(string? nothing)
    {
        // The strip never says "UP TO DATE". A segment that is always there stops being read,
        // and then the one time it changes nobody notices - which is the whole failure mode
        // this notice exists to avoid.
        Assert.DoesNotContain("UPDATE", Line(nothing), StringComparison.Ordinal);
        Assert.Equal(
            "WAVE LINK RUNNING · SETTINGS LAST SAVED 15:22 · AUTOMATIC BACKUP ON",
            Line(nothing));
    }

    [Fact]
    public void The_notice_survives_the_states_that_rewrite_the_rest_of_the_strip()
    {
        // "Wave Link not found" replaces the whole line, and it should: everything else on the
        // strip is a fact about a configuration that could not be read. An update is not - it is
        // a fact about this app, and it is still true.
        var shell = Shell("0.7.5");
        shell.Apply(shell.Facts with { WaveLinkFound = false });

        Assert.Contains("UPDATE 0.7.5 AVAILABLE", shell.StatusStrip, StringComparison.Ordinal);
    }

    [Fact]
    public void A_new_fact_raises_the_strip_so_the_window_repaints()
    {
        // The row-shows-stale-data trap: a value that changes without PropertyChanged updates the
        // field and not the screen. Apply raises everything, and this is what says so.
        var shell = Shell(null);

        var raised = new List<string?>();
        shell.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        shell.Apply(shell.Facts with { UpdateAvailableVersion = "0.7.5" });

        Assert.Contains(nameof(ShellViewModel.StatusStrip), raised);
    }
}
