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

    public bool CloseRequiresElevation =>
        ProcessNames.Any(name => SysProcess.GetProcessesByName(name).Any(CannotReach));

    /// <summary>
    /// Whether this process is above the current process's integrity level, i.e. one we cannot
    /// close from here. The test is the same handle access that <see cref="HasExited"/> uses: a
    /// process at a higher integrity level refuses it with access denied, and that refusal is the
    /// fact this property reports - not its name or path.
    ///
    /// A process that has exited between enumeration and the probe throws
    /// <c>InvalidOperationException</c>; that is "gone", not "unreachable", so it answers false and
    /// the final <see cref="CloseAndVerifyExited"/> verification remains the authority either way.
    /// </summary>
    private static bool CannotReach(SysProcess process)
    {
        try
        {
            // The probe, not the verdict: HasExited throws before it can answer on a process we
            // cannot open a handle to. ProbeHasExited maps that denial to "not verifiably gone",
            // which here reads as "we need more rights than we hold".
            return !ProbeHasExited(() => process.HasExited);
        }
        catch (Exception ex) when (ex is not InvalidOperationException and not System.ComponentModel.Win32Exception)
        {
            // ProbeHasExited documents the two failure shapes it maps; anything else is a genuine
            // surprise and must propagate rather than be read as either answer.
            throw;
        }
        finally
        {
            process.Dispose();
        }
    }

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

    private static bool HasExited(SysProcess process) => ProbeHasExited(() => process.HasExited);

    /// <summary>
    /// The verdict an exit probe yields, given how the probe itself behaves.
    ///
    /// Split out from <see cref="HasExited"/> so its two non-obvious mappings are testable
    /// without touching a live process:
    /// <list type="bullet">
    /// <item><c>InvalidOperationException</c> - the handle is already invalid, i.e. the process
    ///     has exited. That is the normal "gone" answer.</item>
    /// <item><c>Win32Exception</c> (access denied) - the process exists but runs at a higher
    ///     integrity level than this one (WavelinkSEService runs as System), so we cannot even
    ///     open a handle to ask whether it has exited. Reporting "not exited" is the honest
    ///     answer: the kill below fails for the same reason, and the final RunningProcessNames
    ///     check then surfaces WaveLinkStillRunning instead of letting this fault escape as a crash.</item>
    /// </list>
    /// Any other exception is not ours to swallow - it propagates, which is correct for a probe
    /// that should only ever fail in those two documented ways.
    /// </summary>
    public static bool ProbeHasExited(Func<bool> probe)
    {
        try { return probe(); }
        catch (InvalidOperationException) { return true; }
        catch (System.ComponentModel.Win32Exception) { return false; }
    }
}
