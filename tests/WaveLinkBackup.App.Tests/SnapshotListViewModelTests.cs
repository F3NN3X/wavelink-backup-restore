using System.Text;
using WaveLinkBackup.App.Hosting;
using WaveLinkBackup.App.ViewModels;
using WaveLinkBackup.Core.Analysis;
using WaveLinkBackup.Core.Snapshots;
using WaveLinkBackup.Core.Tests.Fakes;

namespace WaveLinkBackup.App.Tests;

/// <summary>
/// The list: grouping, search and the four things it can be showing. Every count and every
/// string is 07-search.md's or README's own.
/// </summary>
public sealed class SnapshotListViewModelTests
{
    private const string StorePath = @"C:\store";

    private static byte[] SettingsFor(params string[] inputs)
    {
        var entries = inputs.Select((n, i) =>
            $"\"K{i}\":{{\"InputName\":\"{n}\",\"AudioPluginConfigurations\":[]}}");

        return Encoding.UTF8.GetBytes(
            $"{{\"MixerConfiguration\":{{\"InputSettings\":{{{string.Join(",", entries)}}}}}}}");
    }

    private sealed class Rig
    {
        // Created up front, and empty: "no backups yet" and "the folder is gone" are different
        // states, and a rig that could not tell them apart would make the empty-store test pass
        // for the wrong reason.
        public FakeFileSystem Fs { get; } = Created();

        private static FakeFileSystem Created()
        {
            var fs = new FakeFileSystem();
            fs.CreateDirectory(StorePath);

            return fs;
        }

        public FakeClock Clock { get; } = new() { UtcNow = new DateTimeOffset(2026, 8, 15, 23, 7, 0, TimeSpan.Zero) };

        public SnapshotStore Store => new(Fs, Clock, StorePath);

        public SnapshotListViewModel List() =>
            new(Store, new HealthProbe(Store, Fs, Clock), Fs, Clock) { Marshal = action => action() };

        public void Add(string name, DateTimeOffset at, params string[] inputs)
        {
            Clock.UtcNow = at;

            var bytes = SettingsFor(inputs.Length == 0
                ? ["Wave Mic 1", "Voice", "Browser", "Game", "System"]
                : inputs);

            Store.Write(bytes, SettingsAnalysis.Analyse(bytes).Value, SnapshotTrigger.Manual, name);
        }
    }

    private static Rig Store14()
    {
        var rig = new Rig();
        var start = new DateTimeOffset(2026, 8, 15, 22, 0, 0, TimeSpan.Zero);

        rig.Add("Auto", start);
        rig.Add("Before restore", start.AddDays(-4).AddMinutes(3), "Elgato Wave:3", "System");
        rig.Add("Before 3.3 beta", start.AddDays(-4));
        rig.Add("Full rig + plugins", start.AddDays(-11));

        for (var i = 0; i < 10; i++) rig.Add($"Spare {i}", start.AddDays(-20 - i));

        rig.Clock.UtcNow = new DateTimeOffset(2026, 8, 15, 23, 7, 0, TimeSpan.Zero);

        return rig;
    }

    // -- loading and grouping ---------------------------------------------------------------

    [Fact]
    public void Refreshing_loads_every_snapshot()
    {
        var list = Store14().List();

        list.Refresh();

        Assert.Equal(14, list.TotalCount);
        Assert.Equal(ListState.Loaded, list.State);
    }

