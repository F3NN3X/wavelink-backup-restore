using System.Text;
using WaveLinkBackup.Core.Abstractions;

namespace WaveLinkBackup.Core.Tests;

/// <summary>
/// The real adapter, against a real temp directory. Needs no Wave Link, so it runs in CI -
/// which matters, because this is the class every fake in the suite is pretending to be.
/// </summary>
public sealed class FileSystemTests : IDisposable
{
    private readonly FileSystem fs = new();
    private readonly string root =
        Path.Combine(Path.GetTempPath(), "wlbackup-tests-" + Guid.NewGuid().ToString("N"));

    public FileSystemTests() => Directory.CreateDirectory(root);

    public void Dispose()
    {
        try { Directory.Delete(root, recursive: true); } catch (IOException) { }
    }

    private string Write(string name, string content)
    {
        var path = Path.Combine(root, name);
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public void Reads_a_file_that_another_handle_holds_open_for_writing()
    {
        // The behaviour the whole file-lock gotcha turns on, proved without needing
        // Wave Link: an exclusive-ish writer is open, and the read still succeeds.
        var path = Write("locked.json", "{}");

        using var holder = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);

        Assert.Equal("{}", Encoding.UTF8.GetString(fs.ReadSharedBytes(path)));
        Assert.Equal("{}", fs.ReadSharedText(path));
    }

    [Fact]
    public void Replace_swaps_the_target_and_leaves_the_previous_content_as_a_backup()
    {
        var target = Write("Settings.json", "old");
        var temp = Write("temp.tmp", "new");
        var backup = Path.Combine(root, "rollback.bak");

        fs.Replace(temp, target, backup);

        Assert.Equal("new", File.ReadAllText(target));
        Assert.Equal("old", File.ReadAllText(backup));
        Assert.False(File.Exists(temp));
    }

    [Fact]
    public void Enumerating_a_missing_directory_yields_nothing_rather_than_throwing()
    {
        var missing = Path.Combine(root, "nope");

        Assert.Empty(fs.EnumerateDirectories(missing, "*"));
        Assert.Empty(fs.EnumerateFiles(missing, "*"));
        Assert.False(fs.DirectoryExists(missing));
    }

    [Fact]
    public void Enumeration_honours_a_glob_and_does_not_recurse()
    {
        Write("Settings.json", "{}");
        Write("Settings.json.bak.1", "{}");
        Directory.CreateDirectory(Path.Combine(root, "nested"));
        File.WriteAllText(Path.Combine(root, "nested", "Settings.json"), "{}");

        var matches = fs.EnumerateFiles(root, "Settings.json*");

        Assert.Equal(2, matches.Count);
        Assert.DoesNotContain(matches, m => m.Contains("nested", StringComparison.Ordinal));
    }

    [Fact]
    public void Directory_enumeration_finds_package_shaped_names()
    {
        Directory.CreateDirectory(Path.Combine(root, "Elgato.WaveLink_g54w8ztgkx496"));
        Directory.CreateDirectory(Path.Combine(root, "Elgato.StreamDeck_abc"));

        var matches = fs.EnumerateDirectories(root, "Elgato.WaveLink_*");

        Assert.Single(matches);
    }

    [Fact]
    public void Reading_a_missing_file_throws_so_callers_can_translate_it()
    {
        Assert.Throws<FileNotFoundException>(() => fs.ReadSharedBytes(Path.Combine(root, "gone.json")));
    }

    [Fact]
    public void Delete_is_idempotent()
    {
        var path = Write("doomed.json", "{}");

        fs.Delete(path);
        fs.Delete(path);

        Assert.False(fs.FileExists(path));
    }

    [Fact]
    public void WriteBytes_then_ReadSharedBytes_round_trips_exactly()
    {
        // Capture is a byte copy. This is the assertion that says so, with the + and /
        // that the withdrawn encoder finding was about.
        var path = Path.Combine(root, "state.json");
        var content = Encoding.UTF8.GetBytes("""{"ParameterState":"ab+cd/ef=="}""");

        fs.WriteBytes(path, content);

        Assert.Equal(content, fs.ReadSharedBytes(path));
    }

    [Fact]
    public void Last_write_time_is_readable_and_recent()
    {
        var path = Write("stamped.json", "{}");

        Assert.InRange(fs.GetLastWriteTimeUtc(path), DateTime.UtcNow.AddMinutes(-5), DateTime.UtcNow.AddMinutes(5));
    }

    // ------------------------------------------------------------------ free space

    [Fact]
    public void Free_space_is_a_positive_figure_for_a_real_directory()
    {
        var free = fs.GetAvailableFreeBytes(root);

        Assert.NotNull(free);
        Assert.True(free > 0, $"Expected a positive figure, got {free}.");
    }

    /// <summary>
    /// The store directory may not exist yet the first time the shell draws the bottom bar,
    /// and the volume underneath it still has a free-space figure worth showing.
    /// </summary>
    [Fact]
    public void Free_space_falls_back_to_the_first_existing_ancestor()
    {
        var notYetCreated = Path.Combine(root, "store", "that", "is", "not", "there");

        Assert.NotNull(fs.GetAvailableFreeBytes(notYetCreated));
    }

    [Fact]
    public void Free_space_is_null_for_a_volume_that_does_not_exist()
    {
        Assert.Null(fs.GetAvailableFreeBytes(@"Q:\nothing\here"));
    }

    [Fact]
    public void Free_space_is_null_rather_than_throwing_for_a_malformed_path()
    {
        Assert.Null(fs.GetAvailableFreeBytes(""));
        Assert.Null(fs.GetAvailableFreeBytes("   "));
    }
}
