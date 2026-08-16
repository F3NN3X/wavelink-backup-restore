using WaveLinkBackup.Cli.Output;

namespace WaveLinkBackup.Cli.Tests;

/// <summary>Captures what the CLI would print, and answers its questions.</summary>
public sealed class FakeOutput(bool confirmAnswer = false) : IOutput
{
    public List<string> Lines { get; } = [];
    public List<string> Errors { get; } = [];
    public List<string> Questions { get; } = [];

    /// <summary>Defaults to NO, matching the real console's behaviour on anything but "y".</summary>
    public bool ConfirmAnswer { get; set; } = confirmAnswer;

    public string All => string.Join("\n", Lines.Concat(Errors));

    public void Write(string line) => Lines.Add(line);

    public void WriteError(string line) => Errors.Add(line);

    public bool Confirm(string question)
    {
        Questions.Add(question);
        return ConfirmAnswer;
    }
}
