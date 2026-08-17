using System.Text;
using WaveLinkBackup.Cli.CommandLine;
using WaveLinkBackup.Cli.Commands;
using WaveLinkBackup.Cli.Output;
using WaveLinkBackup.Core.Analysis;
using WaveLinkBackup.Core.Snapshots;
using WaveLinkBackup.Core.Tests.Fakes;

namespace WaveLinkBackup.Cli.Tests;

/// <summary>
/// Verb behaviour against fakes: no console, no filesystem, no Wave Link.
///
/// The CLI is thin by contract (ADR-004), so these are mostly tests of TRANSLATION —
/// arguments in, the right Core call, the right exit code back.
/// </summary>
public sealed class CommandRunnerTests
{
    private const string LocalAppData = @"C:\Users\test\AppData\Local";
    private const string LocalState =
        LocalAppData + @"\Packages\Elgato.WaveLink_g54w8ztgkx496\LocalState";
    private const string SettingsPath = LocalState + @"\Settings.json";
    private const string StorePath = @"D:\backups";

    private const string Healthy = """
        {"Update":{"LastUpdateVersion":"3.3.0.4108"},
         "MixerConfiguration":{"InputSettings":{
           "BS33J1A05009\\PCM_IN_01_C_00_SD1":{"InputName":"Wave Mic 1","AudioPluginConfigurations":[{"Name":"Pro-Q 4"}]},
           "PCM_OUT_00_V_14_SD8":{"InputName":"Voice","AudioPluginConfigurations":[]}}}}
        """;

    private sealed class Harness(bool confirm = false)
    {
        public FakeFileSystem Fs { get; } = new FakeFileSystem().AddFile(SettingsPath, Healthy);
        public FakeClock Clock { get; } = new();
        public FakeWaveLinkProcess Process { get; } = new() { Running = false };
        public FakeOutput Out { get; } = new(confirm);

        private FakeRecycleBin? bin;

        /// <summary>
        /// Lazy because it needs <see cref="Fs"/> — sending to the Recycle Bin IS the removal,
        /// so a fake that only recorded the call would let a double-delete through.
        /// </summary>
        public FakeRecycleBin Bin => bin ??= new FakeRecycleBin(Fs);

        public CommandRunner Runner => new(Fs, Process, Clock, Out, LocalAppData, Bin);

        public int Run(params string[] args)
        {
            var parsed = CommandLineParser.Parse(args);
            // Every test drives the real store location through --store so nothing touches
            // the developer's actual backups.
            return Runner.Run(parsed with { StorePath = parsed.StorePath ?? StorePath });
        }

        public SnapshotStore Store => new(Fs, Clock, StorePath);

        public void EditSettings(string micName) => Fs.WriteBytes(SettingsPath,
            Encoding.UTF8.GetBytes(Healthy.Replace("Wave Mic 1", micName, StringComparison.Ordinal)));
    }

    // ------------------------------------------------------------------ usage

    [Fact]
    public void No_arguments_prints_help_and_succeeds()
    {
        var h = new Harness();

        Assert.Equal(ExitCode.Success, h.Run());
        Assert.Contains("USAGE", h.Out.All, StringComparison.Ordinal);
    }

    [Fact]
    public void An_unknown_verb_returns_the_usage_exit_code()
    {
        var h = new Harness();

        Assert.Equal(ExitCode.Usage, h.Run("destroy"));
        Assert.NotEmpty(h.Out.Errors);
    }

    [Fact]
    public void Verbs_that_need_an_id_say_so_rather_than_failing_obscurely()
    {
        var h = new Harness();

        Assert.Equal(ExitCode.Usage, h.Run("restore"));
        Assert.Equal(ExitCode.Usage, h.Run("rename", "only-an-id"));
        Assert.Equal(ExitCode.Usage, h.Run("delete"));
    }

