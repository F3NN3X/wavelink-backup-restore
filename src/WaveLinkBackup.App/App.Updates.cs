using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using H.NotifyIcon;
using H.NotifyIcon.Core;
using WaveLinkBackup.App.Hosting;
using WaveLinkBackup.App.Services;
using WaveLinkBackup.App.Startup;
using WaveLinkBackup.App.Updates;
using WaveLinkBackup.App.Theming;
using WaveLinkBackup.App.ViewModels;
using WaveLinkBackup.App.Views;
using WaveLinkBackup.App.Windows;
using WaveLinkBackup.Core.Abstractions;
using WaveLinkBackup.Core.Analysis;
using WaveLinkBackup.Core.Automation;
using WaveLinkBackup.Core.Capture;
using WaveLinkBackup.Core.Discovery;
using WaveLinkBackup.Core.Io;
using WaveLinkBackup.Core.Process;
using WaveLinkBackup.Core.Results;
using WaveLinkBackup.Core.Snapshots;

namespace WaveLinkBackup.App;

/// <summary>Noticing a new release, and installing one when asked.</summary>
public partial class App
{

    /// <summary>
    /// The automatic update check - at startup and daily thereafter, off the UI thread.
    ///
    /// <para>
    /// Fire-and-forget on purpose: startup must not wait on a network call, and a failure here is
    /// not the user's problem. Every outcome that is not "there is a newer release" leaves
    /// <see cref="updateAvailableVersion"/> null, so a feed that is down or a GitHub that is
    /// rate-limiting is silence rather than a scary strip.
    /// </para>
    ///
    /// <para>
    /// The found version lives in memory only. This app is meant to sit in the tray for weeks, so
    /// in the ordinary case it is found once and shown until it is installed. Restarting inside
    /// the daily window does lose the notice until the next check is due - the alternative is a
    /// new persisted settings field for a one-day gap, and a check-on-every-launch would be a
    /// network call per launch. "Check now" in Settings is always there.
    /// </para>
    /// </summary>
    private async Task CheckForUpdateInBackground()
    {
        if (updateCheckInFlight) return;
        if (!settings.CheckForUpdates) return;

        var source = ReleaseSource;
        if (!source.IsConfigured) return;

        var now = DateTimeOffset.Now;
        if (settings.LastUpdateCheckUtc is { } last && now - last < UpdateViewModel.AutoCheckInterval)
        {
            return;
        }

        updateCheckInFlight = true;
        try
        {
            await RunUpdateCheck(source, now).ConfigureAwait(true);
        }
        finally
        {
            updateCheckInFlight = false;
        }
    }

    private async Task RunUpdateCheck(UpdateSource source, DateTimeOffset now)
    {
        try
        {
            var check = await new GitHubReleaseFeed(source, updateHttp)
                .CheckAsync(ReleaseVersion.Current, CancellationToken.None)
                .ConfigureAwait(true);

            RecordUpdateCheck(check);
        }
        catch (Exception ex) when (ex is System.Net.Http.HttpRequestException or TaskCanceledException or IOException)
        {
            // Offline, blocked, or slow. Nothing to say, and nothing to log at the user.
        }
        finally
        {
            // THE ATTEMPT is what backs off, not the success. BackupSettings says so about this
            // very field - "when the last check ran, successful or not... otherwise a machine that
            // is offline for a fortnight re-checks on every tick" - and recording it only on
            // success made that sentence false: the tick is every 15 seconds, so an offline or
            // rate-limited machine would have retried roughly 5,700 times a day. The failure that
            // most needs backing off is exactly the one that used to skip it.
            ApplySettings(settings with { LastUpdateCheckUtc = now });
        }
    }

