using WaveLinkBackup.App.Hosting;
using WaveLinkBackup.App.Tests.Fakes;
using WaveLinkBackup.App.ViewModels;
using WaveLinkBackup.App.Windows;
using WaveLinkBackup.Core.Io;
using WaveLinkBackup.Core.Snapshots;
using WaveLinkBackup.Core.Tests.Fakes;

namespace WaveLinkBackup.App.Tests;

/// <summary>
/// The WHEN WINDOWS STARTS rows (screens/12): a toggle whose state is read from the registry on
/// every refresh, and which cannot be switched on while Task Manager holds a veto. These tests
/// drive ShellViewModel's autostart surface through the same FakeRegistryKeys seam the App wires
/// in production - RunKeyAutostart - so the veto rule is asserted end to end rather than mocked
/// away at the view-model boundary.
/// </summary>
public sealed class AutostartToggleTests
{
    private const string Exe = @"C:\Program Files\WaveLinkBackup\WaveLinkBackup.exe";

    /// <summary>
    /// A bare list is enough: the autostart surface never touches it, so a minimal
    /// SnapshotListViewModel over an empty fake store keeps the test focused on the toggle.
    /// </summary>
    private static SnapshotListViewModel BareList()
    {
        var fs = new FakeFileSystem();
        var clock = new FakeClock(new DateTimeOffset(2026, 8, 19, 12, 0, 0, TimeSpan.Zero));
        var store = new SnapshotStore(fs, clock, @"C:\Users\t\AppData\Local\WaveLinkBackup");

        return new SnapshotListViewModel(store, new HealthProbe(store, fs, clock), fs, clock);
    }

    private static (ShellViewModel Shell, FakeRegistryKeys Registry) Compose(FakeRegistryKeys registry) =>
        (new ShellViewModel(BareList(), new RunKeyAutostart(registry, Exe)), registry);

    [Fact]
    public void With_no_seam_the_toggle_is_off_and_cannot_be_enabled()
    {
        var shell = new ShellViewModel(BareList());

        Assert.False(shell.IsAutostartEnabled);
        Assert.False(shell.CanEnableAutostart);
        Assert.Equal(AutostartState.Off, shell.AutostartState);
        Assert.Null(shell.AutostartBlockedNote);
    }

    [Fact]
    public void A_fresh_registry_reads_off()
    {
        var (shell, _) = Compose(new FakeRegistryKeys());

        shell.RefreshAutostart();

        Assert.Equal(AutostartState.Off, shell.AutostartState);
        Assert.False(shell.IsAutostartEnabled);
        Assert.True(shell.CanEnableAutostart);
    }

    [Fact]
    public void Toggling_on_writes_the_run_key_and_reads_back_on()
    {
        var (shell, registry) = Compose(new FakeRegistryKeys());

        shell.RefreshAutostart();
        shell.ToggleAutostart();

        Assert.True(shell.IsAutostartEnabled);
        Assert.Equal(AutostartState.On, shell.AutostartState);
        Assert.Equal($"\"{Exe}\" --tray", registry.GetString(RunKeyAutostart.RunKeyPath, RunKeyAutostart.ValueName));
    }

    [Fact]
    public void Toggling_off_removes_the_run_key_and_reads_back_off()
    {
        var (shell, _) = Compose(
            new FakeRegistryKeys().WithString(RunKeyAutostart.RunKeyPath, RunKeyAutostart.ValueName, $"\"{Exe}\" --tray"));

        shell.RefreshAutostart();
        shell.ToggleAutostart();

        Assert.False(shell.IsAutostartEnabled);
        Assert.Equal(AutostartState.Off, shell.AutostartState);
    }

    /// <summary>
    /// The heart of the veto rule: a Task Manager-disabled entry reads OFF and cannot be switched
    /// on from this app. Toggling must not write the Run key - it would produce a toggle that flips
    /// on, does nothing at next login, and flips back, which is worse than an honest refusal.
    /// </summary>
    [Fact]
    public void A_blocked_entry_reads_off_and_cannot_be_enabled()
    {
        var (shell, registry) = Compose(
            new FakeRegistryKeys().WithBinary(RunKeyAutostart.ApprovedKeyPath, RunKeyAutostart.ValueName, Disabled));

        shell.RefreshAutostart();

        Assert.Equal(AutostartState.BlockedByTaskManager, shell.AutostartState);
        Assert.False(shell.IsAutostartEnabled);
        Assert.False(shell.CanEnableAutostart);
        Assert.NotNull(shell.AutostartBlockedNote);

        shell.ToggleAutostart();

        // The refusal is honest: nothing was written and the state did not move.
        Assert.Null(registry.GetString(RunKeyAutostart.RunKeyPath, RunKeyAutostart.ValueName));
        Assert.Equal(AutostartState.BlockedByTaskManager, shell.AutostartState);
    }

    /// <summary>
    /// The state is re-read from the registry on every refresh, never trusted to be whatever it was
    /// last tick - so a veto applied in Task Manager while the app runs is picked up on the next
    /// RefreshAutostart rather than only at the next launch.
    /// </summary>
    [Fact]
    public void A_veto_applied_after_startup_is_picked_up_on_the_next_refresh()
    {
        var (shell, registry) = Compose(
            new FakeRegistryKeys().WithString(RunKeyAutostart.RunKeyPath, RunKeyAutostart.ValueName, $"\"{Exe}\" --tray"));

        shell.RefreshAutostart();
        Assert.True(shell.IsAutostartEnabled);
        Assert.True(shell.CanEnableAutostart);

        // Task Manager disables the entry out from under us.
        registry.WithBinary(RunKeyAutostart.ApprovedKeyPath, RunKeyAutostart.ValueName, Disabled);

        shell.RefreshAutostart();

        Assert.Equal(AutostartState.BlockedByTaskManager, shell.AutostartState);
        Assert.False(shell.IsAutostartEnabled);
        Assert.False(shell.CanEnableAutostart);
    }

    /// <summary>0x03 in the first DWORD is Task Manager's disable; the remaining 8 bytes are a FILETIME.</summary>
    private static readonly byte[] Disabled = [0x03, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0];
}
