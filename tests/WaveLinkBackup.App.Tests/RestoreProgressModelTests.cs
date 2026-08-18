using WaveLinkBackup.App.ViewModels;

namespace WaveLinkBackup.App.Tests;

public class RestoreProgressModelTests
{
    [Fact]
    public void Initial_State_Has_Stage_Zero_Current_Rest_Pending()
    {
        var model = new RestoreProgressModel();

        Assert.Equal(4, model.Stages.Count);
        Assert.Equal(new RestoreStageView(0, "CLOSING WAVE LINK", StageStatus.Current), model.Stages[0]);
        Assert.All(model.Stages.Skip(1), s => Assert.Equal(StageStatus.Pending, s.Status));
        Assert.False(model.IsFinished);
        Assert.Equal(0, model.CurrentIndex);
    }

    [Fact]
    public void Advance_Moves_The_Frontier_Earlier_Done_Later_Pending()
    {
        var model = new RestoreProgressModel();

        model.Advance(RestoreStage.WritingSettings);

        Assert.Equal(StageStatus.Done, model.Stages[0].Status);
        Assert.Equal(StageStatus.Current, model.Stages[1].Status);
        Assert.Equal(StageStatus.Pending, model.Stages[2].Status);
        Assert.Equal(StageStatus.Pending, model.Stages[3].Status);
        Assert.Equal(1, model.CurrentIndex);
    }

    [Fact]
    public void Advance_Through_All_Stages_Ends_With_Checking_Current()
    {
        var model = new RestoreProgressModel();

        model.Advance(RestoreStage.WritingSettings);
        model.Advance(RestoreStage.StartingWaveLink);
        model.Advance(RestoreStage.Checking);

        Assert.Equal(3, model.CurrentIndex);
        Assert.Equal(StageStatus.Done, model.Stages[0].Status);
        Assert.Equal(StageStatus.Done, model.Stages[1].Status);
        Assert.Equal(StageStatus.Done, model.Stages[2].Status);
        Assert.Equal(StageStatus.Current, model.Stages[3].Status);
    }

    [Fact]
    public void Advancing_Out_Of_Order_Throws()
    {
        var model = new RestoreProgressModel();
        model.Advance(RestoreStage.WritingSettings);

        Assert.Throws<ArgumentOutOfRangeException>(() => model.Advance(RestoreStage.ClosingWaveLink));
    }

    [Fact]
    public void Advancing_Past_Checking_After_Complete_Is_A_NoOp()
    {
        var model = new RestoreProgressModel();
        model.Complete();

        // No exception, no state change: the restore is over.
        model.Advance(RestoreStage.WritingSettings);

        Assert.True(model.IsFinished);
        Assert.All(model.Stages, s => Assert.Equal(StageStatus.Done, s.Status));
    }

    [Fact]
    public void Complete_Marks_Every_Stage_Done()
    {
        var model = new RestoreProgressModel();
        model.Advance(RestoreStage.StartingWaveLink);

        model.Complete();

        Assert.True(model.IsFinished);
        Assert.All(model.Stages, s => Assert.Equal(StageStatus.Done, s.Status));
    }

    [Fact]
    public void Right_End_Readout_Counts_The_Step_And_Seconds()
    {
        var model = new RestoreProgressModel();
        model.Advance(RestoreStage.StartingWaveLink); // stage index 2 -> STEP 3 OF 4
        model.SetElapsed(TimeSpan.FromSeconds(2));

        Assert.Equal("STEP 3 OF 4 · 2 SECONDS SO FAR", model.RightEndReadout);
    }

    [Fact]
    public void Right_End_Readout_Singular_Second_And_Done()
    {
        var model = new RestoreProgressModel();
        model.SetElapsed(TimeSpan.FromSeconds(1));
        Assert.Equal("STEP 1 OF 4 · 1 SECOND SO FAR", model.RightEndReadout);

        model.Complete();
        Assert.Equal("DONE", model.RightEndReadout);
    }

    [Fact]
    public void Reassurance_Is_The_Design_Line()
    {
        var model = new RestoreProgressModel();

        Assert.Contains("“Before restore”", model.Reassurance);
        Assert.Contains("Your mixer is closed while this happens", model.Reassurance);
    }
}
