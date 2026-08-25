using WaveLinkBackup.Core.Abstractions;
using WaveLinkBackup.Core.Discovery;

namespace WaveLinkBackup.Core.Capture;

/// <summary>
/// The second half of tier 1: Wave Link's OWN backup copies, which live beside the settings file
/// it backs up.
///
/// [[ADR-006]] defines tier 1 as *"`Settings.json` + Wave Link's own backup copies, ~470 KB"* and
/// `SPEC.md` §1 marks both directories BACK UP. They are worth the bytes because they carry
/// history our first run cannot have: the rolling `AutoBackup` copies reach back about three days,
/// and the irregular `.bak` atomic-save artifacts reach back months.
///
/// We copy them and never manage them. No pruning, no rotation, nothing written back on
/// restore. They are Wave Link's files in Wave Link's directory
/// (technical-debt.md §3) - here they are evidence, not payload.
/// </summary>
public sealed class WaveLinkBackupFiles(IFileSystem fileSystem)
{
    /// <summary>Where they sit inside a snapshot.</summary>
    public const string RelativeRoot = "wavelink-backups";

    /// <summary>
    /// The newest this many from each source directory.
    ///
    /// Ten is what Wave Link itself keeps in `AutoBackup`, so on a healthy machine the cap never
    /// binds. It exists for the machine that has not been cleaned in a year: nothing enforces the
    /// rotation on the `.bak` files at all, and an unbounded capture would put a directory of
    /// unknown size into every snapshot for the rest of the store's life.
    /// </summary>
    public const int KeepPerDirectory = 10;

    /// <summary>What tier 1 would copy beyond `Settings.json`, newest first within each group.</summary>
    public IReadOnlyList<SourceFile> Discover(SettingsLocation location)
    {
        var backupDirectory = Path.Combine(location.LocalStatePath, "Backup");
        var autoDirectory = Path.Combine(backupDirectory, "AutoBackup");

        // AutoBackup holds nothing but Settings.auto.<ts>.json, so everything in it is wanted.
        // The .bak artifacts share a directory with whatever else Wave Link decides to keep
        // there, so those are matched by name rather than taken wholesale.
        var auto = Newest(autoDirectory, _ => true, $"{RelativeRoot}/AutoBackup");
        var artifacts = Newest(
            backupDirectory,
            name => name.StartsWith("Settings.json.bak", StringComparison.OrdinalIgnoreCase),
            RelativeRoot);

        return [.. auto, .. artifacts];
    }

    /// <summary>Total bytes without reading a single one — the Settings dialog's row 1.</summary>
    public long Measure(SettingsLocation location) => FileTree.TotalBytes(Discover(location));

    private IReadOnlyList<SourceFile> Newest(
        string directory, Func<string, bool> wanted, string relativeRoot)
    {
        if (!fileSystem.DirectoryExists(directory)) return [];

        IReadOnlyList<string> files;
        try
        {
            files = fileSystem.EnumerateFiles(directory, "*");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }

        return
        [
            .. files
                .Where(f => wanted(Path.GetFileName(f)))
                // Newest by write time, then by name so that two files written in the same
                // second cannot reorder between captures.
                .OrderByDescending(fileSystem.GetLastWriteTimeUtc)
                .ThenBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
                .Take(KeepPerDirectory)
                .Select(f => new SourceFile(
                    f, $"{relativeRoot}/{Path.GetFileName(f)}", fileSystem.GetFileSize(f)))
        ];
    }
}
