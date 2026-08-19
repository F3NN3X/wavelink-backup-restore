using WaveLinkBackup.Core.Abstractions;
using WaveLinkBackup.Core.Analysis;
using WaveLinkBackup.Core.Capture;
using WaveLinkBackup.Core.Io;
using WaveLinkBackup.Core.Process;
using WaveLinkBackup.Core.Results;
using WaveLinkBackup.Core.Snapshots;

namespace WaveLinkBackup.Core.Restore;

/// <param name="PreRestoreSnapshot">
/// Always present on success, and present on most failures too. The way back.
/// </param>
/// <param name="Relaunched">
/// False when the location came from an explicit path with no package around it - the user
/// must start Wave Link themselves. Not a failure.
/// </param>
/// <param name="Verdict">
/// What the log said. Null when the log could not be read, which is not a restore failure -
/// it means the restore cannot be *confirmed*, and callers should say exactly that.
/// </param>
/// <param name="Tiers">
/// What tiers 3 and 4 put back, and what they could not. Never a failure on its own: by the time
/// it runs, the settings file - the product - is already written.
/// </param>
public sealed record RestoreOutcome(
    Snapshot PreRestoreSnapshot,
    bool Relaunched,
    RestoreVerdict? Verdict,
    TierRestoreResult? Tiers = null)
{
    public bool Confirmed => Verdict?.Succeeded == true;
}

/// <summary>
/// The assembled restore sequence. Phase 1 built the primitives and proved each one; this is
/// the order they go in, and the order is load-bearing at every step.
///
/// Deliberately a separate type from SettingsWriter. The write refuses to run while Wave Link
/// is up (see the preconditions-inside-the-operation pattern); it does not go closing things.
/// Keeping orchestration out here preserves the property that SettingsWriter.Write is safe to
/// call from anywhere.
/// </summary>
/// <param name="gatherPayload">
/// What the PRE-RESTORE snapshot captures beyond the settings file. The same delegate
/// <see cref="Automation.BackupService"/> takes, and for the same reason: the pre-restore copy is
/// the one the user comes back to, so it should be as complete as any other capture. Null means
/// settings only.
/// </param>
public sealed class RestoreOrchestrator(
    IFileSystem fileSystem,
    IWaveLinkProcess process,
    SnapshotStore store,
    SettingsWriter writer,
    SettingsReader reader,
    Func<SettingsInspection, SnapshotPayload?>? gatherPayload = null,
    string? appDataPath = null)
{
    private static readonly TimeSpan CloseTimeout = TimeSpan.FromSeconds(10);

    /// <summary>The name a pre-restore snapshot always gets.</summary>
    public const string PreRestoreName = "Before restore";

    /// <param name="options">
    /// Which of the larger tiers to put back. Presets on, plug-in binaries off - the default is
    /// everything that cannot need administrator rights ([[ADR-006]]).
    /// </param>
    public Result<RestoreOutcome> Restore(
        string snapshotId, SettingsInspection live, RestoreOptions? options = null)
    {
        // 1. Verify the snapshot BEFORE touching anything. Restoring a file the app will
        //    reject looks identical to the snapshot being broken, and costs a whole restore
        //    cycle - plus the live config - to tell apart.
        var found = store.Get(snapshotId);
        if (!found.IsSuccess) return found.Propagate<RestoreOutcome>();

        var verified = new SnapshotGuard(fileSystem).Verify(found.Value.Directory);
        if (!verified.IsSuccess) return verified.Propagate<RestoreOutcome>();

        var settings = reader.Read(found.Value.SettingsPath);
        if (!settings.IsSuccess) return settings.Propagate<RestoreOutcome>();

        // 2. Take the pre-restore snapshot. UNCONDITIONAL, and there is no parameter to skip
        //    it - if a caller could, someone would, and it is the entire reason the
        //    destructive button is safe to press. Before the close, because it wants the
        //    live state and wants it even though it is the one being escaped from.
        var preRestore = store.Write(
            live.Bytes, live.Analysis, SnapshotTrigger.PreRestore, PreRestoreName,
            payload: gatherPayload?.Invoke(live));

        if (!preRestore.IsSuccess) return preRestore.Propagate<RestoreOutcome>();

        // 3. Close BOTH processes and verify exited. A graceful exit flushes in-memory config
        //    on the way out; harmless before the write, fatal racing it.
        var closed = process.CloseAndVerifyExited(CloseTimeout);
        if (!closed.IsSuccess) return Result<RestoreOutcome>.Fail(closed.Error);

        // 4. Write. Re-checks the precondition itself - this call site is not trusted, on
        //    purpose.
        var written = writer.Write(live.Location, settings.Value);
        if (!written.IsSuccess) return Result<RestoreOutcome>.Fail(written.Error);

        // 5. Tiers 3 and 4, while Wave Link is still closed - a plug-in binary cannot be
        //    replaced under a host that has it loaded, and presets are read at scan time.
        //    Reported, never fatal: the settings file is already back.
        var tiers = new TierRestore(fileSystem, appDataPath ?? TierCapture.SystemAppData).Restore(
            found.Value,
            new SnapshotPluginReader(fileSystem).Read(found.Value),
            options ?? RestoreOptions.Default);

        // 6. Relaunch, unless we have no package to relaunch through.
        var relaunched = false;
        if (live.Location.CanRelaunch)
        {
            relaunched = process.LaunchByAppId(live.Location.PackageFamilyName).IsSuccess;
        }

        // 7. Verify from the log, never from the UI - a mixer that looks correct can be a
        //    freshly generated default. A log we cannot read means unconfirmed, not failed.
        var log = reader.ReadNewestLog(live.Location.LogsPath);
        var verdict = log.IsSuccess ? LogAnalysis.Verify(log.Value) : null;

        return new RestoreOutcome(preRestore.Value, relaunched, verdict, tiers);
    }

    /// <summary>
    /// What <see cref="Restore"/> would do. Cheap, read-only, and safe to call while Wave
    /// Link runs - this is what the confirmation dialog shows.
    /// </summary>
    public Result<RestorePlan> Plan(string snapshotId, SettingsInspection live)
    {
        var found = store.Get(snapshotId);
        if (!found.IsSuccess) return found.Propagate<RestorePlan>();

        var plan = RestorePlanner.Build(
            found.Value.Manifest, live.Analysis.Fingerprint, live.Analysis.WaveLinkVersion);

        // Tier 2's payoff (phase 6 section 5): "install FabFilter Pro-Q 4, it's missing" instead
        // of a channel that silently loads with its effect switched off. A snapshot that never
        // recorded its plug-ins leaves this null - unknown, not "nothing is missing".
        var recorded = new SnapshotPluginReader(fileSystem).Read(found.Value);
        if (recorded is null) return plan;

        return plan with
        {
            Plugins = new PluginResolution(fileSystem)
                .Check(recorded, new PluginCacheReader(fileSystem).Read(live.Location)),
        };
    }
}