    // "Newest group first, newest row first inside a group." - README.
    //
    // Deviation from the brief: the header for the second group is computed via
    // Readable.DayGroup rather than the brief's literal "TUE 11 AUG". The brief's snapshots sit
    // at 22:00-22:03 UTC, so a machine whose local offset is UTC+2 or later (this box, W. Europe
    // Standard Time, is UTC+2 under August DST) rolls that instant into the next local day -
    // "WED 12 AUG" here, not "TUE 11 AUG". Readable.DayGroup's own format is covered by
    // ReadableTests; this test's job is grouping and ordering, not pinning a weekday string to
    // an assumed UTC-only host.
    [Fact]
    public void Groups_run_newest_first_and_so_do_the_rows_inside_them()
    {
        var rig = Store14();
        var list = rig.List();

        list.Refresh();

        Assert.Equal("TODAY", list.Groups[0].Header);
        Assert.Equal("Auto", list.Groups[0].Rows[0].Name);

        var second = list.Groups[1];
        var now = rig.Clock.UtcNow.ToLocalTime();
        Assert.Equal(Readable.DayGroup(second.Rows[0].TakenAt, now), second.Header);
        Assert.Equal(["Before restore", "Before 3.3 beta"], second.Rows.Select(r => r.Name));
    }

    // 02: "Damaged rows stay in date order. Do not sort them to the bottom without asking; a
    // user looking for a specific date needs to find it where they expect."
    [Fact]
    public void A_damaged_row_stays_where_its_date_puts_it()
    {
        var rig = Store14();
        var list = rig.List();

        list.Refresh();

        var damaged = list.Groups[1].Rows[1];
        damaged.ApplyVerdict(new HealthVerdict(SnapshotHealth.Damaged, 1, 1, rig.Clock.UtcNow));

        list.Refresh();

        Assert.Equal("Before 3.3 beta", list.Groups[1].Rows[1].Name);
    }

    // The collapsed row's slots are amber because the store's own high-water mark is five.
    [Fact]
    public void The_high_water_mark_comes_from_the_store_not_from_a_constant()
    {
        var list = Store14().List();

        list.Refresh();

        var collapsed = list.Groups[1].Rows.Single(r => r.Name == "Before restore");
        var full = list.Groups[1].Rows.Single(r => r.Name == "Before 3.3 beta");

        Assert.Equal(SlotKind.Generic, collapsed.Slots[0].Kind);
        Assert.Equal(SlotKind.Named, full.Slots[0].Kind);
    }

    // -- search -----------------------------------------------------------------------------

    [Fact]
    public void A_query_filters_the_list_and_keeps_the_groups()
    {
        var list = Store14().List();
        list.Refresh();

        list.Query = "beta";

        Assert.Equal(1, list.MatchCount);
        Assert.Equal(14, list.TotalCount);
        Assert.Equal("Before 3.3 beta", list.Groups.Single().Rows.Single().Name);
    }

    [Fact]
    public void A_match_is_marked_in_the_row_s_name()
    {
        var list = Store14().List();
        list.Refresh();

        list.Query = "beta";

        var segments = list.Groups[0].Rows[0].NameSegments;

        Assert.Contains(segments, s => s.IsMatch && s.Text == "beta");
    }

    // 07: status strip left reads "3 OF 14 MATCH \"BETA\"".
    [Fact]
    public void The_status_strip_reports_the_match_count()
    {
        var list = Store14().List();
        list.Refresh();

        list.Query = "spare";

        Assert.Equal("10 OF 14 MATCH \"SPARE\"", list.MatchSummary);
    }

    // 07: footer "SHOWING 3 OF 14 · 11 HIDDEN BY THE SEARCH", right "Show all 14".
    [Fact]
    public void The_footer_says_what_is_hidden_and_offers_to_show_it()
    {
        var list = Store14().List();
        list.Refresh();

        list.Query = "beta";

        Assert.Equal("SHOWING 1 OF 14 · 13 HIDDEN BY THE SEARCH", list.SearchFooter);
        Assert.Equal("Show all 14", list.ShowAllLabel);
    }

    [Fact]
    public void With_no_query_there_is_no_footer_and_no_summary()
    {
        var list = Store14().List();

        list.Refresh();

        Assert.Null(list.SearchFooter);
        Assert.Equal(string.Empty, list.MatchSummary);
    }

    [Fact]
    public void Clearing_the_search_returns_the_full_list()
    {
        var list = Store14().List();
        list.Refresh();
        list.Query = "beta";

        list.ClearSearch();

        Assert.Equal(string.Empty, list.Query);
        Assert.Equal(14, list.MatchCount);
        Assert.Equal(ListState.Loaded, list.State);
    }

