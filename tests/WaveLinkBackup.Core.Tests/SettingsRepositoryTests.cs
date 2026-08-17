using WaveLinkBackup.Core.Automation;
using WaveLinkBackup.Core.Tests.Fakes;

namespace WaveLinkBackup.Core.Tests;

/// <summary>
/// Where the user's choices live. Write on change, never on exit, and atomically once there is
/// something to replace.
/// </summary>
public sealed class SettingsRepositoryTests
{
    private const string Directory = @"C:\Users\t\AppData\Local\WaveLinkBackup";
    private const string File = Directory + @"\settings.json";

    private static SettingsRepository Repository(FakeFileSystem fileSystem) => new(fileSystem, Directory);

    private static IEnumerable<string> TempFiles(FakeFileSystem fileSystem) =>
        // "*.tmp" would be matched literally by the fake's glob, which only understands
        // prefix patterns - so it would pass whether or not a temp file survived.
        fileSystem.EnumerateFiles(Directory, "*").Where(f => f.EndsWith(".tmp", StringComparison.Ordinal));

    [Fact]
    public void Reads_defaults_when_the_file_does_not_exist()
    {
        Assert.Equal(BackupSettings.Default, Repository(new FakeFileSystem()).Read());
    }

    [Fact]
    public void Saves_then_reads_the_same_settings()
    {
        var fileSystem = new FakeFileSystem();
        var repository = Repository(fileSystem);
        var settings = new BackupSettings(@"D:\B", AutoBackupEnabled: false, AutoBackupKeepCount: 9);

        Assert.True(repository.Save(settings).IsSuccess);

        Assert.Equal(settings, repository.Read());
    }

    /// <summary>
    /// The first save has no destination to replace, and File.Replace throws in that case.
    /// SettingsWriter never meets this because Wave Link's Settings.json always already exists,
    /// so copying its shape blindly would break on the very first run.
    /// </summary>
    [Fact]
    public void The_first_save_writes_directly_rather_than_replacing()
    {
        var fileSystem = new FakeFileSystem();

        Assert.True(Repository(fileSystem).Save(BackupSettings.Default).IsSuccess);

        Assert.True(fileSystem.FileExists(File));
        Assert.Empty(fileSystem.Replacements);
    }

    [Fact]
    public void A_later_save_replaces_atomically()
    {
        var fileSystem = new FakeFileSystem();
        var repository = Repository(fileSystem);

        repository.Save(BackupSettings.Default);
        repository.Save(BackupSettings.Default with { AutoBackupKeepCount = 3 });

        var replacement = Assert.Single(fileSystem.Replacements);
        Assert.Equal(File, replacement.Destination);
        Assert.Equal(3, repository.Read().AutoBackupKeepCount);
    }

    [Fact]
    public void Leaves_no_temporary_file_behind()
    {
        var fileSystem = new FakeFileSystem();
        var repository = Repository(fileSystem);

        repository.Save(BackupSettings.Default);
        repository.Save(BackupSettings.Default with { AutoBackupKeepCount = 3 });

        Assert.Empty(TempFiles(fileSystem));
    }

    [Fact]
    public void Reads_defaults_when_the_file_cannot_be_read()
    {
        var fileSystem = new FakeFileSystem().AddFile(File, "{}");
        fileSystem.ReadFailures[File] = new Queue<Exception>([new IOException("locked")]);

        Assert.Equal(BackupSettings.Default, Repository(fileSystem).Read());
    }

    [Fact]
    public void Reports_a_failure_when_the_directory_cannot_be_created()
    {
        var fileSystem = new FakeFileSystem { FailDirectoryCreation = true };

        Assert.False(Repository(fileSystem).Save(BackupSettings.Default).IsSuccess);
    }

    [Fact]
    public void The_file_sits_directly_in_the_given_directory()
    {
        Assert.Equal(File, Repository(new FakeFileSystem()).FilePath);
    }
}
