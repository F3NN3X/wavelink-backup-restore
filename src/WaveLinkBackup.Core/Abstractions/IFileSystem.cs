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

    /// <summary>Creates the directory and any missing parents. No-op if it exists.</summary>
    void CreateDirectory(string path);

    /// <summary>Deletes a directory and everything in it.</summary>
    void DeleteDirectory(string path);

    /// <summary>
    /// Moves a directory. Used by the two-stage delete, where it is what makes deletion
    /// reversible without any shell interop.
    /// </summary>
    void MoveDirectory(string source, string destination);

    void WriteBytes(string path, byte[] bytes);

    /// <summary>Atomic on NTFS, and produces the rollback copy in the same operation.</summary>
    void Replace(string source, string destination, string backup);

    void Delete(string path);

    /// <summary>
    /// Bytes available to this user on the volume holding <paramref name="path"/>, or null when
    /// it cannot be determined.
    ///
    /// Null rather than 0 or a throw: the design's bottom bar reads
    /// "4 BACKUPS · 12.4 MB USED · 118 GB FREE ON THIS DRIVE", and omitting the figure is
    /// honest where printing 0 would quietly claim a full disk.
    /// </summary>
    long? GetAvailableFreeBytes(string path);
}
