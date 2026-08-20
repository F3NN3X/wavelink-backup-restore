using System.Collections.ObjectModel;
using WaveLinkBackup.App.Windows;
using WaveLinkBackup.Core.Automation;

namespace WaveLinkBackup.App.ViewModels;

/// <summary>
/// One installation of Wave Link, as the WHICH WAVE LINK section shows it. A value type: the
/// view-model hands a fresh one to the dialog each time the choice changes, and binding picks
/// up the new reference.
/// </summary>
public sealed record WhichWaveLinkModel(
    string Version,
    string Path,
    DateTimeOffset ChosenAt,
    bool Visible)
{
    /// <summary>
    /// The "CHOSEN 14 AUG" line, upper-cased to match the mono micro-label convention. Formatted
    /// here (not in the view) so the format is unit-testable without a window - the same reason
    /// <see cref="FreeSpaceText"/> and the proportion-bar labels live on their models. The date is
    /// local, not UTC: "chosen" is when the user made the choice, and that is a local moment.
    /// </summary>
    public string ChosenAtText =>
        $"CHOSEN {ChosenAt.ToLocalTime():d MMM}".ToUpperInvariant();
}

/// <summary>
/// The WHERE THESE SETTINGS LIVE block: where the file is, how big it is, and the line that
/// keeps "a command-line flag overrides this for one run" honest. Read-only - nothing in the
/// dialog edits these.
/// </summary>
public sealed record WhereSettingsLiveModel(string FilePath, string SizeText);

/// <summary>
/// One row of the WHAT GOES IN A BACKUP group, and its share of the proportion bar.
/// </summary>
/// <param name="Name">The person-written row label (Rubik).</param>
/// <param name="Description">The plain-language line under the label; empty for rows that need none.</param>
/// <param name="SizeBytes">This tier's honest size; 0 when it is not in a backup.</param>
/// <param name="Enabled">Whether this tier is currently in a backup.</param>
/// <param name="Locked">
/// True for the two tiers that have no switch: the settings file and the effects list are always
/// included, deliberately ([[ADR-006]]) - together they are under half a megabyte and they are the
/// difference between a restore that works and one that leaves the user guessing. Phase 6 moved
/// the presets and plug-in-files rows OUT of this state; they are ordinary toggles now.
/// </param>
public sealed class WhatGoesInRow(
    string name, string description, long sizeBytes, bool enabled, bool locked) : ObservableObject
{
    private bool enabledValue = enabled;

    public string Name { get; } = name;

    public string Description { get; } = description;

    public long SizeBytes { get; } = sizeBytes;

    public bool Locked { get; } = locked;

    /// <summary>
    /// Settable, because the two switchable tiers are switched HERE — this is the control the
    /// design puts in the row. A locked row's toggle is disabled in the view and its setter is
    /// refused here as well, so the rule survives a binding someone rewires later.
    /// </summary>
    public bool Enabled
    {
        get => enabledValue;
        set
        {
            if (Locked) return;
            Set(ref enabledValue, value);
        }
    }

    /// <summary>
    /// The honest size figure, right-aligned in mono. "—" when the tier holds no bytes of its own:
    /// the effects list rides inside the settings file (it is part of it), and a tier that would
    /// capture nothing on this machine has no number to print. Formatted here, not in the view,
    /// for the same reason <see cref="SettingsViewModel.FreeSpaceText"/> is - the view stays a pure
    /// binding and the format is unit-testable without a window.
    /// </summary>
    public string SizeText => SizeBytes > 0 ? Readable.Bytes(SizeBytes) : "—";
}

/// <summary>
/// One segment of the stacked proportion bar. Widths are a fraction of the whole bar (0..1),
/// computed from the enabled tiers - never hard-coded percentages (Task 3 step 2).
/// </summary>
/// <param name="Tier">
/// Which of the four tiers this segment is, 1-based and in the order the rows are listed. It
/// carries the segment's COLOUR: README Screen 3 gives the bar three colours in row order - ok,
/// warn, then accent at 75% - and the view used to pick them by matching <paramref name="Name"/>
/// against one hard-coded English string, which silently painted every other segment ok and would
/// have broken on any copy edit.
/// </param>
/// <param name="Tier">
/// Which of the four the segment is, so the view can colour it by ROW ORDER rather than by
/// matching its name against an English string.
/// </param>
public sealed record ProportionSegment(string Name, long Bytes, double Fraction, int Tier)
{
    /// <summary>
    /// The segment named and sized, for Windows high contrast: <c>YOUR SETUP · 470 KB</c>.
    ///
    /// 11-high-contrast.md: "The proportion bar in Settings loses its colour segments; label the
    /// segments instead." In high contrast every fill in the app is transparent, so the bar's four
    /// bands become one undifferentiated track — the encoding is gone and nothing replaced it
    /// (audit §2.9b).
    /// </summary>
    public string Label => $"{Name.ToUpperInvariant()} · {Readable.Bytes(Bytes)}";
}

