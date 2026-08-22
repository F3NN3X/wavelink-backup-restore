using System;
using System.IO;
using WaveLinkBackup.Core.Abstractions;

namespace WaveLinkBackup.App.Hosting;

/// <summary>
/// Writes an unhandled exception to a file beside shell.json, on the way down.
///
/// This is the cheap half of technical-debt.md §8.1. It needs no design: it turns "the app
/// crashed" into a report that names the line, and it runs before the process is gone, so the
/// information cannot be lost to the Windows event log. The expensive half — a thirteenth error
/// surface in XAML — stays open until [[ADR-004]]'s design question is answered; this file write
/// unblocks that pass by giving it real exception shapes to look at instead of guesses.
///
/// It NEVER throws. A crash handler that throws on a crash path is worse than no handler: the
/// original incident (the app vanishing with nothing behind it) is exactly what this exists to
/// prevent, and an exception escaping here would put us back there.
/// </summary>
public sealed class CrashReportWriter(IFileSystem fileSystem, string directoryPath)
{
    /// <summary>
    /// The report's name. It sits beside shell.json in %LOCALAPPDATA%\WaveLinkBackup, which is
    /// where the user (or a "copy diagnostics" action) will look first — and it is a DIFFERENT
    /// file from settings.json and shell.json, so a crash can never corrupt either of those.
    /// </summary>
    public const string FileName = "crash-report.txt";

    private readonly object gate = new();

    public string FilePath => Path.Combine(directoryPath, FileName);

    /// <summary>
    /// Appends one exception to the report and returns its path, or null when it could not be
    /// written. Appending rather than overwriting means a process that throws twice leaves both
    /// reports — the second crash is usually the interesting one, and the first is what led to it.
    /// </summary>
    public string? Write(Exception exception)
    {
        // Serialized: the dispatcher handler and the AppDomain handler can both fire for the same
        // fault, and two writers into one file would interleave mid-line.
        lock (gate)
        {
            try
            {
                fileSystem.CreateDirectory(directoryPath);

                var header =
                    $"--- unhandled exception at {DateTimeOffset.Now:u} ---" + Environment.NewLine;

                // ToString, not just the type name: the stack is the difference between "it
                // crashed" and "it crashed HERE". The type name is still present (first line of
                // ToString), so a report that only gets its first line still names the fault.
                var body = exception.ToString() + Environment.NewLine;

                if (!fileSystem.FileExists(FilePath))
                {
                    fileSystem.WriteBytes(FilePath, ToUtf8(header));
                }
                else
                {
                    AppendUtf8(fileSystem, FilePath, header);
                }

                AppendUtf8(fileSystem, FilePath, body);
                return FilePath;
            }
            catch
            {
                // Best effort. If the directory is unwritable or the disk is full, the crash still
                // ends the process — but it does not end it having thrown a SECOND exception out of
                // the handler that was supposed to be its last act.
                return null;
            }
        }
    }

    private static byte[] ToUtf8(string text) => System.Text.Encoding.UTF8.GetBytes(text);

    /// <summary>
    /// Appends UTF-8 bytes to a file through IFileSystem, which has no Append method: read what is
    /// there, add the new bytes, write it back. The report is small (a stack trace is kilobytes),
    /// so the read-modify-write is not a cost worth a wider seam for.
    /// </summary>
    private static void AppendUtf8(IFileSystem fileSystem, string path, string text)
    {
        var existing = fileSystem.FileExists(path) ? fileSystem.ReadSharedBytes(path) : Array.Empty<byte>();
        var addition = ToUtf8(text);

        var combined = new byte[existing.Length + addition.Length];
        Buffer.BlockCopy(existing, 0, combined, 0, existing.Length);
        Buffer.BlockCopy(addition, 0, combined, existing.Length, addition.Length);

        fileSystem.WriteBytes(path, combined);
    }
}
