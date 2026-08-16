using WaveLinkBackup.Core.Analysis;

namespace WaveLinkBackup.Core.Tests;

/// <summary>
/// A mixer that looks correct can be a freshly generated default. The log is the only
/// place that distinguishes "restored your config" from "rejected it and made a new one".
/// SPEC.md 4.
/// </summary>
public sealed class LogAnalysisTests
{
    [Fact]
    public void Applied_names_with_no_parse_failure_is_a_success()
    {
        var verdict = LogAnalysis.Verify("""
            [info] Starting Wave Link
            [info] Applied saved friendly name 'Wave Mic 1'
            [info] Applied saved friendly name 'Voice'
            """);

        Assert.True(verdict.Succeeded);
        Assert.Equal(["Wave Mic 1", "Voice"], verdict.AppliedNames);
        Assert.False(verdict.ParseFailed);
    }

    [Fact]
    public void A_parse_failure_is_a_failure_even_when_names_were_applied()
    {
        // The reset path: reject the file, regenerate defaults, then apply names to those.
        var verdict = LogAnalysis.Verify("""
            [error] Failed to parse settings file
            [info] Created a new backup file
            [info] Applied saved friendly name 'System'
            """);

        Assert.False(verdict.Succeeded);
        Assert.True(verdict.ParseFailed);
        Assert.True(verdict.CreatedNewBackup);
    }

    [Fact]
    public void Absence_of_evidence_is_not_success()
    {
        var verdict = LogAnalysis.Verify("[info] Starting Wave Link");

        Assert.False(verdict.Succeeded);
        Assert.Empty(verdict.AppliedNames);
    }

    [Fact]
    public void Empty_and_whitespace_logs_do_not_throw()
    {
        Assert.False(LogAnalysis.Verify("").Succeeded);
        Assert.False(LogAnalysis.Verify("   \n  ").Succeeded);
    }

    [Fact]
    public void Applied_names_are_deduplicated_across_repeated_lines()
    {
        var verdict = LogAnalysis.Verify("""
            Applied saved friendly name 'Wave Mic 1'
            Applied saved friendly name 'Wave Mic 1'
            """);

        Assert.Single(verdict.AppliedNames);
    }
}