/// <summary>
/// The WHAT GOES IN A BACKUP section as a pure projection: the four rows, the computed
/// proportion bar, and the two mono labels under it. No I/O - the app hands in the sizes it has
/// measured, and this only decides how they divide up the bar and what to print. That keeps the
/// "recompute from the enabled tiers" rule unit-testable without a window (Task 3 step 4).
/// </summary>
public sealed class WhatGoesInModel : ObservableObject
{
    private long totalBytes;

    public WhatGoesInModel(
        WhatGoesInRow setup,
        WhatGoesInRow effectsList,
        WhatGoesInRow presets,
        WhatGoesInRow pluginFiles)
    {
        Presets = presets;
        PluginFiles = pluginFiles;
        Rows = new ObservableCollection<WhatGoesInRow> { setup, effectsList, presets, pluginFiles };
        Segments = [];

        // The bar follows the toggles LIVE. "Recompute from the enabled tiers, never hard-code the
        // percentages" is only true if it recomputes when a tier is switched - a bar that was
        // right when the dialog opened and stale by the time the user reads it is a hard-coded
        // percentage with extra steps.
        foreach (var row in Rows) row.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(WhatGoesInRow.Enabled)) Recompute();
        };

        Recompute();
    }

    private void Recompute()
    {
        // A tier that is off contributes nothing, so switching one reflows every other segment
        // rather than shifting a fixed share.
        var enabled = Rows.Where(r => r.Enabled && r.SizeBytes > 0).ToList();
        totalBytes = enabled.Sum(r => r.SizeBytes);

        Segments.Clear();
        foreach (var row in enabled)
        {
            Segments.Add(new ProportionSegment(
                row.Name, row.SizeBytes, totalBytes > 0 ? (double)row.SizeBytes / totalBytes : 0.0,
                Tier: Rows.IndexOf(row) + 1));
        }

        EachBackupLabel = $"EACH BACKUP: ABOUT {Readable.Bytes(totalBytes).ToUpperInvariant()}";

        // The "+ Y MB IF YOU ADD THE PLUG-IN FILES" figure is the size of the tiers that are NOT
        // in a backup today - the honest answer to "what would turning this on cost".
        var notIncluded = Rows.Where(r => !r.Enabled && r.SizeBytes > 0).Sum(r => r.SizeBytes);
        IfYouAddLabel = notIncluded > 0
            ? $"+ {Readable.Bytes(notIncluded).ToUpperInvariant()} IF YOU ADD THE PLUG-IN FILES"
            : string.Empty;

        Raise(nameof(EachBackupLabel));
        Raise(nameof(IfYouAddLabel));
        Raise(nameof(TotalBytes));
    }

    /// <summary>The four rows, top to bottom, for the group's grid.</summary>
    public ObservableCollection<WhatGoesInRow> Rows { get; }

    /// <summary>Tier 3's row, named so the view model can bind its toggle to the setting.</summary>
    public WhatGoesInRow Presets { get; }

    /// <summary>Tier 4's row.</summary>
    public WhatGoesInRow PluginFiles { get; }

    /// <summary>The stacked bar's segments, left to right, each a 0..1 fraction of the whole.</summary>
    public ObservableCollection<ProportionSegment> Segments { get; }

    /// <summary>Left mono label under the bar: "EACH BACKUP: ABOUT X MB".</summary>
    public string EachBackupLabel { get; private set; } = string.Empty;

    /// <summary>Right mono label under the bar, empty when nothing is left out.</summary>
    public string IfYouAddLabel { get; private set; } = string.Empty;

    /// <summary>Total bytes in a backup today - the enabled tiers' sum.</summary>
    public long TotalBytes => totalBytes;

    /// <summary>
    /// The two plain-language notes (Task 3 step 3): lead clause first so it can be set strong.
    /// Instance properties (not static) so the XAML binds to them through the row's DataContext
    /// without an x:Static - a Run cannot take a Binding in its Text attribute, but it can take
    /// one as a property element. The copy is constant; the instance form exists only for binding.
    /// </summary>
    public string NoteOneLead => "Licences are never included.";
    public string NoteOneRest =>
        "A backup copies the effect files, not your right to run them - you reinstall and re-authorise on a new machine, then restore.";

    public string NoteTwoLead => "A backup describes this computer.";
    public string NoteTwoRest =>
        "It names the audio devices plugged into it, so restoring elsewhere leaves those channels dead. Snapshots are machine-local.";
}

