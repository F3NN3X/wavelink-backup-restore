using WaveLinkBackup.Core.Abstractions;
using WaveLinkBackup.Core.Discovery;
using WaveLinkBackup.Core.Io;
using WaveLinkBackup.Core.Process;

namespace WaveLinkBackup.Core.Tests;

/// <summary>
/// Read-only tests against the actual Wave Link installation on this machine.
///
/// Every test here SKIPS when Wave Link is not installed, so CI stays green. Nothing here
/// writes, kills a process, or restores - the live configuration is never touched.
///
/// These exist because the two most expensive bugs in this project are invisible to a fake
/// filesystem: the decoy folder only fools you against a real disk, and the file lock only
/// appears when Wave Link is actually running. CI cannot catch either.
/// </summary>
public sealed class RealInstallTests
{
    private static readonly FileSystem Real = new();

    private static SettingsInspection? Inspect()
    {
        var result = SettingsInspector.For(Real, SettingsLocator.SystemLocalAppData).Inspect();
        if (!result.IsSuccess) Assert.Skip($"Wave Link not usable here: {result.Error!.Message}");
        return result.Value;
    }

    [Fact]
    public void Discovery_finds_the_package_and_not_the_vendor_folder()
    {
        var inspection = Inspect()!;

        Assert.Contains(@"\Packages\Elgato.WaveLink_", inspection.Location.SettingsPath,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(@"\Roaming\Elgato", inspection.Location.SettingsPath,
            StringComparison.OrdinalIgnoreCase);
        Assert.True(inspection.Location.CanRelaunch);
    }

    [Fact]
    public void The_settings_file_reads_while_Wave_Link_is_running()
    {
        // The whole point of FileShare.ReadWrite | FileShare.Delete. If this passes only
        // when Wave Link is closed, the watcher would fail on almost every capture.
        var process = new WaveLinkProcess();
        if (!process.IsRunning) Assert.Skip("Wave Link is not running; this test only means something when it is.");

        var inspection = Inspect()!;

        Assert.NotEmpty(inspection.Bytes);
    }

    [Fact]
    public void The_naive_read_fails_while_Wave_Link_is_running()
    {
        // Pins the gotcha itself. If this ever stops throwing, Wave Link changed its share
        // mode and the guard in SourceGuardTests could be relaxed - but not before.
        var process = new WaveLinkProcess();
        if (!process.IsRunning) Assert.Skip("Wave Link is not running; the file is not locked.");

        var inspection = Inspect()!;

        Assert.Throws<IOException>(() => File.ReadAllBytes(inspection.Location.SettingsPath));
    }

    [Fact]
    public void The_live_configuration_analyses_and_is_free_of_duplicate_keys()
    {
        var inspection = Inspect()!;

        Assert.False(inspection.Analysis.Report.HasCaseInsensitiveDuplicateKeys);
        Assert.True(inspection.Analysis.Fingerprint.InputCount > 0);
        Assert.Equal(inspection.Bytes.LongLength, inspection.Analysis.Fingerprint.SizeBytes);
    }

    [Fact]
    public void Two_reads_of_an_unchanged_file_produce_the_same_hash()
    {
        // The dedup premise. If this is flaky, hash-dedup would store a new snapshot on
        // every capture and the whole retention design falls over.
        var first = Inspect()!;
        var second = Inspect()!;

        Assert.Equal(first.Analysis.Fingerprint.Sha256, second.Analysis.Fingerprint.Sha256);
    }

    [Fact]
    public void A_fingerprint_compared_with_itself_shows_nothing_lost_and_no_change()
    {
        var fingerprint = Inspect()!.Analysis.Fingerprint;

        var comparison = fingerprint.CompareTo(fingerprint);

        Assert.False(comparison.ContentChanged);
        Assert.False(comparison.LooksCollapsed);
        Assert.Empty(comparison.NamesLost);
    }

    [Fact]
    public void The_newest_log_can_be_read_and_parsed()
    {
        var inspection = Inspect()!;
        var log = new SettingsReader(Real).ReadNewestLog(inspection.Location.LogsPath);

        if (!log.IsSuccess) Assert.Skip($"No readable log: {log.Error!.Message}");

        // Only asserting it parses. Whether THIS log shows a successful restore depends on
        // what the user last did, so asserting a verdict would be asserting their history.
        var verdict = Core.Analysis.LogAnalysis.Verify(log.Value);
        Assert.NotNull(verdict.AppliedNames);
    }
}
