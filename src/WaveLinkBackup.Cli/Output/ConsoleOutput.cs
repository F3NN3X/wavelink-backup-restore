namespace WaveLinkBackup.Cli.Output;

/// <summary>
/// The real console. No colour: output gets piped, and escape codes in a log file help nobody.
/// </summary>
public sealed class ConsoleOutput : IOutput
{
    public void Write(string line) => Console.Out.WriteLine(line);

    public void WriteError(string line) => Console.Error.WriteLine(line);

    public bool Confirm(string question)
    {
        // A redirected stdin means nobody is there to answer. Treating EOF as "yes" would let
        // `echo | wlbackup restore x` silently replace someone's configuration.
        if (Console.IsInputRedirected)
        {
            Console.Error.WriteLine("Refusing to continue: no terminal to confirm at. Pass --yes if you mean it.");
            return false;
        }

        Console.Out.Write($"{question} [y/N] ");
        var answer = Console.ReadLine();

        return answer is not null
            && (answer.Equals("y", StringComparison.OrdinalIgnoreCase)
             || answer.Equals("yes", StringComparison.OrdinalIgnoreCase));
    }
}
