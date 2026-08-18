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
public sealed record RestoreDialogModel(
    string Title,
    string Body,
    IReadOnlyList<RestoreDialogRow> Rows,
    string? VersionMismatchNote,
    string? MissingPluginWarning,
    string Reassurance)
{
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
    /// <param name="missingPluginWarning">
    /// The amber block's copy, when an effect in the snapshot has no installed plug-in. Null omits it.
    /// </param>
    public static RestoreDialogModel Build(
        RestorePlan plan,
        DateTimeOffset takenLocal,
        int? presetCountNow = null,
        int? presetCountAfter = null,
        IReadOnlyList<string>? mixNamesNow = null,
        IReadOnlyList<string>? mixNamesAfter = null,
        string? missingPluginWarning = null)
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
        var versionNote = plan.VersionWarning is { } warning && warning.Length > 0 ? warning : null;

        return new RestoreDialogModel(
            Title: $"Restore “{plan.SnapshotName}”?",
            Body: $"This replaces your current Wave Link setup with the one saved on "
                  + $"{takenLocal.ToString("ddd d MMM", CultureInfo.InvariantCulture)} at "
                  + $"{takenLocal.ToString("HH:mm", CultureInfo.InvariantCulture)}. "
                  + "Wave Link will close and reopen.",
            Rows: rows,
            VersionMismatchNote: versionNote,
            MissingPluginWarning: missingPluginWarning,
            Reassurance: ReassuranceText);
    }

    private static string Join(IReadOnlyList<string> names) =>
        names.Count == 0 ? "none" : string.Join(", ", names);
}
