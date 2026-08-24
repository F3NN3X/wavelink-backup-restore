using WaveLinkBackup.Core.Results;

namespace WaveLinkBackup.Core.Process;

/// <summary>
/// Wave Link's lifecycle. The second of Core's two seams, and the reason the shutdown flush
/// race is testable at all.
/// </summary>
public interface IWaveLinkProcess
{
    /// <summary>Which of Wave Link's processes are up. Empty when it is fully exited.</summary>
    IReadOnlyList<string> RunningProcessNames { get; }

    bool IsRunning { get; }

    /// <summary>
    /// Graceful close, then a kill on timeout, then VERIFY. Returns
    /// <see cref="WaveLinkStillRunning"/> if anything is left alive.
    ///
    /// The invariant is exit, not kill method. A graceful exit flushes in-memory config on
    /// the way out - harmless before a write, fatal racing it - so what matters is not how
    /// the process ended but that it has, confirmed.
    /// </summary>
    Result CloseAndVerifyExited(TimeSpan timeout);

    /// <summary>
    /// Whether closing Wave Link from THIS process needs more rights than it currently holds.
    ///
    /// The one question a restore must answer before it commits to running in-process: if any
    /// running Wave Link process is above this process's integrity level, the graceful close and
    /// the kill below both fail for the same reason - we cannot even open a handle to it - and
    /// <see cref="CloseAndVerifyExited"/> would end with <see cref="WaveLinkStillRunning"/>. That
    /// is not a failure of the restore; it is a fact about this process's rights, and the caller
    /// can act on it (elevate) instead of surfacing a refusal that changed nothing.
    ///
    /// Probed, not inferred: the answer comes from actually asking each running process whether
    /// it can be reached, never from its name or path - the same discipline as the plug-in-folder
    /// write probe ([[ADR-011]]). A process we cannot reach answers for itself.
    /// </summary>
    bool CloseRequiresElevation { get; }

    /// <summary>
    /// Starts Wave Link via shell:AppsFolder\(family)!App. An MSIX app cannot be started
    /// from its .exe path; this is not a preference.
    /// </summary>
    Result LaunchByAppId(string packageFamilyName);
}
