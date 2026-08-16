using WaveLinkBackup.Core.Automation;
using WaveLinkBackup.Core.Snapshots;

namespace WaveLinkBackup.Core.Tests;

/// <summary>
/// The real watcher, against a real temp directory.
///
/// These are the ONLY tests in the suite that wait on anything, and the wait is for an OS
/// event rather than for a policy interval — the debounce and rate limit are pure and tested
/// instantly elsewhere. Without these, <see cref="FileSystemSettingsWatcher"/> would be
/// entirely unexercised: every other automation test uses the fake, which by construction
/// cannot tell us whether the real NotifyFilter set is right.
///
/// The generous timeout is deliberate. A loaded CI runner can take a moment to deliver an
/// event, and a flaky watcher test teaches people to ignore failures.
/// </summary>
public sealed class FileSystemSettingsWatcherTests : IDisposable
{
    private static readonly TimeSpan EventTimeout = TimeSpan.FromSeconds(10);

    private readonly string directory =
        Path.Combine(Path.GetTempPath(), "wlbackup-watcher-" + Guid.NewGuid().ToString("N"));

    public FileSystemSettingsWatcherTests() => Directory.CreateDirectory(directory);

    public void Dispose()
    {
        try { Directory.Delete(directory, recursive: true); } catch (IOException) { }
    }

    private string SettingsPath => Path.Combine(directory, "Settings.json");

    private (FileSystemSettingsWatcher Watcher, ManualResetEventSlim Signal) Started()
    {
        var signal = new ManualResetEventSlim(false);
        var watcher = new FileSystemSettingsWatcher(directory);
        watcher.SettingsChanged += (_, _) => signal.Set();
        watcher.Start();
        return (watcher, signal);
    }

    [Fact]
    public void An_ordinary_write_raises_a_change()
    {
        var (watcher, signal) = Started();
        using (watcher)
        using (signal)
        {
            File.WriteAllText(SettingsPath, "{}");

            Assert.True(signal.Wait(EventTimeout, TestContext.Current.CancellationToken),
                "No change event arrived for a plain write.");
        }
    }

    [Fact]
    public void An_atomic_replace_raises_a_change()
    {
        // The case a LastWrite-only filter would miss. Wave Link's own atomic-save REPLACES
        // Settings.json rather than writing through it — so a watcher that only listens for
        // LastWrite misses exactly the saves that matter most.
        File.WriteAllText(SettingsPath, "{\"old\":true}");
        var temp = Path.Combine(directory, "temp.tmp");
        var backup = Path.Combine(directory, "rollback.bak");
        File.WriteAllText(temp, "{\"new\":true}");

        var (watcher, signal) = Started();
        using (watcher)
        using (signal)
        {
            File.Replace(temp, SettingsPath, backup, ignoreMetadataErrors: true);

            Assert.True(signal.Wait(EventTimeout, TestContext.Current.CancellationToken),
                "No change event arrived for an atomic replace.");
        }
    }

    [Fact]
    public void Unrelated_files_in_the_same_directory_are_ignored()
    {
        var (watcher, signal) = Started();
        using (watcher)
        using (signal)
        {
            File.WriteAllText(Path.Combine(directory, "ws-info.json"), "{\"port\":11465}");
            File.WriteAllText(Path.Combine(directory, "Settings.json.bak.1.2"), "{}");

            // A short wait: we are proving a NEGATIVE, so this cannot be generous without
            // making the suite slow. A false pass here shows up as noise in the store, not
            // as data loss.
            Assert.False(signal.Wait(TimeSpan.FromMilliseconds(750), TestContext.Current.CancellationToken),
                "An unrelated file raised a settings-changed event.");
        }
    }

    [Fact]
    public void Stopping_the_watcher_stops_the_events()
    {
        var (watcher, signal) = Started();
        using (watcher)
        using (signal)
        {
            watcher.Stop();
            File.WriteAllText(SettingsPath, "{}");

            Assert.False(signal.Wait(TimeSpan.FromMilliseconds(750), TestContext.Current.CancellationToken),
                "A stopped watcher still raised an event.");
        }
    }

    [Fact]
    public void Disposing_twice_is_harmless()
    {
        var watcher = new FileSystemSettingsWatcher(directory);
        watcher.Dispose();
        watcher.Dispose();
    }

    [Fact]
    public void The_default_settings_point_at_the_default_store()
    {
        var settings = BackupSettings.Default;

        Assert.Equal(SnapshotStore.DefaultStorePath, settings.StorePath);
        Assert.True(settings.AutoBackupEnabled);
        Assert.Equal(SnapshotRetention.DefaultKeepCount, settings.AutoBackupKeepCount);
        Assert.EndsWith("WaveLinkBackup", settings.StorePath, StringComparison.Ordinal);
    }

    [Fact]
    public void Settings_are_a_value_and_change_by_copying()
    {
        var settings = BackupSettings.Default with { AutoBackupEnabled = false, AutoBackupKeepCount = 5 };

        Assert.False(settings.AutoBackupEnabled);
        Assert.Equal(5, settings.AutoBackupKeepCount);
        Assert.Equal(BackupSettings.Default.StorePath, settings.StorePath);
    }
}
