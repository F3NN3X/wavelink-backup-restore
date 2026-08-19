using WaveLinkBackup.App.ViewModels;
using WaveLinkBackup.Core.Restore;

namespace WaveLinkBackup.App.Tests;

public class RestoreDialogModelTests
{
    private static readonly DateTimeOffset Taken = new(2026, 8, 11, 21, 36, 0, TimeSpan.Zero);

    private static RestorePlan Plan(
        string name = "Before 3.3 beta",
        IReadOnlyList<PlanRow>? rows = null,
        string? versionWarning = null,
        PluginRestoreCheck? plugins = null) =>
        new(
            SnapshotName: name,
            SnapshotTakenUtc: Taken,
            Rows: rows ?? DefaultRows(),
            LosesInputs: false,
            InputNamesLost: [],
            SnapshotIsSuspect: false,
            VersionWarning: versionWarning,
            Plugins: plugins);

    private static PluginRestoreCheck Missing(string display, params string[] channels) =>
        new([new PluginPresenceResult(display, @"C:\VST3\x.vst3", "4.1.2", null, channels,
            PluginPresence.Missing)]);

    private static PluginRestoreCheck Drifted(string display) =>
        new([new PluginPresenceResult(display, @"C:\VST3\x.vst3", "4.1.2", "4.2.0", ["Voice"],
            PluginPresence.VersionDrift)]);

    // -------------------------------------------------------- the missing-plug-in warning

    [Fact]
    public void The_missing_plugin_block_renders_when_the_plan_found_one_missing()
    {
        // Phase 6 §5: it was null through all of phase 5 because there was nothing to compare
        // against. Core resolves and words it; the dialog renders both clauses as one sentence.
        var check = Missing("FabFilter Pro-Q 4", "Voice");

        var model = RestoreDialogModel.Build(
            Plan(plugins: check), Taken,
            missingPluginLead: check.MissingLead,
            missingPluginRest: check.MissingRest);

        Assert.Equal("FabFilter Pro-Q 4 isn't installed on this computer.", model.MissingPluginLead);
        Assert.StartsWith("The Voice channel will load", model.MissingPluginRest);
        Assert.Contains("Install it and restore again", model.MissingPluginWarning);
    }

    [Fact]
    public void A_snapshot_with_nothing_missing_renders_no_amber_block_at_all()
    {
        var model = RestoreDialogModel.Build(Plan(plugins: new PluginRestoreCheck([])), Taken);

        Assert.Null(model.MissingPluginWarning);
    }

    [Fact]
    public void Plugin_version_drift_joins_the_quiet_version_line_rather_than_the_amber_block()
    {
        // A plug-in that updated is not missing, and nothing about the restore is un-whole - so
        // it is the same quiet mono line the Wave Link version mismatch uses, never amber.
        var model = RestoreDialogModel.Build(
            Plan(versionWarning: "Made with 3.2.9; you run 3.3.0.", plugins: Drifted("FabFilter Pro-Q 4")),
            Taken);

        Assert.Contains("Made with 3.2.9", model.VersionMismatchNote);
        Assert.Contains("FabFilter Pro-Q 4 4.1.2 → 4.2.0", model.VersionMismatchNote);
        Assert.Null(model.MissingPluginWarning);
    }

    [Fact]
    public void Drift_alone_still_fills_the_quiet_line()
    {
        var model = RestoreDialogModel.Build(Plan(plugins: Drifted("FabFilter Pro-Q 4")), Taken);

        Assert.StartsWith("Plug-in versions have changed", model.VersionMismatchNote);
    }

    /// <summary>The three rows Core always emits, in its order.</summary>
    private static IReadOnlyList<PlanRow> DefaultRows() =>
    [
        Row("Inputs", "5", "5"),
        Row("Channel names", "Wave Mic 1, Voice, Browser, Game, System", "Wave Mic 1, Voice, Browser, Game, System"),
        Row("Effects", "12 on 3 channels", "17 on 4 channels"),
    ];

    /// <summary>Builds a row with the same changed-flag rule Core uses (ordinal inequality).</summary>
    private static PlanRow Row(string label, string now, string after) =>
        new(label, now, after, !string.Equals(now, after, StringComparison.Ordinal));

    [Fact]
    public void Title_Uses_Snapshot_Name_In_Screen_Two_Casing()
    {
        var model = RestoreDialogModel.Build(Plan(), Taken);

        Assert.Equal("Restore “Before 3.3 beta”?", model.Title);
    }

    [Fact]
    public void Body_Names_Date_Time_And_Says_Wave_Link_Restarts()
    {
        var model = RestoreDialogModel.Build(Plan(), Taken);

        Assert.Contains("saved on Tue 11 Aug at 21:36", model.Body);
        Assert.Contains("Wave Link will close and reopen.", model.Body);
        Assert.StartsWith("This replaces your current Wave Link setup", model.Body);
    }

