using WaveLinkBackup.Core.Process;
using WaveLinkBackup.Core.Results;

namespace WaveLinkBackup.Core.Tests.Fakes;

/// <summary>
/// The seam that makes the shutdown flush race testable. The scenario that matters is
/// <see cref="StaysRunningAfterClose"/>: a graceful exit flushes in-memory config on the way
/// out, so a write racing it is silently overwritten seconds later.
/// See _docs/knowledge-base/gotchas/restored-settings-revert-seconds-later.md
/// </summary>
public sealed class FakeWaveLinkProcess : IWaveLinkProcess
{
    /// <summary>Models a process that ignores a close request - and a kill that does not work.</summary>
    public bool StaysRunningAfterClose { get; set; }

    /// <summary>Models a process that ignores the graceful close but dies to the kill.</summary>
    public bool RequiresKill { get; set; }

    public bool Running { get; set; } = true;
    public int CloseAttempts { get; private set; }
    public int KillAttempts { get; private set; }
    public string? LaunchedPackageFamily { get; private set; }

    public IReadOnlyList<string> RunningProcessNames =>
        Running ? ["Elgato.WaveLink", "WavelinkSEService"] : [];

    public bool IsRunning => Running;

    public Result CloseAndVerifyExited(TimeSpan timeout)
    {
        CloseAttempts++;

        if (StaysRunningAfterClose)
        {
            KillAttempts++;
            return new WaveLinkStillRunning(RunningProcessNames);
        }

        if (RequiresKill) KillAttempts++;

        Running = false;
        return Result.Ok();
    }

    public Result LaunchByAppId(string packageFamilyName)
    {
        LaunchedPackageFamily = packageFamilyName;
        Running = true;
        return Result.Ok();
    }
}
