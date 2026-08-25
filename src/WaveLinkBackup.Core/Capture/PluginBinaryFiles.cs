using WaveLinkBackup.Core.Abstractions;
using WaveLinkBackup.Core.Analysis;
using WaveLinkBackup.Core.Snapshots;

namespace WaveLinkBackup.Core.Capture;

/// <param name="Found">
/// False when the path resolved to nothing, or to something that is there but empty. A capture
/// that produced zero bytes is a failure, not a success — that is the bug this tier exists to
/// catch, because a zero-byte "success" looks identical to a working backup until the day it is
/// restored.
/// </param>
/// <param name="IsBundle">
/// True when the `.vst3` was a DIRECTORY. The VST3 spec defines a bundle
/// (<c>Plugin.vst3\Contents\x86_64-win\Plugin.vst3</c>) and installers increasingly ship them that
/// way. Recorded so a fixture can assert the path was taken at all.
/// </param>
public sealed record BinaryDiscovery(bool Found, bool IsBundle, IReadOnlyList<SourceFile> Files)
{
    public static BinaryDiscovery Missing { get; } = new(false, false, []);

    public long Bytes => FileTree.TotalBytes(Files);
}

/// <summary>
/// A binary as tier 2 records it: what it hashed to, and the two figures that say whether that
/// hash is still current.
/// </summary>
/// <param name="Sha256">Null for every way the hash can fail — see <see cref="PluginBinaryFiles.HashOf(string)"/>.</param>
public readonly record struct BinaryIdentity(string? Sha256, long SizeBytes, DateTime? LastWriteUtc)
{
    /// <summary>The path resolved to nothing, or to a bundle, which hashes to null by design.</summary>
    public static BinaryIdentity Unknown { get; } = new(null, 0, null);
}

/// <summary>
/// Tier 4: the `.vst3` files themselves. The phase's defining risk
/// ([[vst3-backs-up-as-nothing]]).
///
/// A `.vst3` may be a directory. All six plugins on the reference machine are single files, so
/// the author's own setup can never exercise the bundle path — which is exactly why the directory
/// test comes FIRST and why a synthetic bundle fixture is a hard exit criterion rather than a
/// nice-to-have.
///
/// Copying a binary does not copy the licence, and this tier never pretends otherwise
/// ([[restored-plugin-demands-a-licence]]).
/// </summary>
public sealed class PluginBinaryFiles(IFileSystem fileSystem)
{
    /// <summary>Where they sit inside a snapshot.</summary>
    public const string RelativeRoot = "plugins";

    /// <summary>
    /// What tier 4 would copy for one plugin.
    ///
    /// <paramref name="taken"/> carries the relative roots already claimed in this capture, so two
    /// vendors both shipping `Clear.vst3` cannot overwrite each other inside the snapshot.
    /// </summary>
    public BinaryDiscovery Discover(ResolvedPlugin plugin, ISet<string> taken)
    {
        if (string.IsNullOrWhiteSpace(plugin.FilePath)) return BinaryDiscovery.Missing;

        var name = Path.GetFileName(plugin.FilePath.TrimEnd('\\', '/'));
        if (name.Length == 0) return BinaryDiscovery.Missing;

        var root = Unique(name, taken);

        // Directory FIRST. A bundle is a directory, and testing for a file first finds nothing,
        // reports success, and stores an empty tier.
        if (fileSystem.DirectoryExists(plugin.FilePath))
        {
            var files = FileTree.Walk(fileSystem, plugin.FilePath, $"{RelativeRoot}/{root}");

            // An empty bundle directory is the silent-success bug wearing its own clothes.
            return files.Count > 0 && FileTree.TotalBytes(files) > 0
                ? new BinaryDiscovery(true, IsBundle: true, files)
                : BinaryDiscovery.Missing;
        }

        if (!fileSystem.FileExists(plugin.FilePath)) return BinaryDiscovery.Missing;

        var size = fileSystem.GetFileSize(plugin.FilePath);
        if (size <= 0) return BinaryDiscovery.Missing;

        return new BinaryDiscovery(
            true,
            IsBundle: false,
            [new SourceFile(plugin.FilePath, $"{RelativeRoot}/{root}", size)]);
    }

    /// <summary>What tier 4 would cost, without reading 40 MB to find out.</summary>
    public long Measure(ResolvedPlugin plugin) =>
        Discover(plugin, new HashSet<string>(StringComparer.OrdinalIgnoreCase)).Bytes;

    /// <summary>
    /// The binary's SHA-256 as it stands right now, for tier 2's record of it — not an
    /// integrity claim about the snapshot. The binary lives outside the snapshot and a plugin
    /// update between capture and restore is a legitimate thing to happen; this is evidence for
    /// the drift check, which is why <c>SnapshotGuard</c> never verifies it.
    ///
    /// Null for every way it can fail: a plugin uninstalled since the settings last named it, a
    /// network volume that is not mounted, a binary locked by its own installer. Tier 2 is always
    /// on, so none of those may throw.
    ///
    /// A bundle hashes to null: what identifies a bundle is its whole tree, and recording the
    /// hash of nothing would be worse than recording that we have none.
    /// </summary>
    public string? HashOf(string filePath) => HashOf(filePath, previous: null).Sha256;

    /// <summary>
    /// The same, but reusing <paramref name="previous"/>'s hash when the binary has not been
    /// touched since that entry was written — same path, same length, same last-write time.
    ///
    /// This is a cache with an invalidation rule, and it exists because tier 2 is always on:
    /// every automatic capture the watcher fires used to read every referenced binary in full,
    /// ~40 MB on the reference rig, for a value that changes only when the user updates a plug-in
    /// (technical-debt.md §4.16). <see cref="PluginManifestEntry.BinaryMatches"/> is deliberately
    /// strict, so the failure direction is a needless hash rather than a stale one.
    ///
    /// Returns the size and time it measured as well, because they are what the NEXT capture
    /// compares against and re-measuring them would be a second round of stat calls.
    /// </summary>
    public BinaryIdentity HashOf(string filePath, PluginManifestEntry? previous)
    {
        if (string.IsNullOrWhiteSpace(filePath)) return BinaryIdentity.Unknown;
        if (fileSystem.DirectoryExists(filePath)) return BinaryIdentity.Unknown;
        if (!fileSystem.FileExists(filePath)) return BinaryIdentity.Unknown;

        var size = fileSystem.GetFileSize(filePath);
        var written = fileSystem.GetLastWriteTimeUtc(filePath);

        if (previous is not null && previous.BinaryMatches(size, written))
        {
            return new BinaryIdentity(previous.Sha256, size, written);
        }

        return new BinaryIdentity(Hash(filePath), size, written);
    }

    private string? Hash(string filePath)
    {
        try
        {
            return Convert.ToHexStringLower(
                System.Security.Cryptography.SHA256.HashData(fileSystem.ReadSharedBytes(filePath)));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static string Unique(string name, ISet<string> taken)
    {
        if (taken.Add(name)) return name;

        for (var attempt = 2; ; attempt++)
        {
            var candidate = $"{attempt}-{name}";
            if (taken.Add(candidate)) return candidate;
        }
    }
}
