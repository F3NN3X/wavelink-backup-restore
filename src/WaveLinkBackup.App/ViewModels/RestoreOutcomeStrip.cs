using WaveLinkBackup.Core.Analysis;
using WaveLinkBackup.Core.Restore;
using WaveLinkBackup.App.Services;

namespace WaveLinkBackup.App.ViewModels;

/// <summary>
/// The four things the restore strip can be showing, and nothing else.
///
/// 03-restore-outcomes.md is authoritative: one inline strip below the status strip, above the
/// column header, full width. Every outcome maps to a distinct visual state and a distinct
/// dismiss rule, and those rules are the whole point of the strip - a restore that cannot be
/// confirmed must not look like one that can.
/// </summary>
public enum RestoreStripKind
{
    /// <summary>Hidden. No restore has happened, or the last one was dismissed.</summary>
    None,

    /// <summary>Succeeded AND confirmed from the log. Quiet: a ringed check in WlOk. Auto-dismisses.</summary>
    SucceededConfirmed,

    /// <summary>
    /// The write went through but the log could not confirm it (unreadable, or no applied names).
    /// Neutral hollow circle - NOT amber. Amber is reserved for a REJECT; an unconfirmed success
    /// that reads as a warning would make the user re-run a restore that probably worked.
    /// </summary>
    SucceededUnconfirmed,

    /// <summary>
    /// Wave Link rejected the settings file (parse failure). Amber WlWarnSoft fill, 3px left edge,
    /// and the status strip above turns amber too. NOT dismissible until acted on - dismissing it
    /// would hide the one piece of evidence that tells you WHY.
    /// </summary>
    Rejected,

    /// <summary>
    /// The restore itself failed before or during the write (close timeout, write refused, ...).
    /// WlDanger. Dismissible - the failure message already said what went wrong.
    /// </summary>
    Failed,
}

/// <summary>
/// The App-layer view of a finished restore, for the inline strip on screen 1.
///
/// A thin read-only projection over Core's <see cref="RestoreOutcome"/> plus the failure case
/// that outcome never carries (a failed restore returns a Result&lt;T&gt;.Fail, not an outcome).
/// The dismiss rules live here, in one place, so the XAML binds to booleans and the tests can
/// assert the exact 03-restore-outcomes.md behaviour without standing up a window.
/// </summary>
public sealed class RestoreOutcomeStrip : ObservableObject
{
    /// <summary>The auto-dismiss interval for the quiet "succeeded + confirmed" strip.</summary>
    public static readonly TimeSpan AutoDismissAfter = TimeSpan.FromSeconds(6);

    private RestoreStripKind _kind = RestoreStripKind.None;
    private string _title = string.Empty;
    private string _detail = string.Empty;
    private bool _autoDismisses;
    private bool _dismissible;
    private bool _hasAction;
    private string _actionLabel = string.Empty;

    /// <summary>
    /// The one thing the strip does when its action button is pressed. Set by the shell; null
    /// when the strip has no action (the "Check again" affordance is wired separately).
    /// </summary>
    public Action? OnAction { get; set; }

    public RestoreStripKind Kind
    {
        get => _kind;
        private set
        {
            if (Set(ref _kind, value))
            {
                Raise(nameof(IsVisible));
                Raise(nameof(HasLeftEdge));
                Raise(nameof(TurnsStatusAmber));
            }
        }
    }

    public bool IsVisible => _kind != RestoreStripKind.None;

    /// <summary>Rejected only: the 3px amber left edge that makes it read as a warning.</summary>
    public bool HasLeftEdge => _kind == RestoreStripKind.Rejected;

    /// <summary>Rejected only: the status strip above turns amber with it.</summary>
    public bool TurnsStatusAmber => _kind == RestoreStripKind.Rejected;

    public string Title { get => _title; private set => Set(ref _title, value); }
    public string Detail { get => _detail; private set => Set(ref _detail, value); }

    /// <summary>True only for SucceededConfirmed - the one outcome that clears itself.</summary>
    public bool AutoDismisses => _autoDismisses;

    /// <summary>
    /// Whether the close (X) button may be shown. Rejected is false: 03-restore-outcomes.md says
    /// it is not dismissible until acted on, and acting means reading why, not hiding it.
    /// </summary>
    public bool Dismissible => _dismissible;

    /// <summary>Whether the strip carries a secondary action button ("Check again").</summary>
    public bool HasAction => _hasAction;

    public string ActionLabel { get => _actionLabel; private set => Set(ref _actionLabel, value); }

