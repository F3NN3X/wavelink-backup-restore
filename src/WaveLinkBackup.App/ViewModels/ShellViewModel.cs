using System.ComponentModel;
using System.Globalization;
using System.Text.RegularExpressions;

namespace WaveLinkBackup.App.ViewModels;

/// <summary>
/// How loud the status strip is. 06's weight rule: "Neutral if nothing happened. Amber only if
/// the configuration - live or restorable - is not whole."
/// </summary>
public enum StripTone
{
    /// <summary>Green dot. Everything is as it should be.</summary>
    Ok,

    /// <summary>Amber dot. Wave Link cannot be read, so the live configuration is not whole.</summary>
    Warn,

    /// <summary>Muted dot. A location is missing; nothing is broken and nothing is lost (08).</summary>
    Neutral,
}

/// <param name="WaveLinkFound">False means no settings file in any of the usual places.</param>
/// <param name="SettingsLastSavedLocal">Null when the file could not be read at all.</param>
public sealed record ShellFacts(
    bool WaveLinkFound,
    bool WaveLinkRunning,
    DateTimeOffset? SettingsLastSavedLocal,
    bool AutoBackupEnabled,
    bool FolderMissing,
    string StorePath,
    long? FreeBytes);

/// <summary>
/// The status strip, the bottom bar, and what the four action buttons may do.
///
/// Nothing here reaches for the store or the process directly: the window hands it a
/// <see cref="ShellFacts"/> on every refresh, which is what lets every one of these strings be
/// asserted from a table.
/// </summary>
public sealed class ShellViewModel : ObservableObject
{
    /// <summary>
    /// Recognises a Windows user profile's AppData\Local, generically - not by asking THIS
    /// machine for its own copy. <see cref="Environment.GetFolderPath"/> would only match a
    /// store path that happens to sit under the account running the process; every other
    /// account, including whichever one runs the test suite or CI, would silently fall through
    /// to the un-shortened branch. The same trap SnapshotListViewModelTests already found for a
    /// hardcoded weekday - see its "Groups_run_newest_first..." deviation note.
    /// </summary>
    private static readonly Regex LocalAppDataPrefix = new(
        @"^[A-Za-z]:\\Users\\[^\\]+\\AppData\\Local\\", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private ShellFacts facts = new(true, false, null, true, false, string.Empty, null);
    private bool isHighContrast;
    private SnapshotRowViewModel? watchedRow;

    public ShellViewModel(SnapshotListViewModel list)
    {
        List = list;
        list.PropertyChanged += OnListPropertyChanged;
    }

    public SnapshotListViewModel List { get; }

    /// <summary>
    /// The flag every structural high-contrast difference switches on: the 3px left edge, the
    /// verdict word in place of the meta line, disabled as GrayText at full opacity rather than
    /// 40%. Design section C keeps these as TEMPLATE switches rather than a fourth palette.
    /// </summary>
    public bool IsHighContrast
    {
        get => isHighContrast;
        set => Set(ref isHighContrast, value);
    }

    /// <summary>
    /// README: "WAVE LINK RUNNING · SETTINGS LAST SAVED 23:07 · AUTOMATIC BACKUP ON".
    ///
    /// A missing folder REPLACES the third segment rather than joining it: 10-decisions section
    /// 6 says the automatic backup does nothing at all while the folder is gone, so printing
    /// "AUTOMATIC BACKUP ON" beside it would be the exact silent lie that rule forbids.
    /// </summary>
    public string StatusStrip
    {
        get
        {
            // 06's status strip (1). Everything else on the strip is a fact about a
            // configuration we could not read, so there is nothing else to say.
            if (!facts.WaveLinkFound) return "WAVE LINK NOT FOUND ON THIS COMPUTER";

            var running = facts.WaveLinkRunning ? "WAVE LINK RUNNING" : "WAVE LINK NOT RUNNING";

            var saved = facts.SettingsLastSavedLocal is { } at
                ? $"SETTINGS LAST SAVED {Readable.TimeOfDay(at)}"
                : "SETTINGS NEVER SAVED";

            var last = facts.FolderMissing
                ? "BACKUP FOLDER UNAVAILABLE"
                : $"AUTOMATIC BACKUP {(facts.AutoBackupEnabled ? "ON" : "OFF")}";

            return $"{running} · {saved} · {last}";
        }
    }

    public StripTone StatusTone =>
        !facts.WaveLinkFound ? StripTone.Warn
        : facts.FolderMissing ? StripTone.Neutral
        : StripTone.Ok;

    /// <summary>
    /// README: "SELECTED · BEFORE 3.3 BETA · 11 AUG 21:36". Absent with no selection.
    ///
    /// InvariantCulture, not CurrentCulture: this is a design-fixed mono label, not text meant
    /// to read naturally in the user's language - the same rule Readable enforces throughout.
    /// </summary>
    public string? SelectedLine => List.Selected is not { } row
        ? null
        : $"SELECTED · {row.Name.ToUpper(CultureInfo.InvariantCulture)} · "
        + $"{row.TakenDate} {row.TakenTime}";

    /// <summary>
    /// README: "4 BACKUPS · 12.4 MB IN %LOCALAPPDATA%\WaveLinkBackup · 118 GB FREE", and 02's
    /// damaged variant, which leads with the refusal because that is what the user needs first.
    /// </summary>
    public string SummaryLine
    {
        get
        {
            var count = List.TotalCount;
            var backups = $"{count} BACKUP{(count == 1 ? "" : "S")}";
            var size = $"{Readable.Bytes(List.TotalBytes)} IN {ShortStorePath}";

            // Omitted, never zero: "0 GB free" is a claim about the disk that we did not make.
            var free = facts.FreeBytes is { } bytes ? $" · {Readable.Bytes(bytes)} FREE" : string.Empty;

            var summary = $"{backups} · {size}{free}";

            return List.Selected?.IsDamaged == true
                ? $"DAMAGED — RESTORE IS OFF FOR THIS ONE · {summary}"
                : summary;
        }
    }

    /// <summary>
    /// %LOCALAPPDATA% back where it came from, exactly as README prints it. A literal
    /// C:\Users\<name>\AppData\Local is longer, less recognisable, and puts the user's name on
    /// screen for no reason (technical-debt section 6).
    /// </summary>
    private string ShortStorePath
    {
        get
        {
            var match = LocalAppDataPrefix.Match(facts.StorePath);

            return match.Success
                ? "%LOCALAPPDATA%\\" + facts.StorePath[match.Length..]
                : facts.StorePath;
        }
    }

    public bool CanRename => !facts.FolderMissing && List.Selected?.CanRename == true;

    public bool CanDelete => !facts.FolderMissing && List.Selected?.CanDelete == true;

    public bool CanRestore => !facts.FolderMissing && List.Selected?.CanRestore == true;

    /// <summary>
    /// Always live EXCEPT when the folder is gone - 08 puts all four buttons at 40% there,
    /// "including Back up now", because there is nowhere to put a backup.
    /// </summary>
    public bool CanBackUpNow => !facts.FolderMissing;

    /// <summary>Called by the window on load, on F5, after a capture, and on every host tick.</summary>
    public void Apply(ShellFacts facts)
    {
        this.facts = facts;
        RaiseAll();
    }

    /// <summary>
    /// A selection change swaps which row's health can move Restore and Rename; a health change
    /// on the row already selected (the probe landing DAMAGED) must be just as immediate. Not in
    /// the printed brief's code block, but required by its own following paragraph - without it
    /// a row flipping to DAMAGED while selected would leave Restore lit until the next Apply.
    /// </summary>
    private void OnListPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(SnapshotListViewModel.Selected)) return;

        if (watchedRow is not null) watchedRow.PropertyChanged -= OnSelectedRowPropertyChanged;
        watchedRow = List.Selected;
        if (watchedRow is not null) watchedRow.PropertyChanged += OnSelectedRowPropertyChanged;

        RaiseAll();
    }

    private void OnSelectedRowPropertyChanged(object? sender, PropertyChangedEventArgs e) => RaiseAll();

    private void RaiseAll()
    {
        foreach (var property in (string[])
        [
            nameof(StatusStrip), nameof(StatusTone), nameof(SelectedLine), nameof(SummaryLine),
            nameof(CanRename), nameof(CanDelete), nameof(CanRestore), nameof(CanBackUpNow),
        ])
        {
            Raise(property);
        }
    }
}
