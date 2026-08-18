using WaveLinkBackup.App.Services;
using WaveLinkBackup.App.ViewModels;
using WaveLinkBackup.Core.Analysis;
using WaveLinkBackup.Core.Restore;
using WaveLinkBackup.Core.Snapshots;

namespace WaveLinkBackup.App.Tests;

/// <summary>
/// The shell-facing restore seam. Its load-bearing logic is the verdict -> RestoreResult mapping:
/// that is what decides which of the four outcome-strip treatments a restore gets, and it is pure,
/// so it is tested here without an orchestrator or a store. The async wrapper's job - run Core off
/// the UI thread and report the four stages forward - is covered by the stage-ordering tests below.
/// </summary>
public sealed class RestoreServiceTests
{
    // A minimal but real outcome: Map only reads Verdict, so the snapshot can be a placeholder.
    private static RestoreOutcome Outcome(RestoreVerdict? verdict) => new(
        PreRestoreSnapshot: new Snapshot(
            "pre-restore",
            @"C:\Users\test\AppData\Local\WaveLinkBackup\pre-restore",
            new SnapshotManifest(
                SchemaVersion: SnapshotManifest.CurrentSchemaVersion,
                DisplayName: "Before restore",
                Notes: string.Empty,
                CreatedUtc: DateTimeOffset.UnixEpoch,
                Trigger: SnapshotTrigger.PreRestore,
                SettingsSha256: new string('0', 64),
                WaveLinkVersion: null,
                InputCount: 3,
                InputNames: ["Wave Mic 1"],
                EffectCount: 0,
                EffectChannelCount: 0,
                HasDuplicateKeys: false,
                Tiers: [],
                Files: new Dictionary<string, SnapshotFile>())),
        Relaunched: true,
        Verdict: verdict);

    // -------------------------------------------------------------- the mapping

    [Fact]
    public void A_parse_failure_maps_to_rejected()
    {
        var result = RestoreService.Map(Outcome(new RestoreVerdict(true, true, [], "3.3.0.4108", null)));

        Assert.Equal(RestoreResult.Rejected, result);
    }

    [Fact]
    public void A_confirmed_verdict_maps_to_confirmed()
    {
        var result = RestoreService.Map(Outcome(new RestoreVerdict(false, false, ["Wave Mic 1"], "3.3.0.4108", null)));

        Assert.Equal(RestoreResult.Confirmed, result);
    }

    [Fact]
    public void A_verdict_with_no_applied_names_maps_to_unconfirmed()
    {
        // No parse failure, but the log recorded nothing Wave Link applied: the write went through,
        // the confirmation did not. Unconfirmed - never a reject (03-restore-outcomes.md).
        var result = RestoreService.Map(Outcome(new RestoreVerdict(false, false, [], "3.3.0.4108", null)));

        Assert.Equal(RestoreResult.Unconfirmed, result);
    }

    [Fact]
    public void A_null_verdict_maps_to_unconfirmed()
    {
        // The log could not be read at all: the restore cannot be confirmed, but that is not a
        // failure and not a reject.
        var result = RestoreService.Map(Outcome(null));

        Assert.Equal(RestoreResult.Unconfirmed, result);
    }

    [Fact]
    public void A_parse_failure_wins_over_applied_names()
    {
        // Defensive: if both flags were ever set, the reject must win - a file Wave Link rejected
        // and regenerated is not confirmed by any names it may have applied on a later load.
        var result = RestoreService.Map(Outcome(new RestoreVerdict(true, false, ["Wave Mic 1"], "3.3.0.4108", null)));

        Assert.Equal(RestoreResult.Rejected, result);
    }
}