/// <summary>
/// The settings dialog's data model. There is no Save button: every control commits on change,
/// so each settable property writes through to the settings file immediately (atomic, via
/// <see cref="SettingsRepository"/>). That is the whole of persistence here - read once at
/// construction, write on every change, never on exit.
///
/// The tier toggles (<see cref="IncludePresets"/>, <see cref="IncludePluginFiles"/>) commit like
/// everything else here. They were locked and unmovable until phase 6 built the tiers behind
/// them; a toggle that writes a setting nothing reads is worse than one that is visibly off.
/// </summary>
/// <summary>
/// The two seams behind Settings' <c>WHEN WINDOWS STARTS</c> section (screens/12).
///
/// A record rather than three more constructor parameters, and injected rather than reached for:
/// one of the two lives in the registry and the other in the shell's own state file, and neither
/// belongs to <see cref="BackupSettings"/> — settings.json describes itself in the dialog as "the
/// folder, the automatic-backup switch, how many to keep and which Wave Link you picked", which a
/// window behaviour would make false.
/// </summary>
/// <param name="Autostart">
/// The Run-key seam. Its three states are the reason the toggle is not a bool: Task Manager can
/// veto the entry, and the design's rule is that the toggle READS BACK what Task Manager did
/// rather than fighting it.
/// </param>
public sealed record StartupSeam(
    IAutostart Autostart,
    Func<bool> ReadClosingHidesToTray,
    Action<bool> WriteClosingHidesToTray);

public sealed class SettingsViewModel : ObservableObject
{
    private const int MinKeepCount = 1;
    private const int MaxKeepCount = 999;

    private readonly Func<BackupSettings, bool> save;

    /// <summary>
    /// The record as it sits in settings.json - WITHOUT any command-line overlay. Commits merge
    /// over this, never over the overlaid value: "a command-line flag overrides this file for
    /// that one run and isn't saved" means a control change must not drag the flag's value into
    /// the file through an unrelated commit.
    /// </summary>
    private BackupSettings persisted;

    private string backupFolder;
    private bool autoBackupEnabled;
    private int autoBackupKeepCount;
    private int autoBackupIntervalMinutes;
    private int? dailyBackupMinutes;
    private bool includePresets;
    private bool includePluginFiles;
    private bool isHighContrast;
    private WhatGoesInModel? whatGoesIn;
    private readonly StartupSeam? startup;
    private AutostartState autostartState;
    private bool closingHidesToTray;

    /// <param name="startup">
    /// The WHEN WINDOWS STARTS section's seams, or null to hide the section entirely. Null is what
    /// a test harness and the CLI-shaped callers pass; the App passes the real ones.
    /// </param>
    public SettingsViewModel(
        BackupSettings settings,
        Func<BackupSettings, bool> save,
        WhereSettingsLiveModel whereSettingsLive,
        WhichWaveLinkModel? whichWaveLink = null,
        StartupSeam? startup = null)
    {
        this.save = save;
        persisted = settings;
        backupFolder = settings.StorePath;
        autoBackupEnabled = settings.AutoBackupEnabled;
        autoBackupKeepCount = settings.AutoBackupKeepCount;
        autoBackupIntervalMinutes = settings.AutoBackupIntervalMinutes;
        dailyBackupMinutes = settings.DailyBackupMinutes;
        includePresets = settings.IncludePresets;
        includePluginFiles = settings.IncludePluginFiles;

        WhereSettingsLive = whereSettingsLive;
        WhichWaveLink = whichWaveLink;

        this.startup = startup;
        autostartState = startup?.Autostart.Read() ?? AutostartState.Off;
        closingHidesToTray = startup?.ReadClosingHidesToTray() ?? true;
    }