    /// <summary>
    /// What an update check MEANS for the three surfaces, wherever the check came from - the timer,
    /// startup, the Settings dialog's own auto-check, or "Check now".
    ///
    /// <para>
    /// One funnel on purpose. Before this, a check run from the Settings dialog updated that
    /// dialog and nothing else, so a user could press "Check now", be told an update existed, close
    /// the dialog, and find the strip still silent. Three surfaces reading one field is only
    /// coherent if one place writes it.
    /// </para>
    /// </summary>
    private void RecordUpdateCheck(UpdateCheck check)
    {
        var found = check.Result == UpdateCheckResult.UpdateAvailable && check.Release is not null
            ? ReleaseVersion.Display(check.Release.Version)
            : null;

        // Clearing matters as much as setting: a release withdrawn, or an update installed, must
        // take the notice down rather than leave a version the user can no longer get.
        if (found == updateAvailableVersion) return;

        updateAvailableVersion = found;

        // The strip and the tray both read the field; these are what make them notice. The balloon
        // rides on RefreshTray, so it fires exactly once per version via TrayNotifications.
        RefreshShellFacts();
        RefreshTray();
    }

    /// <summary>
    /// Where releases are looked for. Read from the environment rather than compiled in, because
    /// it is a fact about a DEPLOYMENT and not about the program (technical-debt.md §5): this repo
    /// has no remote yet, and a hard-coded owner/repo would be a constant that is wrong the moment
    /// one exists. Absent means the UPDATES section hides itself.
    /// </summary>
    internal static UpdateSource ReleaseSource => new(
        Environment.GetEnvironmentVariable("WLBACKUP_UPDATE_OWNER") ?? string.Empty,
        Environment.GetEnvironmentVariable("WLBACKUP_UPDATE_REPO") ?? string.Empty);

    /// <summary>
    /// One client for the life of the process. A new HttpClient per check is the classic way to
    /// exhaust sockets, and this one is used at most weekly.
    /// </summary>
    private static readonly System.Net.Http.HttpClient updateHttp = new()
    {
        Timeout = TimeSpan.FromSeconds(30),
    };

    private UpdateViewModel BuildUpdateViewModel()
    {
        var source = ReleaseSource;
        var feed = new GitHubReleaseFeed(source, updateHttp);

        return new UpdateViewModel(
            // Wrapped rather than passed straight through, so a check the SETTINGS DIALOG runs -
            // its own auto-check, or "Check now" - lights the strip and the tray as well as the
            // dialog. Otherwise the user is told twice and believed once.
            check: async ct =>
            {
                var result = await feed.CheckAsync(ReleaseVersion.Current, ct).ConfigureAwait(true);
                RecordUpdateCheck(result);
                return result;
            },
            install: (release, progress, ct) => InstallUpdateAsync(release, progress, ct),
            persist: (checkForUpdates, checkedAt) => ApplySettings(
                settings with { CheckForUpdates = checkForUpdates, LastUpdateCheckUtc = checkedAt }),
            autoCheckEnabled: settings.CheckForUpdates,
            lastCheckedAt: settings.LastUpdateCheckUtc,
            isConfigured: source.IsConfigured);
    }

    /// <summary>
    /// Download, verify, stage, hand over. Returns null when the hand-over started, at which
    /// point this process is on its way out and has nothing left to report, or the mono line for
    /// the failed-update block.
    ///
    /// The shutdown is deliberate and complete. The staged copy cannot rename a directory this
    /// process holds a handle on, so the watcher, the tray and the store all have to go first, and
    /// a last backup is taken on the way out exactly as a Quit would.
    /// </summary>
    private async Task<string?> InstallUpdateAsync(
        UpdateRelease release, IProgress<double> progress, CancellationToken ct)
    {
        var staging = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "WaveLinkBackup-update");

        var download = await new UpdateDownloader(updateHttp)
            .DownloadAsync(release, staging, progress, ct)
            .ConfigureAwait(true);

        if (!download.Succeeded) return download.FailureDetail;

        var install = System.IO.Path.GetDirectoryName(Environment.ProcessPath);
        if (install is null) return "COULDN'T FIND THIS APP'S OWN FOLDER · NOTHING CHANGED";

        var started = new UpdateInstaller().Begin(download.Path!, install);
        if (!started.Started) return started.FailureDetail;

        // Everything that holds a file handle inside the install directory, and the last backup a
        // Quit would take. ShutdownEverything ends the process, so nothing after this runs.
        ShutdownEverything();
        return null;
    }
}
