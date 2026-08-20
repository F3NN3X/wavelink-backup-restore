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

    // ------------------------------------------------ the streaming seam (technical-debt.md §4.19)

    [Fact]
    public void A_copied_file_arrives_byte_for_byte_with_the_hash_of_what_was_written()
    {
        // Larger than the 1 MiB buffer on purpose: a single-pass copy and a looping one are
        // indistinguishable below it, and the loop is the whole point.
        var bytes = new byte[(1024 * 1024) + 7777];
        Random.Shared.NextBytes(bytes);

        var source = Path.Combine(root, "big.vst3");
        var destination = Path.Combine(root, "copy", "big.vst3");
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.WriteAllBytes(source, bytes);

        var copied = fs.CopyFile(source, destination);

        Assert.Equal(bytes, File.ReadAllBytes(destination));
        Assert.Equal(bytes.LongLength, copied.SizeBytes);
        Assert.Equal(
            Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes)),
            copied.Sha256);
    }

    [Fact]
    public void A_copy_overwrites_a_file_that_is_already_there()
    {
        var source = Path.Combine(root, "new.ffp");
        var destination = Path.Combine(root, "old.ffp");
        File.WriteAllText(source, "new");
        File.WriteAllText(destination, "the older, longer content");

        fs.CopyFile(source, destination);

        Assert.Equal("new", File.ReadAllText(destination));
    }

    /// <summary>
    /// The share mode is the reason this seam exists at all: Settings.json is locked while Wave
    /// Link runs, which is when most captures happen.
    /// </summary>
    [Fact]
    public void A_file_held_open_for_writing_elsewhere_can_still_be_copied()
    {
        var source = Path.Combine(root, "locked.json");
        File.WriteAllText(source, "{}");

        using var holder = new FileStream(
            source, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete);

        var copied = fs.CopyFile(source, Path.Combine(root, "copy.json"));

        Assert.Equal(2, copied.SizeBytes);
    }

    [Fact]
    public void CanReadShared_answers_no_for_a_file_that_is_not_there_and_yes_for_one_that_is()
    {
        var path = Path.Combine(root, "here.json");
        File.WriteAllText(path, "{}");

        Assert.True(fs.CanReadShared(path));
        Assert.False(fs.CanReadShared(Path.Combine(root, "absent.json")));
    }

    [Fact]
    public void CanReadShared_reads_nothing_and_leaves_no_handle_behind()
    {
        var path = Path.Combine(root, "probe.json");
        File.WriteAllText(path, "{}");

        Assert.True(fs.CanReadShared(path));

        // Would throw if the probe had left the file open.
        File.Delete(path);
        Assert.False(File.Exists(path));
    }

    // ------------------------- the writability probe (technical-debt.md §7.5)

    [Fact]
    public void A_directory_this_process_owns_is_writable()
    {
        Assert.True(fs.CanWriteDirectory(root));
    }

    /// <summary>
    /// A destination that does not exist yet is writable if it could be CREATED, which is a
    /// question about its nearest existing ancestor — a plug-in bundle's own folder often does not
    /// exist until the restore makes it.
    /// </summary>
    [Fact]
    public void A_directory_that_does_not_exist_yet_answers_for_its_nearest_ancestor()
    {
        Assert.True(fs.CanWriteDirectory(Path.Combine(root, "not", "created", "yet")));
    }

    [Fact]
    public void A_path_that_is_not_a_path_is_not_writable()
    {
        Assert.False(fs.CanWriteDirectory(string.Empty));
        Assert.False(fs.CanWriteDirectory("   "));
    }

    /// <summary>
    /// The probe must leave nothing behind. It writes a real file to answer the question, and a
    /// stray probe file in somebody's VST3 folder would be this program littering in the one place
    /// it is trying to be careful about.
    /// </summary>
    [Fact]
    public void The_probe_leaves_nothing_behind()
    {
        var before = Directory.GetFileSystemEntries(root).Length;

        Assert.True(fs.CanWriteDirectory(root));

        Assert.Equal(before, Directory.GetFileSystemEntries(root).Length);
        Assert.Empty(Directory.GetFiles(root, ".wlbackup-probe-*"));
    }

    /// <summary>
    /// A location no process can write. Answering false rather than throwing is the contract: the
    /// caller uses it to decide whether to ask for administrator rights, and an exception there
    /// would turn a question into a failure.
    /// </summary>
    [Fact]
    public void An_unwritable_location_answers_false_rather_than_throwing()
    {
        Assert.False(fs.CanWriteDirectory(@"\\?\GLOBALROOT\Device\Null"));
    }
}