    // ----------------------------------------------- when Windows starts (screens/12)

    /// <summary>
    /// Whether to draw the section at all. False when nothing was injected to drive it — a
    /// section of two toggles that write nowhere is worse than no section.
    /// </summary>
    public bool HasStartupSection => startup is not null;

    /// <summary>
    /// "Start with Windows and sit in the tray." Reads the Run key, writes the Run key, and
    /// re-reads after every write — <see cref="IAutostart.Enable"/> can refuse, and a toggle that
    /// showed the value it was ASKED for rather than the one that took would lie about a veto.
    /// </summary>
    public bool StartWithWindows
    {
        get => autostartState == AutostartState.On;
        set
        {
            if (startup is null || value == StartWithWindows) return;

            if (value) startup.Autostart.Enable();
            else startup.Autostart.Disable();

            RefreshAutostart();
        }
    }

    /// <summary>
    /// False when Task Manager has disabled the entry. The design: "Task Manager wins; the note
    /// says so rather than fighting it."
    /// </summary>
    public bool CanStartWithWindows =>
        startup is not null && autostartState != AutostartState.BlockedByTaskManager;

    /// <summary>The note under the toggle when Task Manager holds the veto, else null.</summary>
    public string? StartupBlockedNote => autostartState == AutostartState.BlockedByTaskManager
        ? "Task Manager has disabled this app's startup entry. Re-enable it there first."
        : null;

    /// <summary>The Run-key line the design prints under the section, verbatim.</summary>
    public string StartupRegistryLine =>
        @"HKCU\Software\Microsoft\Windows\CurrentVersion\Run · WaveLinkBackup --tray";

    /// <summary>"Closing the window hides it in the tray." On by default. Lives in ShellState.</summary>
    public bool ClosingHidesToTray
    {
        get => closingHidesToTray;
        set
        {
            if (startup is null || !Set(ref closingHidesToTray, value)) return;

            startup.WriteClosingHidesToTray(value);
        }
    }

    /// <summary>Re-read the Run key. Called after every write, and by the dialog on open.</summary>
    public void RefreshAutostart()
    {
        if (startup is null) return;

        autostartState = startup.Autostart.Read();

        Raise(nameof(StartWithWindows));
        Raise(nameof(CanStartWithWindows));
        Raise(nameof(StartupBlockedNote));
    }

    /// <summary>
    /// The one place a change becomes durable. Returns the save's success so a future caller can
    /// surface a failure; today the dialog shows nothing, matching "changes apply as you make
    /// them" with no error path designed for it.
    /// </summary>
    private bool Commit(BackupSettings next)
    {
        var ok = save(next);
        if (ok) persisted = next;
        return ok;
    }

    /// <summary>Read-only display of WHERE BACKUPS ARE KEPT. Change folder… re-points it.</summary>
    public string BackupFolder => backupFolder;

    /// <summary>The automatic-backup switch. Commits on change.</summary>
    public bool AutoBackupEnabled
    {
        get => autoBackupEnabled;
        set
        {
            if (Set(ref autoBackupEnabled, value))
                Commit(persisted with { AutoBackupEnabled = value });
        }
    }

    /// <summary>The keep-count stepper's value. Commits on change.</summary>
    public int AutoBackupKeepCount
    {
        get => autoBackupKeepCount;
        set
        {
            var clamped = Math.Clamp(value, MinKeepCount, MaxKeepCount);
            if (!Set(ref autoBackupKeepCount, clamped)) return;

            Commit(persisted with { AutoBackupKeepCount = clamped });
            Raise(nameof(KeepCountLabel));
        }
    }

    /// <summary>
    /// The keep-count row's title, which — like <see cref="IntervalLabel"/> — IS the value read
    /// back as a sentence: "Keep the last 30 automatic backups". The XAML carried the sentence
    /// with the number simply removed ("Keep the last automatic backups"), which reads as a
    /// half-finished string beside a stepper showing the number it left out.
    /// </summary>
    public string KeepCountLabel => $"Keep the last {autoBackupKeepCount} automatic backups";

    /// <summary>Move the keep-count stepper by one. The − / + buttons call this and nothing else.</summary>
    public void StepKeepCount(int direction) => AutoBackupKeepCount = autoBackupKeepCount + direction;