    // -- no results, which is NOT the empty state ------------------------------------------

    [Fact]
    public void A_query_that_matches_nothing_is_its_own_state()
    {
        var list = Store14().List();
        list.Refresh();

        list.Query = "wave:3";

        Assert.Equal(ListState.NoResults, list.State);
        Assert.Equal("0 OF 14 MATCH \"WAVE:3\"", list.MatchSummary);
    }

    // 07's body copy, and the sentence that makes the promise the search must keep.
    [Fact]
    public void No_results_names_the_query_and_says_search_looks_at_names_only()
    {
        var list = Store14().List();
        list.Refresh();

        list.Query = "wave:3";

        Assert.Equal("No backup is called \"wave:3\".", list.NoResultsTitle);
        Assert.Equal("14 BACKUPS ARE HERE · SEARCH LOOKS AT NAMES ONLY", list.NoResultsDetail);
    }

    // Fix 6: the noun pluralises ("BACKUPS") but the verb has to agree too - "1 BACKUP ARE HERE"
    // reads as broken English. Only the 14-backup case was pinned before; this exercises the
    // one-backup singular.
    [Fact]
    public void No_results_detail_agrees_the_verb_with_a_single_backup()
    {
        var rig = new Rig();
        rig.Add("Only one", new DateTimeOffset(2026, 8, 15, 22, 0, 0, TimeSpan.Zero));
        var list = rig.List();
        list.Refresh();

        list.Query = "wave:3";

        Assert.Equal("1 BACKUP IS HERE · SEARCH LOOKS AT NAMES ONLY", list.NoResultsDetail);
    }

    // Fix 7: SnapshotSearch.Filter (Core) treats a whitespace-only query as no filter at all, but
    // the strip and footer gated only on Length == 0 - so a single space showed "14 OF 14 MATCH
    // \" \"" instead of going back to the empty-query copy. Core is untouched; only the two
    // gates in this file change to Trim().Length == 0.
    [Fact]
    public void A_whitespace_only_query_is_treated_as_no_query()
    {
        var list = Store14().List();
        list.Refresh();

        list.Query = " ";

        Assert.Equal(string.Empty, list.MatchSummary);
        Assert.Null(list.SearchFooter);
        Assert.Null(list.ShowAllLabel);
    }

    [Fact]
    public void An_empty_store_is_empty_and_not_no_results()
    {
        var list = new Rig().List();

        list.Refresh();

        Assert.Equal(ListState.Empty, list.State);
        Assert.Equal(0, list.TotalCount);
    }

    // 08's error 12 is a later session; the strip saying so is 10-decisions section 6, which is
    // pinned now.
    [Fact]
    public void A_store_folder_that_is_not_there_is_its_own_state()
    {
        var fs = new FakeFileSystem();
        var clock = new FakeClock();
        var gone = new SnapshotStore(fs, clock, @"E:\gone");

        var missing = new SnapshotListViewModel(gone, new HealthProbe(gone, fs, clock), fs, clock)
        {
            Marshal = action => action(),
        };

        missing.Refresh();

        Assert.Equal(ListState.FolderMissing, missing.State);
    }

    // -- selection --------------------------------------------------------------------------

    [Fact]
    public void Nothing_is_selected_after_a_load()
    {
        var list = Store14().List();

        list.Refresh();

        Assert.Null(list.Selected);
    }

    [Fact]
    public void Selecting_a_row_marks_it_and_unmarks_the_last_one()
    {
        var list = Store14().List();
        list.Refresh();

        var first = list.Groups[0].Rows[0];
        var second = list.Groups[1].Rows[0];

        list.Selected = first;
        list.Selected = second;

        Assert.False(first.IsSelected);
        Assert.True(second.IsSelected);
    }

