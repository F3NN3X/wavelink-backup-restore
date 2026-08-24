using System.ComponentModel;
using System.Globalization;
using WaveLinkBackup.App.Windows;

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
/// <param name="WaveLinkInputs">How many inputs the live configuration has - the first-run found-line's "N INPUTS". Zero when the settings could not be read.</param>
/// <param name="WaveLinkSettingsPath">The path to Wave Link's own Settings.json - Screen 4's mono line beneath the found-line. Null when discovery failed.</param>
public sealed record ShellFacts(
    bool WaveLinkFound,
    bool WaveLinkRunning,
    DateTimeOffset? SettingsLastSavedLocal,
    int WaveLinkInputs,
    string? WaveLinkSettingsPath,
    bool AutoBackupEnabled,
    bool FolderMissing,
    string StorePath,
    long? FreeBytes,
    string? WaveLinkVersion = null,
    string? LogsPath = null);

/// <summary>
/// The status strip, the bottom bar, and what the four action buttons may do.
///
/// Nothing here reaches for the store or the process directly: the window hands it a
/// <see cref="ShellFacts"/> on every refresh, which is what lets every one of these strings be
/// asserted from a table.
/// </summary>
public sealed class ShellViewModel : ObservableObject
{
    private ShellFacts facts = new(true, false, null, 0, null, true, false, string.Empty, null);
    private bool isHighContrast;
    private bool isRestoring;
    private RestoreProgressModel restoreProgress = new();
    private SnapshotRowViewModel? watchedRow;
    private AutostartState autostartState = AutostartState.Off;

