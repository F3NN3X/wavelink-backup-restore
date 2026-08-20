using System.Text;
using WaveLinkBackup.Core.Analysis;
using WaveLinkBackup.Core.Automation;
using WaveLinkBackup.Core.Io;
using WaveLinkBackup.Core.Snapshots;
using WaveLinkBackup.Core.Tests.Fakes;

namespace WaveLinkBackup.Core.Tests;

/// <summary>
/// technical-debt.md §6 and SPEC.md §11's privacy note: <c>Settings.json</c> carries hardware
/// serial numbers inside device IDs and absolute paths carrying the Windows username, and users
/// WILL attach it to a bug report.
///
/// **The threat here is not an attacker, it is helpfulness**, which is why the tests below are
/// mostly about what must NOT appear. A redactor that works on the shapes it was written for and
/// passes an unrecognised one through is worse than none: it teaches the user the output is safe.
/// </summary>
public sealed class RedactionTests
{
    // The reference rig's real endpoint ID shape (SPEC.md §5).
    private const string EndpointId = @"BS33J1A05009\PCM_IN_01_C_00_SD1";

    [Fact]
    public void An_endpoint_id_loses_its_serial_and_keeps_its_port()
    {
        var redacted = Redaction.EndpointId(EndpointId);

        Assert.DoesNotContain("BS33J1A05009", redacted, StringComparison.OrdinalIgnoreCase);

        // The tail is kept: it says which physical port the channel is on, which is what a support
        // conversation is about, and every Wave:3 on earth has the same one.
        Assert.Contains("PCM_IN_01_C_00_SD1", redacted, StringComparison.Ordinal);
    }

    /// <summary>
    /// The rule that makes this trustworthy: a shape it does not understand is masked WHOLESALE,
    /// not passed through in the hope that it is harmless.
    /// </summary>
    [Theory]
    [InlineData("BS33J1A05009")]
    [InlineData("SOMETHINGELSEENTIRELY")]
    [InlineData(@"\LeadingSeparator")]
    public void An_id_this_does_not_understand_is_masked_entirely(string id)
    {
        Assert.Equal(Redaction.Mask, Redaction.EndpointId(id));
    }

