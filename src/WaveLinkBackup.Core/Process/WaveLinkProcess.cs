using System.Diagnostics;
using WaveLinkBackup.Core.Results;
using SysProcess = System.Diagnostics.Process;

namespace WaveLinkBackup.Core.Process;

/// <summary>
/// The real thing.
///
/// Covers BOTH processes. Upstream's ProcessControl only ever looks for Elgato.WaveLink and
/// never touches WavelinkSEService (audit finding 6) - so its "verified exited" check can
/// pass while the service is still up. SPEC.md 4 is explicit that both must be closed.
/// </summary>
public sealed class WaveLinkProcess : IWaveLinkProcess
{
    private static readonly string[] ProcessNames = ["Elgato.WaveLink", "WavelinkSEService"];

    public IReadOnlyList<string> RunningProcessNames =>
        [.. ProcessNames.Where(name => SysProcess.GetProcessesByName(name).Length > 0)];

    public bool IsRunning => RunningProcessNames.Count > 0;

    public Result CloseAndVerifyExited(TimeSpan timeout)
    {
        var deadline = timeout;

        foreach (var name in ProcessNames)
        {
            foreach (var process in SysProcess.GetProcessesByName(name))
            {
                using (process)
                {
                    TryCloseGracefully(process, deadline);
                    if (!HasExited(process)) TryKillTree(process);
                }
            }
        }

        // Verify, do not assume. This assertion is the whole point of the method - and it
        // is an assertion rather than a sleep because on a loaded machine a fixed delay
        // fails exactly under the conditions that cause the race.
        var stillRunning = RunningProcessNames;
        return stillRunning.Count > 0 ? new WaveLinkStillRunning(stillRunning) : Result.Ok();
    }

    public Result LaunchByAppId(string packageFamilyName)
    {
        try
        {
            var started = SysProcess.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $@"shell:AppsFolder\{packageFamilyName}!App",
                UseShellExecute = true,
            });

            return started is null
                ? new WriteFailed("Windows did not start Wave Link.")
                : Result.Ok();
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return new WriteFailed($"Windows did not start Wave Link: {ex.Message}");
        }
    }

    private static void TryCloseGracefully(SysProcess process, TimeSpan timeout)
    {
        try
        {
            // A graceful close lets the app checkpoint cleanly. An unconditional kill risks
            // leaving other state inconsistent and buys nothing - the verification below is
            // what actually protects the write.
            process.CloseMainWindow();
            process.WaitForExit((int)timeout.TotalMilliseconds);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            // Already gone, or has no window (the service). Either way, fall through to
            // the kill and then to the verification.
        }
    }

    private static void TryKillTree(SysProcess process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
            process.WaitForExit(5000);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
        {
            // Swallowed deliberately: the caller learns whether this worked from
            // RunningProcessNames, not from an exception here.
        }
    }

    private static bool HasExited(SysProcess process)
    {
        try { return process.HasExited; }
        catch (InvalidOperationException) { return true; }
    }
}
