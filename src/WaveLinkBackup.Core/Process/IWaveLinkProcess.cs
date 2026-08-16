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
    /// Starts Wave Link via shell:AppsFolder\(family)!App. An MSIX app cannot be started
    /// from its .exe path; this is not a preference.
    /// </summary>
    Result LaunchByAppId(string packageFamilyName);
}
