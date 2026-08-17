using WaveLinkBackup.App.Startup;

namespace WaveLinkBackup.App.Tests;

/// <summary>
/// Mandatory, not a nicety: two instances means two watchers racing on one settings file.
/// Every test uses a unique name so the suite never collides with itself or a running app.
/// </summary>
public sealed class SingleInstanceTests
{
    private static string UniqueName() => "WaveLinkBackupTests-" + Guid.NewGuid().ToString("N");

    [Fact]
    public void The_first_instance_wins()
    {
        var name = UniqueName();

        using var first = SingleInstance.TryAcquire(name);

        Assert.True(first.IsFirst);
    }

    [Fact]
    public void A_second_instance_knows_it_is_second()
    {
        var name = UniqueName();

        using var first = SingleInstance.TryAcquire(name);
        using var second = SingleInstance.TryAcquire(name);

        Assert.True(first.IsFirst);
        Assert.False(second.IsFirst);
    }

    [Fact]
    public void Releasing_the_first_lets_a_later_one_win()
    {
        var name = UniqueName();

        var first = SingleInstance.TryAcquire(name);
        Assert.True(first.IsFirst);
        first.Dispose();

        using var later = SingleInstance.TryAcquire(name);
        Assert.True(later.IsFirst);
    }

    /// <summary>
    /// The only message is "show yourself", so there is no payload — but a second launch
    /// carrying --tray must be able to exit silently instead of forcing a window open that
    /// nobody asked for.
    /// </summary>
    [Fact]
    public async Task Signalling_with_a_window_request_raises_activation_on_the_first()
    {
        var name = UniqueName();
        using var first = SingleInstance.TryAcquire(name);

        var activated = new TaskCompletionSource();
        first.ActivationRequested += (_, _) => activated.TrySetResult();
        first.StartListening();

        using (var second = SingleInstance.TryAcquire(name))
        {
            second.SignalExistingInstance(wantsWindow: true);
        }

        await activated.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Signalling_without_a_window_request_does_not_raise_activation()
    {
        var name = UniqueName();
        using var first = SingleInstance.TryAcquire(name);

        var activated = new TaskCompletionSource();
        first.ActivationRequested += (_, _) => activated.TrySetResult();
        first.StartListening();

        using (var second = SingleInstance.TryAcquire(name))
        {
            second.SignalExistingInstance(wantsWindow: false);
        }

        var raised = await Task.WhenAny(
            activated.Task,
            Task.Delay(TimeSpan.FromMilliseconds(600), TestContext.Current.CancellationToken));

        Assert.NotSame(activated.Task, raised);
    }
}
