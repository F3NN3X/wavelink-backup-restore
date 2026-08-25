using WaveLinkBackup.Core.Analysis;
using WaveLinkBackup.Core.Restore;
using WaveLinkBackup.App.Services;

namespace WaveLinkBackup.App.ViewModels;

/// <summary>
/// The four things the restore strip can be showing, and nothing else.
///
/// The restore-outcomes spec is authoritative: one inline strip below the status strip, above the
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

    /// <summary>
    /// One of the errors spec's inline-strip errors (3, 5, 6, 7, 10, 11) - the consequence of
    /// something the user just pressed. All neutral fill: no left edge, no amber status. The
    /// strip carries the error number and the designed sentence; a machine-specific mono meta
    /// line (path, checksum, PID) rides along when the trigger has one.
    /// </summary>
    InlineError,
}

/// <summary>
/// The App-layer view of a finished restore, for the inline strip on screen 1.
///
/// A thin read-only projection over Core's <see cref="RestoreOutcome"/> plus the failure case
/// that outcome never carries (a failed restore returns a Result&lt;T&gt;.Fail, not an outcome).
/// The dismiss rules live here, in one place, so the XAML binds to booleans and the tests can
/// assert the exact the restore-outcomes spec behaviour without standing up a window.
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
    private int _errorNumber;
    private string _monoMeta = string.Empty;
    private bool _hasPrimaryAction;
    private string _primaryActionLabel = string.Empty;
    private string? _recoverySnapshotId;

    /// <summary>
    /// The one thing the strip does when its action button is pressed. Set by the shell; null
    /// when the strip has no action (the "Check again" affordance is wired separately).
    /// </summary>
    public Action? OnAction { get; set; }

    /// <summary>
    /// The accent button's action. Only the rejected strip has one: the restore-outcomes spec, §3
    /// gives it a ghost "Show the log" AND a primary <c>Restore "Before restore"</c>, and the
    /// primary is the recovery path for the only failure that costs someone their mixer.
    /// </summary>
    public Action? OnPrimaryAction { get; set; }

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
    /// Whether the close (X) button may be shown. Rejected is false: the restore-outcomes spec says
    /// it is not dismissible until acted on, and acting means reading why, not hiding it.
    /// </summary>
    public bool Dismissible => _dismissible;

    /// <summary>Whether the strip carries a secondary action button ("Check again").</summary>
    public bool HasAction => _hasAction;

    public string ActionLabel { get => _actionLabel; private set => Set(ref _actionLabel, value); }

    /// <summary>
    /// The 22px mono error number on the left of an inline-strip error (the errors spec anatomy).
    /// Zero when the strip is not showing an inline error.
    /// </summary>
    public int ErrorNumber { get => _errorNumber; private set => Set(ref _errorNumber, value); }

    /// <summary>
    /// The mono meta line under the sentence (a path, a checksum, a PID). Empty when the trigger
    /// carried none - 06 prints one per inline error, but it is machine-specific and arrives at
    /// render time rather than from the catalog.
    /// </summary>
    public string MonoMeta { get => _monoMeta; private set => Set(ref _monoMeta, value); }

    /// <summary>True only while showing one of 06's inline-strip errors.</summary>
    public bool IsInlineError => _kind == RestoreStripKind.InlineError;

    /// <summary>Whether the strip carries an accent primary button beside the ghost one.</summary>
    public bool HasPrimaryAction => _hasPrimaryAction;

    public string PrimaryActionLabel
    {
        get => _primaryActionLabel;
        private set => Set(ref _primaryActionLabel, value);
    }

    /// <summary>
    /// The snapshot the rejected strip's primary button restores: the "Before restore" copy taken
    /// moments earlier. Null on every other kind, and on a rejection with no such copy.
    ///
    /// 03 renders that row selected immediately below the strip, "so the button and the row
    /// are visibly the same object". The window reads this to make the selection.
    /// </summary>
    public string? RecoverySnapshotId
    {
        get => _recoverySnapshotId;
        private set => Set(ref _recoverySnapshotId, value);
    }

    /// <summary>
    /// Show the strip for a restore that produced an outcome. Maps Core's verdict to one of the
    /// four designed states - this is the ONLY place that mapping lives.
    /// </summary>
    public void Show(RestoreOutcome outcome)
    {
        RecoverySnapshotId = null;
        _hasPrimaryAction = false;
        PrimaryActionLabel = string.Empty;

        // A null verdict means the log could not be read: the restore cannot be CONFIRMED, which
        // The restore-outcomes spec treats as unconfirmed (neutral), never as a reject.
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
        Raise(nameof(HasPrimaryAction));
        Raise(nameof(MonoMeta));
            return;
        }

        if (verdict is { ParseFailed: true })
        {
            // Same recovery as ShowResult's Rejected arm, built from the outcome's own
            // pre-restore snapshot rather than an id passed in beside it.
            Kind = RestoreStripKind.Rejected;
            Title = "Wave Link rejected this backup and reset your settings.";
            Detail =
                $"Restore \"{RestoreOrchestrator.PreRestoreName}\" to get back to where you were. "
                + "That copy was taken moments ago, before any of this.";
            _monoMeta = VersionDetail(verdict);
            _autoDismisses = false;
            _dismissible = false;
            _hasAction = true;
            ActionLabel = "Show the log";
            RecoverySnapshotId = outcome.PreRestoreSnapshot.Id;
            _hasPrimaryAction = true;
            PrimaryActionLabel = $"Restore \"{RestoreOrchestrator.PreRestoreName}\"";
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
        Raise(nameof(HasPrimaryAction));
        Raise(nameof(MonoMeta));
    }

    /// <summary>
    /// Show the strip from the shell-facing <see cref="RestoreResult"/> the service returns, rather
    /// than from a Core <see cref="RestoreOutcome"/>. The window only ever holds the former - it does
    /// not re-open the verdict - so this is the entry point Task 6's restore flow uses. Confirmed /
    /// Unconfirmed / Rejected map to their designed states with fixed copy; Failed delegates to
    /// <see cref="ShowFailure"/> with the message the service carried.
    /// </summary>
    /// <param name="recoverySnapshotId">
    /// The "Before restore" snapshot this restore took on its way in. Only the Rejected arm uses
    /// it, and only to build the recovery the design gives that state. Null means there is none:
    /// see the Rejected arm for what the strip does then.
    /// </param>
    /// <param name="monoMeta">
    /// 03 §3's machine line: <c>WAVE LINK 3.3.0.4108 REWROTE settings.json AT 23:12 · 1 INPUT
    /// NOW</c>. Composed by the caller because every figure in it is machine-specific.
    /// </param>
    public void ShowResult(
        RestoreResult result, string? recoverySnapshotId = null, string? monoMeta = null)
    {
        RecoverySnapshotId = null;
        _hasPrimaryAction = false;
        PrimaryActionLabel = string.Empty;
        _monoMeta = string.Empty;

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
                // The restore-outcomes spec, §3. The headline states what happened; the body names the
                // way back, and the primary button IS that way back. Before this the state stated
                // a problem, offered nothing, and could not be closed for the life of the process
                // (technical-debt.md §4.21 item 1).
                Kind = RestoreStripKind.Rejected;
                Title = "Wave Link rejected this backup and reset your settings.";
                _monoMeta = monoMeta ?? string.Empty;
                _autoDismisses = false;

                // "Show the log" is the ghost action in both shapes: it is the evidence for WHY,
                // and 03's reason for making this strip persistent at all.
                _hasAction = true;
                ActionLabel = "Show the log";

                if (recoverySnapshotId is { Length: > 0 })
                {
                    RecoverySnapshotId = recoverySnapshotId;
                    Detail =
                        $"Restore \"{RestoreOrchestrator.PreRestoreName}\" to get back to where you were. "
                        + "That copy was taken moments ago, before any of this.";
                    _hasPrimaryAction = true;
                    PrimaryActionLabel = $"Restore \"{RestoreOrchestrator.PreRestoreName}\"";
                    _dismissible = false;
                }
                else
                {
                    // No pre-restore copy means there is nothing to act ON, and 03's "not
                    // dismissible until acted on" would leave a permanent bar offering a recovery
                    // that does not exist. Reading the log is then the only act available, so the
                    // strip says so and lets the user clear it.
                    Detail =
                        "Wave Link rewrote its settings and there is no \"Before restore\" copy to "
                        + "return to. The log is the first place to look.";
                    _dismissible = true;
                }
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
        Raise(nameof(HasPrimaryAction));
        Raise(nameof(MonoMeta));
    }

    /// <summary>Show the strip for a restore that FAILED (Result.Fail, no outcome).</summary>
    public void ShowFailure(string message)
    {
        RecoverySnapshotId = null;
        _hasPrimaryAction = false;
        PrimaryActionLabel = string.Empty;
        _monoMeta = string.Empty;

        Kind = RestoreStripKind.Failed;
        Title = "Restore failed";
        Detail = message;
        _autoDismisses = false;
        _dismissible = true;
        _hasAction = false;

        Raise(nameof(AutoDismisses));
        Raise(nameof(Dismissible));
        Raise(nameof(HasAction));
        Raise(nameof(HasPrimaryAction));
        Raise(nameof(MonoMeta));
    }

    /// <summary>
    /// Show the strip for one of the errors spec's inline-strip errors (3, 5, 6, 7, 10, 11) - the
    /// consequence of something the user just pressed. All neutral fill: no left edge, no amber
    /// status. The sentence comes from the catalog (the designed copy); <paramref name="monoMeta"/>
    /// is the machine-specific mono line (path, checksum, PID) that 06 prints under the sentence,
    /// supplied at render time because it is not in the catalog; <paramref name="actionLabel"/> is
    /// the designed action (or null for error 11, which has none). Dismissible - these are
    /// refusals, and the user may clear them once read.
    /// </summary>
    public void ShowError(AppError error, string? monoMeta = null, string? actionLabel = null)
    {
        if (error.Placement != ErrorPlacement.InlineStrip)
            throw new ArgumentException(
                $"Error {error.Code} is not an inline-strip error; use its own placement.", nameof(error));

        RecoverySnapshotId = null;
        _hasPrimaryAction = false;
        PrimaryActionLabel = string.Empty;

        Kind = RestoreStripKind.InlineError;
        Title = error.Title;
        Detail = error.Body;
        _errorNumber = error.Code;
        _monoMeta = monoMeta ?? string.Empty;
        _autoDismisses = false;
        _dismissible = true;
        _hasAction = actionLabel is not null;
        ActionLabel = actionLabel ?? string.Empty;

        Raise(nameof(AutoDismisses));
        Raise(nameof(Dismissible));
        Raise(nameof(HasAction));
        Raise(nameof(HasPrimaryAction));
        Raise(nameof(MonoMeta));
        Raise(nameof(ErrorNumber));
        Raise(nameof(MonoMeta));
        Raise(nameof(IsInlineError));
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
        _errorNumber = 0;
        _monoMeta = string.Empty;
        _hasPrimaryAction = false;
        PrimaryActionLabel = string.Empty;
        RecoverySnapshotId = null;

        Raise(nameof(AutoDismisses));
        Raise(nameof(Dismissible));
        Raise(nameof(HasAction));
        Raise(nameof(HasPrimaryAction));
        Raise(nameof(MonoMeta));
        Raise(nameof(ErrorNumber));
        Raise(nameof(MonoMeta));
        Raise(nameof(IsInlineError));
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
        ActionLabel = string.Empty;
        _errorNumber = 0;
        _monoMeta = string.Empty;
        _hasPrimaryAction = false;
        PrimaryActionLabel = string.Empty;
        RecoverySnapshotId = null;

        Raise(nameof(AutoDismisses));
        Raise(nameof(Dismissible));
        Raise(nameof(HasAction));
        Raise(nameof(HasPrimaryAction));
        Raise(nameof(MonoMeta));
        Raise(nameof(ErrorNumber));
        Raise(nameof(MonoMeta));
        Raise(nameof(IsInlineError));
    }

    private static string VersionDetail(RestoreVerdict verdict)
    {
        var version = verdict.Version is null ? "unknown version" : $"Wave Link {verdict.Version}";
        var plural = verdict.AppliedNames.Count == 1 ? "" : "s";
        return $"Applied {verdict.AppliedNames.Count} channel name{plural} · {version}.";
    }
}