    // Back up now inserts a row at the top of TODAY and selects it (README). Selection is by id
    // because the row objects are rebuilt by the refresh that follows.
    [Fact]
    public void A_selection_survives_a_refresh_by_id()
    {
        var list = Store14().List();
        list.Refresh();

        var id = list.Groups[1].Rows[0].Id;
        list.Select(id);

        list.Refresh();

        Assert.Equal(id, list.Selected?.Id);
    }

    [Fact]
    public void Selecting_an_id_that_is_gone_clears_the_selection()
    {
        var list = Store14().List();
        list.Refresh();

        list.Select("no-such-snapshot");

        Assert.Null(list.Selected);
    }

    // Not in the brief - review finding: the Query setter is copied from the brief's Step 3 and
    // never re-synced Selected the way Refresh() does. Rebuild() always constructs fresh row
    // objects, so without this fix, typing into search leaves Selected pointing at a detached
    // row (IsSelected true, but no longer in Groups) while the fresh row for the same snapshot
    // defaults to IsSelected false - the bottom bar Task 10 binds to Selected would show an
    // orphan.
    [Fact]
    public void Selecting_a_row_then_searching_keeps_it_selected_if_it_still_matches()
    {
        var list = Store14().List();
        list.Refresh();

        var row = list.Groups.SelectMany(g => g.Rows).Single(r => r.Name == "Before 3.3 beta");
        list.Select(row.Id);

        list.Query = "beta";

        var current = list.Groups.Single().Rows.Single();
        Assert.Same(current, list.Selected);
        Assert.True(current.IsSelected);
    }

    // -- the probe --------------------------------------------------------------------------

    [Fact]
    public async Task Refreshing_asynchronously_verifies_every_row()
    {
        var rig = Store14();
        var list = rig.List();

        await list.RefreshAsync();

        Assert.All(list.Groups.SelectMany(g => g.Rows), r => Assert.NotEqual(SnapshotHealth.Damaged, r.Health));
    }

    [Fact]
    public async Task A_tampered_snapshot_turns_its_row_damaged()
    {
        var rig = Store14();
        var list = rig.List();

        list.Refresh();

        var victim = rig.Store.List().Single(s => s.Manifest.DisplayName == "Auto");
        rig.Fs.WriteBytes(victim.SettingsPath, "tampered"u8.ToArray());

        await list.RefreshAsync();

        Assert.Equal(SnapshotHealth.Damaged, list.Groups[0].Rows[0].Health);
    }

    // Not in the brief - added because the brief's own RefreshAsync only guards staleness
    // implicitly: its report callback closes over the id->row dictionary built right after ITS
    // Refresh(), so a verdict that lands late can only ever touch the row objects from that
    // generation, never the ones a LATER Refresh() swapped in. This test proves that: it lets a
    // plain Refresh() run in the middle of an in-flight probe (same trick HealthProbeTests uses
    // to cancel mid-probe - triggering the next step from inside a report callback, so no sleep
    // or polling is needed) and checks that the row the user is actually looking at afterwards
    // never saw the stale DAMAGED verdict, even though the detached row from the replaced
    // generation did. Delete the dictionary-per-refresh capture and look rows up live against
    // `Groups` instead, and this goes red.
    [Fact]
    public async Task A_verdict_for_a_row_a_later_refresh_already_replaced_is_ignored()
    {
        var rig = Store14();
        var list = rig.List();

        var victim = rig.Store.List().Single(s => s.Manifest.DisplayName == "Before restore");
        rig.Fs.WriteBytes(victim.SettingsPath, "tampered"u8.ToArray());

        SnapshotRowViewModel? staleRow = null;
        var reportCount = 0;

        list.Marshal = action =>
        {
            reportCount++;

            // The FIRST report to arrive is for "Auto" (today, probed first). Before applying
            // it, grab the CURRENT row for the victim - the generation the probe's own
            // dictionary is bound to - then replace it with a plain Refresh(), exactly as an F5
            // arriving mid-probe would. "Before restore"'s own report, for the now-detached
            // object, comes later in this same loop.
            if (reportCount == 1)
            {
                staleRow = list.Groups.SelectMany(g => g.Rows).Single(r => r.Id == victim.Id);
                list.Refresh();
            }

            action();
        };

        await list.RefreshAsync();

        // The stale verdict WAS produced and delivered - to the row it belongs to, and no
        // further.
        Assert.Equal(SnapshotHealth.Damaged, staleRow!.Health);

        // The row the user is actually looking at, from the Refresh() that replaced it, was
        // never touched by that late verdict.
        var current = list.Groups.SelectMany(g => g.Rows).Single(r => r.Id == victim.Id);
        Assert.NotSame(staleRow, current);
        Assert.NotEqual(SnapshotHealth.Damaged, current.Health);
    }

