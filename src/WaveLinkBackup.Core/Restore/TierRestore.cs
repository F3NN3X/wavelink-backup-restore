using WaveLinkBackup.Core.Abstractions;
using WaveLinkBackup.Core.Capture;
using WaveLinkBackup.Core.Results;
using WaveLinkBackup.Core.Snapshots;

namespace WaveLinkBackup.Core.Restore;

/// <summary>
/// Which of the larger tiers a restore should put back. Settings and the plugin manifest are not
/// here: tier 1 is the restore, and tier 2 is a description rather than something to write.
/// </summary>
/// <param name="Presets">
/// On by default. They go to <c>%APPDATA%</c>, which the user owns, and they are the one thing in
/// a snapshot nobody can re-download.
/// </param>
/// <param name="PluginBinaries">
/// OFF by default, and the only thing in this program that can need administrator rights:
/// `C:\Program Files\Common Files\VST3` is not user-writable ([[ADR-006]]). Asked for explicitly
/// or not at all.
/// </param>
public sealed record RestoreOptions(bool Presets = true, bool PluginBinaries = false)
{
    public static RestoreOptions Default { get; } = new();

    /// <summary>Settings only — nothing outside Wave Link's own LocalState is touched.</summary>
    public static RestoreOptions SettingsOnly { get; } = new(Presets: false, PluginBinaries: false);
}

/// <param name="Skipped">
/// Files that could not be written, by destination. Never empty AND successful: a partial restore
/// says which parts are partial rather than reporting a number that looks complete.
/// </param>
public sealed record TierRestoreResult(
    int PresetFilesRestored,
    int PluginFilesRestored,
    IReadOnlyList<string> Skipped,
    bool NeedsElevation)
{
    public static TierRestoreResult Nothing { get; } = new(0, 0, [], false);

    public bool RestoredAnything => PresetFilesRestored > 0 || PluginFilesRestored > 0;
}

/// <summary>
/// Putting tiers 3 and 4 back.
///
/// **Never fails a restore.** By the time this runs the settings file — the product — is already
/// written; a preset that cannot be copied or a Program Files directory that refuses a write must
/// be reported, not rolled back over. The result says what was skipped and whether elevation was
/// the reason, and the caller decides how loudly to say so.
///
/// **Tier 4 is the only thing here that can need administrator rights.** Tiers 1–3 write to
/// LocalState and <c>%APPDATA%</c>, both of which the user owns, so an ordinary account restores
/// everything that matters without a UAC prompt ([[ADR-006]]).
/// </summary>
public sealed class TierRestore(IFileSystem fileSystem, string appDataPath)
{
    public static TierRestore For(IFileSystem fileSystem) =>
        new(fileSystem, TierCapture.SystemAppData);

    /// <param name="plugins">The snapshot's plugins.json, or null when it never recorded one.</param>
    public TierRestoreResult Restore(Snapshot snapshot, PluginManifest? plugins, RestoreOptions options)
    {
        var skipped = new List<string>();
        var presets = 0;
        var binaries = 0;
        var needsElevation = false;

        if (options.Presets)
        {
            foreach (var (relative, _) in snapshot.Manifest.Files)
            {
                if (!relative.StartsWith(PresetFiles.RelativeRoot + "/", StringComparison.OrdinalIgnoreCase))
                    continue;

                // presets/FabFilter/Pro-Q 4/x.ffp -> %APPDATA%\FabFilter\Pro-Q 4\x.ffp. The
                // capture mirrored the tree from the vendor folder down, so the mapping back is
                // the same path with the root swapped - no name is re-derived or guessed.
                var under = relative[(PresetFiles.RelativeRoot.Length + 1)..];
                var destination = Path.Combine(appDataPath, under.Replace('/', Path.DirectorySeparatorChar));

                if (Copy(SnapshotManifest.PathIn(snapshot.Directory, relative), destination, skipped, ref needsElevation))
                {
                    presets++;
                }
            }
        }

        if (options.PluginBinaries && plugins is not null)
        {
            foreach (var plugin in plugins.Plugins)
            {
                if (plugin.BinaryPath is not { } root || plugin.FilePath.Length == 0) continue;

                foreach (var (relative, _) in snapshot.Manifest.Files)
                {
                    if (!IsUnder(relative, root)) continue;

                    // A bundle keeps its shape: everything under plugins/Name.vst3/ goes back
                    // under the recorded FilePath, which IS the bundle directory.
                    var tail = relative.Length == root.Length ? string.Empty : relative[(root.Length + 1)..];
                    var destination = tail.Length == 0
                        ? plugin.FilePath
                        : Path.Combine(plugin.FilePath, tail.Replace('/', Path.DirectorySeparatorChar));

                    if (Copy(SnapshotManifest.PathIn(snapshot.Directory, relative), destination, skipped, ref needsElevation))
                    {
                        binaries++;
                    }
                }
            }
        }

        return new TierRestoreResult(presets, binaries, skipped, needsElevation);
    }

    /// <summary>Exactly the file, or its children — never a directory that merely shares a prefix.</summary>
    private static bool IsUnder(string relative, string root) =>
        string.Equals(relative, root, StringComparison.OrdinalIgnoreCase)
        || relative.StartsWith(root + "/", StringComparison.OrdinalIgnoreCase);

    private bool Copy(string source, string destination, List<string> skipped, ref bool needsElevation)
    {
        try
        {
            if (Path.GetDirectoryName(destination) is { Length: > 0 } parent)
            {
                fileSystem.CreateDirectory(parent);
            }

            fileSystem.WriteBytes(destination, fileSystem.ReadSharedBytes(source));
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            // The signature of Program Files without administrator rights. Named separately from
            // an IOException because the answer is different: one is "try again elevated", the
            // other is "something else has it".
            needsElevation = true;
            skipped.Add(destination);
            return false;
        }
        catch (IOException)
        {
            skipped.Add(destination);
            return false;
        }
    }
}
