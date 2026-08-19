using System.Collections.ObjectModel;
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
/// <param name="Enabled">Whether this tier is currently in a backup. The locked rows are off.</param>
/// <param name="Locked">True for the two "NOT BUILT YET" tiers - their toggle is off and unmovable (Task 5).</param>
public sealed record WhatGoesInRow(string Name, string Description, long SizeBytes, bool Enabled, bool Locked)
{
    /// <summary>
    /// The honest size figure, right-aligned in mono. "—" when the tier holds no bytes of its own:
    /// the effects list rides inside the settings file (it is part of it), and the locked tiers are
    /// not built yet, so neither carries a separate number. Formatted here, not in the view, for the
    /// same reason <see cref="SettingsViewModel.FreeSpaceText"/> is - the view stays a pure binding
    /// and the format is unit-testable without a window.
    /// </summary>
    public string SizeText => SizeBytes > 0 ? Readable.Bytes(SizeBytes) : "—";
}

/// <summary>
/// One segment of the stacked proportion bar. Widths are a fraction of the whole bar (0..1),
/// computed from the enabled tiers - never hard-coded percentages (Task 3 step 2).
/// </summary>
public sealed record ProportionSegment(string Name, long Bytes, double Fraction);

/// <summary>
/// The WHAT GOES IN A BACKUP section as a pure projection: the four rows, the computed
/// proportion bar, and the two mono labels under it. No I/O - the app hands in the sizes it has
/// measured, and this only decides how they divide up the bar and what to print. That keeps the
/// "recompute from the enabled tiers" rule unit-testable without a window (Task 3 step 4).
/// </summary>
public sealed class WhatGoesInModel
{
    private readonly long totalBytes;

    public WhatGoesInModel(
        WhatGoesInRow setup,
        WhatGoesInRow effectsList,
        WhatGoesInRow presets,
        WhatGoesInRow pluginFiles)
    {
        Rows = new ObservableCollection<WhatGoesInRow> { setup, effectsList, presets, pluginFiles };

        // The bar shows only what is actually in a backup. A tier that is off (or locked off)
        // contributes nothing - the percentages are recomputed from the enabled tiers, so adding
        // or removing one reflows every other segment rather than shifting a fixed share.
        var enabled = Rows.Where(r => r.Enabled && r.SizeBytes > 0).ToList();
        totalBytes = enabled.Sum(r => r.SizeBytes);

        Segments = new ObservableCollection<ProportionSegment>(
            enabled.Select(r => new ProportionSegment(
                r.Name,
                r.SizeBytes,
                totalBytes > 0 ? (double)r.SizeBytes / totalBytes : 0.0)));

        EachBackupLabel = $"EACH BACKUP: ABOUT {Readable.Bytes(totalBytes).ToUpperInvariant()}";

        // The "+ Y MB IF YOU ADD THE PLUG-IN FILES" figure is the size of the tiers that are NOT
        // in a backup today - the honest answer to "what would turn this on cost".
        var notIncluded = Rows.Where(r => !r.Enabled && r.SizeBytes > 0).Sum(r => r.SizeBytes);
        IfYouAddLabel = notIncluded > 0
            ? $"+ {Readable.Bytes(notIncluded).ToUpperInvariant()} IF YOU ADD THE PLUG-IN FILES"
            : string.Empty;
    }

    /// <summary>The four rows, top to bottom, for the group's grid.</summary>
    public ObservableCollection<WhatGoesInRow> Rows { get; }

    /// <summary>The stacked bar's segments, left to right, each a 0..1 fraction of the whole.</summary>
    public ObservableCollection<ProportionSegment> Segments { get; }

    /// <summary>Left mono label under the bar: "EACH BACKUP: ABOUT X MB".</summary>
    public string EachBackupLabel { get; }

    /// <summary>Right mono label under the bar, empty when nothing is left out.</summary>
    public string IfYouAddLabel { get; }

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
/// The tier toggles (<see cref="IncludePresets"/>, <see cref="IncludePluginFiles"/>) are bound
/// but LOCKED: they render off and unmovable (the "NOT BUILT YET" tiers) and a programmatic set
/// is rejected, because <see cref="BackupSettings"/> has no field for them yet - writing one
/// would be a setting nothing reads. They stay on screen so the backup does not look more
/// complete than it is.
/// </summary>
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
    private bool isHighContrast;

    public SettingsViewModel(
        BackupSettings settings,
        Func<BackupSettings, bool> save,
        WhereSettingsLiveModel whereSettingsLive,
        WhichWaveLinkModel? whichWaveLink = null)
    {
        this.save = save;
        persisted = settings;
        backupFolder = settings.StorePath;
        autoBackupEnabled = settings.AutoBackupEnabled;
        autoBackupKeepCount = settings.AutoBackupKeepCount;

        WhereSettingsLive = whereSettingsLive;
        WhichWaveLink = whichWaveLink;
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
            if (Set(ref autoBackupKeepCount, clamped))
                Commit(persisted with { AutoBackupKeepCount = clamped });
        }
    }

    /// <summary>Effect presets tier. Locked off until it is built; a set is rejected.</summary>
    public bool IncludePresets
    {
        get => false;
        set { /* not built yet - the toggle is unmovable */ }
    }

    /// <summary>The plug-in-files tier. Locked off until it is built; a set is rejected.</summary>
    public bool IncludePluginFiles
    {
        get => false;
        set { /* not built yet - the toggle is unmovable */ }
    }

    /// <summary>
    /// A backup today holds only the settings file, so the estimate is that file's size. When
    /// the preset and plug-in tiers are built this becomes their sum - recomputed, never a
    /// hard-coded percentage (Task 3's proportion bar reads this).
    /// </summary>
    public long EstimatedBackupBytes { get; internal set; }

    /// <summary>The WHERE THESE SETTINGS LIVE block. Read-only.</summary>
    public WhereSettingsLiveModel WhereSettingsLive { get; }

    /// <summary>
    /// The WHAT GOES IN A BACKUP section: rows, computed proportion bar and labels. Set by the
    /// app at construction from the sizes it has measured - the VM does not measure anything
    /// itself, so the projection stays pure and testable (Task 3).
    /// </summary>
    public WhatGoesInModel? WhatGoesIn { get; set; }

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
    public string FreeSpaceText =>
        FreeSpaceBytes is { } bytes ? $"{Readable.Bytes(bytes)} FREE" : string.Empty;

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
        WhichWaveLinkModel? whichWaveLink = null) =>
        new(settings, save, whereSettingsLive, whichWaveLink);
}