    [Fact]
    public void Total_bytes_is_the_whole_store_not_the_filtered_view()
    {
        var list = Store14().List();
        list.Refresh();

        var total = list.TotalBytes;

        list.Query = "beta";

        Assert.Equal(total, list.TotalBytes);
    }

    // -- disposal -----------------------------------------------------------------------------

    // Not in the brief - IDisposable is a produced interface with nothing exercising it. Pinning
    // it rather than leaving it correct-by-inspection only.
    [Fact]
    public void Disposing_twice_does_not_throw()
    {
        var list = Store14().List();
        list.Refresh();

        list.Dispose();

        Assert.Null(Record.Exception(list.Dispose));
    }

    [Fact]
    public void Disposing_before_any_refresh_does_not_throw()
    {
        var list = Store14().List();

        Assert.Null(Record.Exception(list.Dispose));
    }
}

/// <summary>
/// In-place rename, plan 6 task 2 step 3: the command's state transitions against a stubbed store.
/// The row holds the draft and the cue; the list owns the store. Both are tested here - the row's
/// machine in isolation (no window), and the full commit path through the real SnapshotStore over
/// FakeFileSystem, which is this codebase's "stubbed store".
/// </summary>
public sealed class RenameCommandTests
{
    private const string StorePath = @"C:\store";

    private static byte[] SettingsFor(params string[] inputs)
    {
        var entries = inputs.Select((n, i) =>
            $"\"K{i}\":{{\"InputName\":\"{n}\",\"AudioPluginConfigurations\":[]}}");

        return Encoding.UTF8.GetBytes(
            $"{{\"MixerConfiguration\":{{\"InputSettings\":{{{string.Join(",", entries)}}}}}}}");
    }

    private sealed class Rig
    {
        public FakeFileSystem Fs { get; } = new();
        public FakeClock Clock { get; } = new() { UtcNow = new DateTimeOffset(2026, 8, 15, 23, 7, 0, TimeSpan.Zero) };
        public SnapshotStore Store => new(Fs, Clock, StorePath);

        public SnapshotListViewModel List() =>
            new(Store, new HealthProbe(Store, Fs, Clock), Fs, Clock) { Marshal = action => action() };

        public void Add(string name, DateTimeOffset at)
        {
            Clock.UtcNow = at;
            var bytes = SettingsFor(["Wave Mic 1", "Voice", "Browser", "Game", "System"]);
            Store.Write(bytes, SettingsAnalysis.Analyse(bytes).Value, SnapshotTrigger.Manual, name);
        }

        public (SnapshotListViewModel List, SnapshotRowViewModel Row) OneRow()
        {
            Fs.CreateDirectory(StorePath);
            Add("Before 3.3 beta", new DateTimeOffset(2026, 8, 15, 22, 0, 0, TimeSpan.Zero));
            Clock.UtcNow = new DateTimeOffset(2026, 8, 15, 23, 7, 0, TimeSpan.Zero);

            var list = List();
            list.Refresh();

            return (list, list.Groups[0].Rows[0]);
        }
    }

    // -- the row's edit machine, in isolation -------------------------------------------------

