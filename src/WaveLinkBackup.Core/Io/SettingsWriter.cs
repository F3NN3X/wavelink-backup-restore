using WaveLinkBackup.Core.Abstractions;
using WaveLinkBackup.Core.Analysis;
using WaveLinkBackup.Core.Discovery;
using WaveLinkBackup.Core.Process;
using WaveLinkBackup.Core.Results;

namespace WaveLinkBackup.Core.Io;

/// <summary>
/// Replaces Settings.json. The one irreversible operation in Core.
///
/// This class does NOT close Wave Link, and does not orchestrate a restore - the assembled
/// sequence (validate, compare, pre-restore snapshot, close, write, relaunch, verify) needs
/// the snapshot store and is phase 2. What it does guarantee is that a write can never
/// happen while Wave Link is up.
/// </summary>
public sealed class SettingsWriter(IFileSystem fileSystem, IWaveLinkProcess process)
{
    public Result Write(SettingsLocation location, byte[] content)
    {
        // PRECONDITION, not a caller's duty.
        //
        // A graceful exit flushes in-memory config on the way out: harmless before a write,
        // fatal racing it - and the failure is invisible, because the write succeeds,
        // verifies, and is silently overwritten seconds later. Enforced here, the race
        // cannot be reintroduced by a future caller who forgets to close first.
        // See _docs/knowledge-base/gotchas/restored-settings-revert-seconds-later.md
        if (process.IsRunning) return new WaveLinkStillRunning(process.RunningProcessNames);

        // Restoring a file the app will reject looks identical to the snapshot being
        // broken, and costs a restore cycle to tell apart. Check before touching anything.
        var analysis = SettingsAnalysis.Analyse(content);
        if (!analysis.IsSuccess) return Result.Fail(analysis.Error);

        var directory = Path.GetDirectoryName(location.SettingsPath);
        if (string.IsNullOrEmpty(directory))
        {
            return new WriteFailed($"'{location.SettingsPath}' has no containing directory.");
        }

        // Same directory, because File.Replace requires source and destination on the same
        // volume - a temp file in %TEMP% may not be.
        var temp = Path.Combine(directory, $".Settings.json.{Guid.NewGuid():N}.tmp");
        var rollback = Path.Combine(directory, $".Settings.json.{Guid.NewGuid():N}.rollback");

        try
        {
            fileSystem.WriteBytes(temp, content);

            // Verify what actually landed, rather than trusting the write. Upstream's idea,
            // and worth keeping.
            var written = fileSystem.ReadSharedBytes(temp);
            if (!written.AsSpan().SequenceEqual(content))
            {
                return new WriteFailed("the temporary file did not match what was written.");
            }

            // Atomic on NTFS, and it produces the rollback copy in the same operation - so
            // there is no window in which Settings.json is half-written.
            fileSystem.Replace(temp, location.SettingsPath, rollback);
            return Result.Ok();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new WriteFailed(ex.Message);
        }
        finally
        {
            try { fileSystem.Delete(temp); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* best effort */ }
        }
    }
}