    // ---------------------------------------------------------------- how often (screens/14)

    /// <summary>
    /// The cap between two automatic backups, in minutes. Snapped to the ladder rather than
    /// clamped to a range: every position has to be a number a person would choose, and a value
    /// arriving from a hand-edited settings file must land on one of them too.
    /// </summary>
    public int AutoBackupIntervalMinutes
    {
        get => autoBackupIntervalMinutes;
        set
        {
            var snapped = Snap(value);
            if (!Set(ref autoBackupIntervalMinutes, snapped)) return;

            Commit(persisted with { AutoBackupIntervalMinutes = snapped });
            Raise(nameof(IntervalText));
            Raise(nameof(IntervalLabel));
        }
    }

    /// <summary>
    /// Move one rung up (+1) or down (-1) the ladder. Stops at both ends rather than wrapping: a
    /// stepper that jumps from 24 h to 15 min on one press is a stepper that mis-sets itself.
    /// </summary>
    public void StepInterval(int direction)
    {
        var ladder = BackupSettings.IntervalLadder;
        var index = ladder.ToList().IndexOf(autoBackupIntervalMinutes);
        if (index < 0) index = ladder.ToList().IndexOf(Snap(autoBackupIntervalMinutes));

        AutoBackupIntervalMinutes = ladder[Math.Clamp(index + direction, 0, ladder.Count - 1)];
    }

    /// <summary>The stepper's mono readout: "15 MIN", "1 H", "24 H".</summary>
    public string IntervalText => autoBackupIntervalMinutes < 60
        ? $"{autoBackupIntervalMinutes} MIN"
        : $"{autoBackupIntervalMinutes / 60} H";

    /// <summary>
    /// The row's title, which IS the value read back as a sentence. Written here rather than as a
    /// fixed string in the XAML so the label and the control cannot drift - the old copy said "at
    /// most one an hour" beside a constant nobody could change, and that was the whole problem.
    /// </summary>
    public string IntervalLabel => autoBackupIntervalMinutes switch
    {
        60 => "At most one automatic backup an hour",
        1440 => "At most one automatic backup a day",
        < 60 => $"At most one automatic backup every {autoBackupIntervalMinutes} minutes",
        _ => $"At most one automatic backup every {autoBackupIntervalMinutes / 60} hours",
    };

    private static int Snap(int minutes) =>
        BackupSettings.IntervalLadder.MinBy(rung => Math.Abs(rung - minutes));

    // ----------------------------------------------------------- and at a set time (screens/14)

    /// <summary>
    /// Whether a daily backup is taken as well. Switching it on starts at 03:00; switching it off
    /// forgets the time rather than keeping a value nothing reads - null IS "off" in the settings
    /// file, and two ways to say off is one too many.
    /// </summary>
    public bool DailyBackupEnabled
    {
        get => dailyBackupMinutes is not null;
        set
        {
            int? next = value ? dailyBackupMinutes ?? BackupSettings.DefaultDailyMinutes : null;
            if (next == dailyBackupMinutes) return;

            dailyBackupMinutes = next;
            Commit(persisted with { DailyBackupMinutes = next });

            Raise(nameof(DailyBackupEnabled));
            Raise(nameof(DailyBackupMinutes));
            Raise(nameof(DailyTimeText));
        }
    }

    /// <summary>Minutes past local midnight, or null when the daily backup is off.</summary>
    public int? DailyBackupMinutes => dailyBackupMinutes;

    /// <summary>
    /// Move the daily time by half an hour, wrapping at midnight. Wrapping is right here and wrong
    /// for the interval: a clock is a circle and 23:30 + 30 min is 00:00, whereas a duration ladder
    /// has two ends.
    /// </summary>
    public void StepDailyTime(int direction)
    {
        if (dailyBackupMinutes is not { } current) return;

        const int day = 24 * 60;
        var next = ((current + (direction * BackupSettings.DailyStepMinutes)) % day + day) % day;
        if (next == current) return;

        dailyBackupMinutes = next;
        Commit(persisted with { DailyBackupMinutes = next });

        Raise(nameof(DailyBackupMinutes));
        Raise(nameof(DailyTimeText));
    }

