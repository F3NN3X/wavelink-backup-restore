namespace WaveLinkBackup.Cli.Output;

/// <summary>
/// Where the CLI writes, and how it asks. A seam so verb behaviour is testable without a
/// console - the same reason Core has IFileSystem.
/// </summary>
public interface IOutput
{
    void Write(string line);

    void WriteError(string line);

    /// <summary>
    /// Asks a yes/no question. Returns false on anything that is not an explicit yes,
    /// INCLUDING a closed or redirected stdin - a piped invocation must never be taken as
    /// consent to an irreversible action.
    /// </summary>
    bool Confirm(string question);
}
