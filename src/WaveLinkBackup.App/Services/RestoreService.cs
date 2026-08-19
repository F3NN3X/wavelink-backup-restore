using WaveLinkBackup.Core.Abstractions;
using WaveLinkBackup.Core.Io;
using WaveLinkBackup.Core.Process;
using WaveLinkBackup.Core.Results;
using WaveLinkBackup.Core.Restore;
using WaveLinkBackup.Core.Snapshots;
using WaveLinkBackup.App.ViewModels;

namespace WaveLinkBackup.App.Services;

/// <summary>
/// The four outcomes a restore can end in, from the shell's point of view. Mapped one-to-one onto
/// the existing RestoreOutcomeStrip kinds (Confirmed -> SucceededConfirmed, Unconfirmed ->
/// SucceededUnconfirmed, Rejected -> Rejected, Failed -> Failed), so the view-model never has to
/// know what a RestoreVerdict is.
/// </summary>
public enum RestoreResult
{
    Confirmed,
    Unconfirmed,
    Rejected,
    Failed,
}

/// <param name="Result">Confirmed / Unconfirmed / Rejected / Failed.</param>
/// <param name="PreRestoreSnapshotId">
/// Present on every non-Failed outcome - the "Before restore" backup is the way back. The strip
/// and any future "restore the pre-restore snapshot" action both want it, so it is carried here
/// rather than re-read from the store.
/// </param>
/// <param name="Relaunched">
/// False when Wave Link had no package to relaunch through - the user must start it themselves.
/// Not a failure, but the outcome line says so.
/// </param>
/// <param name="FailureMessage">Present only on Failed: what went wrong, in the user's words.</param>
/// <param name="CoreError">
/// Present only on Failed: the typed Core error behind the failure. The window maps it to one of
/// the twelve designed errors (06-errors.md) - a damaged backup renders as inline strip 10, an
/// unreadable manifest as 7 - which a plain message string cannot distinguish. Null on every
/// non-Failed outcome and on a cancellation (which is not an expected failure).
/// </param>
public sealed record RestoreResultView(
    RestoreResult Result,
    string? PreRestoreSnapshotId,
    bool Relaunched,
    string? FailureMessage,
    CoreError? CoreError = null);

/// <summary>
/// The shell-facing seam over Core's restore. The view-model and the window never touch a Wave
/// Link process API or a snapshot store directly - they call this, report stages through an
/// IProgress&lt;RestoreStage&gt;, and get back a plain result. That is what keeps "no process API in
/// view or view-model code" (Plan 5 definition of done) a property of the dependency graph rather
/// than a rule to remember.
/// </summary>
public interface IRestoreService
{
    /// <summary>
    /// What restoring <paramref name="snapshotId"/> would do - the read-only description the
    /// confirmation dialog renders. Safe to call while Wave Link runs. The window inspects live
    /// settings and passes them in (see RestoreAsync) so the plan describes the same "what is on
    /// disk right now" that a subsequent restore would act on.
    /// </summary>
    Task<Result<RestorePlan>> PlanAsync(string snapshotId, SettingsInspection live, CancellationToken ct);

    /// <param name="snapshotId">The machine id of the snapshot to restore.</param>
    /// <param name="live">
    /// The current live settings, inspected by the caller. The service builds everything else from
    /// its constructor dependencies; only this - "what is on disk right now" - it cannot know for
    /// itself, because it does not own the locator or the chosen-path setting.
    /// </param>
    Task<RestoreResultView> RestoreAsync(
        string snapshotId, SettingsInspection live, IProgress<RestoreStage>? progress, CancellationToken ct);
}