    /// <summary>
    /// "03:00". Twenty-four hour regardless of the OS clock format: this is a mono value in a
    /// technical row, sitting next to "1 H" and "30", not a timestamp in prose.
    /// </summary>
    public string DailyTimeText => dailyBackupMinutes is { } minutes
        ? $"{minutes / 60:D2}:{minutes % 60:D2}"
        : string.Empty;

    /// <summary>
    /// Tier 3 — effect presets. Commits on change like every other control here; phase 6 built
    /// the tier behind it, so the toggle moves and the next capture obeys it.
    /// </summary>
    public bool IncludePresets
    {
        get => includePresets;
        set
        {
            if (Set(ref includePresets, value))
                Commit(persisted with { IncludePresets = value });
        }
    }

    /// <summary>Tier 4 — the plug-in files themselves. Off by default: ~40 MB, and no licence.</summary>
    public bool IncludePluginFiles
    {
        get => includePluginFiles;
        set
        {
            if (Set(ref includePluginFiles, value))
                Commit(persisted with { IncludePluginFiles = value });
        }
    }

    /// <summary>
    /// What one backup costs, summed from the tiers that are actually on - measured by
    /// <c>TierCapture.Measure</c>, never the design mock's figures ([[ADR-006]]).
    /// </summary>
    public long EstimatedBackupBytes { get; internal set; }

    /// <summary>The WHERE THESE SETTINGS LIVE block. Read-only.</summary>
    public WhereSettingsLiveModel WhereSettingsLive { get; }

    /// <summary>
    /// The WHAT GOES IN A BACKUP section: rows, computed proportion bar and labels. Set by the
    /// app at construction from the sizes it has measured - the VM does not measure anything
    /// itself, so the projection stays pure and testable (Task 3).
    /// </summary>
    public WhatGoesInModel? WhatGoesIn
    {
        get => whatGoesIn;
        set
        {
            whatGoesIn = value;
            if (value is null) return;

            // The two switchable rows ARE the tier toggles: the design puts the control in the
            // row, so the row is where the user changes it and this is where it becomes durable.
            // Row -> setting only; nothing else moves these, and a two-way loop through a commit
            // is how a toggle starts flickering.
            value.Presets.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(WhatGoesInRow.Enabled)) IncludePresets = value.Presets.Enabled;
            };

