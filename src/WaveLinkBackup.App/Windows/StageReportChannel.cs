using System.IO;
using System.IO.Pipes;

namespace WaveLinkBackup.App.Windows;

/// <summary>
/// The line-by-line channel by which the elevated restore copy tells the shell that started it what
/// step it has just finished, so the in-progress strip advances live instead of sitting on "Closing
/// Wave Link" until the whole restore is done (the elevation spec).
///
/// A named pipe rather than redirected stdout: the child is started with <c>UseShellExecute</c> and
/// the <c>runas</c> verb, and stream redirection is incompatible with that combination - it throws
/// before the process even starts. A named pipe needs no redirection at all; the two processes meet
/// on a well-known name derived from the restore id, which works across the UAC boundary because
/// both run as the same local user on the same machine.
///
/// The whole channel is best-effort. Progress is a cosmetic nicety layered over a restore whose
/// verdict is carried by the exit code; nothing in this class may ever throw into the restore path,
/// and a child that cannot reach the pipe (an older build, a timing race) simply reports nothing and
/// the strip stays at "Closing Wave Link" - which is still true.
/// </summary>
public static class StageReportChannel
{
    /// <summary>The marker prefix both ends agree on; anything else on the pipe is ignored.</summary>
    public const string Marker = "WLRESTORE-STAGE";

    /// <summary>
    /// The well-known pipe name for a restore, derived from its snapshot id. Both the shell and the
    /// elevated copy compute it from the same id they already pass on the command line, so no extra
    /// argument is needed to find each other.
    /// </summary>
    public static string NameFor(string snapshotId) =>
        $@"wavelink-restore-{snapshotId}";

    /// <summary>
    /// The shell's half: create the pipe and read one line at a time as the child writes them,
    /// handing each to <paramref name="onLine"/>. Returns as soon as the child closes its end - which
    /// is when the elevated copy exits - so the caller can read this on the same background thread
    /// that waits for the process and stop it with <paramref name="ct"/>.
    ///
    /// <paramref name="onLine"/> runs on the calling (background) thread, never the UI thread; a
    /// caller that touches view state marshals it back itself. Any failure to create or read the
    /// pipe is swallowed: no progress is better than a restore that dies over a cosmetic channel.
    /// </summary>
    public static void ReadLines(string snapshotId, Action<string> onLine, CancellationToken ct)
    {
        try
        {
            using var server = new NamedPipeServerStream(
                NameFor(snapshotId), PipeDirection.In, 1,
                PipeTransmissionMode.Byte, PipeOptions.Asynchronous);

            // Block until the child connects. The parent starts the pipe before it asks Windows to
            // launch the elevated copy, so by the time the child tries to connect this is already
            // waiting - and if the child never comes (declined prompt, start failure) the wait ends
            // with the cancellation token firing when the caller gives up.
            server.WaitForConnectionAsync(ct).GetAwaiter().GetResult();

            using var reader = new StreamReader(server);
            string? line;
            while ((line = reader.ReadLine()) is not null)
            {
                ct.ThrowIfCancellationRequested();
                onLine(line);
            }
        }
        catch (OperationCanceledException) { /* the caller stopped waiting; nothing to report */ }
        catch (Exception) { /* best-effort: a broken progress channel must never fail the restore */ }
    }

    /// <summary>
    /// The elevated copy's half: connect to the shell's pipe and write one line per completed step.
    /// Returns null when the shell is not there to listen, in which case the caller simply reports
    /// nothing - the restore proceeds exactly as it would have without this channel.
    /// </summary>
    public static StreamWriter? Connect(string snapshotId)
    {
        try
        {
            var client = new NamedPipeClientStream(".", NameFor(snapshotId), PipeDirection.Out);
            // The shell creates the pipe before launching us, so it is already waiting; a short
            // timeout guards against the rare case where it has not, and costs nothing on the happy
            // path because the connect succeeds immediately.
            client.Connect(2000);
            return new StreamWriter(client) { AutoFlush = true };
        }
        catch (Exception)
        {
            // No shell to report to - decline quietly rather than throw into the restore.
            return null;
        }
    }

    /// <summary>Write one stage marker line if a channel is open; a no-op when there is none.</summary>
    public static void Report(StreamWriter? channel, string step)
    {
        try
        {
            channel?.WriteLine($"{Marker} {step}");
        }
        catch (Exception)
        {
            // The pipe broke mid-restore; drop progress rather than fail the restore over it.
        }
    }
}