    [Fact]
    public void Rows_Mirror_Core_Plan_Verbatim_In_Its_Order()
    {
        var model = RestoreDialogModel.Build(Plan(), Taken);

        Assert.Equal(["Inputs", "Channel names", "Effects"], model.Rows.Select(r => r.Label).ToArray());

        var effects = model.Rows.Single(r => r.Label == "Effects");
        Assert.Equal("12 on 3 channels", effects.NowValue);
        Assert.Equal("17 on 4 channels", effects.AfterValue);
    }

    [Fact]
    public void Changed_Flags_Come_From_Core_Not_Recomputed()
    {
        var model = RestoreDialogModel.Build(Plan(), Taken);

        // Inputs and channel names are identical in the plan -> no dot. Effects differ -> dot.
        Assert.False(model.Rows.Single(r => r.Label == "Inputs").Changed);
        Assert.False(model.Rows.Single(r => r.Label == "Channel names").Changed);
        Assert.True(model.Rows.Single(r => r.Label == "Effects").Changed);
    }

    [Fact]
    public void Version_Note_Shown_Only_When_Versions_Differ()
    {
        var mismatch = RestoreDialogModel.Build(
            Plan(versionWarning: "This backup was made with Wave Link 3.2.9; you are running 3.3.0.4108."), Taken);
        var match = RestoreDialogModel.Build(Plan(versionWarning: null), Taken);

        Assert.NotNull(mismatch.VersionMismatchNote);
        Assert.Contains("3.2.9", mismatch.VersionMismatchNote);
        Assert.Null(match.VersionMismatchNote);
    }

    private const string WarningLead = "FabFilter Pro-Q 3 isn't installed on this computer.";
    private const string WarningRest =
        "The Voice channel will load with that effect switched off. Install it and restore again to get it back.";

    [Fact]
    public void Missing_Plugin_Warning_Passes_Through_And_Is_Null_By_Default()
    {
        var withWarning = RestoreDialogModel.Build(
            Plan(), Taken, missingPluginLead: WarningLead, missingPluginRest: WarningRest);
        var without = RestoreDialogModel.Build(Plan(), Taken);

        Assert.Equal($"{WarningLead} {WarningRest}", withWarning.MissingPluginWarning);
        Assert.Null(without.MissingPluginWarning);
    }

    /// <summary>
    /// README Screen 2 weights the two clauses differently - the naming sentence is --wl-strong,
    /// the consequence is body colour - so they have to reach the view as two values. One string
    /// cannot be rendered in two weights.
    /// </summary>
    [Fact]
    public void The_warning_reaches_the_view_as_a_lead_and_a_rest()
    {
        var model = RestoreDialogModel.Build(
            Plan(), Taken, missingPluginLead: WarningLead, missingPluginRest: WarningRest);

        Assert.Equal(WarningLead, model.MissingPluginLead);
        Assert.Equal(WarningRest, model.MissingPluginRest);
    }

    /// <summary>
    /// A lead with no consequence yet is still worth showing - it already names what is missing,
    /// which is the whole point of the block. It must not print a trailing space.
    /// </summary>
    [Fact]
    public void A_lead_on_its_own_is_the_whole_warning()
    {
        var model = RestoreDialogModel.Build(Plan(), Taken, missingPluginLead: WarningLead);

        Assert.Equal(WarningLead, model.MissingPluginWarning);
        Assert.Null(model.MissingPluginRest);
    }

    [Fact]
    public void Reassurance_Is_The_Fixed_Way_Back_Sentence()
    {
        var model = RestoreDialogModel.Build(Plan(), Taken);

        Assert.Equal(RestoreDialogModel.ReassuranceText, model.Reassurance);
        Assert.Contains("“Before restore”", model.Reassurance);
    }

    [Fact]
    public void Presets_And_Mixes_Rows_Appear_Only_When_Both_Sides_Known()
    {
        var hidden = RestoreDialogModel.Build(Plan(), Taken);
        Assert.DoesNotContain(hidden.Rows, r => r.Label is "Saved presets" or "Mixes");

        var shown = RestoreDialogModel.Build(
            Plan(), Taken,
            presetCountNow: 1, presetCountAfter: 3,
            mixNamesNow: ["Stream", "Monitor"], mixNamesAfter: ["Stream", "Monitor"]);

        var presets = shown.Rows.Single(r => r.Label == "Saved presets");
        Assert.Equal("1", presets.NowValue);
        Assert.Equal("3", presets.AfterValue);
        Assert.True(presets.Changed); // 1 -> 3 earns the dot

        var mixes = shown.Rows.Single(r => r.Label == "Mixes");
        Assert.False(mixes.Changed); // identical lists, no dot
    }

    [Fact]
    public void Preset_Row_Hides_When_Only_One_Side_Known()
    {
        // A half-known row would be a guess; the dialog must not print one.
        var model = RestoreDialogModel.Build(Plan(), Taken, presetCountNow: 1);

        Assert.DoesNotContain(model.Rows, r => r.Label == "Saved presets");
    }
}
