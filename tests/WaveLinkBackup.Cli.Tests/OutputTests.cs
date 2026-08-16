using WaveLinkBackup.Cli.CommandLine;
using WaveLinkBackup.Cli.Output;
using WaveLinkBackup.Core.Restore;
using WaveLinkBackup.Core.Results;
using WaveLinkBackup.Core.Snapshots;

namespace WaveLinkBackup.Cli.Tests;

public sealed class ExitCodeTests
{
    [Theory]
    [InlineData(typeof(WaveLinkNotInstalled), ExitCode.NotInstalled)]
    [InlineData(typeof(MultiplePackagesFound), ExitCode.MultiplePackages)]
    [InlineData(typeof(SettingsUnreadable), ExitCode.Unreadable)]
    [InlineData(typeof(MalformedSettings), ExitCode.Unreadable)]
    [InlineData(typeof(WaveLinkStillRunning), ExitCode.StillRunning)]
    [InlineData(typeof(SnapshotNotFound), ExitCode.NotFound)]
    [InlineData(typeof(NotASnapshot), ExitCode.Damaged)]
    [InlineData(typeof(SnapshotCorrupted), ExitCode.Damaged)]
    [InlineData(typeof(MalformedManifest), ExitCode.Damaged)]
    [InlineData(typeof(UnsupportedSnapshotSchema), ExitCode.Damaged)]
    [InlineData(typeof(StoreUnavailable), ExitCode.StoreFailed)]
    [InlineData(typeof(WriteFailed), ExitCode.Failure)]
    public void Every_error_type_maps_to_its_documented_code(Type errorType, int expected)
    {
        // Scripts branch on these. A silent remapping would break someone's automation
        // without breaking a build.
        Assert.Equal(expected, ExitCode.For(Sample(errorType)));
    }

    [Fact]
    public void Distinct_failures_get_distinct_codes()
    {
        int[] codes = [
            ExitCode.Success, ExitCode.Failure, ExitCode.NotInstalled, ExitCode.MultiplePackages,
            ExitCode.Unreadable, ExitCode.StillRunning, ExitCode.NotFound, ExitCode.Damaged,
            ExitCode.StoreFailed, ExitCode.Declined, ExitCode.Usage];

        Assert.Equal(codes.Length, codes.Distinct().Count());
    }

    [Fact]
    public void Usage_is_the_conventional_sysexits_value()
    {
        Assert.Equal(64, ExitCode.Usage);
    }

    private static CoreError Sample(Type type) => type switch
    {
        _ when type == typeof(WaveLinkNotInstalled) => new WaveLinkNotInstalled(),
        _ when type == typeof(MultiplePackagesFound) => new MultiplePackagesFound(["a", "b"]),
        _ when type == typeof(SettingsUnreadable) => new SettingsUnreadable("p", "why"),
        _ when type == typeof(MalformedSettings) => new MalformedSettings("why"),
        _ when type == typeof(WaveLinkStillRunning) => new WaveLinkStillRunning(["Elgato.WaveLink"]),
        _ when type == typeof(SnapshotNotFound) => new SnapshotNotFound("id"),
        _ when type == typeof(NotASnapshot) => new NotASnapshot("p", "why"),
        _ when type == typeof(SnapshotCorrupted) => new SnapshotCorrupted("p", "why"),
        _ when type == typeof(MalformedManifest) => new MalformedManifest("why"),
        _ when type == typeof(UnsupportedSnapshotSchema) => new UnsupportedSnapshotSchema(2, 1),
        _ when type == typeof(StoreUnavailable) => new StoreUnavailable("p", "why"),
        _ => new WriteFailed("why"),
    };
}

