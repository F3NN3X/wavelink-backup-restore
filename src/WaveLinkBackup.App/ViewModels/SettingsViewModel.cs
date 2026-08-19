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
    bool Visible);

/// <summary>
/// The WHERE THESE SETTINGS LIVE block: where the file is, how big it is, and the line that
/// keeps "a command-line flag overrides this for one run" honest. Read-only - nothing in the
/// dialog edits these.
/// </summary>
public sealed record WhereSettingsLiveModel(string FilePath, string SizeText);

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
