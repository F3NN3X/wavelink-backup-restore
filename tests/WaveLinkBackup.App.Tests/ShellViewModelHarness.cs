using System.Text;
using WaveLinkBackup.App.Hosting;
using WaveLinkBackup.App.ViewModels;
using WaveLinkBackup.Core.Analysis;
using WaveLinkBackup.Core.Io;
using WaveLinkBackup.Core.Snapshots;
using WaveLinkBackup.Core.Tests.Fakes;

namespace WaveLinkBackup.App.Tests;

/// <summary>
/// Stands up a <see cref="ShellViewModel"/> for the strip and bottom-bar tests, so a test does
/// not have to build a store, a process and an inspector by hand to assert one string.
///
/// The SAVED-AT time is wired straight into <see cref="ShellFacts"/> rather than round-tripped
/// through <see cref="FakeFileSystem.GetLastWriteTimeUtc"/>: that fake ignores its path argument
/// and always answers one fixed instant, so it cannot stand in for the range of save times this
/// test file exercises. What it stands for - the real last-write time App.xaml.cs reads off the
/// live Settings.json - is exactly what SAVED-AT already represents, so passing it directly loses
/// nothing. Nothing in tests/WaveLinkBackup.Core.Tests/Fakes changed to make this work.
/// </summary>
internal static class ShellViewModelHarness
{
    private const string LocalAppData = @"C:\Users\t\AppData\Local";
    private const string LocalState =
        LocalAppData + @"\Packages\Elgato.WaveLink_g54w8ztgkx496\LocalState";
    private const string SettingsPath = LocalState + @"\Settings.json";

    private static byte[] SettingsFor(string micName) => Encoding.UTF8.GetBytes(
        """{"MixerConfiguration":{"InputSettings":{"a":{"InputName":"NAME"}}}}"""
            .Replace("NAME", micName, StringComparison.Ordinal));

    public static ShellViewModel Build(
        bool waveLinkRunning,
        bool waveLinkFound,
        bool folderMissing,
        bool autoBackupEnabled,
        long? freeBytes,
        string storePath,
        DateTimeOffset savedAt,
        bool emptyStore = false)
    {
        var fs = new FakeFileSystem { FreeBytes = freeBytes };
        var clock = new FakeClock(savedAt);

        // waveLinkFound: false -> the live Settings.json is never added, so Inspect() fails.
        if (waveLinkFound) fs.AddFile(SettingsPath, SettingsFor("Wave Mic 1"));

        var store = new SnapshotStore(fs, clock, storePath);

        // folderMissing: true -> the store is never written to, so its directory never comes
        // into being (Write() is what creates it) and SummaryLine has nothing to count.
        // emptyStore: true -> a found Wave Link with an empty store: the first-run screen.
        if (!folderMissing && !emptyStore)
        {
            for (var i = 0; i < 3; i++)
            {
                clock.UtcNow = savedAt.AddMinutes(-i);
                var bytes = SettingsFor($"Mic {i}");
                store.Write(bytes, SettingsAnalysis.Analyse(bytes).Value, SnapshotTrigger.Manual, $"Backup {i}");
            }

            clock.UtcNow = savedAt;
        }

        var list = new SnapshotListViewModel(store, new HealthProbe(store, fs, clock), fs, clock)
        {
            Marshal = action => action(),
        };

        var process = new FakeWaveLinkProcess { Running = waveLinkRunning };
        var found = SettingsInspector.For(fs, LocalAppData).Inspect().IsSuccess;

        var facts = new ShellFacts(
            WaveLinkFound: found,
            WaveLinkRunning: process.IsRunning,
            SettingsLastSavedLocal: found ? savedAt : null,
            AutoBackupEnabled: autoBackupEnabled,
            FolderMissing: folderMissing,
            StorePath: storePath,
            FreeBytes: freeBytes);

        var shell = new ShellViewModel(list);
        shell.Apply(facts);

        return shell;
    }
}
