using System.Globalization;
using WaveLinkBackup.Core.Restore;

namespace WaveLinkBackup.App.ViewModels;

/// <summary>
/// One row of the restore confirmation's "now vs. after" table (Screen 2).
/// </summary>
/// <param name="Label">The left column: Inputs, Channel names, Effects, Saved presets, Mixes.</param>
/// <param name="NowValue">What is live right now.</param>
/// <param name="AfterValue">What the restore would put in place.</param>
/// <param name="Changed">
/// True when the value differs. This is the ONLY thing that earns the 5px accent dot and the
/// --wl-strong fill; unchanged values stay muted with no dot.
/// </param>
public sealed record RestoreDialogRow(string Label, string NowValue, string AfterValue, bool Changed);

/// <summary>
/// The restore confirmation dialog's entire content, computed BEFORE anything is shown.
///
/// A pure projection: in comes a <see cref="RestorePlan"/> (Core's read-only "what would happen"
/// description) and out goes exactly what Screen 2 renders — title, body, the now-vs-after table,
/// the version-mismatch note, the missing-plug-in warning, and the reassurance line. No I/O, no
/// WPF. The view binds to this; it does not compute.
///
/// The three rows Core knows about (Inputs, Channel names, Effects) come straight from the plan,
/// so the dialog can never disagree with what a restore would actually do. The two the design
/// adds (Saved presets, Mixes) are passed in separately because Core's manifest does not record
/// them yet — they render only when values are supplied, and are simply absent until that data
/// exists rather than invented.
/// </summary>
/// <param name="MissingPluginLead">
/// The amber block's first clause, rendered in --wl-strong: the sentence that names what is
/// missing ("FabFilter Pro-Q 3 isn't installed on this computer."). Null omits the whole block.
///
/// Split from the rest because README Screen 2 weights the two differently, and the naming clause
/// is the one a user has to read - a paragraph in one uniform colour buries it.
/// </param>
/// <param name="MissingPluginRest">
/// The consequence and the way out, in body colour ("The Voice channel will load with that effect
/// switched off. Install it and restore again to get it back."). Null renders the lead alone.
/// </param>
public sealed record RestoreDialogModel(
    string Title,
    string Body,
    IReadOnlyList<RestoreDialogRow> Rows,
    string? VersionMismatchNote,
    string? MissingPluginLead,
    string? MissingPluginRest,
    string Reassurance)
{
    /// <summary>
    /// The two clauses as one sentence, or null when there is no warning. The view binds its
    /// visibility to this rather than testing two properties, and a screen reader reads the whole
    /// warning from it as one announcement instead of two fragments.
    /// </summary>
    public string? MissingPluginWarning => (MissingPluginLead, MissingPluginRest) switch
    {
        (null, null) => null,
        ({ } lead, null) => lead,
        (null, { } rest) => rest,
        var (lead, rest) => $"{lead} {rest}",
    };

    /// <summary>The fixed order Screen 2 prints its table in.</summary>
    public static readonly string[] RowOrder = ["Inputs", "Channel names", "Effects", "Saved presets", "Mixes"];

    /// <summary>
    /// Always present: the way back. The pre-restore backup is automatic and always named
    /// "Before restore" — that is what makes the one destructive button safe to press.
    /// </summary>
    public const string ReassuranceText =
        "Your current settings are saved as “Before restore” first, so you can come back to today.";

    /// <param name="plan">Core's read-only description of the restore; source of the table and note.</param>
    /// <param name="takenLocal">When the snapshot was taken, local time, for the body sentence.</param>
    /// <param name="presetCountNow">Current saved-preset count. Null hides the row (data not yet tracked).</param>
    /// <param name="presetCountAfter">The snapshot's saved-preset count. Null hides the row.</param>
    /// <param name="mixNamesNow">Current mix names. Null hides the row.</param>
    /// <param name="mixNamesAfter">The snapshot's mix names. Null hides the row.</param>
    /// <param name="missingPluginLead">
    /// The amber block's naming clause, when an effect in the snapshot has no installed plug-in.
    /// Null omits the block. Phase 6 §5 is what starts supplying it - until the plug-in manifest
    /// exists there is nothing to compare against, so it stays null and the block never renders.
    /// </param>
    /// <param name="missingPluginRest">The consequence clause. Null renders the lead alone.</param>
    public static RestoreDialogModel Build(
        RestorePlan plan,
        DateTimeOffset takenLocal,
        int? presetCountNow = null,
        int? presetCountAfter = null,
        IReadOnlyList<string>? mixNamesNow = null,
        IReadOnlyList<string>? mixNamesAfter = null,
        string? missingPluginLead = null,
        string? missingPluginRest = null)
    {
        // Core's three rows, in the order it emits them. The Changed flag is already computed there.
        var rows = new List<RestoreDialogRow>(plan.Rows.Count + 2);

        foreach (var row in plan.Rows)
        {
            rows.Add(new RestoreDialogRow(row.Label, row.Now, row.After, row.Changes));
        }

        // The design's two extra rows. Only when BOTH sides are known — a half-known row would be
        // a guess, and this dialog must not print one for the app's single irreversible action.
        if (presetCountNow is { } nowPresets && presetCountAfter is { } afterPresets)
        {
            rows.Add(new RestoreDialogRow(
                "Saved presets", nowPresets.ToString(), afterPresets.ToString(),
                nowPresets != afterPresets));
        }

        if (mixNamesNow is not null && mixNamesAfter is not null)
        {
            var nowMixes = Join(mixNamesNow);
            var afterMixes = Join(mixNamesAfter);
            rows.Add(new RestoreDialogRow("Mixes", nowMixes, afterMixes, nowMixes != afterMixes));
        }

        // 09-restore-dialog-additions.md: a quiet mono line under the body, above the table. Present
        // only when the versions differ; Core already wrote the sentence (RestorePlanner).
        //
        // Plug-in version drift joins it here rather than getting a surface of its own. It is the
        // same kind of fact — "something is a different version than when this was taken" — and it
        // is deliberately NOT the amber block: a plug-in that updated is not missing, and nothing
        // about the restore is un-whole.
        var versionNote = Sentences(plan.VersionWarning, plan.Plugins?.DriftNote);

        return new RestoreDialogModel(
            Title: $"Restore “{plan.SnapshotName}”?",
            Body: $"This replaces your current Wave Link setup with the one saved on "
                  + $"{takenLocal.ToString("ddd d MMM", CultureInfo.InvariantCulture)} at "
                  + $"{takenLocal.ToString("HH:mm", CultureInfo.InvariantCulture)}. "
                  + "Wave Link will close and reopen.",
            Rows: rows,
            VersionMismatchNote: versionNote,
            MissingPluginLead: missingPluginLead,
            MissingPluginRest: missingPluginRest,
            Reassurance: ReassuranceText);
    }

    /// <summary>The non-empty sentences as one line, or null when there are none.</summary>
    private static string? Sentences(params string?[] parts)
    {
        var present = parts.Where(p => !string.IsNullOrWhiteSpace(p)).ToList();
        return present.Count == 0 ? null : string.Join(" ", present);
    }

    private static string Join(IReadOnlyList<string> names) =>
        names.Count == 0 ? "none" : string.Join(", ", names);
}
