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
    /// A file's length, or 0 when it cannot be determined.
    ///
    /// Exists so that "how big would a backup be?" can be answered without reading 40 MB of
    /// plug-in binaries into memory — the Settings dialog asks that question every time it
    /// opens, and tiers 3 and 4 are large enough that measuring by reading would be felt.
    /// </summary>
    long GetFileSize(string path);

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

    /// <summary>
    /// Whether the file can be opened for reading right now, under
    /// <see cref="ReadSharedBytes"/>'s share mode.
    ///
    /// Exists so a caller can decide a tier's fate — tier 4 is all or nothing — without reading
    /// the bytes to find out. It opens a handle and closes it; it reads nothing.
    /// </summary>
    bool CanReadShared(string path);

    /// <summary>
    /// Copies a file without either end of it being held in memory, hashing the bytes as they
    /// pass, and returns what the manifest needs to record.
    ///
    /// **Both halves matter.** A sample-library instrument runs to hundreds of megabytes and
    /// nothing stops one being on a channel, so <see cref="ReadSharedBytes"/> into
    /// <see cref="WriteBytes"/> puts the whole file on the heap twice. Returning the hash from the
    /// same pass is what stops the caller reading it a second time to compute one.
    ///
    /// Reads with <see cref="ReadSharedBytes"/>'s share mode, for the same reason.
    /// </summary>
    FileCopy CopyFile(string source, string destination);

    /// <summary>Creates the directory and any missing parents. No-op if it exists.</summary>
    void CreateDirectory(string path);

    /// <summary>
    /// Whether this process could write a file into <paramref name="directory"/> **right now**,
    /// as it is currently running.
    ///
    /// Asked rather than assumed, because the assumption is wrong more often than it looks.
    /// `C:\Program Files\Common Files\VST3` is not user-writable by Windows' default ACL — but
    /// several audio plug-in installers loosen it so their own updates need no administrator, and
    /// on a machine where one has, tier 4 restores perfectly well with no prompt at all. Deciding
    /// from the path alone means prompting people who did not need to be asked.
    ///
    /// **Probes by writing**, not by reading the ACL. An effective-permissions calculation has to
    /// account for group membership, inherited denies, UAC's filtered token and the odd
    /// virtualisation case; a temp file in the target directory answers the question that is
    /// actually being asked. A missing directory reports whether its nearest existing ancestor
    /// would accept the creation.
    /// </summary>
    bool CanWriteDirectory(string directory);

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

/// <summary>What a <see cref="IFileSystem.CopyFile"/> wrote, as the manifest records it.</summary>
/// <param name="Sha256">Lowercase hex, computed over the bytes as they were copied.</param>
public readonly record struct FileCopy(string Sha256, long SizeBytes);
