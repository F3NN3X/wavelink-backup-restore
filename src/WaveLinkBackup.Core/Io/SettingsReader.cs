using WaveLinkBackup.Core.Abstractions;
using WaveLinkBackup.Core.Results;

namespace WaveLinkBackup.Core.Io;

/// <summary>
/// Reads settings bytes. Every read in Core goes through here.
///
/// Capture is a byte copy: hash the source bytes, write the source bytes. Nothing here
/// parses or re-serializes, because a backup tool that rewrites the thing it is backing up
/// has already lost. See _docs/knowledge-base/gotchas/every-snapshot-differs-with-no-real-change.md
/// </summary>
public sealed class SettingsReader(IFileSystem fileSystem)
{
    public Result<byte[]> Read(string path)
    {
        try
        {
            return fileSystem.ReadSharedBytes(path);
        }
        catch (FileNotFoundException)
        {
            return new SettingsUnreadable(path, "the file does not exist");
        }
        catch (DirectoryNotFoundException)
        {
            return new SettingsUnreadable(path, "the folder does not exist");
        }
        catch (UnauthorizedAccessException ex)
        {
            return new SettingsUnreadable(path, ex.Message);
        }
        catch (IOException ex)
        {
            // Reached only if something holds the file even more exclusively than Wave Link
            // does - shared mode already covers the normal running case.
            return new SettingsUnreadable(path, ex.Message);
        }
    }

    /// <summary>Reads the newest file in a log directory. Empty when there is nothing to read.</summary>
    public Result<string> ReadNewestLog(string logsPath)
    {
        if (!fileSystem.DirectoryExists(logsPath))
        {
            return new SettingsUnreadable(logsPath, "the log folder does not exist");
        }

        var newest = fileSystem
            .EnumerateFiles(logsPath, "*")
            .OrderByDescending(fileSystem.GetLastWriteTimeUtc)
            .FirstOrDefault();

        if (newest is null) return new SettingsUnreadable(logsPath, "the log folder is empty");

        try
        {
            return fileSystem.ReadSharedText(newest);
        }
        catch (IOException ex)
        {
            return new SettingsUnreadable(newest, ex.Message);
        }
    }
}
