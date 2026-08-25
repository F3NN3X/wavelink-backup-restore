using System;
using System.IO;
using System.Runtime.InteropServices;
using WaveLinkBackup.Core.Abstractions;
using WaveLinkBackup.Core.Analysis;

namespace WaveLinkBackup.App.Hosting;

/// <summary>
/// Writes an unhandled exception to a file beside shell.json, on the way down.
///
/// This is the cheap half of technical-debt.md §8.1, and it carries the design answer for §8.1a:
/// the crash's *surface* is this report, redacted, plus a pointer to it wherever the app can still
/// speak (the restore-failure strip). No thirteenth error surface in XAML — the package specifies
/// twelve errors and none of them is "something unexpected happened", and inventing one is what
/// [[ADR-004]] exists to prevent. What IS owed on a fault is evidence that names the line, and it
/// must be easy to investigate: every report carries an environment block (version, OS, culture,
/// runtime) beside the exception, so a pasted report answers "which build, on what machine" before
/// anyone asks.
///
/// The stack goes through <see cref="Redaction"/>. A stack trace names absolute paths —
/// <c>C:\Users\joran\AppData\Local\…</c> — and this file is the thing a user pastes into a public
/// tracker when they attach anything at all. Redaction strips the username and any endpoint serial
/// the way "copy diagnostics" does, so the report is safe to share by construction rather than by
/// whoever attaches it remembering. If redaction itself faults on a shape it has never seen, the
/// unredacted text is written with a marker instead: a report that cannot be produced is worse than
/// one that over-shares, and the marker tells us the redactor needs the new shape.
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
    public string? Write(Exception exception) => Write(exception, appVersion: null, userName: null);

    /// <param name="appVersion">
    /// The running build, when the caller knows it (the shell does). A report that names its own
    /// version answers half of every "can you reproduce this?" before the question is asked.
    /// </param>
    /// <param name="userName">
    /// The Windows username to strip from paths in the stack. Defaults to the current user when
    /// null — passed explicitly so a test can pin what gets redacted without depending on who runs it.
    /// </param>
    public string? Write(Exception exception, string? appVersion, string? userName)
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
                //
                // Redacted on the way out — see the type's summary for why the stack cannot leave
                // this file naming the user's profile folder.
                var body = Redact(exception.ToString(), userName) + Environment.NewLine;

                if (!fileSystem.FileExists(FilePath))
                {
                    fileSystem.WriteBytes(FilePath, ToUtf8(header));
                }
                else
                {
                    AppendUtf8(fileSystem, FilePath, header);
                }

                AppendUtf8(fileSystem, FilePath, body);

                // The environment block comes AFTER the exception: a report that is truncated
                // mid-write still carries the fault first, which is what makes it worth having.
                AppendUtf8(fileSystem, FilePath, EnvironmentBlock(appVersion));
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

    /// <summary>
    /// The block that answers "which build, on what machine" without anyone asking: version, OS,
    /// culture and runtime. Every value is best-effort — a field that cannot be read says
    /// "unknown" rather than throwing out of the handler.
    /// </summary>
    private string EnvironmentBlock(string? appVersion)
    {
        var lines = new List<string>(4);

        void Line(string label, Func<string?> value) =>
            lines.Add($"{label}: {Safe(value)}");

        Line("App", () => appVersion);
        Line("OS", () => Environment.OSVersion.ToString());
        Line("Culture", () => System.Globalization.CultureInfo.CurrentCulture.Name);
        Line("Runtime", () => RuntimeInformation.FrameworkDescription);

        return "--- environment ---" + Environment.NewLine
            + string.Join(Environment.NewLine, lines) + Environment.NewLine;
    }

    private static string Safe(Func<string?> value)
    {
        try
        {
            var result = value();
            return string.IsNullOrWhiteSpace(result) ? "unknown" : result!;
        }
        catch
        {
            // A field that faults is a missing fact, not a reason to abandon the report.
            return "unknown";
        }
    }

    /// <summary>
    /// Runs the exception text through Core's <see cref="Redaction"/> — the same rule "copy
    /// diagnostics" uses, so the report and the clipboard report share one definition of safe.
    /// Fails OPEN with a marker rather than dropping the stack: the investigation value of a
    /// redacted-but-missing trace is lower than its privacy cost, but NO trace at all loses the
    /// report's entire purpose, and the marker tells us the redactor met a shape it must learn.
    /// </summary>
    private static string Redact(string text, string? userName)
    {
        var user = userName ?? Redaction.CurrentUserName;

        try
        {
            return Redaction.Text(text, user);
        }
        catch
        {
            // Only reachable if Redaction itself faults on an unrecognised shape. The marker is
            // deliberate: a silent pass-through would be the exact failure this exists to prevent.
            return "[redaction failed — text below is unredacted]" + Environment.NewLine + text;
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
