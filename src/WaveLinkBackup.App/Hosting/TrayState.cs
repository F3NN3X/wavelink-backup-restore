using System.Globalization;
using WaveLinkBackup.Core.Results;

namespace WaveLinkBackup.App.Hosting;

/// <summary>The four icon states from screens/12-tray-autostart-update.md.</summary>
public enum TrayStatus
{
    /// <summary>shield + check, --wl-text.</summary>
    Watching,

    /// <summary>shield + down arrow, --wl-text.</summary>
    BackingUp,

    /// <summary>shield + exclamation, --wl-warn. The only colour the icon ever takes.</summary>
    NeedsYou,

    /// <summary>shield + slash, --wl-muted at 55%.</summary>
    Paused,
}

/// <param name="LastError">
/// From TickResult. Non-null means the watcher tried and failed — which is what makes NEEDS YOU
/// reachable at all (technical-debt.md 7.3).
/// </param>
public readonly record struct TrayConditions(
    bool AutoBackupEnabled,
    bool IsPaused,
    bool IsCapturing,
    CoreError? LastError);

/// <summary>
/// The tray's entire behaviour, as a pure function. Deliberately not a stored field that
/// something has to remember to update: a derived state cannot go stale.
/// </summary>
public static class TrayState
{
    public static TrayStatus From(TrayConditions conditions)
    {
        // Amber outranks everything. Something the user must act on must not be hidden by a
        // quieter state that also happens to be true.
        if (conditions.LastError is not null) return TrayStatus.NeedsYou;

        // Then whatever is actually happening right now.
        if (conditions.IsCapturing) return TrayStatus.BackingUp;

        // Paused and switched-off both leave nothing watching, and share one icon.
        if (conditions.IsPaused || !conditions.AutoBackupEnabled) return TrayStatus.Paused;

        return TrayStatus.Watching;
    }

    public static string Tooltip(
        TrayConditions conditions,
        DateTimeOffset? lastBackupAt,
        IFormatProvider? culture = null)
    {
        const string Name = "Wave Link Backup";

        if (conditions.LastError is not null) return $"{Name} — {Explain(conditions.LastError)}";

        var when = lastBackupAt is null
            ? "No backup yet"
            : $"last backup {lastBackupAt.Value.ToLocalTime().ToString("HH:mm", culture ?? CultureInfo.CurrentCulture)}";

        return conditions.IsPaused || !conditions.AutoBackupEnabled
            ? $"{Name} — paused · {when}"
            : $"{Name} — {when}";
    }

    /// <summary>
    /// Core's message is written for a log and a CLI; the tray needs the design's shorter
    /// phrasing. Translating here rather than changing CoreError keeps Core's wording intact
    /// for the CLI, which is where the longer form belongs.
    /// </summary>
    private static string Explain(CoreError error) => error switch
    {
        StoreUnavailable => "the backup folder can't be used",
        WaveLinkNotInstalled => "Wave Link wasn't found",
        MultiplePackagesFound => "choose which Wave Link to watch",
        SettingsUnreadable or MalformedSettings => "Wave Link's settings can't be read",
        _ => error.Message,
    };
}
