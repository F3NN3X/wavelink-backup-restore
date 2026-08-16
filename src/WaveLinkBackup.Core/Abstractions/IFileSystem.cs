namespace WaveLinkBackup.Core.Abstractions;

/// <summary>
/// Every disk touch in Core goes through here. One of two seams (ADR-004); the clock
/// upstream carries is deferred to phase 2, where snapshot timestamps first need it.
/// </summary>
public interface IFileSystem
{
    bool DirectoryExists(string path);

    bool FileExists(string path);

    IReadOnlyList<string> EnumerateDirectories(string path, string pattern);

    IReadOnlyList<string> EnumerateFiles(string path, string pattern);

    DateTime GetLastWriteTimeUtc(string path);

    /// <summary>
    /// Reads with FileShare.ReadWrite | FileShare.Delete.
    ///
    /// A named method rather than a general Open(path, share) on purpose: callers cannot
    /// pick the wrong share mode because they never pick one. Settings.json is locked
    /// while Wave Link runs - which is when most captures happen - so File.ReadAllBytes
    /// fails with "being used by another process".
    /// See _docs/knowledge-base/gotchas/capture-fails-while-wave-link-is-running.md
    /// </summary>
    byte[] ReadSharedBytes(string path);

    /// <summary><see cref="ReadSharedBytes"/> as UTF-8 text. Used for log files.</summary>
    string ReadSharedText(string path);

    void WriteBytes(string path, byte[] bytes);

    /// <summary>Atomic on NTFS, and produces the rollback copy in the same operation.</summary>
    void Replace(string source, string destination, string backup);

    void Delete(string path);
}
