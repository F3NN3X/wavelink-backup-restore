using WaveLinkBackup.App.Hosting;
using WaveLinkBackup.App.ViewModels;
using WaveLinkBackup.Core.Snapshots;

namespace WaveLinkBackup.App.Tests;

/// <summary>
/// The row, which is the screen. Every string here is README's or 02's own.
/// </summary>
public sealed class SnapshotRowViewModelTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 15, 23, 7, 0, TimeSpan.Zero);

    private static readonly string[] Healthy =
        ["Wave Mic 1", "Voice", "Browser", "Game", "System"];

    private static Snapshot Snapshot(
        string name = "Before 3.3 beta",
        SnapshotTrigger trigger = SnapshotTrigger.Manual,
        string[]? inputs = null,
        bool suspect = false,
        long bytes = 12687769,
        DateTimeOffset? takenAt = null,
        string[]? tiers = null)
    {
        inputs ??= Healthy;

        return new Snapshot(
            Id: "2026-08-11_2136-abc12345",
            Directory: @"C:\store\2026-08-11_2136-abc12345",
            Manifest: new SnapshotManifest(
                SchemaVersion: SnapshotManifest.CurrentSchemaVersion,
                DisplayName: name,
                Notes: "",
                CreatedUtc: takenAt ?? new DateTimeOffset(2026, 8, 11, 21, 36, 0, TimeSpan.Zero),
                Trigger: trigger,
                SettingsSha256: "abc",
                WaveLinkVersion: "3.3.0.4108",
                InputCount: inputs.Length,
                InputNames: inputs,
                EffectCount: 17,
                EffectChannelCount: 4,
                HasDuplicateKeys: suspect,
                Tiers: tiers ?? ["settings", "presets"],
                Files: new Dictionary<string, SnapshotFile>(StringComparer.Ordinal)
                {
                    ["settings.json"] = new("abc", bytes),
                }));
    }

    private static SnapshotRowViewModel Row(
        Snapshot? snapshot = null, int peak = 5, string? query = null) =>
        new(snapshot ?? Snapshot(), peak, Now, query);

    // -- the five slots -------------------------------------------------------------------

    [Fact]
    public void There_are_always_five_slots()
    {
        Assert.Equal(5, Row().Slots.Count);
        Assert.Equal(5, Row(Snapshot(inputs: ["Elgato Wave:3", "System"])).Slots.Count);
    }

    [Fact]
    public void A_full_rig_reads_as_five_named_slots()
    {
        Assert.All(Row().Slots, s => Assert.Equal(SlotKind.Named, s.Kind));
    }

    // -- health ---------------------------------------------------------------------------

    [Fact]
    public void A_row_starts_whole_before_the_probe_answers()
    {
        Assert.Equal(SnapshotHealth.Whole, Row().Health);
    }

    // The manifest already knows this without hashing anything, so the amber row is right on
    // the first frame rather than a quarter second later.
    [Fact]
    public void A_snapshot_that_failed_validation_starts_suspect()
    {
        Assert.Equal(SnapshotHealth.Suspect, Row(Snapshot(suspect: true)).Health);
    }

    [Fact]
    public void A_verdict_can_turn_a_row_damaged()
    {
        var row = Row();

        row.ApplyVerdict(new HealthVerdict(SnapshotHealth.Damaged, 481280, 12288, Now));

        Assert.Equal(SnapshotHealth.Damaged, row.Health);
        Assert.True(row.IsDamaged);
    }

    // -- the meta line --------------------------------------------------------------------

    // README's row table: "12.1 MB · 4 days ago".
    [Fact]
    public void A_whole_row_reads_size_then_age()
    {
        Assert.Equal("12.1 MB · 4 days ago", Row().MetaLine);
    }

    // 02, verbatim: "471 KB · FAILED VALIDATION". Uppercase, unlike the whole row's - 02 is the
    // screen-specific spec and postdates README.
    [Fact]
    public void A_suspect_row_says_what_failed()
    {
        Assert.Equal("471 KB · FAILED VALIDATION", Row(Snapshot(suspect: true, bytes: 482304)).MetaLine);
    }

    [Fact]
    public void A_damaged_row_says_the_checksums_do_not_match()
    {
        var row = Row();
        row.ApplyVerdict(new HealthVerdict(SnapshotHealth.Damaged, 481280, 12288, Now));

        Assert.Equal("CHECKSUMS DON'T MATCH", row.MetaLine);
    }

    // 11-high-contrast: the meta line becomes a verdict word plus a glyph, in the NAME cell, on
    // the line where the size and relative time normally sit. No new column.
    [Theory]
    [InlineData(false, false, "WHOLE · 12.1 MB")]
    [InlineData(true, false, "SUSPECT · 12.1 MB · FAILED VALIDATION")]
    [InlineData(false, true, "DAMAGED · CHECKSUMS DON'T MATCH")]
    public void High_contrast_turns_the_meta_line_into_a_verdict(bool suspect, bool damaged, string expected)
    {
        var row = Row(Snapshot(suspect: suspect));

        if (damaged) row.ApplyVerdict(new HealthVerdict(SnapshotHealth.Damaged, 1, 1, Now));

        Assert.Equal(expected, row.VerdictLine);
    }

    // -- the badge ------------------------------------------------------------------------

    [Fact]
    public void Only_an_unhealthy_row_carries_a_badge()
    {
        Assert.Null(Row().HealthBadge);
        Assert.Equal("SUSPECT", Row(Snapshot(suspect: true)).HealthBadge);
    }

    [Fact]
    public void A_damaged_row_carries_the_damaged_badge()
    {
        var row = Row();
        row.ApplyVerdict(new HealthVerdict(SnapshotHealth.Damaged, 1, 1, Now));

        Assert.Equal("DAMAGED", row.HealthBadge);
    }

    // -- the other columns ----------------------------------------------------------------

    [Fact]
    public void Taken_is_a_time_over_a_date()
    {
        var row = Row();

        Assert.Equal("21:36", row.TakenTime);
        Assert.Equal("11 AUG", row.TakenDate);
    }

    // README: "MANUAL at --wl-text; AUTOMATIC and PRE-RESTORE at --wl-muted."
    [Theory]
    [InlineData(SnapshotTrigger.Manual, "MANUAL", true)]
    [InlineData(SnapshotTrigger.Automatic, "AUTOMATIC", false)]
    [InlineData(SnapshotTrigger.PreRestore, "PRE-RESTORE", false)]
    public void Why_is_a_pill_and_only_manual_is_primary(
        SnapshotTrigger trigger, string label, bool primary)
    {
        var row = Row(Snapshot(trigger: trigger));

        Assert.Equal(label, row.Why);
        Assert.Equal(primary, row.WhyIsPrimary);
    }

    // "Three fixed slots, always three wide, so the column is scannable."
    [Fact]
    public void There_are_always_three_tier_badges_in_a_fixed_order()
    {
        var tiers = Row().Tiers;

        Assert.Equal(["SETTINGS", "PRESETS", "PLUGINS"], tiers.Select(t => t.Label));
        Assert.Equal([true, true, false], tiers.Select(t => t.IsPresent));
    }

    [Fact]
    public void Settings_is_present_on_every_snapshot()
    {
        Assert.True(Row(Snapshot(tiers: ["settings"])).Tiers[0].IsPresent);
    }

    // -- actions --------------------------------------------------------------------------

    [Fact]
    public void A_healthy_row_allows_every_action()
    {
        var row = Row();

        Assert.True(row.CanRename);
        Assert.True(row.CanDelete);
        Assert.True(row.CanRestore);
    }

    // 02: "all enabled, including Restore". A suspect backup may be the only copy there is.
    [Fact]
    public void A_suspect_row_can_still_be_restored()
    {
        Assert.True(Row(Snapshot(suspect: true)).CanRestore);
    }

    // 02's bottom bar for a damaged selection: Rename and Restore disabled, Delete ENABLED,
    // because deleting is the only useful action.
    [Fact]
    public void A_damaged_row_can_only_be_deleted()
    {
        var row = Row();
        row.ApplyVerdict(new HealthVerdict(SnapshotHealth.Damaged, 1, 1, Now));

        Assert.False(row.CanRename);
        Assert.False(row.CanRestore);
        Assert.True(row.CanDelete);
    }

    // -- search ---------------------------------------------------------------------------

    [Fact]
    public void With_no_query_the_name_is_one_unmatched_segment()
    {
        var segments = Row().NameSegments;

        Assert.Single(segments);
        Assert.False(segments[0].IsMatch);
    }

    [Fact]
    public void A_query_marks_the_matched_run_of_the_name()
    {
        var segments = Row(query: "beta").NameSegments;

        Assert.Equal([("Before 3.3 ", false), ("beta", true)],
                     segments.Select(s => (s.Text, s.IsMatch)));
    }

    // -- the selected row's detail --------------------------------------------------------

    // README: "17 EFFECTS ON 4 CHANNELS · 3 PRESETS · STREAM + MONITOR MIXES". Presets and mixes
    // are phase 6 and are omitted rather than printed as zero.
    [Fact]
    public void The_detail_line_reports_what_the_manifest_actually_knows()
    {
        Assert.Equal("17 EFFECTS ON 4 CHANNELS", Row().DetailLine);
    }

    [Fact]
    public void The_detail_names_the_snapshot_on_disk()
    {
        Assert.Equal("2026-08-11_2136-abc12345", Row().DetailFileName);
    }

    // 02: "MANIFEST SAYS 470 KB · FILE IS 12 KB · CHECKED 23:09".
    [Fact]
    public void A_damaged_row_shows_both_sizes_and_when_it_was_checked()
    {
        var row = Row();
        row.ApplyVerdict(new HealthVerdict(SnapshotHealth.Damaged, 481280, 12288, Now));

        Assert.Equal("MANIFEST SAYS 470 KB · FILE IS 12 KB · CHECKED 23:07", row.DamagedDetail);
    }

    [Fact]
    public void An_unreadable_file_says_so_rather_than_claiming_zero_bytes()
    {
        var row = Row();
        row.ApplyVerdict(new HealthVerdict(SnapshotHealth.Damaged, 481280, null, Now));

        Assert.Contains("FILE CAN'T BE READ", row.DamagedDetail!, StringComparison.Ordinal);
        Assert.DoesNotContain("0 B", row.DamagedDetail!, StringComparison.Ordinal);
    }

    [Fact]
    public void A_healthy_row_has_no_damaged_detail_or_sentence()
    {
        Assert.Null(Row().DamagedDetail);
        Assert.Null(Row().DamagedSentence);
    }

    [Fact]
    public void The_damaged_sentence_says_it_cannot_be_restored_and_what_to_do()
    {
        var row = Row();
        row.ApplyVerdict(new HealthVerdict(SnapshotHealth.Damaged, 1, 1, Now));

        Assert.Contains("can't be restored", row.DamagedSentence!, StringComparison.Ordinal);
        Assert.Contains("take a fresh one", row.DamagedSentence!, StringComparison.Ordinal);
    }

    // -- screen readers -------------------------------------------------------------------

    // 7.4: "the five-slot strip reads as five unlabelled cells without an AutomationProperties
    // name." A strip whose whole meaning is which cells are filled is exactly the thing a reader
    // cannot infer.
    [Fact]
    public void The_strip_names_every_input_and_says_how_many_are_missing()
    {
        var name = Row(Snapshot(inputs: ["Elgato Wave:3", "System"])).SlotsAutomationName;

        Assert.Contains("2 of 5", name, StringComparison.Ordinal);
        Assert.Contains("Elgato Wave:3", name, StringComparison.Ordinal);
        Assert.Contains("System", name, StringComparison.Ordinal);
    }

    [Fact]
    public void The_row_reads_as_a_sentence_rather_than_six_cells()
    {
        var name = Row().AutomationName;

        Assert.Contains("Before 3.3 beta", name, StringComparison.Ordinal);
        Assert.Contains("manual", name, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("21:36", name, StringComparison.Ordinal);
    }

    [Fact]
    public void A_damaged_row_says_so_to_a_reader_first()
    {
        var row = Row();
        row.ApplyVerdict(new HealthVerdict(SnapshotHealth.Damaged, 1, 1, Now));

        Assert.StartsWith("Damaged", row.AutomationName, StringComparison.Ordinal);
    }

    // -- change notification --------------------------------------------------------------

    // The probe answers after the row is on screen. Without this the flip to DAMAGED happens in
    // the object and nowhere else.
    [Fact]
    public void Applying_a_verdict_raises_change_notification_for_everything_it_moves()
    {
        var row = Row();
        var raised = new List<string?>();

        row.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        row.ApplyVerdict(new HealthVerdict(SnapshotHealth.Damaged, 1, 1, Now));

        Assert.Contains(nameof(SnapshotRowViewModel.Health), raised);
        Assert.Contains(nameof(SnapshotRowViewModel.MetaLine), raised);
        Assert.Contains(nameof(SnapshotRowViewModel.CanRestore), raised);
        Assert.Contains(nameof(SnapshotRowViewModel.HealthBadge), raised);
    }

    [Fact]
    public void An_unchanged_verdict_is_still_safe_to_apply()
    {
        var row = Row();

        row.ApplyVerdict(new HealthVerdict(SnapshotHealth.Whole, 1, 1, Now));

        Assert.Equal(SnapshotHealth.Whole, row.Health);
        Assert.Equal("12.1 MB · 4 days ago", row.MetaLine);
    }
}
