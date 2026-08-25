using System.IO;
using WaveLinkBackup.App.Hosting;
using WaveLinkBackup.App.Updates;
using WaveLinkBackup.App.ViewModels;

namespace WaveLinkBackup.App.Tests;

/// <summary>
/// What happens when the swap does not go in.
///
/// <para>
/// <b>This is the observed failure, not a hypothetical.</b> An update downloaded, verified and
/// staged correctly, then did not replace the install — and the user got the old version back with
/// nothing anywhere saying why. The swap runs in the staged process, after the process the user
/// was looking at has exited, so there was no window to report into and no log: the app did
/// nothing, successfully, and told nobody.
/// </para>
///
/// <para>
/// Two things had to change. The swap retries, because a process exiting is not the same as
/// Windows finishing with its files. And when it still fails it leaves a breadcrumb the next launch
/// reads — beside <c>settings.json</c>, never in the install directory, which is the thing being
/// renamed.
/// </para>
/// </summary>
public sealed class UpdateSwapFailureTests : IDisposable
{
    private readonly string state = Path.Combine(
        Path.GetTempPath(), $"wl-swap-{Guid.NewGuid():N}");

    public UpdateSwapFailureTests() => Directory.CreateDirectory(state);

    public void Dispose()
    {
        if (Directory.Exists(state)) Directory.Delete(state, recursive: true);
    }

    private static readonly DateTimeOffset At = new(2026, 8, 25, 17, 2, 0, TimeSpan.Zero);

    [Fact]
    public void A_recorded_failure_survives_the_process_that_recorded_it()
    {
        // The whole point: the reporting process is gone by the time anyone can read this.
        UpdateInstaller.RecordFailure(state, "something still had the app's folder open", At);

        Assert.True(File.Exists(Path.Combine(state, UpdateInstaller.FailureFileName)));
        Assert.Equal("something still had the app's folder open", UpdateInstaller.TakeFailure(state));
    }

    [Fact]
    public void Reading_it_clears_it_so_it_is_news_exactly_once()
    {
        // A notice that reappears on every launch is a nag about something the user cannot fix by
        // being told again.
        UpdateInstaller.RecordFailure(state, "couldn't replace the old install", At);

        Assert.NotNull(UpdateInstaller.TakeFailure(state));
        Assert.Null(UpdateInstaller.TakeFailure(state));
        Assert.False(File.Exists(Path.Combine(state, UpdateInstaller.FailureFileName)));
    }

    [Fact]
    public void No_failure_is_silence()
    {
        Assert.Null(UpdateInstaller.TakeFailure(state));
    }

    [Fact]
    public void An_unwritable_state_directory_is_not_a_second_failure()
    {
        // This runs while something is already going wrong. Throwing here would replace a failed
        // update with a crash.
        var exception = Record.Exception(() =>
            UpdateInstaller.RecordFailure(
                Path.Combine("Z:", "does", "not", "exist"), "detail", At));

        Assert.Null(exception);
    }

    [Fact]
    public void The_swap_is_patient_rather_than_giving_up_on_the_first_lock()
    {
        // A process exiting is not Windows finishing with its files - an image section, a shell
        // extension, or a scanner reading eight megabytes of fresh DLLs each hold the folder for a
        // moment. One attempt is what made the update fail.
        Assert.True(UpdateInstaller.SwapPatience >= TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void A_failed_update_outranks_an_available_one_on_the_strip()
    {
        // Telling someone 0.7.5 is available, immediately after it refused to install, reads as
        // the app not knowing what just happened.
        var shell = ShellViewModelHarness.Build(
            waveLinkRunning: true,
            waveLinkFound: true,
            folderMissing: false,
            autoBackupEnabled: true,
            freeBytes: 100_000_000,
            storePath: @"C:\store",
            savedAt: At);

        shell.Apply(shell.Facts with
        {
            UpdateAvailableVersion = "0.7.5",
            UpdateFailureNotice = "something still had the app's folder open",
        });

        Assert.EndsWith("· UPDATE DIDN'T INSTALL", shell.StatusStrip, StringComparison.Ordinal);
        Assert.DoesNotContain("AVAILABLE", shell.StatusStrip, StringComparison.Ordinal);
    }

    [Fact]
    public void The_failure_notification_carries_the_reason_and_says_the_backups_are_safe()
    {
        var notice = TrayNotifications.UpdateFailed("something still had the app's folder open")!;

        Assert.Equal(TrayNotificationKind.UpdateFailed, notice.Kind);
        Assert.Contains("folder open", notice.Body, StringComparison.Ordinal);
        Assert.NotEmpty(notice.ActionLabel);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Nothing_to_report_is_no_notification(string? nothing)
    {
        Assert.Null(TrayNotifications.UpdateFailed(nothing));
    }
}
