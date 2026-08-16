using System.Text;
using WaveLinkBackup.Core.Discovery;
using WaveLinkBackup.Core.Io;
using WaveLinkBackup.Core.Results;
using WaveLinkBackup.Core.Tests.Fakes;

namespace WaveLinkBackup.Core.Tests;

/// <summary>
/// The write is the one irreversible operation, and the one that fails invisibly: a write
/// that races Wave Link's shutdown flush succeeds, verifies, and is gone seconds later.
/// See _docs/knowledge-base/gotchas/restored-settings-revert-seconds-later.md
/// </summary>
public sealed class SettingsWriterTests
{
    private const string LocalState =
        @"C:\Users\test\AppData\Local\Packages\Elgato.WaveLink_g54w8ztgkx496\LocalState";
    private const string SettingsPath = LocalState + @"\Settings.json";

    private const string Valid = """
        {"MixerConfiguration":{"InputSettings":{"a":{"InputName":"Wave Mic 1"}}}}
        """;

    private static readonly SettingsLocation Location = new(
        SettingsPath, "Elgato.WaveLink_g54w8ztgkx496", LocalState, LocalState + @"\Logs");

    private static (SettingsWriter Writer, FakeFileSystem Fs, FakeWaveLinkProcess Process) Subject(
        bool running = false)
    {
        var fs = new FakeFileSystem().AddFile(SettingsPath, "{\"old\":true}");
        var process = new FakeWaveLinkProcess { Running = running };
        return (new SettingsWriter(fs, process), fs, process);
    }

    [Fact]
    public void Refuses_to_write_while_Wave_Link_is_running()
    {
        // The precondition is INSIDE the write, not a duty the caller is trusted with.
        // Enforced at the boundary, the race cannot be reintroduced by a future caller.
        var (writer, fs, _) = Subject(running: true);

        var result = writer.Write(Location, Encoding.UTF8.GetBytes(Valid));

        Assert.IsType<WaveLinkStillRunning>(result.Error);
        Assert.Empty(fs.Replacements);
        Assert.Equal("{\"old\":true}", Encoding.UTF8.GetString(fs.Read(SettingsPath)));
    }

    [Fact]
    public void Writes_atomically_through_Replace_with_a_rollback_copy()
    {
        var (writer, fs, _) = Subject();

        var result = writer.Write(Location, Encoding.UTF8.GetBytes(Valid));

        Assert.True(result.IsSuccess);
        var replacement = Assert.Single(fs.Replacements);
        Assert.False(string.IsNullOrWhiteSpace(replacement.Backup));
        Assert.Equal(Valid, Encoding.UTF8.GetString(fs.Read(SettingsPath)));
    }

    [Fact]
    public void The_temp_file_is_created_in_the_destination_directory()
    {
        // File.Replace requires source and destination on the same volume. A temp file in
        // %TEMP% may not be.
        var (writer, fs, _) = Subject();

        writer.Write(Location, Encoding.UTF8.GetBytes(Valid));

        Assert.Equal(LocalState, Path.GetDirectoryName(fs.Replacements[0].Source));
    }

    [Fact]
    public void Refuses_to_write_content_that_is_not_valid_settings()
    {
        // Restoring a file the app will reject looks identical to the snapshot being broken.
        var (writer, fs, _) = Subject();

        var result = writer.Write(Location, Encoding.UTF8.GetBytes("{ not settings }"));

        Assert.IsType<MalformedSettings>(result.Error);
        Assert.Empty(fs.Replacements);
    }

    [Fact]
    public void The_temp_file_is_cleaned_up_when_the_write_is_rejected()
    {
        var (writer, fs, _) = Subject();

        writer.Write(Location, Encoding.UTF8.GetBytes("{ not settings }"));

        Assert.DoesNotContain(fs.EnumerateFiles(LocalState, "*"), f => f.EndsWith(".tmp", StringComparison.Ordinal));
    }

    [Fact]
    public void Closing_then_writing_succeeds_when_the_process_actually_exits()
    {
        var (writer, fs, process) = Subject(running: true);

        Assert.True(process.CloseAndVerifyExited(TimeSpan.FromSeconds(10)).IsSuccess);
        Assert.True(writer.Write(Location, Encoding.UTF8.GetBytes(Valid)).IsSuccess);
        Assert.Single(fs.Replacements);
    }

    [Fact]
    public void A_process_that_survives_the_kill_blocks_the_write()
    {
        var (writer, fs, process) = Subject(running: true);
        process.StaysRunningAfterClose = true;

        Assert.IsType<WaveLinkStillRunning>(process.CloseAndVerifyExited(TimeSpan.FromSeconds(10)).Error);
        Assert.IsType<WaveLinkStillRunning>(writer.Write(Location, Encoding.UTF8.GetBytes(Valid)).Error);
        Assert.Empty(fs.Replacements);
    }
}
