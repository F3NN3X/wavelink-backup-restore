using System.Text;
using WaveLinkBackup.Core.Abstractions;

namespace WaveLinkBackup.Core.Tests.Fakes;

/// <summary>
/// An in-memory filesystem. Paths are compared case-insensitively, as on Windows.
///
/// It records how many times each file was read, because "retry exactly once" is a
/// behaviour worth asserting rather than assuming.
/// </summary>
public sealed class FakeFileSystem : IFileSystem
{
    private readonly Dictionary<string, byte[]> files = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> directories = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, int> ReadCounts { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Queued per-path failures, popped one per read. Models a locked or torn file.</summary>
    public Dictionary<string, Queue<Exception>> ReadFailures { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Queued per-path byte payloads, popped one per read. Models a file changing under us.</summary>
    public Dictionary<string, Queue<byte[]>> ReadSequence { get; } = new(StringComparer.OrdinalIgnoreCase);

    public List<(string Source, string Destination, string Backup)> Replacements { get; } = [];

    /// <summary>Models an unwritable store location — a read-only drive, or a denied path.</summary>
    public bool FailDirectoryCreation { get; init; }

    /// <summary>Models one directory locked by a sync client while its neighbours are fine.</summary>
    public string? FailDirectoryDeleteFor { get; set; }

    /// <summary>
    /// What <see cref="GetAvailableFreeBytes"/> reports. Null - the default - models a volume
    /// whose free space cannot be determined, which the bottom bar renders by omitting it.
    /// </summary>
    public long? FreeBytes { get; set; }

    public FakeFileSystem AddFile(string path, string content) => AddFile(path, Encoding.UTF8.GetBytes(content));

    public FakeFileSystem AddFile(string path, byte[] content)
    {
        files[path] = content;
        for (var dir = Path.GetDirectoryName(path); !string.IsNullOrEmpty(dir); dir = Path.GetDirectoryName(dir))
        {
            directories.Add(dir);
        }
        return this;
    }

    public FakeFileSystem AddDirectory(string path)
    {
        directories.Add(path);
        return this;
    }

    public byte[] Read(string path) => files[path];

    public bool DirectoryExists(string path) => directories.Contains(path);

    public bool FileExists(string path) => files.ContainsKey(path);

    public IReadOnlyList<string> EnumerateDirectories(string path, string pattern)
    {
        if (!directories.Contains(path)) return [];

        var regex = Glob(pattern);
        return [.. directories
            .Where(d => string.Equals(Path.GetDirectoryName(d), path, StringComparison.OrdinalIgnoreCase))
            .Where(d => regex(Path.GetFileName(d)))
            .Order(StringComparer.OrdinalIgnoreCase)];
    }

    public IReadOnlyList<string> EnumerateFiles(string path, string pattern)
    {
        var regex = Glob(pattern);
        return [.. files.Keys
            .Where(f => string.Equals(Path.GetDirectoryName(f), path, StringComparison.OrdinalIgnoreCase))
            .Where(f => regex(Path.GetFileName(f)))
            .Order(StringComparer.OrdinalIgnoreCase)];
    }

    /// <summary>
    /// The default is one fixed instant for every file. <see cref="SetLastWriteTimeUtc"/> gives a
    /// file its own, which is what "keep the newest ten" needs to be a test rather than an
    /// assumption.
    /// </summary>
    public Dictionary<string, DateTime> LastWriteTimes { get; } = new(StringComparer.OrdinalIgnoreCase);

    public FakeFileSystem SetLastWriteTimeUtc(string path, DateTime utc)
    {
        LastWriteTimes[path] = utc;
        return this;
    }

    public DateTime GetLastWriteTimeUtc(string path) =>
        LastWriteTimes.TryGetValue(path, out var written)
            ? written
            : new DateTime(2026, 8, 16, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>Never throws for an absent file — the real one does not either.</summary>
    public long GetFileSize(string path) => files.TryGetValue(path, out var content) ? content.LongLength : 0;

    public byte[] ReadSharedBytes(string path)
    {
        ReadCounts[path] = ReadCounts.GetValueOrDefault(path) + 1;

        if (ReadFailures.TryGetValue(path, out var failures) && failures.Count > 0) throw failures.Dequeue();
        if (ReadSequence.TryGetValue(path, out var sequence) && sequence.Count > 0) return sequence.Dequeue();

        return files.TryGetValue(path, out var content)
            ? content
            : throw new FileNotFoundException("No such file in the fake filesystem.", path);
    }

    public string ReadSharedText(string path) => Encoding.UTF8.GetString(ReadSharedBytes(path));

    public void CreateDirectory(string path)
    {
        if (FailDirectoryCreation) throw new UnauthorizedAccessException($"Access to '{path}' is denied.");

        for (var dir = path; !string.IsNullOrEmpty(dir); dir = Path.GetDirectoryName(dir))
        {
            directories.Add(dir);
        }
    }

    public void MoveDirectory(string source, string destination)
    {
        var sourcePrefix = source.TrimEnd('\\') + "\\";
        var destPrefix = destination.TrimEnd('\\') + "\\";

        foreach (var file in files.Keys.Where(f => f.StartsWith(sourcePrefix, StringComparison.OrdinalIgnoreCase)).ToList())
        {
            var moved = destPrefix + file[sourcePrefix.Length..];
            AddFile(moved, files[file]);
            files.Remove(file);
        }

        foreach (var dir in directories.Where(d =>
                     string.Equals(d, source, StringComparison.OrdinalIgnoreCase) ||
                     d.StartsWith(sourcePrefix, StringComparison.OrdinalIgnoreCase)).ToList())
        {
            directories.Remove(dir);
        }

        directories.Add(destination);
    }

    public void DeleteDirectory(string path)
    {
        if (FailDirectoryDeleteFor is not null &&
            string.Equals(FailDirectoryDeleteFor, path, StringComparison.OrdinalIgnoreCase))
        {
            throw new IOException($"'{path}' is in use by another process.");
        }

        var prefix = path.TrimEnd('\\') + "\\";

        foreach (var file in files.Keys.Where(f => f.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToList())
        {
            files.Remove(file);
        }

        foreach (var dir in directories.Where(d =>
                     string.Equals(d, path, StringComparison.OrdinalIgnoreCase) ||
                     d.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToList())
        {
            directories.Remove(dir);
        }
    }

    /// <summary>
    /// Queued per-path failures, popped one per write. Models the two that read differently to a
    /// user: an UnauthorizedAccessException is Program Files without administrator rights, an
    /// IOException is something else holding the file.
    /// </summary>
    public Dictionary<string, Queue<Exception>> WriteFailures { get; } = new(StringComparer.OrdinalIgnoreCase);

    public void WriteBytes(string path, byte[] bytes)
    {
        if (WriteFailures.TryGetValue(path, out var failures) && failures.Count > 0) throw failures.Dequeue();

        AddFile(path, bytes);
    }

    public void Replace(string source, string destination, string backup)
    {
        if (!files.ContainsKey(source)) throw new FileNotFoundException("No temp file to replace with.", source);

        Replacements.Add((source, destination, backup));
        if (files.TryGetValue(destination, out var previous)) files[backup] = previous;
        files[destination] = files[source];
        files.Remove(source);
    }

    public void Delete(string path) => files.Remove(path);

    public long? GetAvailableFreeBytes(string path) => FreeBytes;

    private static Func<string, bool> Glob(string pattern)
    {
        var prefix = pattern.TrimEnd('*');
        return pattern.EndsWith('*')
            ? name => name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            : name => string.Equals(name, pattern, StringComparison.OrdinalIgnoreCase);
    }
}
