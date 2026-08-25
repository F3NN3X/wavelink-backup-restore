using System.Diagnostics;
using System.IO;
using System.IO.Compression;

namespace WaveLinkBackup.App.Updates;

/// <param name="FailureDetail">
/// Null on success. Neutral copy, per screens/12: "a failed update leaves a working app, so
/// nothing is un-whole."
/// </param>
public sealed record UpdateInstall(bool Started, string? FailureDetail)
{
    public static UpdateInstall Failed(string detail) => new(false, detail);

    public static UpdateInstall Started_ { get; } = new(true, null);
}

/// <summary>
/// Replacing a running program with a newer copy of itself.
///
/// **A process cannot overwrite its own executable while it is running**, so this is necessarily
/// two-stage: the archive is expanded to a staging folder, then the app relaunches ITSELF from
/// that folder with <c>--apply-update</c> and exits. The staged copy waits for the old process to
/// go, swaps the directories, and starts the installed copy again. The design's row is called
/// "Install and restart" because that is literally what has to happen.
///
/// **The ordering is chosen so that every interruption leaves something that runs.** The previous
/// install is MOVED aside rather than deleted, and it is only removed once the new one is in
/// place; a failure after the move puts it straight back. There is no window in which neither
/// exists.
///
/// **What this deliberately does not do:** ask for elevation. An install under
/// <c>C:\Program Files</c> is not writable by the user, and the honest answer there is to say the
/// update could not be written and offer the download — which is exactly the failed-update block
/// screens/12 draws, with "Download it yourself" beside "Try again". Silently escalating to
/// administrator to overwrite binaries is the shape of a thing users are right to distrust.
/// </summary>
public sealed class UpdateInstaller
{
    /// <summary>The flag the staged copy is relaunched with.</summary>
    public const string ApplyFlag = "--apply-update";

    /// <summary>Where the previous install is kept while the new one goes in.</summary>
    public const string PreviousSuffix = ".previous";

    /// <summary>
    /// The breadcrumb a failed swap leaves, beside <c>settings.json</c> - NOT in the install
    /// directory, which is the thing being renamed.
    ///
    /// <para>
    /// A swap runs in the staged process, after the process the user was looking at has already
    /// exited. There is no window left to report into and no log: when it failed, the old version
    /// simply came back and nothing anywhere said why. That is the worst failure this program can
    /// have - it did nothing, successfully, and told nobody. The next launch reads this file and
    /// says so, the same shape as the crash report §8.1 writes.
    /// </para>
    /// </summary>
    public const string FailureFileName = "update-failed.txt";