    [Fact]
    public void A_profile_path_loses_the_name_and_keeps_the_shape()
    {
        var redacted = Redaction.Path(@"C:\Users\joran\AppData\Local\WaveLinkBackup\settings.json");

        Assert.DoesNotContain("joran", redacted, StringComparison.OrdinalIgnoreCase);

        // Which folder is the entire diagnostic value of a path.
        Assert.Contains(@"C:\Users\", redacted, StringComparison.Ordinal);
        Assert.Contains(@"AppData\Local\WaveLinkBackup\settings.json", redacted, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(@"c:\users\joran\x")]
    [InlineData(@"D:\Users\joran\x")]
    public void The_profile_rule_is_case_and_drive_insensitive(string path)
    {
        Assert.DoesNotContain("joran", Redaction.Path(path), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The case the profile rule cannot catch: a store on another drive, a redirected Documents
    /// folder, a plug-in under a home-made path. The name is replaced wherever it appears.
    /// </summary>
    [Fact]
    public void The_users_name_is_removed_from_a_path_that_is_not_under_their_profile()
    {
        var redacted = Redaction.Path(@"D:\joran-backups\WaveLinkBackup", userName: "joran");

        Assert.DoesNotContain("joran", redacted, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(@"D:\", redacted, StringComparison.Ordinal);
    }

    [Fact]
    public void Redacting_text_catches_an_endpoint_id_embedded_in_a_sentence()
    {
        var redacted = Redaction.Text($"Input {EndpointId} could not be read", userName: "joran");

        Assert.DoesNotContain("BS33J1A05009", redacted, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("could not be read", redacted, StringComparison.Ordinal);
    }

    /// <summary>
    /// Channel names are what the user calls their own channels, they are the subject of nearly
    /// every support question, and they name a setup rather than a person.
    /// </summary>
    [Fact]
    public void An_input_name_is_not_redacted()
    {
        Assert.Equal("Wave Mic 1, Voice, Browser", Redaction.Text("Wave Mic 1, Voice, Browser"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Nothing_in_yields_nothing_out(string? value)
    {
        Assert.Equal(string.Empty, Redaction.Path(value));
        Assert.Equal(string.Empty, Redaction.Text(value));
        Assert.Equal(string.Empty, Redaction.EndpointId(value));
    }

    // --------------------------------------------------------------- the report itself

    private const string LocalAppData = @"C:\Users\joran\AppData\Local";
    private const string SettingsPath =
        LocalAppData + @"\Packages\Elgato.WaveLink_g54w8ztgkx496\LocalState\Settings.json";

    /// <summary>
    /// A settings file keyed by the real endpoint-ID shape — the serial has to be IN the data for
    /// "the report does not contain it" to mean anything.
    /// </summary>
    private static readonly string Live =
        """
        {"Update":{"LastUpdateVersion":"3.3.0.4108"},
         "MixerConfiguration":{"InputSettings":{
           "ENDPOINT":{"InputName":"Wave Mic 1","AudioPluginConfigurations":[]}}}}
        """.Replace("ENDPOINT", EndpointId.Replace("\\", "\\\\"), StringComparison.Ordinal);

    private static (SettingsInspection Live, IReadOnlyList<Snapshot> Snapshots) Rig()
    {
        var fs = new FakeFileSystem().AddFile(SettingsPath, Live);
        var clock = new FakeClock(new DateTimeOffset(2026, 8, 20, 12, 0, 0, TimeSpan.Zero));
        var store = new SnapshotStore(fs, clock, @"C:\Users\joran\Backups");

        var bytes = Encoding.UTF8.GetBytes(Live);
        store.Write(bytes, SettingsAnalysis.Analyse(bytes).Value, SnapshotTrigger.Manual, "Dave's session");

        return (SettingsInspector.For(fs, LocalAppData).Inspect().Value, store.List());
    }

    /// <summary>
    /// The one assertion this whole feature exists for. If it ever fails, somebody has added a
    /// field that bypasses Redaction — which is exactly how this goes wrong.
    /// </summary>
    [Fact]
    public void The_report_contains_neither_a_serial_number_nor_a_user_name()
    {
        var (live, snapshots) = Rig();

        var report = Core.Analysis.Diagnostics.Report(
            "0.6.3",
            BackupSettings.Default with { StorePath = @"C:\Users\joran\Backups" },
            live,
            snapshots,
            new DateTimeOffset(2026, 8, 20, 12, 0, 0, TimeSpan.Zero),
            userName: "joran");

        Assert.DoesNotContain("BS33J1A05009", report, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("joran", report, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The display name is the one free-text field in a snapshot, and people put anything in it.
    /// Nothing in a support conversation needs it.
    /// </summary>
    [Fact]
    public void The_report_leaves_out_the_name_the_user_typed()
    {
        var (live, snapshots) = Rig();

        var report = Core.Analysis.Diagnostics.Report(
            "0.6.3", BackupSettings.Default, live, snapshots,
            new DateTimeOffset(2026, 8, 20, 12, 0, 0, TimeSpan.Zero), userName: "joran");

        Assert.DoesNotContain("Dave", report, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// A redacted copy of a file is still a copy of a file. The report describes STRUCTURE — how
    /// many inputs, what they are called, which tiers — and never quotes the settings.
    /// </summary>
    [Fact]
    public void The_report_never_includes_the_settings_file_itself()
    {
        var (live, snapshots) = Rig();

        var report = Core.Analysis.Diagnostics.Report(
            "0.6.3", BackupSettings.Default, live, snapshots,
            new DateTimeOffset(2026, 8, 20, 12, 0, 0, TimeSpan.Zero), userName: "joran");

        Assert.DoesNotContain("MixerConfiguration", report, StringComparison.Ordinal);
        Assert.DoesNotContain("AudioPluginConfigurations", report, StringComparison.Ordinal);
    }

    [Fact]
    public void The_report_still_says_the_things_a_support_question_needs()
    {
        var (live, snapshots) = Rig();

        var report = Core.Analysis.Diagnostics.Report(
            "0.6.3", BackupSettings.Default, live, snapshots,
            new DateTimeOffset(2026, 8, 20, 12, 0, 0, TimeSpan.Zero), userName: "joran");

        Assert.Contains("0.6.3", report, StringComparison.Ordinal);
        Assert.Contains("3.3.0.4108", report, StringComparison.Ordinal);
        Assert.Contains("Wave Mic 1", report, StringComparison.Ordinal);
        Assert.Contains("Backups", report, StringComparison.Ordinal);
    }

    [Fact]
    public void A_machine_with_no_wave_link_still_produces_a_report()
    {
        var report = Core.Analysis.Diagnostics.Report(
            "0.6.3", BackupSettings.Default, live: null, snapshots: [],
            new DateTimeOffset(2026, 8, 20, 12, 0, 0, TimeSpan.Zero), userName: "joran");

        Assert.Contains("Not found", report, StringComparison.Ordinal);
    }
}
