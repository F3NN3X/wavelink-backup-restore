using System.Runtime.InteropServices;
using WaveLinkBackup.Core.Abstractions;
using WaveLinkBackup.Core.Analysis;
using WaveLinkBackup.Core.Automation;
using WaveLinkBackup.Core.Io;
using WaveLinkBackup.Core.Snapshots;

namespace WaveLinkBackup.Core.Tests;

/// <summary>
/// The endpoint inspector, and the diagnostics section that reports it.
///
/// <para>
/// The enumeration itself can only be checked against a real audio stack, so those tests skip off
/// Windows exactly as <see cref="RealInstallTests"/> does. What CI can hold down is the part that
/// matters most: that endpoint ids and device names never reach the report.
/// </para>
/// </summary>
public sealed class AudioEndpointInspectorTests
{
    private static readonly AudioEndpoint[] Sample =
    [
        // A realistic id: Wave Link keys channels by exactly this, and the serial in the middle
        // is the reason snapshots are machine-local (technical-debt.md 3).
        new("{0.0.1.00000000}.{BS33J1A05009-1111-2222-3333-444455556666}",
            "Wave XLR", EndpointDirection.Capture, EndpointState.Active),
        new("{0.0.1.00000000}.{aaaabbbb-cccc-dddd-eeee-ffff00001111}",
            "Line In (Realtek)", EndpointDirection.Capture, EndpointState.Unplugged),
        new("{0.0.0.00000000}.{99998888-7777-6666-5555-444433332222}",
            "Speakers", EndpointDirection.Render, EndpointState.Active),
    ];

    private static string Report(IReadOnlyList<AudioEndpoint>? endpoints) =>
        Diagnostics.Report(
            "1.2.3",
            BackupSettings.Default,
            live: null,
            snapshots: [],
            now: DateTimeOffset.UnixEpoch,
            userName: "someone",
            endpoints: endpoints);

    [Fact]
    public void An_endpoint_id_never_reaches_the_diagnostics_report()
    {
        // The whole point of the report is being safe to paste into a public tracker, and an
        // endpoint id carries a device serial. Counting them is useful; naming them is a leak.
        var report = Report(Sample);

        Assert.DoesNotContain("BS33J1A05009", report, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("0.0.1.00000000", report, StringComparison.Ordinal);
        foreach (var endpoint in Sample)
        {
            Assert.DoesNotContain(endpoint.Id, report, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void A_device_name_never_reaches_the_diagnostics_report()
    {
        // A friendly name is the hardware someone owns. Input names ARE kept - they are what the
        // user calls their own channels - but the device behind them is not the same thing.
        var report = Report(Sample);

        Assert.DoesNotContain("Wave XLR", report, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Realtek", report, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Speakers", report, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_report_counts_endpoints_by_direction_and_state()
    {
        var report = Report(Sample);

        Assert.Contains("Audio endpoints", report, StringComparison.Ordinal);
        Assert.Contains("Capture: 1 active, 1 unplugged", report, StringComparison.Ordinal);
        Assert.Contains("Render: 1 active", report, StringComparison.Ordinal);
    }

    [Fact]
    public void An_empty_list_says_so_rather_than_printing_an_empty_section()
    {
        // A machine with no audio service reports nothing, and "none" is a finding: it explains a
        // dead channel that would otherwise look like a settings problem.
        Assert.Contains("None reported", Report([]), StringComparison.Ordinal);
    }

    [Fact]
    public void Omitting_endpoints_omits_the_section_entirely()
    {
        // Every caller that predates the inspector passes nothing, and must keep the report it
        // had. The parameter is optional for exactly this reason.
        Assert.DoesNotContain("Audio endpoints", Report(null), StringComparison.Ordinal);
    }

    // ------------------------------------------------- against the real audio stack

    private static IReadOnlyList<AudioEndpoint> Enumerate()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            Assert.Skip("Core Audio is Windows only.");
        }

        return new WindowsAudioEndpointInspector().List();
    }

    [Fact]
    public void The_machine_reports_at_least_one_endpoint_in_each_direction()
    {
        var endpoints = Enumerate();

        if (endpoints.Count == 0)
        {
            // A build agent with no audio device at all. Not a failure - List() promises an
            // empty list rather than an exception for exactly this machine.
            Assert.Skip("No audio endpoints on this machine.");
        }

        Assert.Contains(endpoints, e => e.Direction == EndpointDirection.Capture);
        Assert.Contains(endpoints, e => e.Direction == EndpointDirection.Render);
    }

    [Fact]
    public void Every_enumerated_endpoint_has_an_id_and_a_known_state()
    {
        var endpoints = Enumerate();
        if (endpoints.Count == 0) Assert.Skip("No audio endpoints on this machine.");

        Assert.All(endpoints, endpoint =>
        {
            // The id is the only field a channel key can match on. A blank one would make the
            // endpoint useless for the question this class exists to answer.
            Assert.NotEmpty(endpoint.Id);

            // Unknown means the state word was something ToEndpointState does not name, which
            // would mean the constants have drifted from the SDK.
            Assert.NotEqual(EndpointState.Unknown, endpoint.State);
        });
    }

    [Fact]
    public void Enumerating_twice_agrees_with_itself()
    {
        // Catches the failure mode that a COM lifetime bug actually produces: not a crash, but a
        // second call returning fewer endpoints because the first over-released something.
        var first = Enumerate();
        if (first.Count == 0) Assert.Skip("No audio endpoints on this machine.");

        var second = new WindowsAudioEndpointInspector().List();

        Assert.Equal(
            first.Select(e => e.Id).OrderBy(id => id, StringComparer.Ordinal),
            second.Select(e => e.Id).OrderBy(id => id, StringComparer.Ordinal));
    }
}
