using WaveLinkBackup.Core.Abstractions;

namespace WaveLinkBackup.Core.Tests;

/// <summary>
/// The real shell interop, against real temp directories.
///
/// These genuinely recycle things, which is safe: they create their own directories under
/// %TEMP% and send only those. The alternative — leaving `SHFileOperation` entirely
/// unexercised — would mean the one call that can permanently destroy a user's backups had
/// never run. That is not an exemption worth taking.
/// </summary>
public sealed class RecycleBinTests : IDisposable
{
    private readonly RecycleBin bin = new();
    private readonly string root =
        Path.Combine(Path.GetTempPath(), "wlbackup-bin-" + Guid.NewGuid().ToString("N"));

    public RecycleBinTests() => Directory.CreateDirectory(root);

    public void Dispose()
    {
        try { Directory.Delete(root, recursive: true); } catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    [Fact]
    public void A_local_fixed_drive_has_a_recycle_bin()
    {
        Assert.True(bin.IsAvailableFor(root),
            $"{root} is on a fixed drive, so it should report a Recycle Bin.");
    }

    [Fact]
    public void A_unc_path_does_not()
    {
        // The case that made deletion two-stage: someone keeping backups on a NAS.
        Assert.False(bin.IsAvailableFor(@"\\server\share\backups"));
    }

    [Fact]
    public void An_unresolvable_path_reports_unavailable_rather_than_throwing()
    {
        // Fails toward the honest message: "permanent" rather than promising an undo.
        Assert.False(bin.IsAvailableFor("\0invalid"));
        Assert.False(bin.IsAvailableFor(@"Q:\almost certainly not mounted"));
    }

    [Fact]
    public void Sending_a_directory_removes_it_from_its_original_location()
    {
        var doomed = Path.Combine(root, "snapshot");
        Directory.CreateDirectory(doomed);
        File.WriteAllText(Path.Combine(doomed, "settings.json"), "{}");
        File.WriteAllText(Path.Combine(doomed, "manifest.json"), "{}");

        var result = bin.Send(doomed);

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.False(Directory.Exists(doomed));
    }

    [Fact]
    public void Sending_leaves_its_siblings_alone()
    {
        // pFrom is a double-null-terminated LIST. A single terminator reads past the end and
        // can take more than intended with it — the exact bug this asserts against.
        var doomed = Path.Combine(root, "doomed");
        var keeper = Path.Combine(root, "keeper");
        Directory.CreateDirectory(doomed);
        Directory.CreateDirectory(keeper);
        File.WriteAllText(Path.Combine(keeper, "settings.json"), "{}");

        Assert.True(bin.Send(doomed).IsSuccess);

        Assert.False(Directory.Exists(doomed));
        Assert.True(Directory.Exists(keeper));
        Assert.True(File.Exists(Path.Combine(keeper, "settings.json")));
    }

    [Fact]
    public void Sending_something_that_does_not_exist_is_an_expected_failure()
    {
        var result = bin.Send(Path.Combine(root, "never existed"));

        Assert.False(result.IsSuccess);
    }
}
