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

    // The exit-probe verdicts, tested through ProbeHasExited with fake probes so no live process
    // is touched. These are the two exception shapes Process.HasExited actually throws; the third
    // case is the regression that crashed v0.7.3 in the wild (WavelinkSEService runs as System).

    [Fact]
    public void Exit_probe_that_reads_false_means_still_running()
    {
        Assert.False(WaveLinkProcess.ProbeHasExited(() => false));
    }

    [Fact]
    public void Exit_probe_that_throws_invalid_operation_means_already_gone()
    {
        // The handle is invalid because the process has exited.
        Assert.True(
            WaveLinkProcess.ProbeHasExited(() => throw new InvalidOperationException()));
    }

    [Fact]
    public void Exit_probe_denied_access_to_an_elevated_process_means_not_verifiably_gone()
    {
        // WavelinkSEService runs as System; a user-level app cannot open a handle to it, so
        // HasExited throws Win32Exception(5) instead of returning. That must read as "not
        // exited" - the kill and the final verify then report WaveLinkStillRunning rather than
        // letting the fault escape as an unhandled crash.
        Assert.False(
            WaveLinkProcess.ProbeHasExited(
                () => throw new System.ComponentModel.Win32Exception(5, "Access is denied.")));
    }

    [Fact]
    public void Exit_probe_that_throws_something_else_is_not_swallowed()
    {
        // Only the two documented failure shapes are mapped. Anything else is a genuine surprise
        // and must propagate, not be silently read as either verdict.
        Assert.Throws<System.IO.IOException>(
            () => WaveLinkProcess.ProbeHasExited(
                () => throw new System.IO.IOException("disk full")));
    }

    // CloseRequiresElevation reads live processes, so the only case that is deterministic on a
    // test machine is "nothing running" - no process to probe means no elevation needed. The
    // other verdicts (reachable -> false, higher-integrity -> true) ride on ProbeHasExited's two
    // exception shapes above; the real property is the same handle access that HasExited uses.

    [Fact]
    public void No_Wave_Link_running_needs_no_elevation()
    {
        var process = new WaveLinkProcess();
        if (process.IsRunning) Assert.Skip("Wave Link is running on this machine.");

        // Nothing to close means nothing to elevate for: the restore can run in-process.
        Assert.False(process.CloseRequiresElevation);
    }

    [Fact]
    public void Elevation_never_needed_when_nothing_is_running_agrees_with_IsRunning()
    {
        var process = new WaveLinkProcess();

        // When it is not running the answer must be false; when it is running the probe answers
        // for each live process, so we only assert the half that holds on any machine.
        if (!process.IsRunning) Assert.False(process.CloseRequiresElevation);
    }
}
