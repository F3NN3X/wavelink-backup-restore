using WaveLinkBackup.App.ViewModels;
using WaveLinkBackup.Core.Snapshots;

namespace WaveLinkBackup.App.Tests;

/// <summary>
/// 04-in-progress.md's backing-up strip. The half of that screen that had no implementation of any
/// kind until 0.6.1 — the restore half shipped complete and this was a <c>BackupHost.IsCapturing</c>
/// flag only the tray icon read (technical-debt.md §4.21 item 2).
///
/// The rule worth pinning is the honesty of the bar: it is determinate because the bytes are real,
/// and 04 bans a spinner for a reason that applies twice as hard to a made-up percentage.
/// </summary>
public sealed class BackupProgressModelTests
{
    [Fact]
    public void Nothing_shows_until_a_capture_begins()
    {
        var model = new BackupProgressModel();

        Assert.False(model.IsCapturing);
        Assert.Equal(string.Empty, model.Meta);
        Assert.Equal(0, model.Fraction);
    }

    /// <summary>
    /// The strip is up BEFORE the first report. 04: "replaced in place by the result line; the
    /// strip never disappears and reappear-flashes" — which needs it present for the whole
    /// operation, not just the part where a number exists.
    /// </summary>
    [Fact]
    public void The_strip_is_up_before_a_single_byte_is_known()
    {
        var model = new BackupProgressModel();
        model.Begin();

        Assert.True(model.IsCapturing);
        Assert.Equal("Backing up your setup…", model.Sentence);
        Assert.Equal("MEASURING", model.Meta);
    }

    [Fact]
    public void A_report_moves_the_bar_and_prints_what_is_on_disk()
    {
        var model = new BackupProgressModel();
        model.Begin();

        model.Report(new SnapshotWriteProgress(WrittenBytes: 235_000, TotalBytes: 470_000, Done: false));

        Assert.Equal(0.5, model.Fraction, 3);
        // The figure is what is DOWN, not the total - the whole point of the line.
        Assert.Equal($"{Readable.Bytes(235_000)} · WRITING", model.Meta);
    }

    [Fact]
    public void Completing_takes_the_strip_down_so_the_outcome_can_take_the_slot()
    {
        var model = new BackupProgressModel();
        model.Begin();
        model.Report(new SnapshotWriteProgress(470_000, 470_000, Done: true));
        model.Complete();

        Assert.False(model.IsCapturing);
        Assert.Equal(1, model.Fraction);
    }

    /// <summary>
    /// A late report from a capture that already finished must not raise the strip again — the one
    /// way this model could produce the reappear-flash 04 forbids.
    /// </summary>
    [Fact]
    public void A_report_after_the_capture_finished_is_ignored()
    {
        var model = new BackupProgressModel();
        model.Begin();
        model.Complete();

        model.Report(new SnapshotWriteProgress(1, 2, Done: false));

        Assert.False(model.IsCapturing);
    }

    [Fact]
    public void A_second_capture_starts_from_zero_rather_than_the_last_ones_figure()
    {
        var model = new BackupProgressModel();
        model.Begin();
        model.Report(new SnapshotWriteProgress(470_000, 470_000, Done: true));
        model.Complete();

        model.Begin();

        Assert.Equal(0, model.Fraction);
        Assert.Equal("MEASURING", model.Meta);
    }

    /// <summary>A zero-byte total is complete, not a divide by zero.</summary>
    [Fact]
    public void A_total_of_nothing_reads_as_finished()
    {
        Assert.Equal(1, new SnapshotWriteProgress(0, 0, Done: true).Fraction);
    }

    [Fact]
    public void The_fraction_never_leaves_zero_to_one()
    {
        Assert.Equal(1, new SnapshotWriteProgress(900, 400, Done: false).Fraction);
        Assert.Equal(0, new SnapshotWriteProgress(-5, 400, Done: false).Fraction);
    }
}