/// <summary>
/// Wraps a RestoreOrchestrator and reports its phases as the four named stages.
///
/// The orchestrator is synchronous and exposes no stage callbacks - it does all six steps in one
/// call. The stages are therefore reported AROUND that call, at the points where each step has
/// provably completed:
///   ClosingWaveLink  - before the call (the close is its third step; we are about to do it)
///   WritingSettings  - not observable mid-call, so it is reported together with the relaunch
///                      below as "the write happened and Wave Link is coming back up"
///   StartingWaveLink - after a successful outcome whose location could relaunch
///   Checking         - always, last: the log-verify is the final step and its result is the verdict
///
/// The in-order guarantee RestoreProgressModel enforces (it throws on a backwards Advance) holds
/// because this reports strictly forward, never twice to the same stage.
/// </summary>
/// <param name="gatherPayload">
/// What the pre-restore snapshot captures beyond the settings file. Passed through to the
/// orchestrator: the copy the user comes back to should be as complete as any other.
/// </param>
public sealed class RestoreService(
    IFileSystem fileSystem,
    IWaveLinkProcess process,
    SnapshotStore store,
    Func<SettingsInspection, SnapshotPayload?>? gatherPayload = null) : IRestoreService
{
    public Task<Result<RestorePlan>> PlanAsync(string snapshotId, SettingsInspection live, CancellationToken ct)
    {
        // Read-only and cheap, but it touches the store - run off the UI thread like RestoreAsync
        // so a slow disk never freezes the confirmation dialog's own window.
        return Task.Run(() => new RestoreOrchestrator(
            fileSystem, process, store, new SettingsWriter(fileSystem, process),
            new SettingsReader(fileSystem), gatherPayload)
            .Plan(snapshotId, live), ct);
    }

    public Task<RestoreResultView> RestoreAsync(
        string snapshotId, SettingsInspection live, IProgress<RestoreStage>? progress, CancellationToken ct)
    {
        var orchestrator = new RestoreOrchestrator(
            fileSystem, process, store, new SettingsWriter(fileSystem, process),
            new SettingsReader(fileSystem), gatherPayload);

        // Run the synchronous Core sequence off the UI thread so the strip can animate while it
        // runs. The stages are reported from this background context; IProgress<T> marshals each
        // report back to the caller's synchronization context (the WPF dispatcher).
        return Task.Run(() =>
        {
            try
            {
                ct.ThrowIfCancellationRequested();

                progress?.Report(RestoreStage.ClosingWaveLink);

                var result = orchestrator.Restore(snapshotId, live);
                if (!result.IsSuccess)
                {
                    // The close, the write, or the pre-restore snapshot failed. Wave Link may be
                    // closed and not relaunched - the user is told plainly rather than left guessing.
                    progress?.Report(RestoreStage.Checking);
                    return new RestoreResultView(
                        RestoreResult.Failed, null, false, result.Error!.Message, result.Error);
                }

                var outcome = result.Value;

                // The write succeeded and, where possible, Wave Link has been relaunched. Report the
                // two middle stages now: from outside the call we cannot tell them apart in time,
                // but both are done by the point Restore returns, and reporting them forward keeps
                // the strip honest about what has already happened to the mixer.
                progress?.Report(RestoreStage.WritingSettings);
                if (outcome.Relaunched) progress?.Report(RestoreStage.StartingWaveLink);

                progress?.Report(RestoreStage.Checking);

                return new RestoreResultView(
                    Map(outcome), outcome.PreRestoreSnapshot.Id, outcome.Relaunched, null);
            }
            catch (OperationCanceledException)
            {
                // A cancelled restore is not a failure of the restore - it never got to change
                // anything. Surface it as Failed with a message that says so, so the strip does
                // not claim a result for work that was interrupted.
                progress?.Report(RestoreStage.Checking);
                return new RestoreResultView(
                    RestoreResult.Failed, null, false, "The restore was cancelled.");
            }
        }, ct);
    }

    /// <summary>
    /// The one place a RestoreVerdict becomes a shell-facing result. Kept as a pure function so it
    /// is unit-testable without an orchestrator: the service's only other job is threading and stage
    /// reporting, which these tests do not need to exercise.
    /// </summary>
    internal static RestoreResult Map(RestoreOutcome outcome) => outcome.Verdict switch
    {
        // Wave Link rejected the settings file and regenerated defaults - the one true reject.
        { ParseFailed: true } => RestoreResult.Rejected,
        // The log said names were applied and nothing failed to parse - confirmed.
        { Succeeded: true } => RestoreResult.Confirmed,
        // A verdict with neither a parse failure nor applied names: the write went through but the
        // confirmation did not. Unconfirmed, never a reject (03-restore-outcomes.md).
        _ => RestoreResult.Unconfirmed,
    };
}