    [Fact]
    public void Version_prints_something_version_shaped()
    {
        var h = new Harness();

        Assert.Equal(ExitCode.Success, h.Run("version"));
        Assert.Contains("wlbackup", h.Out.Lines[0], StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------ backup / list

    [Fact]
    public void Backup_writes_a_snapshot_and_names_it()
    {
        var h = new Harness();

        Assert.Equal(ExitCode.Success, h.Run("backup", "--name", "Before 3.3 beta"));

        var snapshot = Assert.Single(h.Store.List());
        Assert.Equal("Before 3.3 beta", snapshot.Manifest.DisplayName);
        Assert.Equal(SnapshotTrigger.Manual, snapshot.Manifest.Trigger);
    }

    [Fact]
    public void Backup_without_a_name_still_produces_one()
    {
        var h = new Harness();
        h.Run("backup");

        Assert.False(string.IsNullOrWhiteSpace(h.Store.List()[0].Manifest.DisplayName));
    }

    [Fact]
    public void Backup_of_unchanged_settings_still_writes_because_the_user_asked()
    {
        var h = new Harness();
        h.Run("backup");
        h.Run("backup");

        Assert.Equal(2, h.Store.List().Count);
    }

    [Fact]
    public void List_on_an_empty_store_says_so_rather_than_printing_nothing()
    {
        var h = new Harness();

        Assert.Equal(ExitCode.Success, h.Run("list"));
        Assert.Contains("No backups yet", h.Out.All, StringComparison.Ordinal);
    }

    [Fact]
    public void List_shows_friendly_input_names_and_never_a_device_id()
    {
        // Device IDs embed hardware serial numbers. technical-debt.md 6.
        var h = new Harness();
        h.Run("backup", "--name", "x");
        h.Out.Lines.Clear();

        h.Run("list");

        Assert.Contains("Wave Mic 1", h.Out.All, StringComparison.Ordinal);
        Assert.DoesNotContain("BS33J1A05009", h.Out.All, StringComparison.Ordinal);
        Assert.DoesNotContain("PCM_IN_01", h.Out.All, StringComparison.Ordinal);
    }

    [Fact]
    public void Json_output_never_contains_a_device_id_either()
    {
        var h = new Harness();
        h.Run("backup", "--name", "x");
        h.Out.Lines.Clear();

        h.Run("list", "--json");

        Assert.StartsWith("[", h.Out.Lines[0], StringComparison.Ordinal);
        Assert.Contains("\"inputNames\"", h.Out.Lines[0], StringComparison.Ordinal);
        Assert.DoesNotContain("BS33J1A05009", h.Out.Lines[0], StringComparison.Ordinal);
    }

    [Fact]
    public void Json_output_escapes_a_name_that_would_break_it()
    {
        var h = new Harness();
        h.Run("backup", "--name", "He said \"hi\"\\done");
        h.Out.Lines.Clear();

        h.Run("list", "--json");

        Assert.Contains("\\\"hi\\\"", h.Out.Lines[0], StringComparison.Ordinal);
        Assert.Contains("\\\\done", h.Out.Lines[0], StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------ restore

    [Fact]
    public void Restore_without_yes_shows_the_plan_and_does_NOT_restore()
    {
        // The one irreversible action. Declining must change nothing.
        var h = new Harness(confirm: false);
        h.Run("backup", "--name", "the good one");
        h.EditSettings("Broken");

        var before = Encoding.UTF8.GetString(h.Fs.Read(SettingsPath));
        var id = h.Store.List().First(s => s.Manifest.DisplayName == "the good one").Id;
        h.Out.Lines.Clear();

        var code = h.Run("restore", id);

        Assert.Equal(ExitCode.Declined, code);
        Assert.Single(h.Out.Questions);
        Assert.Equal(before, Encoding.UTF8.GetString(h.Fs.Read(SettingsPath)));
    }

    [Fact]
    public void Restore_with_yes_does_not_ask()
    {
        var h = new Harness(confirm: false);
        h.Run("backup", "--name", "good");
        h.EditSettings("Broken");
        var id = h.Store.List().First(s => s.Manifest.DisplayName == "good").Id;

        h.Run("restore", id, "--yes");

        Assert.Empty(h.Out.Questions);
        Assert.Contains("Wave Mic 1", Encoding.UTF8.GetString(h.Fs.Read(SettingsPath)), StringComparison.Ordinal);
    }

    [Fact]
    public void Restore_prints_the_plan_marking_what_changes()
    {
        var h = new Harness(confirm: false);
        h.Run("backup", "--name", "good");
        h.EditSettings("Broken");
        var id = h.Store.List().First(s => s.Manifest.DisplayName == "good").Id;
        h.Out.Lines.Clear();

        h.Run("restore", id);

        Assert.Contains("Channel names", h.Out.All, StringComparison.Ordinal);
        Assert.Contains("Before restore", h.Out.All, StringComparison.Ordinal);
    }

    [Fact]
    public void Restoring_an_unknown_id_returns_the_not_found_code()
    {
        var h = new Harness(confirm: true);

        Assert.Equal(ExitCode.NotFound, h.Run("restore", "no-such-id", "--yes"));
    }

    [Fact]
    public void A_restore_blocked_by_a_running_Wave_Link_returns_its_own_code()
    {
        var h = new Harness(confirm: true);
        h.Run("backup", "--name", "good");
        var id = h.Store.List()[0].Id;
        h.Process.Running = true;
        h.Process.StaysRunningAfterClose = true;

        Assert.Equal(ExitCode.StillRunning, h.Run("restore", id, "--yes"));
    }

    // ------------------------------------------------------------------ rename / delete

    [Fact]
    public void Rename_changes_the_name_and_moves_nothing()
    {
        var h = new Harness();
        h.Run("backup", "--name", "old");
        var snapshot = h.Store.List()[0];
        var filesBefore = h.Fs.EnumerateFiles(snapshot.Directory, "*").ToArray();

        Assert.Equal(ExitCode.Success, h.Run("rename", snapshot.Id, @"Mic chain 3/4"""));

        Assert.Equal(@"Mic chain 3/4""", h.Store.List()[0].Manifest.DisplayName);
        Assert.Equal(filesBefore, h.Fs.EnumerateFiles(snapshot.Directory, "*").ToArray());
    }

    [Fact]
    public void Delete_asks_first_and_declining_keeps_the_backup()
    {
        var h = new Harness(confirm: false);
        h.Run("backup", "--name", "keep me");
        var id = h.Store.List()[0].Id;

        Assert.Equal(ExitCode.Declined, h.Run("delete", id));
        Assert.Single(h.Store.List());
    }

    [Fact]
    public void Delete_with_yes_moves_it_to_the_trash_rather_than_destroying_it()
    {
        var h = new Harness();
        h.Run("backup", "--name", "doomed");
        var id = h.Store.List()[0].Id;

        Assert.Equal(ExitCode.Success, h.Run("delete", id, "--yes"));

        Assert.Empty(h.Store.List());
        Assert.Single(h.Store.ListTrash());
        Assert.Contains("trash", h.Out.All, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_delete_prompt_names_the_trash_not_the_recycle_bin()
    {
        // On a network store there is no Recycle Bin, so saying so here would be untrue for
        // exactly the users most careful about backups.
        var h = new Harness(confirm: false);
        h.Run("backup", "--name", "doomed");

        h.Run("delete", h.Store.List()[0].Id);

        var question = Assert.Single(h.Out.Questions);
        Assert.Contains("trash", question, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Recycle Bin", question, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Empty_trash_on_an_empty_trash_says_so()
    {
        var h = new Harness();

        Assert.Equal(ExitCode.Success, h.Run("empty-trash"));
        Assert.Contains("empty", h.Out.All, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Empty_trash_removes_what_delete_put_there()
    {
        var h = new Harness();
        h.Run("backup", "--name", "doomed");
        h.Run("delete", h.Store.List()[0].Id, "--yes");

        Assert.Equal(ExitCode.Success, h.Run("empty-trash", "--yes"));

        Assert.Empty(h.Store.ListTrash());
        Assert.Single(h.Bin.Recycled);
    }

    [Fact]
    public void Empty_trash_says_permanent_where_there_is_no_recycle_bin()
    {
        var h = new Harness(confirm: false);
        h.Bin.Available = false;
        h.Run("backup", "--name", "doomed");
        h.Run("delete", h.Store.List()[0].Id, "--yes");
        h.Out.Questions.Clear();

        Assert.Equal(ExitCode.Declined, h.Run("empty-trash"));

        var question = Assert.Single(h.Out.Questions);
        Assert.Contains("permanently", question, StringComparison.OrdinalIgnoreCase);
    }

    // ------------------------------------------------------------------ verify / prune

    [Fact]
    public void Verify_passes_a_healthy_snapshot()
    {
        var h = new Harness();
        h.Run("backup", "--name", "x");
        h.Out.Lines.Clear();

        Assert.Equal(ExitCode.Success, h.Run("verify"));
        Assert.Contains("OK", h.Out.All, StringComparison.Ordinal);
    }

    [Fact]
    public void Verify_reports_a_damaged_snapshot_with_a_distinct_exit_code()
    {
        var h = new Harness();
        h.Run("backup", "--name", "x");
        var snapshot = h.Store.List()[0];
        h.Fs.WriteBytes(snapshot.SettingsPath, Encoding.UTF8.GetBytes("{}"));
        h.Out.Lines.Clear();

        Assert.Equal(ExitCode.Damaged, h.Run("verify"));
        Assert.Contains("DAMAGED", h.Out.All, StringComparison.Ordinal);
    }

    [Fact]
    public void Verify_on_an_empty_store_succeeds()
    {
        var h = new Harness();

        Assert.Equal(ExitCode.Success, h.Run("verify"));
    }

    [Fact]
    public void Prune_never_removes_manual_backups()
    {
        var h = new Harness();
        h.Run("backup", "--name", "one");
        h.Clock.Advance(TimeSpan.FromMinutes(1));
        h.Run("backup", "--name", "two");
        h.Out.Lines.Clear();

        Assert.Equal(ExitCode.Success, h.Run("prune", "--keep", "0"));
        Assert.Equal(2, h.Store.List().Count);
        Assert.Contains("Nothing to remove", h.Out.All, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------ errors

    [Fact]
    public void A_missing_Wave_Link_returns_its_own_exit_code()
    {
        var fs = new FakeFileSystem();
        var runner = new CommandRunner(fs, new FakeWaveLinkProcess(), new FakeClock(), new FakeOutput(), LocalAppData, new FakeRecycleBin());

        var code = runner.Run(CommandLineParser.Parse(["backup", "--store", StorePath]));

        Assert.Equal(ExitCode.NotInstalled, code);
    }

    [Fact]
    public void Settings_path_bypasses_discovery_for_backup_too()
    {
        // technical-debt.md 2.2: the escape hatch has to reach every verb, not just restore.
        var fs = new FakeFileSystem().AddFile(@"D:\rescued\Settings.json", Healthy);
        var clock = new FakeClock();
        var runner = new CommandRunner(fs, new FakeWaveLinkProcess(), clock, new FakeOutput(), LocalAppData, new FakeRecycleBin());

        var code = runner.Run(CommandLineParser.Parse(
            ["backup", "--settings-path", @"D:\rescued\Settings.json", "--store", StorePath]));

        Assert.Equal(ExitCode.Success, code);
        Assert.Single(new SnapshotStore(fs, clock, StorePath).List());
    }

    [Fact]
    public void Every_verb_and_option_appears_in_the_help_text()
    {
        // The price of a hand-rolled parser (ADR-009): help can drift. This is the guard.
        var help = string.Join("\n", HelpText.Lines);

        foreach (var verb in (string[])["backup", "list", "restore", "rename", "delete", "verify", "prune", "watch", "version", "help"])
        {
            Assert.Contains(verb, help, StringComparison.Ordinal);
        }

        foreach (var option in (string[])["--name", "--settings-path", "--store", "--keep", "--interval", "--yes", "--json"])
        {
            Assert.Contains(option, help, StringComparison.Ordinal);
        }
    }
}
