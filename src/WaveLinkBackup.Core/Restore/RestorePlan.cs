using WaveLinkBackup.Core.Analysis;
using WaveLinkBackup.Core.Snapshots;

namespace WaveLinkBackup.Core.Restore;

/// <param name="Changes">True when this row differs. The UI marks changed rows and only those.</param>
public sealed record PlanRow(string Label, string Now, string After, bool Changes);

/// <summary>
/// What tier 4 a snapshot is carrying, if any — the one part of a restore that can need
/// administrator rights ([[ADR-006]]).
///
/// Answered here rather than left to the shell to work out, because the shell's only correct
/// question is "is there anything to offer, and what does it cost". A dialog that offers to put
/// plug-in files back when the snapshot holds none is offering a capability the restore is about
/// to refuse (operations/design/screens/13-elevation.md).
/// </summary>
/// <param name="Count">How many plug-ins have their binary in this snapshot.</param>
/// <param name="Bytes">What those binaries weigh, so the row can print an honest figure.</param>
public sealed record PluginBinaryPayload(int Count, long Bytes)
{
    public static PluginBinaryPayload None { get; } = new(0, 0);

    /// <summary>False hides the whole opt-in row, which is the common case: tier 4 is off by default.</summary>
    public bool Any => Count > 0;
}

/// <summary>
/// What a restore would do, described before it does it.
///
/// PURE - two fingerprints in, a description out. This is exactly the restore dialog's
/// "now vs. after" table, built here so phase 5 renders rather than computes.
/// </summary>
/// <param name="Plugins">
/// What the snapshot's plug-ins would find on this machine, or null when the snapshot predates
/// tier 2 and there is nothing to check against. Attached by
/// <see cref="RestoreOrchestrator.Plan"/> rather than computed here, because answering it means
/// touching the disk and this type is pure.
/// </param>
public sealed record RestorePlan(
    string SnapshotName,
    DateTimeOffset SnapshotTakenUtc,
    IReadOnlyList<PlanRow> Rows,
    bool LosesInputs,
    IReadOnlyList<string> InputNamesLost,
    bool SnapshotIsSuspect,
    string? VersionWarning,
    PluginRestoreCheck? Plugins = null,
    PluginBinaryPayload? Binaries = null)
{
    /// <summary>Never null, so the dialog can ask without a guard.</summary>
    public PluginBinaryPayload BinaryPayload => Binaries ?? PluginBinaryPayload.None;

    /// <summary>Anything the user should read before pressing the button.</summary>
    public bool HasWarnings =>
        LosesInputs || SnapshotIsSuspect || VersionWarning is not null || Plugins?.HasMissing == true;
}

public static class RestorePlanner
{
    /// <param name="liveVersion">The running Wave Link version, if known.</param>
    public static RestorePlan Build(
        SnapshotManifest snapshot,
        HealthFingerprint live,
        string? liveVersion = null)
    {
        var after = new HealthFingerprint(
            snapshot.InputCount, snapshot.InputNames,
            snapshot.EffectCount, snapshot.EffectChannelCount,
            SizeBytes: snapshot.Files.TryGetValue(SnapshotManifest.SettingsFileName, out var f) ? f.SizeBytes : 0,
            Sha256: snapshot.SettingsSha256);

        var comparison = after.CompareTo(live);

        var rows = new List<PlanRow>
        {
            Row("Inputs", live.InputCount.ToString(), after.InputCount.ToString()),
            Row("Channel names", Join(live.InputNames), Join(after.InputNames)),
            Row("Effects",
                $"{live.EffectCount} on {live.EffectChannelCount} channels",
                $"{after.EffectCount} on {after.EffectChannelCount} channels"),
        };

        return new RestorePlan(
            SnapshotName: snapshot.DisplayName,
            SnapshotTakenUtc: snapshot.CreatedUtc,
            Rows: rows,
            // Note the direction: "loses" is measured on what the restore would DO, so it
            // compares the snapshot against what is live now.
            LosesInputs: comparison.LooksCollapsed,
            InputNamesLost: comparison.NamesLost,
            SnapshotIsSuspect: snapshot.IsSuspect,
            VersionWarning: VersionWarning(snapshot.WaveLinkVersion, liveVersion));
    }

    /// <summary>
    /// 3.3.0.4108 Beta rejected a file 3.2.9 accepted. When a restore fails, the first
    /// question is whether the config is bad or the validator changed - so a mismatch is
    /// surfaced before the attempt, not diagnosed after it. SPEC.md 5.
    /// </summary>
    private static string? VersionWarning(string? snapshotVersion, string? liveVersion) =>
        snapshotVersion is null || liveVersion is null ||
        string.Equals(snapshotVersion, liveVersion, StringComparison.Ordinal)
            ? null
            : $"This backup was made with Wave Link {snapshotVersion}; you are running " +
              $"{liveVersion}. If the restore fails, the version difference is the first thing to suspect.";

    private static PlanRow Row(string label, string now, string after) =>
        new(label, now, after, !string.Equals(now, after, StringComparison.Ordinal));

    private static string Join(IReadOnlyList<string> names) =>
        names.Count == 0 ? "none" : string.Join(", ", names);
}
