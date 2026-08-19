using WaveLinkBackup.Core.Abstractions;

namespace WaveLinkBackup.Core.Capture;

/// <summary>
/// A file on disk that a tier wants, and where it goes inside the snapshot.
/// </summary>
/// <param name="RelativePath">
/// Forward slashes, always. It becomes a key in the manifest's <c>files</c> map and a path under
/// the snapshot directory, and one spelling for both is what lets <c>SnapshotGuard</c> verify
/// every tier's files with no knowledge of which tier they came from.
/// </param>
public sealed record SourceFile(string Path, string RelativePath, long SizeBytes);

/// <summary>
/// Walks a directory through the filesystem seam. Every tier that captures more than one file
/// goes through here, so "what does a recursive capture mean" is answered once.
/// </summary>
public static class FileTree
{
    /// <summary>
    /// Deep enough for any real plugin bundle or preset library, shallow enough that a directory
    /// junction pointing at its own ancestor cannot spin. Nothing legitimate is 32 deep.
    /// </summary>
    public const int MaxDepth = 32;

    /// <summary>
    /// Every file under <paramref name="root"/>, deepest-last and sorted, with each mapped under
    /// <paramref name="relativeRoot"/>.
    ///
    /// Sorted because the manifest records these by name: an unordered walk would make two
    /// identical captures produce different-looking manifests, and a diff between snapshots is
    /// something a person reads.
    ///
    /// Never throws. A directory that vanishes mid-walk yields what was found before it went -
    /// the alternative is a capture that fails because a plugin installer happened to be running.
    /// </summary>
    public static IReadOnlyList<SourceFile> Walk(IFileSystem fileSystem, string root, string relativeRoot)
    {
        var found = new List<SourceFile>();
        Walk(fileSystem, root, relativeRoot, depth: 0, found);
        return found;
    }

    private static void Walk(
        IFileSystem fileSystem, string directory, string relativeRoot, int depth, List<SourceFile> found)
    {
        if (depth > MaxDepth || !fileSystem.DirectoryExists(directory)) return;

        IReadOnlyList<string> files;
        IReadOnlyList<string> directories;
        try
        {
            files = fileSystem.EnumerateFiles(directory, "*");
            directories = fileSystem.EnumerateDirectories(directory, "*");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return;
        }

        foreach (var file in files.OrderBy(System.IO.Path.GetFileName, StringComparer.OrdinalIgnoreCase))
        {
            var name = System.IO.Path.GetFileName(file);
            if (name.Length == 0) continue;

            found.Add(new SourceFile(file, $"{relativeRoot}/{name}", fileSystem.GetFileSize(file)));
        }

        foreach (var child in directories.OrderBy(System.IO.Path.GetFileName, StringComparer.OrdinalIgnoreCase))
        {
            var name = System.IO.Path.GetFileName(child);
            if (name.Length == 0) continue;

            Walk(fileSystem, child, $"{relativeRoot}/{name}", depth + 1, found);
        }
    }

    /// <summary>Total bytes, for the "what would this cost?" question the Settings dialog asks.</summary>
    public static long TotalBytes(IEnumerable<SourceFile> files) => files.Sum(f => f.SizeBytes);
}