    /// <summary>
    /// Records why a swap failed, where the next launch will find it. Best-effort by design: this
    /// runs while something is already going wrong, and a second failure here must not make it
    /// worse.
    /// </summary>
    public static void RecordFailure(string stateDirectory, string detail, DateTimeOffset now)
    {
        try
        {
            Directory.CreateDirectory(stateDirectory);
            File.WriteAllText(
                Path.Combine(stateDirectory, FailureFileName),
                $"{now:u}{Environment.NewLine}{detail}{Environment.NewLine}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Nothing to say, and nowhere left to say it.
        }
    }

    /// <summary>The recorded failure, or null. Reading it CLEARS it - it is news exactly once.</summary>
    public static string? TakeFailure(string stateDirectory)
    {
        var path = Path.Combine(stateDirectory, FailureFileName);

        try
        {
            if (!File.Exists(path)) return null;

            var text = File.ReadAllText(path);
            File.Delete(path);

            var detail = text
                .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Skip(1)
                .FirstOrDefault();

            return string.IsNullOrWhiteSpace(detail) ? null : detail;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Where a new version is expanded before it takes over.</summary>
    public const string StagingSuffix = ".staged";

    /// <summary>
    /// Expand <paramref name="archivePath"/> beside the install and hand over to it.
    ///
    /// Returns rather than throwing for every expected failure — a read-only install directory, a
    /// corrupt archive, an antivirus holding a file — because each of them is a row the design
    /// already draws.
    /// </summary>
    public UpdateInstall Begin(string archivePath, string installDirectory)
    {
        var staging = installDirectory.TrimEnd(Path.DirectorySeparatorChar) + StagingSuffix;

        try
        {
            if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);

            ZipFile.ExtractToDirectory(archivePath, staging);
        }
        catch (InvalidDataException)
        {
            return UpdateInstall.Failed("THE DOWNLOAD WASN'T A VALID ARCHIVE · NOTHING CHANGED");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return UpdateInstall.Failed(
                $"COULDN'T WRITE TO {Path.GetDirectoryName(staging)?.ToUpperInvariant()} · ACCESS DENIED");
        }

        // The staged copy must be a complete install, or the swap would produce a broken one.
        var executable = Path.Combine(staging, Path.GetFileName(Environment.ProcessPath ?? "WaveLinkBackup.exe"));
        if (!File.Exists(executable))
        {
            TryDelete(staging);
            return UpdateInstall.Failed("THE DOWNLOAD DIDN'T CONTAIN THE APP · NOTHING CHANGED");
        }

        try
        {
            // Detached, from the STAGED copy: it is the one that will still be running when this
            // process and this directory are gone.
            Process.Start(new ProcessStartInfo(executable)
            {
                ArgumentList = { ApplyFlag, Environment.ProcessId.ToString(), installDirectory },
                UseShellExecute = false,
                WorkingDirectory = staging,
            });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException)
        {
            TryDelete(staging);
            return UpdateInstall.Failed("THE UPDATE COULDN'T BE STARTED · NOTHING CHANGED");
        }

        return UpdateInstall.Started_;
    }

    /// <summary>
    /// The staged copy's side: wait for the old process to exit, swap the directories, and start
    /// the installed copy.
    ///
    /// Returns false when the swap could not be completed, having put the previous install back.
    /// The caller (the staged process's entry point) then exits without starting anything — the
    /// old install is intact, and the user will find the app exactly as it was.
    /// </summary>
    public bool Apply(int previousProcessId, string installDirectory, TimeSpan timeout)
    {
        WaitForExit(previousProcessId, timeout);

        var staging = installDirectory.TrimEnd(Path.DirectorySeparatorChar) + StagingSuffix;
        var previous = installDirectory.TrimEnd(Path.DirectorySeparatorChar) + PreviousSuffix;

        try
        {
            if (Directory.Exists(previous)) Directory.Delete(previous, recursive: true);

            // Move, not delete. Between these two lines the install directory does not exist, and
            // that is the only unsafe instant in the whole operation - it is a directory rename,
            // which NTFS does atomically, and the previous copy is one rename from being restored.
            if (Directory.Exists(installDirectory) && !TryMove(installDirectory, previous))
            {
                return false;
            }

            if (!TryMove(staging, installDirectory))
            {
                // Put it back. A failure here must not leave the user with no app at all.
                if (!Directory.Exists(installDirectory) && Directory.Exists(previous))
                {
                    TryMove(previous, installDirectory);
                }

                return false;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }

        // Only now: the new install is in place, so the old one has stopped being the way back.
        TryDelete(previous);

        return true;
    }

    /// <summary>
    /// Start the freshly-installed copy. Separate from <see cref="Apply"/> so a caller can swap
    /// without relaunching, which is what a test does.
    /// </summary>
    public static bool Relaunch(string installDirectory, string executableName)
    {
        try
        {
            Process.Start(new ProcessStartInfo(Path.Combine(installDirectory, executableName))
            {
                UseShellExecute = false,
                WorkingDirectory = installDirectory,
            });

            return true;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException)
        {
            return false;
        }
    }

    /// <summary>How long the swap keeps trying before giving up. Ten attempts, 250ms apart.</summary>
    internal static readonly TimeSpan SwapPatience = TimeSpan.FromMilliseconds(2500);

    private const int SwapAttempts = 10;

    /// <summary>
    /// A directory rename, retried.
    ///
    /// <para>
    /// <b>One attempt was not enough, and the failure looked like nothing happening at all.</b> The
    /// old process exiting does not mean Windows has finished with the directory: an image section
    /// for a just-terminated executable, a shell extension that has the folder open, and above all
    /// a virus scanner reading eight megabytes of freshly-extracted DLLs will each hold it for a
    /// moment. <see cref="WaitForExit"/> waits for the PROCESS, which is a different thing from
    /// waiting for its files.
    /// </para>
    ///
    /// <para>
    /// The observed failure was exactly this shape: download, verify and stage all succeeded, the
    /// swap did not, and the user got the old version back with nothing said. Two and a half
    /// seconds of patience costs a user who is already restarting their app nothing, and it is the
    /// difference between an update that works and one that silently does not.
    /// </para>
    /// </summary>
    private static bool TryMove(string from, string to)
    {
        var delay = SwapPatience / SwapAttempts;

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                Directory.Move(from, to);
                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                if (attempt >= SwapAttempts) return false;
                Thread.Sleep(delay);
            }
        }
    }

    /// <summary>
    /// Waits for the old process, and gives up rather than hanging. A process that will not exit
    /// is a reason to abandon the update — the swap would fail on its locked files anyway, and
    /// the ordering above turns that into "nothing changed".
    /// </summary>
    private static void WaitForExit(int processId, TimeSpan timeout)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            process.WaitForExit((int)timeout.TotalMilliseconds);
        }
        catch (ArgumentException)
        {
            // Already gone, which is the outcome being waited for.
        }
        catch (InvalidOperationException)
        {
            // Same.
        }
    }

    private static void TryDelete(string directory)
    {
        try
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A leftover staging folder costs disk, not correctness.
        }
    }
}
