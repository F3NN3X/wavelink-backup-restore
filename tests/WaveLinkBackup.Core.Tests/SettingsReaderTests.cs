using System.Text;
using WaveLinkBackup.Core.Io;
using WaveLinkBackup.Core.Results;
using WaveLinkBackup.Core.Tests.Fakes;

namespace WaveLinkBackup.Core.Tests;

public sealed class SettingsReaderTests
{
    private const string Path = @"C:\ls\Settings.json";
    private const string Logs = @"C:\ls\Logs";

    [Fact]
    public void Returns_the_bytes_verbatim()
    {
        var content = """{"ParameterState":"ab+cd/ef=="}""";
        var fs = new FakeFileSystem().AddFile(Path, content);

        var result = new SettingsReader(fs).Read(Path);

        Assert.True(result.IsSuccess);
        Assert.Equal(Encoding.UTF8.GetBytes(content), result.Value);
    }

    [Theory]
    [InlineData(typeof(FileNotFoundException))]
    [InlineData(typeof(DirectoryNotFoundException))]
    [InlineData(typeof(UnauthorizedAccessException))]
    [InlineData(typeof(IOException))]
    public void Every_expected_io_failure_becomes_SettingsUnreadable_rather_than_an_exception(Type exception)
    {
        var fs = new FakeFileSystem().AddFile(Path, "{}");
        fs.ReadFailures[Path] = new Queue<Exception>([(Exception)Activator.CreateInstance(exception)!]);

        var result = new SettingsReader(fs).Read(Path);

        Assert.IsType<SettingsUnreadable>(result.Error);
    }

    [Fact]
    public void Reads_the_newest_log_by_write_time()
    {
        var fs = new FakeFileSystem()
            .AddFile(Logs + @"\old.log", "older")
            .AddFile(Logs + @"\new.log", "Applied saved friendly name 'Wave Mic 1'");

        var result = new SettingsReader(fs).ReadNewestLog(Logs);

        Assert.True(result.IsSuccess);
        Assert.Contains("Applied saved", result.Value, StringComparison.Ordinal);
    }

    [Fact]
    public void A_missing_log_folder_is_an_expected_failure()
    {
        Assert.IsType<SettingsUnreadable>(new SettingsReader(new FakeFileSystem()).ReadNewestLog(Logs).Error);
    }

    [Fact]
    public void An_empty_log_folder_is_an_expected_failure()
    {
        var fs = new FakeFileSystem().AddDirectory(Logs);

        Assert.IsType<SettingsUnreadable>(new SettingsReader(fs).ReadNewestLog(Logs).Error);
    }

    [Fact]
    public void A_log_that_cannot_be_read_is_an_expected_failure()
    {
        var fs = new FakeFileSystem().AddFile(Logs + @"\a.log", "x");
        fs.ReadFailures[Logs + @"\a.log"] = new Queue<Exception>([new IOException("locked")]);

        Assert.IsType<SettingsUnreadable>(new SettingsReader(fs).ReadNewestLog(Logs).Error);
    }
}