    /// <summary>
    /// Show the strip for a restore that produced an outcome. Maps Core's verdict to one of the
    /// four designed states - this is the ONLY place that mapping lives.
    /// </summary>
    public void Show(RestoreOutcome outcome)
    {
        // A null verdict means the log could not be read: the restore cannot be CONFIRMED, which
        // 03-restore-outcomes.md treats as unconfirmed (neutral), never as a reject.
        if (outcome.Verdict is not { } verdict)
        {
            Kind = RestoreStripKind.SucceededUnconfirmed;
            Title = "Restore completed - not confirmed";
            Detail = "Wave Link's log could not be read, so the restore cannot be confirmed. If your mixer looks right, it probably worked.";
            _autoDismisses = false;
            _dismissible = true;
            _hasAction = true;
            ActionLabel = "Check again";

            Raise(nameof(AutoDismisses));
            Raise(nameof(Dismissible));
            Raise(nameof(HasAction));
            return;
        }

        if (verdict is { ParseFailed: true })
        {
            Kind = RestoreStripKind.Rejected;
            Title = "Wave Link rejected the settings file";
            Detail = VersionDetail(verdict);
            _autoDismisses = false;
            _dismissible = false;
            _hasAction = false;
        }
        else if (verdict is { Succeeded: true })
        {
            Kind = RestoreStripKind.SucceededConfirmed;
            Title = "Restore confirmed";
            Detail = VersionDetail(verdict);
            _autoDismisses = true;
            _dismissible = true;
            _hasAction = false;
        }
        else
        {
            // No parse failure and no applied names: the write went through but the log could not
            // confirm it. This is NOT a reject - do not use amber.
            Kind = RestoreStripKind.SucceededUnconfirmed;
            Title = "Restore completed - not confirmed";
            Detail = "Wave Link's log did not record the restore. If your mixer looks right, it probably worked.";
            _autoDismisses = false;
            _dismissible = true;
            _hasAction = true;
            ActionLabel = "Check again";
        }

        Raise(nameof(AutoDismisses));
        Raise(nameof(Dismissible));
        Raise(nameof(HasAction));
    }

    /// <summary>
    /// Show the strip from the shell-facing <see cref="RestoreResult"/> the service returns, rather
    /// than from a Core <see cref="RestoreOutcome"/>. The window only ever holds the former - it does
    /// not re-open the verdict - so this is the entry point Task 6's restore flow uses. Confirmed /
    /// Unconfirmed / Rejected map to their designed states with fixed copy; Failed delegates to
    /// <see cref="ShowFailure"/> with the message the service carried.
    /// </summary>
    public void ShowResult(RestoreResult result)
    {
        switch (result)
        {
            case RestoreResult.Confirmed:
                Kind = RestoreStripKind.SucceededConfirmed;
                Title = "Restore confirmed";
                Detail = "Wave Link's log recorded the restore.";
                _autoDismisses = true;
                _dismissible = true;
                _hasAction = false;
                break;

            case RestoreResult.Unconfirmed:
                Kind = RestoreStripKind.SucceededUnconfirmed;
                Title = "Restore completed - not confirmed";
                Detail = "Wave Link's log did not record the restore. If your mixer looks right, it probably worked.";
                _autoDismisses = false;
                _dismissible = true;
                _hasAction = true;
                ActionLabel = "Check again";
                break;

            case RestoreResult.Rejected:
                Kind = RestoreStripKind.Rejected;
                Title = "Wave Link rejected the settings file";
                Detail = "The file Wave Link wrote back could not be parsed, so it regenerated its defaults. The version difference is the first thing to check.";
                _autoDismisses = false;
                _dismissible = false;
                _hasAction = false;
                break;

            case RestoreResult.Failed:
                ShowFailure("The restore failed.");
                return;

            default:
                Kind = RestoreStripKind.None;
                return;
        }

        Raise(nameof(AutoDismisses));
        Raise(nameof(Dismissible));
        Raise(nameof(HasAction));
    }

    /// <summary>Show the strip for a restore that FAILED (Result.Fail, no outcome).</summary>
    public void ShowFailure(string message)
    {
        Kind = RestoreStripKind.Failed;
        Title = "Restore failed";
        Detail = message;
        _autoDismisses = false;
        _dismissible = true;
        _hasAction = false;

        Raise(nameof(AutoDismisses));
        Raise(nameof(Dismissible));
        Raise(nameof(HasAction));
    }

    /// <summary>Hide the strip. Rejected ignores this until acted on - see Dismissible.</summary>
    public void Dismiss()
    {
        if (!Dismissible) return;

        Kind = RestoreStripKind.None;
        Title = string.Empty;
        Detail = string.Empty;
        _autoDismisses = false;
        _dismissible = false;
        _hasAction = false;
        ActionLabel = string.Empty;

        Raise(nameof(AutoDismisses));
        Raise(nameof(Dismissible));
        Raise(nameof(HasAction));
    }

    /// <summary>
    /// The "acted on" path for a rejected strip: the user has read why and wants to clear it.
    /// This is the ONLY way a Rejected strip goes away - Dismiss() refuses while Kind is Rejected.
    /// </summary>
    public void AcknowledgeReject()
    {
        if (Kind != RestoreStripKind.Rejected) return;

        Kind = RestoreStripKind.None;
        Title = string.Empty;
        Detail = string.Empty;
        _autoDismisses = false;
        _dismissible = false;
        _hasAction = false;

        Raise(nameof(AutoDismisses));
        Raise(nameof(Dismissible));
        Raise(nameof(HasAction));
    }

    private static string VersionDetail(RestoreVerdict verdict)
    {
        var version = verdict.Version is null ? "unknown version" : $"Wave Link {verdict.Version}";
        var plural = verdict.AppliedNames.Count == 1 ? "" : "s";
        return $"Applied {verdict.AppliedNames.Count} channel name{plural} · {version}.";
    }
}
