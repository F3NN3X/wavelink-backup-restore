using WaveLinkBackup.App.Updates;

namespace WaveLinkBackup.App.ViewModels;

/// <summary>
/// Settings' <c>UPDATES</c> section (screens/12), which did not exist in any form — error 8's
/// *"Get the update"* button deep-linked to a section that was never built
/// (technical-debt.md §4.21 item 5).
///
/// The design's hardest rule is a rule about restraint, and it is structural here. "An
/// available update is NEVER a notification, a badge or a banner", and "It never installs anything
/// without you." So: this model can be asked to check, and it can be asked to install, and it does
/// neither on its own. <see cref="ShouldAutoCheck"/> is the one thing that happens unprompted, and
/// all it does is look.
/// </summary>
public sealed class UpdateViewModel : ObservableObject
{
    /// <summary>
    /// Daily, where the design says weekly — "Check for updates on its own — weekly, on by
    /// default." [[ADR-018]] carries the change and the reason.
    ///
    /// The short version: weekly was the right number when the check only ever ran on the way into
    /// the Settings dialog, because that is a rare and deliberate visit and a stale answer there is
    /// cheap. Now that the check runs on its own and SAYS something when it finds one, the interval
    /// is how long a shipped fix can sit unmentioned in front of someone using the app - and a week
    /// is too long for that. It is still cheap: one conditional request against a release feed, on
    /// an app that is meant to run for weeks.
    /// </summary>
    public static readonly TimeSpan AutoCheckInterval = TimeSpan.FromHours(24);

    private readonly Func<CancellationToken, Task<UpdateCheck>> check;
    private readonly Func<UpdateRelease, IProgress<double>, CancellationToken, Task<string?>> install;
    private readonly Func<bool, DateTimeOffset?, bool> persist;

    private UpdateCheck state = UpdateCheck.Unknown;
    private bool autoCheck;
    private DateTimeOffset? lastCheckedAt;
    private bool isBusy;
    private double progress;
    private string? failure;

    /// <param name="install">
    /// Downloads, verifies and hands over. Returns null on success (the app is about to exit) or
    /// the mono failure line. Async and Func-shaped so the model never touches HTTP or a process.
    /// </param>
    /// <param name="persist">
    /// Writes the auto-check preference and the last-checked stamp. Returns whether it stuck.
    /// </param>
    public UpdateViewModel(
        Func<CancellationToken, Task<UpdateCheck>> check,
        Func<UpdateRelease, IProgress<double>, CancellationToken, Task<string?>> install,
        Func<bool, DateTimeOffset?, bool> persist,
        bool autoCheckEnabled,
        DateTimeOffset? lastCheckedAt,
        bool isConfigured = true)
    {
        this.check = check;
        this.install = install;
        this.persist = persist;

        autoCheck = autoCheckEnabled;
        this.lastCheckedAt = lastCheckedAt;
        IsConfigured = isConfigured;
    }

    /// <summary>
    /// Whether there is a release feed to reach at all. False hides the whole section: a
    /// "Check now" that cannot reach anything is worse than no button (technical-debt.md §5 —
    /// the feed is a fact about a deployment, not about the program).
    /// </summary>
    public bool IsConfigured { get; }

    /// <summary>The running build, printed as the design writes it: <c>1.2.3</c>.</summary>
    public string CurrentVersion => ReleaseVersion.Display(ReleaseVersion.Current);

    public UpdateCheckResult Result => state.Result;

    /// <summary>Row 1's headline: "Up to date", or "1.4.0 is available".</summary>
    public string Headline => state.Result switch
    {
        UpdateCheckResult.UpToDate => "Up to date",
        UpdateCheckResult.UpdateAvailable when state.Release is { } release =>
            $"{ReleaseVersion.Display(release.Version)} is available",
        UpdateCheckResult.CheckFailed => "Couldn't check for updates",
        _ => "Updates",
    };

    /// <summary>
    /// The mono line under it. Up to date prints <c>1.2.3 · CHECKED TODAY 09:14</c>; an available
    /// update prints <c>YOU HAVE 1.2.3 · RELEASED 12 AUG · 4.1 MB</c>. Every figure is read, never
    /// fixed text.
    /// </summary>
    public string Meta => state.Result switch
    {
        UpdateCheckResult.UpdateAvailable when state.Release is { } release => string.Join(
            " · ",
            new[]
            {
                $"YOU HAVE {CurrentVersion}",
                release.PublishedAt is { } at ? $"RELEASED {at.ToLocalTime():d MMM}".ToUpperInvariant() : null,
                release.SizeBytes > 0 ? Readable.Bytes(release.SizeBytes).ToUpperInvariant() : null,
            }.Where(part => part is not null)),

        UpdateCheckResult.CheckFailed => state.FailureDetail ?? string.Empty,

        _ => lastCheckedAt is { } checkedAt
            ? $"{CurrentVersion} · CHECKED {Readable.WhenChecked(checkedAt)}"
            : $"{CurrentVersion} · NEVER CHECKED",
    };

