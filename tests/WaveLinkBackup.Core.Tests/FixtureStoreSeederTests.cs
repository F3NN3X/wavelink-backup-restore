using System.Reflection;
using WaveLinkBackup.Core.Abstractions;
using WaveLinkBackup.Core.Analysis;
using WaveLinkBackup.Core.Snapshots;

namespace WaveLinkBackup.Core.Tests;

/// <summary>
/// Runs tools/seed-fixture-store.ps1 for real and reads what it wrote with the same code the app
/// uses.
///
/// <para>
/// The seeder hand-writes manifest.json, so it duplicates ManifestSerializer's field names and
/// casing in PowerShell where no compiler checks them. Renaming a manifest field would leave the
/// script writing a shape nothing reads - and the failure arrives as "every fixture snapshot is
/// damaged" during a by-eye sitting, which is the worst possible moment to debug a tool. This is
/// the test that fails first instead.
/// </para>
///
/// <para>
/// Tracked as technical-debt.md 8.2: the seeder exists so that checklist item 5's rigs do not have
/// to be built by hand in Wave Link.
/// </para>
/// </summary>
public sealed class FixtureStoreSeederTests : IDisposable
{
    private readonly string workspace = Path.Combine(
        Path.GetTempPath(), $"wlbackup-seeder-test-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(workspace)) Directory.Delete(workspace, recursive: true);
    }

    private static string ScriptPath => Path.Combine(
        Assembly.GetExecutingAssembly()
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .Single(a => a.Key == "ToolsSourceRoot").Value!,
        "seed-fixture-store.ps1");

    private static bool TryRunSeeder(string path, out string output)
    {
        var start = new System.Diagnostics.ProcessStartInfo("pwsh")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        start.ArgumentList.Add("-NoProfile");
        start.ArgumentList.Add("-NonInteractive");
        start.ArgumentList.Add("-File");
        start.ArgumentList.Add(ScriptPath);
        start.ArgumentList.Add("-Path");
        start.ArgumentList.Add(path);

        System.Diagnostics.Process? process;
        try
        {
            process = System.Diagnostics.Process.Start(start);
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // No pwsh on this machine. The seeder is a developer tool, not a shipped path.
            output = "";
            return false;
        }

        if (process is null)
        {
            output = "";
            return false;
        }

        using (process)
        {
            output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
            process.WaitForExit(milliseconds: 60_000);
            return process.HasExited && process.ExitCode == 0;
        }
    }

    private Snapshot[] SeedAndList()
    {
        var storePath = Path.Combine(workspace, "store");

        Assert.True(TryRunSeeder(storePath, out var output),
            $"seed-fixture-store.ps1 did not succeed:{Environment.NewLine}{output}");

        var store = new SnapshotStore(new FileSystem(), new SystemClock(), storePath);

        return [.. store.List()];
    }

    [Fact]
    public void The_seeded_store_holds_the_rigs_checklist_item_5_asks_for()
    {
        var snapshots = SeedAndList();

        // The four the verdict look needs: a whole rig, a collapsed one, and the two crowded
        // widths the old strip could not draw.
        var byInputCount = snapshots.Select(s => s.Manifest.InputCount).OrderBy(n => n).ToArray();

        Assert.Equal([2, 5, 5, 9, 12], byInputCount);
    }

    [Fact]
    public void Every_seeded_snapshot_passes_the_hash_guard()
    {
        // A fixture that reads as damaged is worse than no fixture: the sitting would spend its
        // time on the damage rather than on the pixels it came to look at.
        var guard = new SnapshotGuard(new FileSystem());

        foreach (var snapshot in SeedAndList())
        {
            var verified = guard.Verify(snapshot.Directory);

            Assert.True(verified.IsSuccess,
                $"{snapshot.Manifest.DisplayName} failed verification: {verified.Error}");
        }
    }

    [Fact]
    public void The_settings_payload_parses_into_the_matrix_the_details_dialog_draws()
    {
        // The routing matrix is the half of item 5 that reads the payload rather than the
        // manifest, so a manifest that parses proves only half the fixture.
        var snapshots = SeedAndList();
        var chains = snapshots.Single(s => s.Manifest.DisplayName == "Long effect chains");

        var detail = ConfigurationDetail.Read(File.ReadAllBytes(chains.SettingsPath));

        Assert.True(detail.IsSuccess, $"the payload did not parse: {detail.Error}");

        var configuration = detail.Value!;
        Assert.Equal(5, configuration.Channels.Count);
        Assert.NotEmpty(configuration.Mixes);

        // A dot lands where a channel's routing line says it feeds, so every channel needs at
        // least one mix and the mix has to be one the matrix has a column for.
        var mixNames = configuration.Mixes.Select(m => m.Name).ToHashSet();
        foreach (var channel in configuration.Channels)
        {
            Assert.NotEmpty(channel.Mixes);
            Assert.All(channel.Mixes, mix => Assert.Contains(mix, mixNames));
        }

        // Six effects on each of five channels is what makes the dialog hit its 720px cap.
        Assert.All(configuration.Channels, channel => Assert.Equal(6, channel.Effects.Count));
    }

    [Fact]
    public void The_seeder_refuses_to_write_into_the_real_store()
    {
        // The one refusal that matters. A fixture in the real store looks exactly like a real
        // snapshot in the list, and the user finds out by restoring it.
        var realStore = SnapshotStore.DefaultStorePath;

        Assert.False(TryRunSeeder(Path.Combine(realStore, "fixtures"), out var output),
            "the seeder wrote inside the real store");
        Assert.Contains("Refusing to seed inside the real store", output, StringComparison.Ordinal);
    }
}
