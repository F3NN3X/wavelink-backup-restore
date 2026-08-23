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

    [Fact]
    public void A_report_carries_an_environment_block_that_answers_which_build_on_what_machine()
    {
        // §8.1a: a pasted report must answer "which build, on what machine" before anyone asks.
        // The version comes from the caller (the shell knows its own build); OS, culture and
        // runtime are read on the way down.
        var fileSystem = new FakeFileSystem();

        Writer(fileSystem).Write(new InvalidOperationException("crash"), appVersion: "0.7.2", userName: "t");

        var text = fileSystem.ReadSharedText(File);
        Assert.Contains("--- environment ---", text, StringComparison.Ordinal);
        Assert.Contains("App: 0.7.2", text, StringComparison.Ordinal);
        Assert.Contains("OS:", text, StringComparison.Ordinal);
        Assert.Contains("Culture:", text, StringComparison.Ordinal);
        Assert.Contains("Runtime:", text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_missing_version_reads_unknown_rather_than_throwing_or_omitting_the_field()
    {
        // The App field is best-effort: a fault during early startup can hit before the caller
        // knows its version. Omitting the line would make "which build" unanswerable; "unknown"
        // says that honestly.
        var fileSystem = new FakeFileSystem();

        Writer(fileSystem).Write(new InvalidOperationException("crash"), appVersion: null, userName: "t");

        Assert.Contains("App: unknown", fileSystem.ReadSharedText(File), StringComparison.Ordinal);
    }

    [Fact]
    public void The_stack_is_redacted_before_it_leaves_the_report()
    {
        // This file is the thing a user pastes into a public tracker. A stack trace names absolute
        // paths — C:\Users\<name>\… — so it goes through Core's Redaction on the way out, the same
        // rule "copy diagnostics" uses. The username must not survive; the path shape does.
        var fileSystem = new FakeFileSystem();
        var exception = new InvalidOperationException(
            @"failed to read C:\Users\t\AppData\Local\WaveLinkBackup\shell.json");

        Writer(fileSystem).Write(exception, appVersion: "0.7.2", userName: "t");

        var text = fileSystem.ReadSharedText(File);
        Assert.DoesNotContain(@"C:\Users\t\", text, StringComparison.Ordinal);
        Assert.Contains("[redacted]", text, StringComparison.Ordinal);
    }

    [Fact]
    public void An_endpoint_serial_in_the_stack_is_redacted_too()
    {
        // A fault that names a Core Audio endpoint ID carries the device's serial in its leading
        // segment. The same redactor strips it — the port descriptor survives, the serial does not.
        //
        // userName is "tester" (not "t"): Redaction.Text replaces the username EVERYWHERE it
        // appears, and a one-character name would replace every 't' in the text, mangling even the
        // type name. A realistic multi-character name keeps the assertion meaningful.
        var fileSystem = new FakeFileSystem();
        var exception = new InvalidOperationException(
            "endpoint BS33J1A05009\\PCM_IN_01_C_00_SD1 stopped responding");

        Writer(fileSystem).Write(exception, appVersion: "0.7.2", userName: "tester");

        var text = fileSystem.ReadSharedText(File);
        Assert.DoesNotContain("BS33J1A05009", text, StringComparison.Ordinal);
        Assert.Contains(@"[redacted]\PCM_IN_01_C_00_SD1", text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_faulting_environment_field_reads_unknown_and_the_report_still_writes()
    {
        // Every environment field is wrapped so a fault in one (a culture probe on an odd OS)
        // cannot abandon the report. The exception still lands; the broken field says "unknown".
        var fileSystem = new FakeFileSystem();

        var path = Writer(fileSystem).Write(new InvalidOperationException("crash"), appVersion: "0.7.2", userName: "t");

        Assert.Equal(File, path);
        // The fault is first — a truncated report still carries it — and the environment block
        // followed, with no field left blank.
        var text = fileSystem.ReadSharedText(File);
        Assert.True(text.IndexOf("InvalidOperationException", StringComparison.Ordinal)
            < text.IndexOf("--- environment ---", StringComparison.Ordinal));
    }
}
