using System.Runtime.InteropServices;

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

    /// <summary>
    /// 0 rather than a throw for a file that has gone between the enumeration and the question.
    /// Measuring a tier walks directories that Wave Link and plug-in installers also write to,
    /// so a file disappearing mid-walk is ordinary rather than exceptional.
    /// </summary>
    public long GetFileSize(string path)
    {
        try
        {
            var info = new FileInfo(path);
            return info.Exists ? info.Length : 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return 0;
        }
    }

    public byte[] ReadSharedBytes(string path)
    {
        using var stream = OpenShared(path);

        var bytes = new byte[stream.Length];
        stream.ReadExactly(bytes);
        return bytes;
    }

    /// <summary>
    /// The one place the share mode is chosen. FileShare.ReadWrite permits Wave Link's existing
    /// handle; FileShare.Delete additionally tolerates the file being replaced underneath us,
    /// which is exactly what Wave Link's own atomic-save does.
    /// </summary>
    private static FileStream OpenShared(string path) =>
        new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

    public string ReadSharedText(string path) =>
        System.Text.Encoding.UTF8.GetString(ReadSharedBytes(path));

    /// <summary>
    /// Opens and closes. Every way the open can fail is the answer "no", including the file
    /// having gone between the enumeration and the question.
    /// </summary>
    public bool CanReadShared(string path)
    {
        try
        {
            using var stream = OpenShared(path);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// 1 MiB at a time, through an incremental hash. Peak memory is the buffer, not the file, so
    /// a 24 MB plug-in and a 600 MB sample library cost the same.
    ///
    /// Exceptions are NOT caught: the caller decides what a failed copy costs, and every caller
    /// today has a different answer.
    /// </summary>
    public FileCopy CopyFile(string source, string destination)
    {
        using var reader = OpenShared(source);
        using var writer = new FileStream(
            destination, FileMode.Create, FileAccess.Write, FileShare.None);
        using var hash = System.Security.Cryptography.IncrementalHash.CreateHash(
            System.Security.Cryptography.HashAlgorithmName.SHA256);

        var buffer = new byte[1024 * 1024];
        long total = 0;

        for (int read; (read = reader.Read(buffer, 0, buffer.Length)) > 0;)
        {
            hash.AppendData(buffer, 0, read);
            writer.Write(buffer, 0, read);
            total += read;
        }

        return new FileCopy(Convert.ToHexStringLower(hash.GetHashAndReset()), total);
    }

    public void CreateDirectory(string path) => Directory.CreateDirectory(path);

    public void DeleteDirectory(string path)
    {
        if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
    }

    public void MoveDirectory(string source, string destination) =>
        Directory.Move(source, destination);

    public void WriteBytes(string path, byte[] bytes) => File.WriteAllBytes(path, bytes);

    public void Replace(string source, string destination, string backup) =>
        File.Replace(source, destination, backup, ignoreMetadataErrors: true);

    public void Delete(string path)
    {
        if (File.Exists(path)) File.Delete(path);
    }

    /// <summary>
    /// GetDiskFreeSpaceEx rather than DriveInfo, which throws on UNC paths. Keeping backups on
    /// a NAS is supported — it is the reason deletion goes to .trash instead of the Recycle Bin
    /// at all (screens/10-decisions.md 3) — so a UNC store has to report free space like any
    /// other volume.
    ///
    /// Probes the first EXISTING ancestor of the path, because the store directory may not have
    /// been created yet the first time the shell draws the bottom bar.
    ///
    /// DllImport rather than LibraryImport, matching <see cref="RecycleBin"/>: the generator
    /// requires AllowUnsafeBlocks for the whole project.
    /// </summary>
    public long? GetAvailableFreeBytes(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;

        string? probe;
        try
        {
            probe = Path.GetFullPath(path);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }

        for (; !string.IsNullOrEmpty(probe); probe = Path.GetDirectoryName(probe))
        {
            if (!Directory.Exists(probe)) continue;

            return GetDiskFreeSpaceExW(probe, out var available, out _, out _)
                ? (long)available
                : null;
        }

        return null;
    }

    [DllImport("kernel32.dll", EntryPoint = "GetDiskFreeSpaceExW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetDiskFreeSpaceExW(
        string directoryName,
        out ulong freeBytesAvailableToCaller,
        out ulong totalNumberOfBytes,
        out ulong totalNumberOfFreeBytes);
}
