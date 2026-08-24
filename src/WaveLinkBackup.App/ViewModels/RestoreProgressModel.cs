namespace WaveLinkBackup.App.ViewModels;

/// <summary>The four named stages of a restore, in the only order it runs in.</summary>
public enum RestoreStage
{
    ClosingWaveLink = 0,
    WritingSettings = 1,
    StartingWaveLink = 2,
    Checking = 3,
}

/// <summary>The three treatments a stage row can carry (04-in-progress.md).</summary>
public enum StageStatus
{
    Pending,
    Current,
    Done,
}

/// <summary>One row of the in-progress strip: a named stage and its current treatment.</summary>
/// <param name="Index">0–3, left to right. Drives "STEP n OF 4".</param>
/// <param name="Label">The ALL-CAPS mono label exactly as designed.</param>
/// <param name="Status">Pending / Current / Done — the view maps each to its own treatment.</param>
public sealed record RestoreStageView(int Index, string Label, StageStatus Status);

/// <summary>
/// The restore in-progress strip's state: four named stages advancing left to right, no spinner.
///
/// 04-in-progress.md is authoritative. Wave Link is closed during a restore, so the work is
/// shown as four named steps rather than an abstract bar — a spinner would imply uncertainty
/// that does not exist. This model owns the frontier; the view just renders each stage's
/// status. The orchestrator drives it in order via <see cref="Advance"/>; anything out of order
/// is a bug and throws, because silently re-ordering the steps would lie to the user about
/// what has already happened to their mixer.
/// </summary>
public sealed class RestoreProgressModel : ObservableObject
{
    /// <summary>The stage labels exactly as 04-in-progress.md prints them.</summary>
    public static readonly string[] StageLabels =
    [
        "CLOSING WAVE LINK",
        "WRITING SETTINGS",
        "STARTING WAVE LINK",
        "CHECKING",
    ];

    /// <summary>The reassurance line under the stages, verbatim from 04-in-progress.md.</summary>
    public const string ReassuranceText =
        "Your mixer is closed while this happens. Nothing is lost if it takes longer than you "
        + "expect — today's settings are already saved as “Before restore”.";

    private readonly RestoreStageView[] _stages;
    private int _current;
    private bool _finished;
    private TimeSpan _elapsed;

    public RestoreProgressModel()
    {
        // Start with stage 0 current, the rest pending — a restore that has begun is already closing Wave Link.
        _stages = StageLabels.Select((label, index) => new RestoreStageView(
            index, label, index == 0 ? StageStatus.Current : StageStatus.Pending)).ToArray();

        Raise(nameof(Stages));
        Raise(nameof(CurrentIndex));
        Raise(nameof(RightEndReadout));
    }

    /// <summary>The four stage rows, left to right. The view binds a panel to this.</summary>
    public IReadOnlyList<RestoreStageView> Stages => _stages;

    /// <summary>0–3: which stage is (or was) current. Drives "STEP n OF 4".</summary>
    public int CurrentIndex => _finished ? StageLabels.Length - 1 : _current;

    /// <summary>True once the final stage has been marked done — the restore finished.</summary>
    public bool IsFinished => _finished;

    /// <summary>Seconds elapsed, whole seconds, for the right-end readout.</summary>
    public int ElapsedSeconds => (int)Math.Floor(_elapsed.TotalSeconds);

    /// <summary>
    /// The mono readout at the strip's right end: "STEP 3 OF 4 · 2 SECONDS SO FAR". While a stage
    /// is current it counts up; once finished it reads "DONE".
    /// </summary>
    public string RightEndReadout => _finished
        ? "DONE"
        : $"STEP {CurrentIndex + 1} OF {StageLabels.Length} · {ElapsedSeconds} SECOND{(ElapsedSeconds == 1 ? "" : "S")} SO FAR";

    /// <summary>The reassurance line, always present while the strip is showing.</summary>
    public string Reassurance => ReassuranceText;

    /// <summary>
    /// 0–1: how far through the four stages the restore has got. It counts COMPLETED stages, not
    /// the current one — stage 0 current = 0.25 (one quarter of the way in), stage 1 = 0.5,
    /// stage 2 = 0.75, and the bar holds at 0.75 while the final stage is still running; only
    /// Complete() takes it to 1.0. Counting the current stage would fill the bar to full a moment
    /// before "DONE" actually prints, which reads as finished when it is not. Drives the strip's
    /// red progress bar the same way BackupProgress.Fraction drives the backup strip's — one
    /// number, one converter.
    /// </summary>
    public double Fraction => (_finished ? StageLabels.Length : _current) / (double)StageLabels.Length;

    /// <summary>
    /// Move the frontier to <paramref name="stage"/>: it becomes Current, every earlier stage
    /// Done, every later one Pending. The orchestrator calls this in order as each step completes.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">A stage before the current frontier.</exception>
    public void Advance(RestoreStage stage)
    {
        if (_finished) return; // Advancing past Checking is a no-op; the restore is over.

        var index = (int)stage;
        if (index < _current)
        {
            throw new ArgumentOutOfRangeException(
                nameof(stage), stage,
                "Stages advance in order; the orchestrator drives them and never goes backwards.");
        }

        for (var i = 0; i < _stages.Length; i++)
        {
            var status = i < index ? StageStatus.Done : i == index ? StageStatus.Current : StageStatus.Pending;
            if (_stages[i].Status != status)
            {
                _stages[i] = _stages[i] with { Status = status };
            }
        }

        _current = index;
        Raise(nameof(Stages));
        Raise(nameof(CurrentIndex));
        Raise(nameof(Fraction));
        Raise(nameof(RightEndReadout));
    }

    /// <summary>Mark every stage Done. The restore finished; the strip hands off to the outcome.</summary>
    public void Complete()
    {
        if (_finished) return;

        for (var i = 0; i < _stages.Length; i++)
        {
            if (_stages[i].Status != StageStatus.Done)
            {
                _stages[i] = _stages[i] with { Status = StageStatus.Done };
            }
        }

        _finished = true;
        Raise(nameof(Stages));
        Raise(nameof(CurrentIndex));
        Raise(nameof(Fraction));
        Raise(nameof(IsFinished));
        Raise(nameof(RightEndReadout));
    }

    /// <summary>Advance the elapsed clock. The view-model ticks this while a restore runs.</summary>
    public void SetElapsed(TimeSpan elapsed)
    {
        if (elapsed == _elapsed) return;

        _elapsed = elapsed;
        Raise(nameof(ElapsedSeconds));
        Raise(nameof(RightEndReadout));
    }
}
