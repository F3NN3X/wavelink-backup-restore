using System.Text;
using WaveLinkBackup.App.Hosting;
using WaveLinkBackup.Core.Analysis;
using WaveLinkBackup.Core.Snapshots;
using WaveLinkBackup.Core.Tests.Fakes;

namespace WaveLinkBackup.App.Tests;

/// <summary>
/// Where DAMAGED comes from. The store will not hash on List() and is right not to, so the
/// shell hashes on its own thread and the rows flip when it answers.
/// </summary>
public sealed class HealthProbeTests
{
    private const string StorePath = @"C:\store";

    private static readonly byte[] Settings = Encoding.UTF8.GetBytes("""
        {"MixerConfiguration":{"InputSettings":{
          "A":{"InputName":"Wave Mic 1","AudioPluginConfigurations":[]},
          "B":{"InputName":"Voice","AudioPluginConfigurations":[]}}}}
        """);

    private static (HealthProbe Probe, FakeFileSystem Fs, Snapshot Snapshot) Rig(bool suspect = false)
    {
        var fs = new FakeFileSystem();
        var clock = new FakeClock();
        var store = new SnapshotStore(fs, clock, StorePath);

        var analysis = SettingsAnalysis.Analyse(Settings).Value;

        if (suspect)
        {
            analysis = analysis with
            {
                Report = analysis.Report with
                {
                    DuplicateKeys = [new DuplicateKeyFinding("$.MixerConfiguration.InputSettings", ["A", "a"])],
                },
            };
        }

        var written = store.Write(Settings, analysis, SnapshotTrigger.Manual, "Before 3.3 beta");

        return (new HealthProbe(store, fs, clock), fs, written.Value);
    }

    [Fact]
    public void A_snapshot_that_verifies_and_is_not_suspect_is_whole()
    {
        var (probe, _, snapshot) = Rig();

        Assert.Equal(SnapshotHealth.Whole, probe.Check(snapshot).Health);
    }

    [Fact]
    public void A_snapshot_that_verifies_but_failed_validation_is_suspect()
    {
        var (probe, _, snapshot) = Rig(suspect: true);

        Assert.Equal(SnapshotHealth.Suspect, probe.Check(snapshot).Health);
    }

    [Fact]
    public void A_snapshot_whose_bytes_changed_is_damaged()
    {
        var (probe, fs, snapshot) = Rig();

        fs.WriteBytes(snapshot.SettingsPath, "tampered"u8.ToArray());

        Assert.Equal(SnapshotHealth.Damaged, probe.Check(snapshot).Health);
    }

    // "Contents are unknowable" beats "contents are not whole". A row cannot draw both, and the
    // louder claim is the one that is still true.
    [Fact]
    public void Damaged_outranks_suspect()
    {
        var (probe, fs, snapshot) = Rig(suspect: true);

        fs.WriteBytes(snapshot.SettingsPath, "tampered"u8.ToArray());

        Assert.Equal(SnapshotHealth.Damaged, probe.Check(snapshot).Health);
    }

    [Theory]
    [InlineData(true, false, SnapshotHealth.Whole)]
    [InlineData(true, true, SnapshotHealth.Suspect)]
    [InlineData(false, false, SnapshotHealth.Damaged)]
    [InlineData(false, true, SnapshotHealth.Damaged)]
    public void The_decision_is_a_table(bool verified, bool isSuspect, SnapshotHealth expected)
    {
        Assert.Equal(expected, HealthProbe.Decide(verified, isSuspect));
    }

    // 02's selected-damaged detail line is "MANIFEST SAYS 470 KB · FILE IS 12 KB · CHECKED
    // 23:09". Without both figures the row can only say something went wrong, which is what the
    // design deliberately refuses to settle for.
    [Fact]
    public void A_damaged_verdict_carries_both_sizes_and_the_time_it_was_checked()
    {
        var (probe, fs, snapshot) = Rig();

        fs.WriteBytes(snapshot.SettingsPath, "short"u8.ToArray());

        var verdict = probe.Check(snapshot);

        Assert.Equal(Settings.LongLength, verdict.ManifestBytes);
        Assert.Equal(5, verdict.ActualBytes);
        Assert.NotEqual(default, verdict.CheckedAt);
    }

    [Fact]
    public void A_missing_file_reports_a_null_actual_size_rather_than_zero()
    {
        var (probe, fs, snapshot) = Rig();

        fs.Delete(snapshot.SettingsPath);

        var verdict = probe.Check(snapshot);

        Assert.Equal(SnapshotHealth.Damaged, verdict.Health);
        Assert.Null(verdict.ActualBytes);
    }

    [Fact]
    public async Task Probing_reports_every_snapshot_by_id()
    {
        var (probe, _, snapshot) = Rig();

        var reported = new Dictionary<string, HealthVerdict>(StringComparer.Ordinal);

        await probe.ProbeAsync([snapshot], (id, verdict) => reported[id] = verdict, CancellationToken.None);

        Assert.Equal(SnapshotHealth.Whole, reported[snapshot.Id].Health);
    }

    // F5 while a probe is still running must not have the old run writing verdicts into the new
    // list. Cancellation is what makes a refresh cheap instead of racy.
    [Fact]
    public async Task A_cancelled_probe_reports_nothing()
    {
        var (probe, _, snapshot) = Rig();

        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        var reported = 0;

        try
        {
            await probe.ProbeAsync([snapshot], (_, _) => reported++, cancellation.Token);
        }
        catch (OperationCanceledException)
        {
            // Task.Run with an already-cancelled token faults rather than running. Either way
            // the assertion below is the point.
        }

        Assert.Equal(0, reported);
    }
}