public sealed class ConsoleOutputTests
{
    /// <summary>
    /// The safety property: a piped invocation must never be taken as consent to an
    /// irreversible action. `echo | wlbackup restore x` must refuse, not proceed.
    /// </summary>
    [Fact]
    public void Confirm_refuses_when_stdin_is_redirected()
    {
        // The test host itself runs with redirected stdin, which is exactly the condition
        // being asserted — so this needs no setup, only the check.
        Assert.True(Console.IsInputRedirected,
            "This test assumes the runner redirects stdin; if it does not, it proves nothing.");

        var stderr = new StringWriter();
        var original = Console.Error;
        try
        {
            Console.SetError(stderr);
            Assert.False(new ConsoleOutput().Confirm("Do the irreversible thing?"));
        }
        finally { Console.SetError(original); }

        Assert.Contains("--yes", stderr.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Write_goes_to_stdout_and_WriteError_to_stderr()
    {
        // Separated so `wlbackup list > backups.txt` captures data and not diagnostics.
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var originalOut = Console.Out;
        var originalError = Console.Error;

        try
        {
            Console.SetOut(stdout);
            Console.SetError(stderr);

            var output = new ConsoleOutput();
            output.Write("data");
            output.WriteError("diagnostic");
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalError);
        }

        Assert.Contains("data", stdout.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("diagnostic", stdout.ToString(), StringComparison.Ordinal);
        Assert.Contains("diagnostic", stderr.ToString(), StringComparison.Ordinal);
    }
}

public sealed class FormatTests
{
    private static RestorePlan Plan(
        bool losesInputs = false, bool suspect = false, string? versionWarning = null,
        string nowNames = "Wave Mic 1, Voice") => new(
            "Before 3.3 beta",
            new DateTimeOffset(2026, 8, 11, 21, 36, 0, TimeSpan.Zero),
            [
                new PlanRow("Inputs", "5", "5", false),
                new PlanRow("Channel names", nowNames, "Wave Mic 1, Voice, Browser", true),
            ],
            losesInputs,
            losesInputs ? ["Browser"] : [],
            suspect,
            versionWarning);

    [Fact]
    public void A_plan_marks_only_the_rows_that_change()
    {
        var lines = Format.PlanLines(Plan()).ToList();

        Assert.Contains(lines, l => l.StartsWith("* Channel names", StringComparison.Ordinal));
        Assert.Contains(lines, l => l.StartsWith("  Inputs", StringComparison.Ordinal));
    }

    [Fact]
    public void A_plan_that_loses_inputs_says_which()
    {
        var text = string.Join("\n", Format.PlanLines(Plan(losesInputs: true)));

        Assert.Contains("WARNING", text, StringComparison.Ordinal);
        Assert.Contains("Browser", text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_suspect_snapshot_is_flagged_in_the_plan()
    {
        var text = string.Join("\n", Format.PlanLines(Plan(suspect: true)));

        Assert.Contains("failed validation", text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_version_mismatch_is_surfaced_before_the_restore_not_after()
    {
        var text = string.Join("\n", Format.PlanLines(Plan(versionWarning: "made with 3.2.9")));

        Assert.Contains("3.2.9", text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_clean_plan_carries_no_warnings()
    {
        var text = string.Join("\n", Format.PlanLines(Plan()));

        Assert.DoesNotContain("WARNING", text, StringComparison.Ordinal);
        Assert.Contains("Before restore", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Long_channel_lists_are_truncated_so_the_columns_still_line_up()
    {
        var long_ = string.Join(", ", Enumerable.Repeat("A very long channel name", 5));

        var text = string.Join("\n", Format.PlanLines(Plan(nowNames: long_)));

        Assert.Contains("…", text, StringComparison.Ordinal);
        Assert.DoesNotContain(long_, text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_snapshot_line_shows_the_trigger_and_never_a_device_id()
    {
        var manifest = new SnapshotManifest(
            SnapshotManifest.CurrentSchemaVersion, "Auto", "",
            new DateTimeOffset(2026, 8, 15, 23, 7, 0, TimeSpan.Zero),
            SnapshotTrigger.Automatic, "abc", "3.3.0.4108", 1,
            ["Wave Mic 1"], 0, 0, false, ["settings"],
            new Dictionary<string, SnapshotFile>());

        var line = Format.SnapshotLine(new Snapshot("id", @"C:\store\id", manifest));

        Assert.Contains("automatic", line, StringComparison.Ordinal);
        Assert.Contains("Wave Mic 1", line, StringComparison.Ordinal);
        Assert.Contains("1 input", line, StringComparison.Ordinal);
    }

    [Fact]
    public void A_suspect_snapshot_is_marked_in_the_list()
    {
        var manifest = new SnapshotManifest(
            SnapshotManifest.CurrentSchemaVersion, "Broken", "",
            DateTimeOffset.UnixEpoch, SnapshotTrigger.Manual, "abc", null, 2,
            ["Elgato Wave:3", "System"], 0, 0, HasDuplicateKeys: true, ["settings"],
            new Dictionary<string, SnapshotFile>());

        Assert.Contains("SUSPECT", Format.SnapshotLine(new Snapshot("id", "d", manifest)),
            StringComparison.Ordinal);
    }

    [Fact]
    public void A_snapshot_with_no_inputs_prints_none_rather_than_an_empty_gap()
    {
        var manifest = new SnapshotManifest(
            SnapshotManifest.CurrentSchemaVersion, "Empty", "",
            DateTimeOffset.UnixEpoch, SnapshotTrigger.Manual, "abc", null, 0,
            [], 0, 0, false, ["settings"], new Dictionary<string, SnapshotFile>());

        Assert.Contains("none", Format.SnapshotLine(new Snapshot("id", "d", manifest)),
            StringComparison.Ordinal);
    }
}
