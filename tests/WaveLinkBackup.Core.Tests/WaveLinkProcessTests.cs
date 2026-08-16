using WaveLinkBackup.Core.Process;

namespace WaveLinkBackup.Core.Tests;

/// <summary>
/// Read-only tests of the real process adapter.
///
/// <see cref="IWaveLinkProcess.CloseAndVerifyExited"/> is deliberately NOT tested here: it
/// would close the user's Wave Link. Its contract is exercised through
/// <c>FakeWaveLinkProcess</c> in <see cref="SettingsWriterTests"/>, which is the whole
/// reason the seam exists.
///
/// Nor is <see cref="IWaveLinkProcess.LaunchByAppId"/> - launching an app as a side effect
/// of running tests is not acceptable, however small.
/// </summary>
public sealed class WaveLinkProcessTests
{
    [Fact]
    public void IsRunning_agrees_with_the_process_names_it_reports()
    {
        var process = new WaveLinkProcess();

        Assert.Equal(process.RunningProcessNames.Count > 0, process.IsRunning);
    }

    [Fact]
    public void It_reports_only_Wave_Links_own_processes()
    {
        // Upstream only ever looks for Elgato.WaveLink and never touches WavelinkSEService,
        // so its "verified exited" check can pass while the service is still up.
        // Audit finding 6.
        var process = new WaveLinkProcess();

        Assert.All(process.RunningProcessNames,
            name => Assert.Contains(name, (string[])["Elgato.WaveLink", "WavelinkSEService"]));
    }

    [Fact]
    public void Both_processes_are_covered_not_just_the_gui()
    {
        if (!new WaveLinkProcess().IsRunning) Assert.Skip("Wave Link is not running.");

        // On a machine with Wave Link actually up, the service should be seen too. If this
        // ever fails, the shutdown sequence is only closing half of Wave Link.
        Assert.Contains("WavelinkSEService", new WaveLinkProcess().RunningProcessNames);
    }
}