    /// <summary>Whether the available-update row's two actions are showing.</summary>
    public bool HasUpdate => state.Result == UpdateCheckResult.UpdateAvailable && state.Release is not null;

    /// <summary>"What changed" — the release's own page. Null hides the button.</summary>
    public string? NotesUrl => state.Release?.NotesUrl;

    /// <summary>A check or an install is running. Both buttons hold while one is.</summary>
    public bool IsBusy
    {
        get => isBusy;
        private set
        {
            if (Set(ref isBusy, value)) Raise(nameof(CanAct));
        }
    }

    /// <summary>Whether either button may be pressed.</summary>
    public bool CanAct => IsConfigured && !isBusy;

    /// <summary>0 to 1 while an install downloads.</summary>
    public double Progress
    {
        get => progress;
        private set => Set(ref progress, value);
    }

    /// <summary>
    /// The failed-update block's mono line, or null. NEUTRAL, not amber — screens/12: "a failed
    /// update leaves a working app, so nothing is un-whole."
    /// </summary>
    public string? Failure
    {
        get => failure;
        private set
        {
            if (Set(ref failure, value)) Raise(nameof(HasFailed));
        }
    }

    public bool HasFailed => failure is not null;

    /// <summary>The failed block's fixed copy, verbatim from the design.</summary>
    public string FailureLead => "The update didn't install. Nothing changed.";

    public string FailureBody =>
        $"Your backups and settings are untouched — {CurrentVersion} is still running and still watching.";

    /// <summary>"Check for updates on its own", weekly. Commits on change.</summary>
    public bool AutoCheck
    {
        get => autoCheck;
        set
        {
            if (Set(ref autoCheck, value)) persist(value, lastCheckedAt);
        }
    }

    /// <summary>
    /// Whether the daily check is due.
    ///
    /// This used to say "it only looks - an available update is never a notification, a badge or
    /// a banner", and that is no longer true. [[ADR-018]] added exactly that: the strip, a tray
    /// menu line, and one notification per version. What the sentence was protecting still holds -
    /// the check never INSTALLS anything, and nothing here acts without a press.
    /// </summary>
    public bool ShouldAutoCheck(DateTimeOffset now) =>
        IsConfigured && autoCheck && (lastCheckedAt is not { } last || now - last >= AutoCheckInterval);

    /// <summary>"Check now", and the weekly check. Never installs anything.</summary>
    public async Task CheckAsync(DateTimeOffset now, CancellationToken ct = default)
    {
        if (!CanAct) return;

        IsBusy = true;
        Failure = null;

        try
        {
            state = await check(ct).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            state = UpdateCheck.Unknown;
            return;
        }
        finally
        {
            IsBusy = false;
        }

        // The stamp records that we LOOKED, including when the look failed: otherwise a machine
        // that is offline for a fortnight re-checks on every tick.
        lastCheckedAt = now;
        persist(autoCheck, now);

        RaiseAll();
    }

    /// <summary>
    /// "Install and restart". Only ever from a press — see the class comment.
    ///
    /// On success the app is about to be replaced and restarted, so nothing here reports one:
    /// the evidence of success is the new version running.
    /// </summary>
    public async Task InstallAsync(CancellationToken ct = default)
    {
        if (!CanAct || state.Release is not { } release) return;

        IsBusy = true;
        Failure = null;
        Progress = 0;

        try
        {
            Failure = await install(release, new Progress<double>(p => Progress = p), ct)
                .ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            Failure = "THE UPDATE WAS CANCELLED · NOTHING CHANGED";
        }
        finally
        {
            IsBusy = false;
            Progress = 0;
        }
    }

    /// <summary>The failed block's "Try again" — clears the block and re-runs the install.</summary>
    public Task RetryAsync(CancellationToken ct = default)
    {
        Failure = null;
        return InstallAsync(ct);
    }

    private void RaiseAll()
    {
        Raise(nameof(Result));
        Raise(nameof(Headline));
        Raise(nameof(Meta));
        Raise(nameof(HasUpdate));
        Raise(nameof(NotesUrl));
    }
}
