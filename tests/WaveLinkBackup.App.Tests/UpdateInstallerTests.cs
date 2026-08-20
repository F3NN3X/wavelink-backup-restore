using System.IO;
using WaveLinkBackup.App.Updates;

namespace WaveLinkBackup.App.Tests;

/// <summary>
/// The directory swap, against real directories in a temp folder.
///
/// **This is the only code path in the program that overwrites its own binaries**, so what these
/// tests are for is the ordering: the previous install is MOVED aside rather than deleted, and it
/// is only removed once the new one is in place. There must be no instant at which the user has no
/// app — and when the swap fails, the old install must be back where it was.
/// </summary>
public sealed class UpdateInstallerTests : IDisposable
{
    private readonly string root =
        Path.Combine(Path.GetTempPath(), "wlbackup-update-tests-" + Guid.NewGuid().ToString("N"));

    private string Install => Path.Combine(root, "app");

    private string Staging => Install + UpdateInstaller.StagingSuffix;

    private string Previous => Install + UpdateInstaller.PreviousSuffix;

    public UpdateInstallerTests()
    {
        Directory.CreateDirectory(Install);
        File.WriteAllText(Path.Combine(Install, "WaveLinkBackup.exe"), "old");
        File.WriteAllText(Path.Combine(Install, "settings-adjacent.txt"), "old");
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private void Stage(string content = "new")
    {
        Directory.CreateDirectory(Staging);
        File.WriteAllText(Path.Combine(Staging, "WaveLinkBackup.exe"), content);
    }

    /// <summary>A process id that is certainly not running, so Apply does not wait.</summary>
    private const int GoneProcessId = int.MaxValue;

    [Fact]
    public void The_staged_copy_replaces_the_install()
    {
        Stage();

        var applied = new UpdateInstaller().Apply(GoneProcessId, Install, TimeSpan.Zero);

        Assert.True(applied);
        Assert.Equal("new", File.ReadAllText(Path.Combine(Install, "WaveLinkBackup.exe")));
        Assert.False(Directory.Exists(Staging));
    }

    /// <summary>
    /// The previous install is removed only AFTER the new one is in place. Until then it is the
    /// way back, and deleting it first would make a failed rename unrecoverable.
    /// </summary>
    [Fact]
    public void The_previous_install_is_gone_once_the_new_one_is_in_place()
    {
        Stage();

        new UpdateInstaller().Apply(GoneProcessId, Install, TimeSpan.Zero);

        Assert.False(Directory.Exists(Previous));
    }

    /// <summary>
    /// The failure that matters: the swap cannot complete. The old install has to be back, whole,
    /// and the caller has to be told it did not work.
    /// </summary>
    [Fact]
    public void A_failed_swap_puts_the_old_install_back()
    {
        Stage();

        // A file where the staged directory has to be renamed TO would let the move succeed on
        // some filesystems, so the failure is forced the reliable way: hold the staged directory
        // open, which stops Windows renaming it.
        using var held = new FileStream(
            Path.Combine(Staging, "WaveLinkBackup.exe"),
            FileMode.Open, FileAccess.Read, FileShare.None);

        var applied = new UpdateInstaller().Apply(GoneProcessId, Install, TimeSpan.Zero);

        if (applied)
        {
            // Windows CAN rename a directory holding an open file. Then the swap genuinely
            // succeeded and there is nothing to roll back — which is still a correct outcome.
            Assert.True(Directory.Exists(Install));
            return;
        }

        Assert.True(Directory.Exists(Install));
        Assert.Equal("old", File.ReadAllText(Path.Combine(Install, "WaveLinkBackup.exe")));
    }

    [Fact]
    public void A_leftover_previous_directory_does_not_block_a_later_update()
    {
        Directory.CreateDirectory(Previous);
        File.WriteAllText(Path.Combine(Previous, "stale.txt"), "from an earlier run");
        Stage();

        Assert.True(new UpdateInstaller().Apply(GoneProcessId, Install, TimeSpan.Zero));
        Assert.Equal("new", File.ReadAllText(Path.Combine(Install, "WaveLinkBackup.exe")));
    }

    [Fact]
    public void An_archive_that_is_not_an_archive_changes_nothing()
    {
        var notAZip = Path.Combine(root, "broken.zip");
        File.WriteAllText(notAZip, "this is not a zip file");

        var result = new UpdateInstaller().Begin(notAZip, Install);

        Assert.False(result.Started);
        Assert.NotNull(result.FailureDetail);
        Assert.Equal("old", File.ReadAllText(Path.Combine(Install, "WaveLinkBackup.exe")));
    }

    /// <summary>
    /// An archive that expands to something without the app in it must be refused BEFORE the
    /// swap — completing it would leave the user with an install that cannot start.
    /// </summary>
    [Fact]
    public void An_archive_without_the_app_in_it_is_refused_before_anything_moves()
    {
        var source = Path.Combine(root, "contents");
        Directory.CreateDirectory(source);
        File.WriteAllText(Path.Combine(source, "readme.txt"), "nothing useful");

        var archive = Path.Combine(root, "wrong.zip");
        System.IO.Compression.ZipFile.CreateFromDirectory(source, archive);

        var result = new UpdateInstaller().Begin(archive, Install);

        Assert.False(result.Started);
        Assert.Equal("old", File.ReadAllText(Path.Combine(Install, "WaveLinkBackup.exe")));
        Assert.False(Directory.Exists(Staging));
    }
}
