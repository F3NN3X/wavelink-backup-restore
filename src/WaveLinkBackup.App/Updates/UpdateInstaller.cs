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
            if (Directory.Exists(installDirectory)) Directory.Move(installDirectory, previous);

            try
            {
                Directory.Move(staging, installDirectory);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Put it back. A failure here must not leave the user with no app at all.
                if (!Directory.Exists(installDirectory) && Directory.Exists(previous))
                {
                    Directory.Move(previous, installDirectory);
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
