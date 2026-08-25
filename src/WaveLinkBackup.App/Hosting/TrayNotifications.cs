namespace WaveLinkBackup.App.Hosting;

/// <summary>
/// Which notification this is. Two are the design's; the third is ours, and ADR-018 argues it.
/// </summary>
public enum TrayNotificationKind
{
    /// <summary>Nothing has been backed up for nine days.</summary>
    NothingBackedUp,

    /// <summary>Wave Link rejected a restored backup and reset the user's settings.</summary>
    WaveLinkReset,

    /// <summary>
    /// A newer release exists. Added past the design package - see <see cref="TrayNotifications"/>
    /// for why this is not the third notice the design forbids, and [[ADR-018]] for the decision.
    /// </summary>
    UpdateAvailable,

    /// <summary>An update was downloaded and verified, but could not replace the old install.</summary>
    UpdateFailed,
}

/// <param name="ActionLabel">
/// The designed action. Carried in the body rather than as a button: a classic balloon has no
/// buttons, and Windows renders one as a toast without them, see <c>TrayNotifier</c>.
/// </param>
public sealed record TrayNotification(
    TrayNotificationKind Kind, string Title, string Body, string ActionLabel);

/// <summary>
/// Whether to notify, as a pure function: the same shape as <see cref="TrayState"/> and for the
/// same reason: a decision this consequential should be assertable from a table rather than
/// inferred from whichever code path happened to reach the tray.
///
/// The design allows exactly two notifications, and forbids the obvious third. "A successful
/// backup NEVER notifies. A safety net that congratulates itself weekly gets muted, and then it is
/// not a safety net." Nothing here can produce a success notice, because nothing here takes a
/// success as an input. That guard is unchanged and still enforced by the type: no method takes a
/// completed backup.
///
/// There is now a third notification, and it is not the one that rule forbids. The rule is
/// about the app talking about ITSELF DOING ITS JOB - routine, repeating, and therefore muted.
/// <see cref="UpdateAvailable"/> is none of those: it is rare, it is about a version rather than a
/// run, and it fires once per version rather than once per check. Before it existed, the ONLY
/// place an update was mentioned was the Settings dialog's UPDATES section - and the weekly
/// auto-check ran when that dialog opened, so a user who never opened Settings was never told a
/// fix existed. [[ADR-018]] carries the argument and the alternatives.
///
/// Built as its own type rather than as methods on the App because it carries state that decides
/// whether a thing happens to the user: <see cref="NothingBackedUp"/> fires ONCE per episode, and
/// "once" is the whole difference between a warning and a nag (technical-debt.md §4.21 item 6).
/// </summary>
public sealed class TrayNotifications
{
    /// <summary>
    /// The design's figure. Nine days rather than a week because Wave Link's own AutoBackups
    /// "cover about three days". The notice is meant to arrive after that cover has run out, not
    /// while it still holds.
    /// </summary>
    public static readonly TimeSpan Silence = TimeSpan.FromDays(9);

    private bool nothingBackedUpFired;

    /// <summary>
    /// The version the user has already been told about. Per-VERSION rather than per-process or
    /// per-episode: an update stays available until it is installed, so "once per episode" would
    /// mean once ever, and "once per process" would nag every launch until they gave in. Telling
    /// someone once about 0.7.5, and again only when 0.7.6 appears, is the honest cadence.
    /// </summary>
    private string? notifiedVersion;

    /// <summary>
    /// The nine-day notice, or null.
    ///
    /// <paramref name="lastBackupAt"/> being null means there has never been one, which counts:
    /// a store that has never held a backup is exactly the case this exists to catch.
    /// </summary>
    public TrayNotification? NothingBackedUp(DateTimeOffset? lastBackupAt, DateTimeOffset now)
    {
        var silent = lastBackupAt is not { } last || now - last >= Silence;

        // The condition clearing re-arms it. Without this the notice fires once per PROCESS, and
        // a machine left running for months would be told once and never again.
        if (!silent)
        {
            nothingBackedUpFired = false;
            return null;
        }

        // "The nine-day notice fires once, not daily."
        if (nothingBackedUpFired) return null;

        nothingBackedUpFired = true;

        return new TrayNotification(
            TrayNotificationKind.NothingBackedUp,
            "Nothing has been backed up for 9 days.",
            "The backup folder can't be used. Wave Link's own copies cover about three days.",
            "Choose a folder…");
    }

    /// <summary>
    /// The update that did not go in, said once on the launch after it happened.
    ///
    /// Not rate-limited, for the same reason <see cref="WaveLinkReset"/> is not: it can only
    /// follow an install the user just asked for, so it cannot repeat on its own - and the file it
    /// comes from is deleted as it is read.
    /// </summary>
    public static TrayNotification? UpdateFailed(string? detail) =>
        string.IsNullOrWhiteSpace(detail)
            ? null
            : new TrayNotification(
                TrayNotificationKind.UpdateFailed,
                "The update didn't install.",
                detail,
                "Open Settings to try again…");

    /// <summary>
    /// A newer release exists, or null - no check has run, the check failed, this build is
    /// current, or the user has already been told about this exact version.
    /// </summary>
    /// <param name="version">
    /// Display-formatted, as the strip shows it. Null or empty re-arms: an update that stops being
    /// available (installed, or the release withdrawn) should be announced again if it returns.
    /// </param>
    public TrayNotification? UpdateAvailable(string? version)
    {
        if (string.IsNullOrEmpty(version))
        {
            notifiedVersion = null;
            return null;
        }

        if (notifiedVersion == version) return null;

        notifiedVersion = version;

        return new TrayNotification(
            TrayNotificationKind.UpdateAvailable,
            $"Update {version} is available.",
            "Your backups are unaffected - installing replaces the app, not the folder.",
            "Open Settings to install…");
    }

    /// <summary>
    /// The reject notice. Not rate-limited: it can only follow a restore the user just asked for,
    /// so it cannot repeat on its own, and suppressing it would hide the one event in this program
    /// that costs somebody their mixer.
    /// </summary>
    public static TrayNotification WaveLinkReset(string preRestoreName) =>
        new(TrayNotificationKind.WaveLinkReset,
            "Wave Link reset your settings.",
            $"It rejected the backup you restored. \"{preRestoreName}\" will put you back.",
            $"Restore \"{preRestoreName}\"");
}
