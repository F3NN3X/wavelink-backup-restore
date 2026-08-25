using WaveLinkBackup.Core.Snapshots;

namespace WaveLinkBackup.App.ViewModels;

/// <summary>
/// The backing-up half of 04-in-progress.md, which had no implementation of any kind: the restore
/// half shipped complete, four named stages, connectors, STEP n OF 4, and this one was a
/// <c>BackupHost.IsCapturing</c> flag that only the tray icon read (technical-debt.md §4.21 item 2).
///
/// The bar is determinate because the numbers are real. The design bans a spinner ("a spinner
/// implies uncertainty that does not exist here"), which makes an invented determinate bar the
/// worse version of the same lie. <see cref="SnapshotWriteProgress"/> reports bytes actually on
/// disk against a total the payload knew before the first write.
///
/// It is deliberately NOT part of <see cref="RestoreProgressModel"/>. The two occupy the same strip
/// slot and are mutually exclusive, but a restore is four named stages that must advance in order
/// and a capture is one number going up.
/// </summary>
public sealed class BackupProgressModel : ObservableObject
{
    private bool isCapturing;
    private double fraction;
    private long writtenBytes;
    private long totalBytes;
    private bool writing;

    /// <summary>Whether the strip is showing. Nothing else in the app decides this.</summary>
    public bool IsCapturing
    {
        get => isCapturing;
        private set
        {
            if (Set(ref isCapturing, value)) Raise(nameof(Meta));
        }
    }

    /// <summary>04's sentence, fixed: "Backing up your setup…".</summary>
    public string Sentence => "Backing up your setup…";

    /// <summary>
    /// 04's mono meta, right-aligned: <c>470 KB · WRITING</c>. The figure is what has been written
    /// so far once anything has, and the total before that, so the line is never blank and never
    /// claims a byte that is not down yet.
    /// </summary>
    public string Meta
    {
        get
        {
            if (!isCapturing) return string.Empty;

            var bytes = writing ? writtenBytes : totalBytes;
            var stage = writing ? "WRITING" : "MEASURING";

            return bytes > 0 ? $"{Readable.Bytes(bytes)} · {stage}" : stage;
        }
    }

    /// <summary>0 to 1, for the 2px bar across the strip's bottom edge.</summary>
    public double Fraction
    {
        get => fraction;
        private set => Set(ref fraction, value);
    }

    /// <summary>
    /// Show the strip, before a single byte is known. Called on the press, not on the first
    /// report: 04's rule is that the strip is "replaced in place by the result line" and never
    /// "disappears and reappear-flashes", which needs it up for the whole operation.
    /// </summary>
    public void Begin()
    {
        writtenBytes = 0;
        totalBytes = 0;
        writing = false;
        Fraction = 0;
        IsCapturing = true;
    }

    /// <summary>One report from the store. Ignored once the capture has finished.</summary>
    public void Report(SnapshotWriteProgress progress)
    {
        if (!isCapturing) return;

        writtenBytes = progress.WrittenBytes;
        totalBytes = progress.TotalBytes;
        writing = true;

        Fraction = progress.Fraction;
        Raise(nameof(Meta));
    }

    /// <summary>
    /// Hide the strip. The caller shows the outcome in the same slot immediately afterwards,
    /// which is what makes the replacement "in place".
    /// </summary>
    public void Complete()
    {
        Fraction = 1;
        IsCapturing = false;
    }
}
