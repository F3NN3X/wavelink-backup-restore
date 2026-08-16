namespace WaveLinkBackup.Core.Abstractions;

/// <summary>
/// The real filesystem. Thin by design: everything interesting is in the callers, which is
/// what makes them testable.
/// </summary>
public sealed class FileSystem : IFileSystem
{
    public bool DirectoryExists(string path) => Directory.Exists(path);

    public bool FileExists(string path) => File.Exists(path);

    public IReadOnlyList<string> EnumerateDirectories(string path, string pattern) =>
        Directory.Exists(path)
            ? [.. Directory.EnumerateDirectories(path, pattern, SearchOption.TopDirectoryOnly)]
            : [];

    public IReadOnlyList<string> EnumerateFiles(string path, string pattern) =>
        Directory.Exists(path)
            ? [.. Directory.EnumerateFiles(path, pattern, SearchOption.TopDirectoryOnly)]
            : [];

    public DateTime GetLastWriteTimeUtc(string path) => File.GetLastWriteTimeUtc(path);

    public byte[] ReadSharedBytes(string path)
    {
        // FileShare.ReadWrite permits Wave Link's existing handle; FileShare.Delete
        // additionally tolerates the file being replaced underneath us, which is exactly
        // what Wave Link's own atomic-save does.
        using var stream = new FileStream(
            path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

        var bytes = new byte[stream.Length];
        stream.ReadExactly(bytes);
        return bytes;
    }

    public string ReadSharedText(string path) =>
        System.Text.Encoding.UTF8.GetString(ReadSharedBytes(path));

    public void CreateDirectory(string path) => Directory.CreateDirectory(path);

    public void DeleteDirectory(string path)
    {
        if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
    }

    public void WriteBytes(string path, byte[] bytes) => File.WriteAllBytes(path, bytes);

    public void Replace(string source, string destination, string backup) =>
        File.Replace(source, destination, backup, ignoreMetadataErrors: true);

    public void Delete(string path)
    {
        if (File.Exists(path)) File.Delete(path);
    }
}
