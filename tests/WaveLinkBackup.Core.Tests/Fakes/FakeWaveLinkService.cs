using WaveLinkBackup.Core.Process;
using WaveLinkBackup.Core.Results;

namespace WaveLinkBackup.Core.Tests.Fakes;

/// <summary>
/// The seam that makes "bring WavelinkSEService back before relaunching" testable without touching
/// the Service Control Manager. No test may start a real service.
/// </summary>
public sealed class FakeWaveLinkService : IWaveLinkService
{
    public bool Exists { get; set; } = true;

    /// <summary>Whether the service is already running when <see cref="EnsureStarted"/> is called.</summary>
    public bool Running { get; set; }

    /// <summary>
    /// Models a machine where starting the service fails (not elevated, dependency missing, or a
    /// start timeout). When set, <see cref="EnsureStarted"/> reports that failure instead of Ok.
    /// </summary>
    public bool StartFails { get; set; }

    public int EnsureStartedCalls { get; private set; }

    public bool IsRunning => Running;

    public Result EnsureStarted()
    {
        EnsureStartedCalls++;

        if (!Exists) return Result.Ok();
        if (StartFails) return new WaveLinkServiceStartFailed("simulated failure");

        Running = true;
        return Result.Ok();
    }
}
