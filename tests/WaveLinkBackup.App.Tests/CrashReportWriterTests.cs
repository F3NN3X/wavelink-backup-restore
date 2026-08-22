using System;
using WaveLinkBackup.App.Hosting;
using WaveLinkBackup.Core.Tests.Fakes;

namespace WaveLinkBackup.App.Tests;

/// <summary>
/// The cheap half of technical-debt.md §8.1: an unhandled exception leaves a report beside
/// shell.json before the process is gone, so "the app crashed" stops meaning "nothing to look at".
///
/// These tests guard the CONTRACT, not the formatting — that the file appears in the right place,
/// that it names the fault with its stack, that a second crash appends rather than overwrites, and
/// that an unwritable location degrades to null instead of throwing out of the handler.
/// </summary>
public sealed class CrashReportWriterTests
{
    private const string Directory = @"C:\Users\t\AppData\Local\WaveLinkBackup";
    private const string File = @"C:\Users\t\AppData\Local\WaveLinkBackup\crash-report.txt";

    private static CrashReportWriter Writer(FakeFileSystem fileSystem) => new(fileSystem, Directory);

    [Fact]
    public void The_report_sits_beside_shell_json()
    {
        Assert.Equal(File, Writer(new FakeFileSystem()).FilePath);
    }

    [Fact]
    public void A_crash_writes_a_report_that_names_the_fault_and_its_stack()
    {
        var fileSystem = new FakeFileSystem();
        var exception = new InvalidOperationException("the button did the thing");

        var path = Writer(fileSystem).Write(exception);

        Assert.Equal(File, path);
        var text = fileSystem.ReadSharedText(File);
        // The type name — so a report that only survives its first line still names the fault.
        Assert.Contains("InvalidOperationException", text, StringComparison.Ordinal);
        // The message and the stack — the difference between "it crashed" and "it crashed HERE".
        Assert.Contains("the button did the thing", text, StringComparison.Ordinal);
        Assert.Contains("at ", text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_second_crash_appends_rather_than_overwriting()
    {
        var fileSystem = new FakeFileSystem();
        var writer = Writer(fileSystem);

        writer.Write(new InvalidOperationException("first"));
        writer.Write(new NullReferenceException("second"));

        var text = fileSystem.ReadSharedText(File);
        Assert.Contains("InvalidOperationException", text, StringComparison.Ordinal);
        Assert.Contains("NullReferenceException", text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_report_is_written_before_anything_that_follows_can_throw()
    {
        // The whole point of §8.1 is that the report exists on the way down. Writing it first and
        // returning the path is what makes "the file is there" a fact a caller can rely on, not an
        // assumption about ordering inside Write.
        var fileSystem = new FakeFileSystem();

        var path = Writer(fileSystem).Write(new InvalidOperationException("on the way down"));

        Assert.True(fileSystem.FileExists(File));
        Assert.Equal(File, path);
    }

    [Fact]
    public void An_unwritable_location_returns_null_instead_of_throwing()
    {
        // A crash handler that throws on a crash path is worse than no handler. The original
        // incident was the app vanishing with nothing behind it; an exception escaping here would
        // put us back there. So an unwritable directory degrades to null, never a throw.
        var fileSystem = new FakeFileSystem { FailDirectoryCreation = true };

        Assert.Null(Writer(fileSystem).Write(new InvalidOperationException("crash")));
    }
}
