using System.Collections.ObjectModel;
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
    private readonly List<Snapshot> all = [];

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

    public ObservableCollection<DateGroup> Groups { get; } = [];

    public int TotalCount => all.Count;

    public int MatchCount { get; private set; }

    public int HiddenCount => TotalCount - MatchCount;

    /// <summary>The whole store, never the filtered view - the bottom bar counts backups, not results.</summary>
    public long TotalBytes => all.Sum(s => s.Manifest.Files.Values.Sum(f => f.SizeBytes));

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

    /// <summary>Reads the store and rebuilds the rows. F5, and every load.</summary>
    public void Refresh()
    {
        var selectedId = selected?.Id;

        all.Clear();
        all.AddRange(store.List());

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
    /// Selection is by ID, not by object: Refresh builds new rows, and "Back up now inserts a
    /// row at the top of TODAY and selects it" needs a name for the thing to select.
    /// </summary>
    public void Select(string id) =>
        Selected = Groups
            .SelectMany(g => g.Rows)
            .FirstOrDefault(r => string.Equals(r.Id, id, StringComparison.Ordinal));

    private void Rebuild()
    {
        var now = clock.UtcNow.ToLocalTime();

        // The high-water mark is the STORE's, not the filtered view's - hiding the full rig
        // behind a search must not make a collapsed row look whole.
        var peak = all.Count == 0 ? 0 : all.Max(s => s.Manifest.InputCount);

        var matched = SnapshotSearch.Filter(all, query);
        MatchCount = matched.Count;

        Groups.Clear();

        foreach (var group in matched
            .OrderByDescending(s => s.Manifest.CreatedUtc)
            .GroupBy(s => s.Manifest.CreatedUtc.ToLocalTime().Date))
        {
            Groups.Add(new DateGroup(
                Readable.DayGroup(new DateTimeOffset(group.Key, now.Offset), now),
                [.. group.Select(s => new SnapshotRowViewModel(s, peak, now, NullIfEmpty(query)))]));
        }

        // Asked here, on every refresh, rather than of the store: "is the folder there" is a
        // question about a MOMENT, and a stored answer would be stale before anyone read it.
        // It is also why this needs no Core change - nothing in this plan touches Core.
        State = !fileSystem.DirectoryExists(store.StorePath) ? ListState.FolderMissing
            : all.Count == 0 ? ListState.Empty
            : matched.Count == 0 ? ListState.NoResults
            : ListState.Loaded;

        foreach (var property in (string[])
        [
            nameof(TotalCount), nameof(MatchCount), nameof(HiddenCount), nameof(TotalBytes),
            nameof(MatchSummary), nameof(SearchFooter), nameof(ShowAllLabel),
            nameof(NoResultsTitle), nameof(NoResultsDetail),
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
