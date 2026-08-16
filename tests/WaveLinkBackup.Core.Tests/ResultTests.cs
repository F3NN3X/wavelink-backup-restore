using WaveLinkBackup.Core.Results;

namespace WaveLinkBackup.Core.Tests;

public sealed class ResultTests
{
    [Fact]
    public void A_success_carries_its_value()
    {
        Result<int> result = 42;

        Assert.True(result.IsSuccess);
        Assert.Equal(42, result.Value);
        Assert.Null(result.Error);
    }

    [Fact]
    public void A_failure_carries_its_error()
    {
        Result<int> result = new WaveLinkNotInstalled();

        Assert.False(result.IsSuccess);
        Assert.IsType<WaveLinkNotInstalled>(result.Error);
    }

    [Fact]
    public void Reading_the_value_of_a_failure_throws_because_that_is_a_bug()
    {
        Result<int> result = new WaveLinkNotInstalled();

        var ex = Assert.Throws<InvalidOperationException>(() => result.Value);
        Assert.Contains("IsSuccess", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Propagate_carries_a_failure_across_a_type_change()
    {
        Result<int> failure = new MalformedSettings("bad");

        var propagated = failure.Propagate<string>();

        Assert.False(propagated.IsSuccess);
        Assert.Same(failure.Error, propagated.Error);
    }

    [Fact]
    public void Propagating_a_success_is_a_bug_and_throws()
    {
        Result<int> success = 1;

        Assert.Throws<InvalidOperationException>(() => success.Propagate<string>());
    }

    [Fact]
    public void The_void_result_distinguishes_success_from_failure()
    {
        Assert.True(Result.Ok().IsSuccess);

        Result failure = new WriteFailed("disk full");
        Assert.False(failure.IsSuccess);
        Assert.Contains("disk full", failure.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Error_messages_are_written_for_a_person_to_read()
    {
        // These reach the GUI verbatim, so they are part of the product, not diagnostics.
        Assert.Contains("was not found", new WaveLinkNotInstalled().Message, StringComparison.Ordinal);
        Assert.Contains("2", new MultiplePackagesFound(["a", "b"]).Message, StringComparison.Ordinal);
        Assert.Contains("still running", new WaveLinkStillRunning(["Elgato.WaveLink"]).Message, StringComparison.Ordinal);
        Assert.Contains("malformed", new MalformedSettings("x").Message, StringComparison.Ordinal);
        Assert.Contains("Could not read", new SettingsUnreadable("p", "why").Message, StringComparison.Ordinal);
        Assert.Contains("could not be replaced", new WriteFailed("why").Message, StringComparison.Ordinal);
    }
}
