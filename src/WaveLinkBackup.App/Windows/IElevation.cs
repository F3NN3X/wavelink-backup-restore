using System.ComponentModel;
using System.Diagnostics;
using System.IO;

namespace WaveLinkBackup.App.Windows;

/// <summary>What came back from asking Windows for administrator rights.</summary>
public enum ElevationResult
{
    /// <summary>The elevated copy ran and reported success.</summary>
    Completed,

    /// <summary>The user said no at the UAC prompt. Nothing was attempted (screens/13 §13).</summary>
    Declined,

    /// <summary>It started and came back non-zero, or could not be started at all.</summary>
    Failed,
}

/// <param name="ExitCode">
/// The elevated copy's exit code, or null when it never started. Carried so the caller can map a
/// typed failure onto the designed error rather than showing one generic sentence for every way a
/// restore can go wrong.
/// </param>
public sealed record ElevationOutcome(ElevationResult Result, int? ExitCode = null);

/// <summary>
/// Asking Windows for administrator rights, as a seam.
///
/// The one operation in this program that needs them is putting tier 4's `.vst3` files back into
/// `C:\Program Files\Common Files\VST3` ([[ADR-006]]). It is a seam for the same reason
/// <see cref="IWindowChrome"/> and <see cref="ISystemTheme"/> are: a test cannot answer a UAC
/// prompt, and a restore flow that can only be exercised by a human clicking Yes is a restore flow
/// nobody exercises.
/// </summary>
public interface IElevation
{
    /// <summary>
    /// Runs this same executable elevated with <paramref name="arguments"/> and waits for it.
    ///
    /// Blocking on purpose: the caller is showing the in-progress strip and has nothing to do until
    /// the restore either happened or did not (screens/13, "The UAC prompt").
    /// </summary>
    ElevationOutcome RunElevated(IReadOnlyList<string> arguments, CancellationToken ct);
}

/// <summary>
/// The real one: `ShellExecute` with the `runas` verb, which is what shows the consent dialog
/// Windows draws itself. We never paint an administrator prompt. A program that draws its own is
/// teaching the user to trust a thing they should not.
/// </summary>
public sealed class ShellExecuteElevation(string? executablePath = null) : IElevation
{
    /// <summary>
    /// The Win32 error `ShellExecute` raises when the user dismisses the consent dialog. It is the
    /// ONLY way to tell "said no" from "failed", and the two need different words on screen: one is
    /// a refusal that changed nothing, the other is something to report.
    /// </summary>
    private const int ErrorCancelled = 1223;

    /// <summary>
    /// The running executable. `Environment.ProcessPath` rather than the assembly location: those
    /// differ for a single-file publish, and the one worth relaunching is the one the user started.
    /// </summary>
    private readonly string executable = executablePath ?? Environment.ProcessPath ?? string.Empty;

    public ElevationOutcome RunElevated(IReadOnlyList<string> arguments, CancellationToken ct)
    {
        if (executable.Length == 0) return new ElevationOutcome(ElevationResult.Failed);

        var info = new ProcessStartInfo(executable)
        {
            // Required by `runas`: the verb is only honoured through the shell. No stream
            // redirection here - it is incompatible with UseShellExecute, and the child's progress
            // reaches the caller over a named pipe instead (StageReportChannel), which works across
            // the UAC boundary without touching the process's standard streams at all.
            UseShellExecute = true,
            Verb = "runas",
        };

        foreach (var argument in arguments) info.ArgumentList.Add(argument);

        try
        {
            using var process = Process.Start(info);
            if (process is null) return new ElevationOutcome(ElevationResult.Failed);

            process.WaitForExit();
            return new ElevationOutcome(
                process.ExitCode == 0 ? ElevationResult.Completed : ElevationResult.Failed,
                process.ExitCode);
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == ErrorCancelled)
        {
            return new ElevationOutcome(ElevationResult.Declined);
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException)
        {
            return new ElevationOutcome(ElevationResult.Failed);
        }
    }
}
