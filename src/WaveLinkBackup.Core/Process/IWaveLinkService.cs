using WaveLinkBackup.Core.Results;

namespace WaveLinkBackup.Core.Process;

/// <summary>
/// The lifecycle of Wave Link's background service, <c>WavelinkSEService</c>.
///
/// A restore closes BOTH of Wave Link's processes - the app and this service (SPEC.md 4) - and
/// then relaunches only the app. Left down, the app comes back up against a missing service and
/// shows its own "Start Service / Exit App" box. This seam is what lets a restore put the service
/// back before it launches the app, so the user never sees that box.
///
/// Kept as its own seam rather than a method on <see cref="IWaveLinkProcess"/> because the two are
/// different kinds of thing: the process is closed and verified by name, while the service is
/// started through the Service Control Manager - a different API with different failure shapes
/// (a service that will not start reports a timeout, not a still-running process).
/// </summary>
public interface IWaveLinkService
{
    /// <summary>Whether the service exists on this machine. False when Wave Link is absent.</summary>
    bool Exists { get; }

    /// <summary>Whether the service is running right now.</summary>
    bool IsRunning { get; }

    /// <summary>
    /// Starts the service if it is not already running, and waits for it to come up. A no-op that
    /// succeeds when the service is absent (there is nothing to start) or already running.
    /// </summary>
    Result EnsureStarted();
}
