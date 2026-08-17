using WaveLinkBackup.Core.Abstractions;
using WaveLinkBackup.Core.Results;

namespace WaveLinkBackup.Core.Automation;

/// <summary>
/// Where the user's choices live: %LOCALAPPDATA%\WaveLinkBackup\settings.json.
///
/// In Core rather than in the shell because the design's own sentence - "a command-line flag
/// overrides this file for that one run and isn't saved" - is a claim about the CLI as much as
/// the GUI. If this lived in the App project, `wlbackup list` would keep ignoring the folder
/// chosen in the GUI, and that sentence would be false.
/// See operations/design/screens/08-settings-persistence.md.
///
/// Write on change, never on exit.
/// </summary>
public sealed class SettingsRepository(IFileSystem fileSystem, string directoryPath)
{
    public const string FileName = "settings.json";

    /// <summary>
    /// Resolved through GetFolderPath rather than composed from a string - %LOCALAPPDATA% is
    /// redirected on some corporate and OneDrive setups, the same reason
    /// <see cref="Snapshots.SnapshotStore.DefaultStorePath"/> does it.
    /// </summary>
    public static string DefaultDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "WaveLinkBackup");

    public string FilePath { get; } = Path.Combine(directoryPath, FileName);

    /// <summary>
    /// Never fails. A missing file means "not configured yet"; an unreadable one means the user
    /// gets defaults rather than a dead app. Both are preferences problems, not data loss - the
    /// backups are elsewhere and untouched either way.
    /// </summary>
    public BackupSettings Read()
    {
        if (!fileSystem.FileExists(FilePath)) return BackupSettings.Default;

        try
        {
            return SettingsSerializer.Read(fileSystem.ReadSharedBytes(FilePath));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return BackupSettings.Default;
        }
    }

    public Result Save(BackupSettings settings)
    {
        var bytes = SettingsSerializer.Write(settings);

        try
        {
            fileSystem.CreateDirectory(directoryPath);

            // File.Replace THROWS when the destination does not exist, which is exactly the
            // first-ever save. SettingsWriter never meets this case because Wave Link's
            // Settings.json is always already there by the time we replace it.
            if (!fileSystem.FileExists(FilePath))
            {
                fileSystem.WriteBytes(FilePath, bytes);
                return Result.Ok();
            }

            // Same directory, because File.Replace requires source and destination on one
            // volume - a temp file in %TEMP% may not be.
            var temp = Path.Combine(directoryPath, $".{FileName}.{Guid.NewGuid():N}.tmp");
            var rollback = Path.Combine(directoryPath, $".{FileName}.{Guid.NewGuid():N}.rollback");

            try
            {
                fileSystem.WriteBytes(temp, bytes);
                fileSystem.Replace(temp, FilePath, rollback);
                return Result.Ok();
            }
            finally
            {
                Discard(temp);
                Discard(rollback);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new WriteFailed(ex.Message);
        }
    }

    private void Discard(string path)
    {
        try { fileSystem.Delete(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* best effort */ }
    }
}
