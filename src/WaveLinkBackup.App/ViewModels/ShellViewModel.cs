using System.ComponentModel;
using System.Globalization;

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
    private ShellFacts facts = new(true, false, null, true, false, string.Empty, null);
    private bool isHighContrast;
    private bool isRestoring;
    private RestoreProgressModel restoreProgress = new();
    private SnapshotRowViewModel? watchedRow;

    public ShellViewModel(SnapshotListViewModel list)
    {
        List = list;
        Strip = new RestoreOutcomeStrip();
        list.PropertyChanged += OnListPropertyChanged;
    }

    public SnapshotListViewModel List { get; }

    /// <summary>
    /// The inline restore-result strip (03-restore-outcomes.md), below the status strip and above
    /// the column header. Hidden until a restore finishes; the window feeds it the outcome or the
    /// failure, and its own dismiss rules decide when it goes away.
    /// </summary>
    public RestoreOutcomeStrip Strip { get; }

    /// <summary>
    /// The four-stage in-progress strip's state (04-in-progress.md). One instance for the window's
    /// life: a restore that begins calls <see cref="BeginRestore"/>, which swaps in a fresh model,
    /// and the orchestrator drives it via <see cref="RestoreProgressModel.Advance"/>. The view binds
    /// to this; nothing here reaches for the store or the process - Task 6's Restore command is
    /// what calls Begin/Complete and feeds the stages.
    /// </summary>
    public RestoreProgressModel RestoreProgress
    {
        get => restoreProgress;
        private set => Set(ref restoreProgress, value);
    }

    /// <summary>
    /// True while a restore runs. The window uses it to (a) show the in-progress strip instead of
    /// the outcome strip, and (b) hold the list actions and Back up now at 40% so the window cannot
    /// be driven mid-restore (Task 5 Step 2).
    /// </summary>
    public bool IsRestoring
    {
        get => isRestoring;
        private set => Set(ref isRestoring, value);
    }

    /// <summary>
    /// The in-progress strip's status line: RESTORING "NAME" · WAVE LINK IS CLOSED. Null when no
    /// restore runs - the XAML shows the plain status strip in that case. Uppercase name, matching
    /// how SelectedLine already prints a row's name on the bottom bar.
    /// </summary>
    public string? RestoreStatusLabel { get; private set; }

    /// <summary>
    /// Begin a restore: swap in a fresh four-stage model (stage 0 current) and mark the window as
    /// restoring. The window calls this when the user confirms the dialog, before the orchestrator
    /// starts closing Wave Link.
    /// </summary>
    public void BeginRestore(string snapshotName)
    {
        RestoreProgress = new RestoreProgressModel();
        RestoreStatusLabel = $"RESTORING \"{snapshotName.ToUpper(CultureInfo.InvariantCulture)}\" · WAVE LINK IS CLOSED";
        isRestoring = true;

        Raise(nameof(RestoreProgress));
        Raise(nameof(IsRestoring));
        Raise(nameof(RestoreStatusLabel));
        // The four CanX facts all fold in not-IsRestoring, so re-raise them: the buttons and their
        // keyboard shortcuts go quiet for the duration of the restore.
        RaiseAll();
    }

    /// <summary>
    /// End a restore: mark every stage done (the strip hands off to the outcome) and release the
    /// window. The window calls this once the orchestrator returns, before it feeds the outcome
    /// into <see cref="Strip"/>.
    /// </summary>
    public void CompleteRestore()
    {
        RestoreProgress.Complete();
        isRestoring = false;

        Raise(nameof(IsRestoring));
        RaiseAll();
    }

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
    /// 06's first-run variant of error 1: when the store is empty, the status strip says
    /// "WAVE LINK NOT FOUND · NO SETTINGS FILE IN THE USUAL PLACE" and the empty state below it
    /// carries the mono "looked in" line. The window swaps this in for <see cref="StatusStrip"/>
    /// while the first-run screen is showing (Task 6); otherwise StatusStrip stands on its own.
    /// </summary>
    public string? FirstRunError1Label => facts.WaveLinkFound || List.TotalCount != 0
        ? null
        : "WAVE LINK NOT FOUND · NO SETTINGS FILE IN THE USUAL PLACE";

    /// <summary>The mono line beneath the first-run label: where we looked, verbatim.</summary>
    public string? FirstRunLookedInLabel => FirstRunError1Label is null
        ? null
        : "LOOKED IN %LOCALAPPDATA%\\Packages\\Elgato.WaveLink_*";

    /// <summary>
    /// Re-raise the status tone so a binding re-reads it. The window calls this when the restore
    /// strip's TurnsStatusAmber flips: 03-restore-outcomes.md says a Rejected strip turns the
    /// status strip amber too, and that is an ADDITIONAL condition on top of StatusTone's own
    /// ShellFacts-derived value. The XAML ORs the two (the strip's TurnsStatusAmber DataTrigger
    /// overrides the dot fill), so the window only needs to tell the binding to re-evaluate -
    /// it does not compute a second tone here, which would give the VM two sources of truth.
    /// </summary>
    public void RaiseStatusTone() => Raise(nameof(StatusTone));

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
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

            return localAppData.Length > 0
                && facts.StorePath.StartsWith(localAppData, StringComparison.OrdinalIgnoreCase)
                    ? "%LOCALAPPDATA%" + facts.StorePath[localAppData.Length..]
                    : facts.StorePath;
        }
    }

    // Every one of these folds in not-IsRestoring (04): while a restore runs the list actions and
    // Back up now hold at 40%, and the keyboard shortcuts that share these same CanX facts go
    // quiet with them. IsRestoring flips via BeginRestore/CompleteRestore, which raise it directly.
    public bool CanRename => !isRestoring && !facts.FolderMissing && List.Selected?.CanRename == true;

    public bool CanDelete => !isRestoring && !facts.FolderMissing && List.Selected?.CanDelete == true;

    public bool CanRestore => !isRestoring && !facts.FolderMissing && List.Selected?.CanRestore == true;

    /// <summary>
    /// Always live EXCEPT when the folder is gone - 08 puts all four buttons at 40% there,
    /// "including Back up now", because there is nowhere to put a backup. Also quiet while a
    /// restore runs (04): a capture mid-restore would race the settings write.
    /// </summary>
    public bool CanBackUpNow => !isRestoring && !facts.FolderMissing;

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
            nameof(FirstRunError1Label), nameof(FirstRunLookedInLabel),
            nameof(CanRename), nameof(CanDelete), nameof(CanRestore), nameof(CanBackUpNow),
        ])
        {
            Raise(property);
        }
    }
}
