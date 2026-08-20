using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using System.Globalization;
using WaveLinkBackup.App.Hosting;
using WaveLinkBackup.Core.Abstractions;
using WaveLinkBackup.Core.Snapshots;

namespace WaveLinkBackup.App.ViewModels;

/// <summary>What the list area is showing. Four states, and they are not interchangeable.</summary>
public enum ListState
{
    Loaded,

    /// <summary>
    /// A search matched nothing. NOT the empty state - 07 is explicit that the status strip,
    /// the column header and the count stay on screen precisely so an empty RESULT never looks
    /// like an empty APP.
    /// </summary>
    NoResults,

    /// <summary>No backups at all. Screen 4's first run belongs here and is a later session.</summary>
    Empty,

    /// <summary>The store folder is not there. Error 12's full screen is a later session.</summary>
    FolderMissing,
}

/// <summary>A date-group header and the rows under it.</summary>
public sealed record DateGroup(string Header, IReadOnlyList<SnapshotRowViewModel> Rows);

/// <summary>
/// The list: what is in the store, grouped by day, filtered by the search field, with one row
/// selected.
///
/// The store is read on Refresh and NOT held open - SnapshotStore.List() re-reads the manifests
/// each time, which is what makes F5 mean something. Health arrives separately, from the probe,
/// on a background thread.
/// </summary>
public sealed class SnapshotListViewModel(
    SnapshotStore store, HealthProbe probe, IFileSystem fileSystem, IClock clock)
    : ObservableObject, IDisposable
{
    // Mutable on purpose: error 12's "Choose a folder…" re-points the store at a new path and
    // every subsequent read (this list, the tray readout, the next backup) must follow it. The
    // store is constructed once in App.Compose; changing its target is cheaper than rebuilding
    // the whole composition graph on a folder change, and nothing else holds a second copy of
    // the path that would go stale.
    private SnapshotStore currentStore = store;

    private readonly List<Snapshot> all = [];

    /// <summary>
    /// The grouped view over <see cref="Rows"/>. Built once and never replaced: the window binds
    /// to it, and swapping the instance would silently drop that binding.
    /// </summary>
    private ListCollectionView? view;

    private CancellationTokenSource? probing;
    private string query = string.Empty;
    private SnapshotRowViewModel? selected;
    private ListState state = ListState.Empty;

    /// <summary>
    /// How a verdict gets from the probe's thread back to the UI thread. Set by the window to
    /// Dispatcher.Invoke; the tests set it to run inline, which is what makes the probe
    /// assertable without a dispatcher.
    /// </summary>
    public Action<Action>? Marshal { get; set; }

    /// <summary>
    /// Grouping lives here rather than in the XAML's own CollectionViewSource so a test can
    /// assert it without a window — which is the whole reason the old shape's defect went
    /// unnoticed for a phase.
    /// </summary>
    private ListCollectionView BuildView()
    {
        var built = new ListCollectionView(Rows);
        built.GroupDescriptions.Add(
            new PropertyGroupDescription(nameof(SnapshotRowViewModel.GroupHeader)));

        return built;
    }

    /// <summary>
    /// Every matching row, newest first, in ONE flat collection.
    ///
    /// This is the list the window binds to (through <see cref="View"/>), and it is what replaced
    /// one <see cref="System.Windows.Controls.ListBox"/> per date group. That shape gave native
    /// row selection but made the list several Selectors, so a selection could not span them and
    /// arrow keys stopped dead at every date boundary (technical-debt.md §4.14). A single Selector
    /// is single-select and continuous by construction, which deletes both problems rather than
    /// managing them.
    /// </summary>
    public ObservableCollection<SnapshotRowViewModel> Rows { get; } = [];

    /// <summary>
    /// <see cref="Rows"/> grouped by <see cref="SnapshotRowViewModel.GroupHeader"/>, for the
    /// window's <c>GroupStyle</c>. The grouping is the SAME derivation as before — it just lives
    /// in a CollectionView instead of in a second collection of collections.
    /// </summary>
    public ListCollectionView View => view ??= BuildView();

    /// <summary>
    /// The date groups, still. Nothing in the app renders this any more — the view groups
    /// <see cref="View"/> instead — but "TODAY holds these three rows" is the thing several
    /// suites are actually about, and asserting it against a projection is clearer than
    /// walking a CollectionView's group objects.
    /// </summary>
    public ObservableCollection<DateGroup> Groups { get; } = [];

    public int TotalCount => all.Count;

    public int MatchCount { get; private set; }

    public int HiddenCount => TotalCount - MatchCount;

    /// <summary>The whole store, never the filtered view - the bottom bar counts backups, not results.</summary>
    public long TotalBytes => all.Sum(s => s.Manifest.TotalSizeBytes);

    public ListState State
    {
        get => state;
        private set => Set(ref state, value);
    }

    public string Query
    {
        get => query;
        set
        {
            if (!Set(ref query, value ?? string.Empty)) return;

            // Mirrors Refresh(): Rebuild() always constructs fresh row objects, so without this
            // Selected would keep pointing at a detached row that is no longer in Groups.
            var selectedId = selected?.Id;

            Rebuild();

            if (selectedId is not null) Select(selectedId);
        }
    }

    public SnapshotRowViewModel? Selected
    {
        get => selected;
        set
        {
            if (ReferenceEquals(selected, value)) return;

            if (selected is not null) selected.IsSelected = false;
            selected = value;
            if (selected is not null) selected.IsSelected = true;

            Raise();
        }
    }

    /// <summary>07: `3 OF 14 MATCH "BETA"`. Empty with no query - the strip says other things then.</summary>
    public string MatchSummary => query.Trim().Length == 0
        ? string.Empty
        : $"{MatchCount} OF {TotalCount} MATCH \"{query.ToUpper(CultureInfo.InvariantCulture)}\"";

    /// <summary>07: `SHOWING 3 OF 14 · 11 HIDDEN BY THE SEARCH`.</summary>
    public string? SearchFooter => query.Trim().Length == 0 || State != ListState.Loaded
        ? null
        : $"SHOWING {MatchCount} OF {TotalCount} · {HiddenCount} HIDDEN BY THE SEARCH";

    public string? ShowAllLabel => SearchFooter is null ? null : $"Show all {TotalCount}";

    /// <summary>07's line 1. Lower case and in quotes, because it echoes what the user typed.</summary>
    public string NoResultsTitle => $"No backup is called \"{query}\".";

    /// <summary>
    /// 07's line 2. "SEARCH LOOKS AT NAMES ONLY" is a promise, and SnapshotSearch keeps it -
    /// widening the filter later would make this copy a lie.
    /// </summary>
    public string NoResultsDetail => TotalCount == 1
        ? "1 BACKUP IS HERE · SEARCH LOOKS AT NAMES ONLY"
        : $"{TotalCount} BACKUPS ARE HERE · SEARCH LOOKS AT NAMES ONLY";

    // Error 12's full screen (08-settings-persistence.md "The folder is gone"). These are the
    // designed copy for the centred body; they render only when State == FolderMissing, and
    // every one of them is asserted from a table in MainWindowListStateTests.

    /// <summary>The h2: Rubik 500 26px, --wl-strong.</summary>
    public string FolderMissingTitle => "The backup folder can't be used";

    /// <summary>
    /// The reason. Max 430px, centred, --wl-text. Explains the three ways this happens and makes
    /// the one claim that matters: nothing has been deleted.
    /// </summary>
    public string FolderMissingBody =>
        "It isn't there any more — a drive that isn't plugged in, a folder that moved, or one " +
        "this account can't write to. Your backups are wherever that folder went; nothing has " +
        "been deleted.";

    /// <summary>The mono path line: the store path exactly as configured.</summary>
    public string FolderMissingPath => currentStore.StorePath;

    /// <summary>
    /// Re-points the store at a new folder. Called from error 12's "Choose a folder…" and "Use
    /// the default folder" (App.SetStorePath / App.UseDefaultStore) so that the list, the tray
    /// readout and the next backup all follow the user's choice rather than the stale path. The
    /// caller persists the new path to settings; this only swaps the in-memory target.
    /// </summary>
    public void SetStorePath(string path) => currentStore = new SnapshotStore(fileSystem, clock, path);

    /// <summary>
    /// The last-seen line, mono at 75%. Computed from the newest snapshot's manifest - the most
    /// recent thing we know was written into that folder. Absent when there is no snapshot to
    /// date (an empty or unreadable store), in which case the design prints nothing rather than
    /// guess a time.
    /// </summary>
    public string? FolderMissingLastSeen
    {
        get
        {
            if (all.Count == 0) return null;

            var newest = all.Max(s => s.Manifest.CreatedUtc).ToLocalTime();
            var day = newest.ToString("d MMM", CultureInfo.CurrentCulture).ToUpper(CultureInfo.CurrentCulture);
            var time = newest.ToString("HH:mm", CultureInfo.InvariantCulture);
            var count = TotalCount == 1 ? "1 BACKUP" : $"{TotalCount} BACKUPS";

            return $"LAST SEEN {day} {time} · {count} THEN";
        }
    }

    /// <summary>Reads the store and rebuilds the rows. F5, and every load.</summary>
    public void Refresh()
    {
        var selectedId = selected?.Id;

        all.Clear();
        all.AddRange(currentStore.List());

        Rebuild();

        if (selectedId is not null) Select(selectedId);
    }

    /// <summary>
    /// Refresh, then verify everything off the UI thread. The rows are on screen before the
    /// first hash starts, which is the whole point of splitting them.
    /// </summary>
    public async Task RefreshAsync()
    {
        Refresh();

        // An F5 while a probe is running must not leave the old run writing verdicts into the
        // rows that replaced them.
        probing?.Cancel();
        probing?.Dispose();
        probing = new CancellationTokenSource();

        var rows = Groups.SelectMany(g => g.Rows).ToDictionary(r => r.Id, StringComparer.Ordinal);

        // A frozen copy, not the live field: `all` is mutated in place by the next Refresh(),
        // and HealthProbe.ProbeAsync is mid-foreach over whatever list it was handed - an F5
        // arriving mid-probe would otherwise throw "collection was modified" rather than simply
        // being ignored by the rows dictionary above.
        var snapshotsToProbe = all.ToList();

        await probe.ProbeAsync(
            snapshotsToProbe,
            (id, verdict) => (Marshal ?? (a => a()))(() =>
            {
                if (rows.TryGetValue(id, out var row)) row.ApplyVerdict(verdict);
            }),
            probing.Token);
    }

    public void ClearSearch() => Query = string.Empty;

    /// <summary>
    /// The store half of in-place rename (README Interactions). Validates the draft against the
    /// row's own rule, then hands it to the store. On success the list re-reads so the renamed row
    /// - and every readout that prints its name - picks up the new one; on failure the row stays
    /// in edit mode and shows the reason. Returns true when the commit landed.
    /// </summary>
    public bool CommitRename(SnapshotRowViewModel row) =>
        row.TryCommitEdit(name =>
        {
            var result = currentStore.Rename(row.Id, name);
            if (!result.IsSuccess) return result.Error!.Message;

            Refresh();
            Select(row.Id);
            return null;
        });

    /// <summary>
    /// The store half of two-stage delete (05-delete-dialogs.md): move the snapshot into
    /// <c>.trash</c> inside the backup folder - a plain directory move, nothing destroyed. On
    /// success the list re-reads, and because <see cref="SnapshotStore.List"/> skips the trash
    /// folder, the row vanishes from the list, the search index and every count/size readout in
    /// one step; selection was on the deleted row, so it clears. Returns the store's reason when
    /// the move failed, null when it landed.
    /// </summary>
    public string? Delete(string id)
    {
        var result = currentStore.Delete(id);
        if (!result.IsSuccess) return result.Error!.Message;

        Refresh();
        return null;
    }

    /// <summary>
    /// The raw store snapshot behind an id, or null if it is no longer listed (it was deleted,
    /// or the list went stale). Exposed so the window can build the delete confirmation's model
    /// - which needs the manifest and the total count - without the view reaching into Core
    /// types itself. The trash folder is already excluded from <c>all</c>, so a trashed id never
    /// resolves here.
    /// </summary>
    public Snapshot? FindSnapshot(string id) =>
        all.FirstOrDefault(s => string.Equals(s.Id, id, StringComparison.Ordinal));

    /// <summary>
    /// Selection is by ID, not by object: Refresh builds new rows, and "Back up now inserts a
    /// row at the top of TODAY and selects it" needs a name for the thing to select.
    /// </summary>
    public void Select(string id) =>
        Selected = Rows.FirstOrDefault(r => string.Equals(r.Id, id, StringComparison.Ordinal));

    private void Rebuild()
    {
        var now = clock.UtcNow.ToLocalTime();

        // The high-water mark is the STORE's, not the filtered view's - hiding the full rig
        // behind a search must not make a collapsed row look whole.
        var peak = all.Count == 0 ? 0 : all.Max(s => s.Manifest.InputCount);

        var matched = SnapshotSearch.Filter(all, query);
        MatchCount = matched.Count;

        Groups.Clear();
        Rows.Clear();

        foreach (var group in matched
            .OrderByDescending(s => s.Manifest.CreatedUtc)
            .GroupBy(s => s.Manifest.CreatedUtc.ToLocalTime().Date))
        {
            var header = Readable.DayGroup(new DateTimeOffset(group.Key, now.Offset), now);

            // The header is carried BY each row rather than by a group object, because that is
            // what a CollectionView can group on - and it keeps one row's date and one row's
            // header from ever disagreeing.
            var rows = group
                .Select(s => new SnapshotRowViewModel(s, peak, now, NullIfEmpty(query)) { GroupHeader = header })
                .ToList();

            Groups.Add(new DateGroup(header, rows));

            foreach (var row in rows) Rows.Add(row);
        }

        // Asked here, on every refresh, rather than of the store: "is the folder there" is a
        // question about a MOMENT, and a stored answer would be stale before anyone read it.
        // It is also why this needs no Core change - nothing in this plan touches Core.
        State = !fileSystem.DirectoryExists(currentStore.StorePath) ? ListState.FolderMissing
            : all.Count == 0 ? ListState.Empty
            : matched.Count == 0 ? ListState.NoResults
            : ListState.Loaded;

        foreach (var property in (string[])
        [
            nameof(TotalCount), nameof(MatchCount), nameof(HiddenCount), nameof(TotalBytes),
            nameof(MatchSummary), nameof(SearchFooter), nameof(ShowAllLabel),
            nameof(NoResultsTitle), nameof(NoResultsDetail),
            nameof(FolderMissingTitle), nameof(FolderMissingBody), nameof(FolderMissingPath),
            nameof(FolderMissingLastSeen),
        ])
        {
            Raise(property);
        }
    }

    private static string? NullIfEmpty(string value) => value.Length == 0 ? null : value;

    public void Dispose()
    {
        probing?.Cancel();
        probing?.Dispose();
        probing = null;
    }
}
