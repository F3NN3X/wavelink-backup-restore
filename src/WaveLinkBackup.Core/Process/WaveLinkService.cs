using System.Runtime.Versioning;
using System.ServiceProcess;
using WaveLinkBackup.Core.Results;

namespace WaveLinkBackup.Core.Process;

/// <summary>
/// The real thing, through the Service Control Manager.
///
/// <c>WavelinkSEService</c> runs as LocalSystem. Starting it needs rights a normal user process
/// does not hold - but a restore already runs elevated (it closes a System process), so this call
/// succeeds there without a second prompt. A non-elevated caller gets access denied, which is
/// reported as a failure rather than swallowed: the restore can then proceed and Wave Link will
/// simply show its own "start the service" box, exactly as before this seam existed.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WaveLinkService : IWaveLinkService
{
    /// <summary>The Windows service name. Stable per product; not a path or version.</summary>
    public const string ServiceName = "WavelinkSEService";

    private static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(15);

    public bool Exists => HasService(new ServiceController(ServiceName));

    public bool IsRunning
    {
        get
        {
            using var service = new ServiceController(ServiceName);
            return HasService(service) && service.Status == ServiceControllerStatus.Running;
        }
    }

    public Result EnsureStarted()
    {
        try
        {
            using var service = new ServiceController(ServiceName);

            // No such service on this machine - Wave Link is not installed. Nothing to start, and
            // the relaunch will simply have no service to complain about either.
            if (!HasService(service)) return Result.Ok();

            if (service.Status == ServiceControllerStatus.Running) return Result.Ok();

            service.Start();
            service.WaitForStatus(ServiceControllerStatus.Running, StartTimeout);

            return service.Status == ServiceControllerStatus.Running
                ? Result.Ok()
                : new WaveLinkServiceStartFailed("did not reach the running state");
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            // Access denied (not elevated), a dependency that will not start, or a timeout all land
            // here. Report it; do not crash the restore over a service we could not bring up.
            return new WaveLinkServiceStartFailed(ex.Message);
        }
    }

    /// <summary>
    /// Whether the service exists at all. Constructing a <see cref="ServiceController"/> never throws
    /// for an unknown name - it only surfaces on the first status read - so this probes that read.
    /// </summary>
    private static bool HasService(ServiceController service)
    {
        try
        {
            _ = service.Status;
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }
}

/// <summary>
/// The Wave Link background service could not be started. Not fatal to a restore - the settings
/// are already written and the app can still be launched - but it means the user may see Wave
/// Link's own "start the service" prompt, so callers that surface failures should say so.
/// </summary>
public sealed record WaveLinkServiceStartFailed(string Reason)
    : CoreError($"Could not start the WavelinkSEService service: {Reason}");