            value.PluginFiles.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(WhatGoesInRow.Enabled))
                    IncludePluginFiles = value.PluginFiles.Enabled;
            };
        }
    }

    /// <summary>
    /// The WHICH WAVE LINK section, or null when there is only one installation and the section
    /// hides itself entirely.
    /// </summary>
    public WhichWaveLinkModel? WhichWaveLink { get; private set; }

    /// <summary>
    /// The empty-trash row hosted under WHERE BACKUPS ARE KEPT (Plan 6's projection). Set by the
    /// app before the dialog opens and re-set on a folder change - never cached across a move.
    /// Null when there is no store to read (the row hides itself).
    /// </summary>
    public TrashRowModel? TrashRow { get; set; }

    /// <summary>
    /// Free bytes on the volume holding the backup folder, for the mono stats line. Null when the
    /// figure cannot be read (the line omits it, matching the bottom bar's convention).
    /// </summary>
    public long? FreeSpaceBytes { get; set; }

    /// <summary>
    /// The free-space figure as the mono stats line prints it: "118 GB FREE". Empty when the
    /// figure cannot be read - the XAML then collapses the whole line rather than print a number
    /// that is not there. Formatted here (not in the view) so the view stays a pure binding and
    /// the format is unit-testable without a window.
    /// </summary>
    /// <summary>How many backups the store holds. Set by the caller, which owns the store.</summary>
    public int BackupCount { get; set; }

    /// <summary>What those backups weigh. Set by the caller, from the same read.</summary>
    public long UsedBytes { get; set; }

    /// <summary>
    /// The design's full stats line: <c>N BACKUPS · X MB USED · Y GB FREE ON THIS DRIVE</c>.
    ///
    /// It printed only the free figure until 0.6.1 — the count and the used bytes live on the
    /// shell, and the code's own comment said so while nothing plumbed them through (audit §2.9a).
    /// Each of the three omits itself when it cannot be read, so a drive whose free space is
    /// unknowable still prints the two figures we do have rather than the whole line vanishing.
    /// </summary>
    public string FreeSpaceText
    {
        get
        {
            var parts = new List<string>(3);

            if (BackupCount > 0) parts.Add($"{BackupCount} BACKUP{(BackupCount == 1 ? "" : "S")}");
            if (UsedBytes > 0) parts.Add($"{Readable.Bytes(UsedBytes)} USED");
            if (FreeSpaceBytes is { } bytes) parts.Add($"{Readable.Bytes(bytes)} FREE ON THIS DRIVE");

            return string.Join(" · ", parts);
        }
    }

    // ------------------------------------------------- error 9, in place (06-errors.md §9)

    private string? notABackupFolderPath;
    private int notABackupFolderFileCount;

    /// <summary>
    /// Whether the amber "that folder is not a Wave Link Backup" block is showing.
    ///
    /// 06's placement TABLE files error 9 under Dialogs; §9's own text says it "appears in
    /// Settings, in place, after Change folder…", which is more specific and is what this
    /// implements. It is the one error whose whole point is sitting beside the control that
    /// caused it.
    /// </summary>
    public bool ShowsNotABackupFolder => notABackupFolderPath is not null;

    /// <summary>The error's mono line: <c>D:\Recordings\ · 38 FILES · NO manifest.json</c>.</summary>
    public string NotABackupFolderMeta => notABackupFolderPath is null
        ? string.Empty
        : $"{notABackupFolderPath} · {notABackupFolderFileCount} FILE"
          + $"{(notABackupFolderFileCount == 1 ? "" : "S")} · NO manifest.json";

    public string NotABackupFolderTitle => AppError.ByCode(9).Title;

    public string NotABackupFolderBody => AppError.ByCode(9).Body;

    /// <summary>
    /// Raise the block for <paramref name="path"/>. Called after a Change folder… that landed
    /// somewhere holding files but no snapshot.
    /// </summary>
    public void ShowNotABackupFolder(string path, int fileCount)
    {
        notABackupFolderPath = path;
        notABackupFolderFileCount = fileCount;
        RaiseNotABackupFolder();
    }

    /// <summary>"Keep the current folder" — and every successful folder change.</summary>
    public void ClearNotABackupFolder()
    {
        if (notABackupFolderPath is null) return;

        notABackupFolderPath = null;
        notABackupFolderFileCount = 0;
        RaiseNotABackupFolder();
    }

    private void RaiseNotABackupFolder()
    {
        Raise(nameof(ShowsNotABackupFolder));
        Raise(nameof(NotABackupFolderMeta));
    }

    /// <summary>
    /// Change folder…: persist the new store path and re-point the read-only display. The
    /// caller (Task 2) is responsible for re-pointing the live store and re-detecting the trash
    /// row's volume; this only owns the settings value.
    /// </summary>
    public bool ChangeBackupFolder(string path)
    {
        if (!Commit(persisted with { StorePath = path })) return false;

        Set(ref backupFolder, path);
        return true;
    }

    /// <summary>
    /// Whether the OS is in high-contrast mode. The dialog's controls (the toggle, the stepper)
    /// carry high-contrast triggers that bind to this through the window's DataContext - the same
    /// convention MainWindow uses via ShellViewModel.IsHighContrast. Set by the app at construction;
    /// a false default is safe because on a non-HC OS every trigger stays inert.
    /// </summary>
    public bool IsHighContrast
    {
        get => isHighContrast;
        set => Set(ref isHighContrast, value);
    }

    /// <summary>
    /// The WHICH WAVE LINK "Change…" action: persist which installation to watch and restore
    /// into. This is what error 2 (Plan 7) resolves to - without storing the answer the chooser
    /// asks again on every launch.
    /// </summary>
    public bool ChooseWaveLink(WhichWaveLinkModel chosen)
    {
        if (!Commit(persisted with { ChosenWaveLinkPath = chosen.Path })) return false;

        WhichWaveLink = chosen;
        Raise(nameof(WhichWaveLink));
        return true;
    }

    /// <summary>Build the dialog model from a read settings value and its two sections.</summary>
    public static SettingsViewModel Build(
        BackupSettings settings,
        Func<BackupSettings, bool> save,
        WhereSettingsLiveModel whereSettingsLive,
        WhichWaveLinkModel? whichWaveLink = null,
        StartupSeam? startup = null) =>
        new(settings, save, whereSettingsLive, whichWaveLink, startup);
}