    [Fact]
    public void BeginEdit_seeds_the_draft_from_the_stored_name()
    {
        var (_, row) = new Rig().OneRow();

        row.BeginEdit();

        Assert.True(row.IsEditing);
        Assert.Equal("Before 3.3 beta", row.DraftName);
        Assert.Null(row.RenameError);
    }

    [Fact]
    public void A_damaged_row_cannot_begin_editing()
    {
        var (_, row) = new Rig().OneRow();
        row.ApplyVerdict(new HealthVerdict(SnapshotHealth.Damaged, 1, 1, DateTimeOffset.UtcNow));

        row.BeginEdit();

        Assert.False(row.IsEditing);
    }

    [Fact]
    public void A_valid_commit_persists_and_clears_edit()
    {
        var (list, row) = new Rig().OneRow();

        row.BeginEdit();
        row.DraftName = "After the fix";

        Assert.True(list.CommitRename(row));

        Assert.False(row.IsEditing);
        Assert.Null(row.RenameError);
        Assert.True(row.CanRename);
    }

    [Fact]
    public void A_valid_commit_is_visible_to_a_fresh_refresh()
    {
        var (list, row) = new Rig().OneRow();

        row.BeginEdit();
        row.DraftName = "After the fix";
        Assert.True(list.CommitRename(row));

        list.Refresh();

        var renamed = list.Groups[0].Rows.Single(r => r.Id == row.Id);
        Assert.Equal("After the fix", renamed.Name);
    }

    [Fact]
    public void Escape_reverts_to_the_stored_name()
    {
        var (list, row) = new Rig().OneRow();

        row.BeginEdit();
        row.DraftName = "Something else entirely";
        row.CancelEdit();

        Assert.False(row.IsEditing);
        Assert.Null(row.RenameError);
        Assert.True(row.CanRename);

        // The store never saw the draft.
        list.Refresh();
        Assert.Equal("Before 3.3 beta", list.Groups[0].Rows.Single(r => r.Id == row.Id).Name);
    }

    [Fact]
    public void An_empty_draft_stays_in_edit_and_shows_the_cue()
    {
        var (list, row) = new Rig().OneRow();

        row.BeginEdit();
        row.DraftName = "   ";

        Assert.False(list.CommitRename(row));
        Assert.True(row.IsEditing);
        Assert.Equal("A name can't be empty.", row.RenameError);
    }

    [Fact]
    public void An_illegal_character_stays_in_edit_and_names_the_character()
    {
        var (list, row) = new Rig().OneRow();

        row.BeginEdit();
        row.DraftName = "Bad/name";

        Assert.False(list.CommitRename(row));
        Assert.True(row.IsEditing);
        Assert.Equal("A name can't contain '/'.", row.RenameError);
    }

    [Fact]
    public void A_failed_store_commit_stays_in_edit_and_shows_the_store_reason()
    {
        var (list, row) = new Rig().OneRow();

        row.BeginEdit();
        row.DraftName = "Valid but the store refuses";

        // The commit path is injectable: a failing store reports its reason, and the row keeps
        // the draft rather than yanking the user out of the box they are typing in.
        Assert.False(row.TryCommitEdit(_ => "The folder is read-only."));

        Assert.True(row.IsEditing);
        Assert.Equal("The folder is read-only.", row.RenameError);
    }

    [Fact]
    public void A_valid_commit_after_a_failed_one_recovers()
    {
        var (list, row) = new Rig().OneRow();

        row.BeginEdit();
        row.DraftName = "First attempt";
        Assert.False(row.TryCommitEdit(_ => "The folder is read-only."));

        // The cue clears and the commit lands on retry.
        Assert.True(list.CommitRename(row));

        Assert.False(row.IsEditing);
        Assert.Null(row.RenameError);
    }

    [Fact]
    public void Entering_edit_mode_disables_the_rename_action_until_it_ends()
    {
        var (_, row) = new Rig().OneRow();

        Assert.True(row.CanRename);

        row.BeginEdit();
        Assert.False(row.CanRename);

        row.CancelEdit();
        Assert.True(row.CanRename);
    }
}