    /// <param name="autostart">
    /// The seam behind the WHEN WINDOWS STARTS rows (screens/12). Optional so the existing
    /// constructor keeps its shape for callers that do not surface autostart - the App passes
    /// the real RunKeyAutostart and drives RefreshAutostart on every tick.
    /// </param>
    public ShellViewModel(SnapshotListViewModel list, IAutostart? autostart = null)
    {
        List = list;
        Strip = new RestoreOutcomeStrip();
        Autostart = autostart;
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
    /// The backing-up strip's state (04-in-progress.md's first half). One instance for the
    /// window's life, like <see cref="RestoreProgress"/> — and for the same reason: 04 says the
    /// strip is "replaced in place by the result line" and never reappear-flashes, which a model
    /// swapped out per capture would make hard to hold to.
    /// </summary>
    public BackupProgressModel BackupProgress { get; } = new();

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
        // Fraction lives on the swapped-in model, and its value (0) is identical to the old
        // instance's - so a binding that cached the path at first resolution never re-subscribes
        // and the fill stays at zero width even as stages advance. Raising it here forces every
        // RestoreProgress.* path to re-resolve against the fresh model before Advance() fires its
        // own change events.
        Raise(nameof(RestoreProgress.Fraction));
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
    /// Screen 4 (first-run / empty state): the store has no backups yet. This is what swaps the
    /// list area for the centred column - caption bar and bottom bar stay as usual, Restore /
    /// Rename / Delete hold at 40% (they have nothing to act on), Back up now stays live. It is
    /// NOT the same fact as <see cref="FirstRunError1Label"/>: that one is the Wave-Link-not-found
    /// variant of error 1, which only shows when discovery ALSO failed; here Wave Link is found
    /// and there is simply nothing backed up yet.
    /// </summary>
    public bool IsFirstRun => List.TotalCount == 0 && !facts.FolderMissing;

    /// <summary>
    /// Screen 4's found-line, line 6: "WAVE LINK FOUND · N INPUTS · SETTINGS LAST SAVED …".
    /// The design fixes this to the Wave-Link-found variant; when discovery failed the not-found
    /// variant takes its place (an open gap - see technical-debt), so this returns null then and
    /// the view keeps only the ok-dot line absent.
    /// </summary>
    public string? FoundLine => !facts.WaveLinkFound || List.TotalCount != 0
        ? null
        : $"WAVE LINK FOUND · {facts.WaveLinkInputs} INPUTS · "
          + (facts.SettingsLastSavedLocal is { } at
              ? $"SETTINGS LAST SAVED {Readable.TimeOfDay(at)}"
              : "SETTINGS NEVER SAVED");

    /// <summary>
    /// Screen 4's settings-path line, beneath the found-line in mono at 80%: the path to Wave
    /// Link's own Settings.json. Absent when discovery failed (nothing to point at) or once a
    /// backup exists (the list has taken over).
    /// </summary>
    public string? FoundSettingsPath => !facts.WaveLinkFound || List.TotalCount != 0
        ? null
        : facts.WaveLinkSettingsPath;

    /// <summary>
    /// The configured store path, verbatim. Screen 4's footer strip shows it on the left
    /// ("where your backups will live"); the bottom bar already renders it as its mono line, so
    /// this is a plain projection of <see cref="facts"/> with no state gating.
    /// </summary>
    public string StorePath => facts.StorePath;

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

    /// <summary>
    /// The seam behind the WHEN WINDOWS STARTS rows (screens/12). Null for callers that do not
    /// surface autostart; every property below degrades to "off and cannot be enabled" in that
    /// case, which is also exactly what a blocked entry renders as.
    /// </summary>
    public IAutostart? Autostart { get; }

    /// <summary>
    /// The live autostart state, read from the registry seam on every refresh - never trusted to
    /// be whatever it was last tick, because Task Manager can change it out from under us at any
    /// time. Defaults to Off until the first RefreshAutostart.
    /// </summary>
    public AutostartState AutostartState
    {
        get => autostartState;
        private set => Set(ref autostartState, value);
    }

    /// <summary>
    /// The veto rule (screens/12): a Task Manager-disabled entry reads OFF and cannot be switched
    /// on here. Task Manager wins; the note says so rather than fighting it. So "blocked" renders
    /// as unchecked AND disabled - the control is off, and the user is told why they cannot turn
    /// it on from this app.
    /// </summary>
    public bool IsAutostartEnabled => autostartState == AutostartState.On;

    /// <summary>False only while Task Manager holds a veto (or no seam is wired at all).</summary>
    public bool CanEnableAutostart => Autostart is not null && autostartState != AutostartState.BlockedByTaskManager;

    /// <summary>
    /// The note shown when the entry is blocked: Task Manager won, and this app will not fight it.
    /// Null in every other state - there is nothing to explain then.
    /// </summary>
    public string? AutostartBlockedNote => autostartState == AutostartState.BlockedByTaskManager
        ? "DISABLED IN TASK MANAGER — TURN IT ON THERE"
        : null;

    /// <summary>
    /// Re-read the state from the registry seam. The App calls this on startup and on every tick,
    /// so a veto applied in Task Manager while the app runs is picked up on the next refresh rather
    /// than only at the next launch. No-op when no seam is wired.
    /// </summary>
    public void RefreshAutostart()
    {
        if (Autostart is null) return;

        var state = Autostart.Read();

        if (!Equals(state, autostartState))
        {
            AutostartState = state;
            Raise(nameof(IsAutostartEnabled));
            Raise(nameof(CanEnableAutostart));
            Raise(nameof(AutostartBlockedNote));
        }
    }

    /// <summary>
    /// Flip the Run key via the seam. When blocked, nothing is written and the state stays put -
    /// Enable() itself refuses under a veto, so re-reading after the attempt keeps the three
    /// derived properties honest without this method having to special-case the refusal.
    /// </summary>
    public void ToggleAutostart()
    {
        if (Autostart is null) return;

        if (autostartState == AutostartState.On) Autostart.Disable();
        else if (CanEnableAutostart) Autostart.Enable();

        RefreshAutostart();
    }

    /// <summary>
    /// What the last <see cref="Apply"/> was told. Read by the window for the few lines that are
    /// composed at render time from machine-specific figures — 03 §3's rejection meta among them —
    /// rather than being a property the view model can name in advance.
    /// </summary>
    public ShellFacts Facts => facts;

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
        if (e.PropertyName == nameof(SnapshotListViewModel.Selected))
        {
            if (watchedRow is not null) watchedRow.PropertyChanged -= OnSelectedRowPropertyChanged;
            watchedRow = List.Selected;
            if (watchedRow is not null) watchedRow.PropertyChanged += OnSelectedRowPropertyChanged;

            RaiseAll();
            return;
        }

        // The bottom bar's first two figures are the LIST's, and nothing re-read them when the
        // list finished loading - only a selection or the 15-second tick did. So every launch
        // showed "0 BACKUPS · 0 B IN %LOCALAPPDATA%\WaveLinkBackup" under a window full of
        // backups, for as long as it took the user to click something.
        if (e.PropertyName is nameof(SnapshotListViewModel.TotalCount)
            or nameof(SnapshotListViewModel.TotalBytes))
        {
            Raise(nameof(SummaryLine));
        }
    }

    private void OnSelectedRowPropertyChanged(object? sender, PropertyChangedEventArgs e) => RaiseAll();

    private void RaiseAll()
    {
        foreach (var property in (string[])
        [
            nameof(StatusStrip), nameof(StatusTone), nameof(SelectedLine), nameof(SummaryLine),
            nameof(FirstRunError1Label), nameof(FirstRunLookedInLabel),
            nameof(IsFirstRun), nameof(FoundLine), nameof(FoundSettingsPath), nameof(StorePath),
            nameof(CanRename), nameof(CanDelete), nameof(CanRestore), nameof(CanBackUpNow),
        ])
        {
            Raise(property);
        }
    }
}
