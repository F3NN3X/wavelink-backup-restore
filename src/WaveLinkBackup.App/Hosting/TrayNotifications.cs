namespace WaveLinkBackup.App.Hosting;

/// <summary>Which of the two designed notifications this is. There is no third.</summary>
public enum TrayNotificationKind
{
    /// <summary>Nothing has been backed up for nine days.</summary>
    NothingBackedUp,

    /// <summary>Wave Link rejected a restored backup and reset the user's settings.</summary>
    WaveLinkReset,
}

/// <param name="ActionLabel">
/// The designed action. Carried in the body rather than as a button: a classic balloon has no
/// buttons, and Windows renders one as a toast without them — see <c>TrayNotifier</c>.
/// </param>
public sealed record TrayNotification(
    TrayNotificationKind Kind, string Title, string Body, string ActionLabel);

/// <summary>
/// Whether to notify, as a pure function — the same shape as <see cref="TrayState"/> and for the
/// same reason: a decision this consequential should be assertable from a table rather than
/// inferred from whichever code path happened to reach the tray.
///
/// **The design allows exactly two notifications, and forbids the obvious third.** "A successful
/// backup NEVER notifies. A safety net that congratulates itself weekly gets muted, and then it is
/// not a safety net." Nothing here can produce a success notice, because nothing here takes a
/// success as an input.
///
/// Built as its own type rather than as methods on the App because it carries state that decides
/// whether a thing happens to the user: <see cref="NothingBackedUp"/> fires ONCE per episode, and
/// "once" is the whole difference between a warning and a nag (technical-debt.md §4.21 item 6).
/// </summary>
public sealed class TrayNotifications
{
    /// <summary>
    /// The design's figure. Nine days rather than a week because Wave Link's own AutoBackups
    /// "cover about three days" — the notice is meant to arrive after that cover has run out, not
    /// while it still holds.
    /// </summary>
    public static readonly TimeSpan Silence = TimeSpan.FromDays(9);

    private bool nothingBackedUpFired;

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
