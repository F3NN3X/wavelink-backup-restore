using System.Text;
using WaveLinkBackup.Cli.CommandLine;
using WaveLinkBackup.Cli.Commands;
using WaveLinkBackup.Core.Automation;
using WaveLinkBackup.Core.Snapshots;
using WaveLinkBackup.Core.Tests.Fakes;

namespace WaveLinkBackup.Cli.Tests;

/// <summary>
/// settings.json is the base; flags win for one run and are never written back
/// (operations/design/screens/08-settings-persistence.md).
///
/// This harness deliberately differs from <see cref="CommandRunnerTests"/> in one way: it does
/// NOT inject --store on every call, because which store gets used is the assertion here.
/// </summary>
public sealed class SettingsFileTests
{
    private const string LocalAppData = @"C:\Users\test\AppData\Local";
    private const string LocalState =
        LocalAppData + @"\Packages\Elgato.WaveLink_g54w8ztgkx496\LocalState";
    private const string SettingsPath = LocalState + @"\Settings.json";
    private const string FromSettings = @"D:\from-settings";
    private const string FromFlag = @"D:\from-flag";

    private const string Healthy = """
        {"Update":{"LastUpdateVersion":"3.3.0.4108"},
         "MixerConfiguration":{"InputSettings":{
           "BS33J1A05009\\PCM_IN_01_C_00_SD1":{"InputName":"Wave Mic 1","AudioPluginConfigurations":[{"Name":"Pro-Q 4"}]},
           "PCM_OUT_00_V_14_SD8":{"InputName":"Voice","AudioPluginConfigurations":[]}}}}
        """;

    private sealed class Harness(BackupSettings settings)
    {
        public FakeFileSystem Fs { get; } = new FakeFileSystem().AddFile(SettingsPath, Healthy);
        public FakeClock Clock { get; } = new();
        public FakeWaveLinkProcess Process { get; } = new() { Running = false };
        public FakeOutput Out { get; } = new();

        private FakeRecycleBin? bin;
        public FakeRecycleBin Bin => bin ??= new FakeRecycleBin(Fs);

        public int Run(params string[] args) =>
            new CommandRunner(Fs, Process, Clock, Out, LocalAppData, Bin, settings)
                .Run(CommandLineParser.Parse(args));

        public SnapshotStore StoreAt(string path) => new(Fs, Clock, path);
    }

    [Fact]
    public void The_store_from_the_settings_file_is_used_when_no_flag_is_given()
    {
        var h = new Harness(BackupSettings.Default with { StorePath = FromSettings });

        Assert.Equal(ExitCode.Success, h.Run("backup", "--name", "x"));

        Assert.Single(h.StoreAt(FromSettings).List());
    }

    [Fact]
    public void A_store_flag_beats_the_settings_file()
    {
        var h = new Harness(BackupSettings.Default with { StorePath = FromSettings });

        Assert.Equal(ExitCode.Success, h.Run("backup", "--name", "x", "--store", FromFlag));

        Assert.Single(h.StoreAt(FromFlag).List());
        Assert.Empty(h.StoreAt(FromSettings).List());
    }

    [Fact]
    public void The_keep_count_from_the_settings_file_is_used_when_no_flag_is_given()
    {
        var h = new Harness(BackupSettings.Default with
        {
            StorePath = FromSettings,
            AutoBackupKeepCount = 7,
        });

        h.Run("prune");

        Assert.Contains("keeping 7", h.Out.All, StringComparison.Ordinal);
    }

    [Fact]
    public void A_keep_count_flag_beats_the_settings_file()
    {
        var h = new Harness(BackupSettings.Default with
        {
            StorePath = FromSettings,
            AutoBackupKeepCount = 7,
        });

        h.Run("prune", "--keep", "3");

        Assert.Contains("keeping 3", h.Out.All, StringComparison.Ordinal);
    }

    [Fact]
    public void The_chosen_installation_is_used_when_no_settings_path_flag_is_given()
    {
        var h = new Harness(BackupSettings.Default with
        {
            StorePath = FromSettings,
            ChosenWaveLinkPath = SettingsPath,
        });

        Assert.Equal(ExitCode.Success, h.Run("backup", "--name", "x"));
    }

    /// <summary>The whole point of "a flag isn't saved": nothing the CLI does writes the file.</summary>
    [Fact]
    public void Running_a_command_never_writes_the_settings_file()
    {
        var h = new Harness(BackupSettings.Default with { StorePath = FromSettings });

        h.Run("backup", "--name", "x", "--store", FromFlag, "--keep", "3");

        var settingsFile = Path.Combine(SettingsRepository.DefaultDirectory, SettingsRepository.FileName);

        Assert.False(h.Fs.FileExists(settingsFile));
    }
}
