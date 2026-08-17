using WaveLinkBackup.App.Tests.Fakes;
using WaveLinkBackup.App.Windows;

namespace WaveLinkBackup.App.Tests;

/// <summary>
/// "If Task Manager has disabled the entry, the toggle reads off and cannot be switched on
/// here. Task Manager wins; the note says so rather than fighting it."
/// </summary>
public sealed class AutostartTests
{
    private const string Exe = @"C:\Program Files\WaveLinkBackup\WaveLinkBackup.exe";

    private static RunKeyAutostart Autostart(FakeRegistryKeys registry) => new(registry, Exe);

    [Fact]
    public void Reads_off_when_there_is_no_run_entry()
    {
        Assert.Equal(AutostartState.Off, Autostart(new FakeRegistryKeys()).Read());
    }

    [Fact]
    public void Reads_on_when_the_run_entry_exists_and_nothing_vetoed_it()
    {
        var registry = new FakeRegistryKeys()
            .WithString(RunKeyAutostart.RunKeyPath, RunKeyAutostart.ValueName, $"\"{Exe}\" --tray");

        Assert.Equal(AutostartState.On, Autostart(registry).Read());
    }

    [Fact]
    public void Enabling_writes_the_exe_with_the_tray_flag()
    {
        var registry = new FakeRegistryKeys();

        Assert.True(Autostart(registry).Enable());

        var written = registry.GetString(RunKeyAutostart.RunKeyPath, RunKeyAutostart.ValueName);
        Assert.Equal($"\"{Exe}\" --tray", written);
    }

    [Fact]
    public void Disabling_removes_the_entry()
    {
        var registry = new FakeRegistryKeys()
            .WithString(RunKeyAutostart.RunKeyPath, RunKeyAutostart.ValueName, $"\"{Exe}\" --tray");

        Autostart(registry).Disable();

        Assert.Null(registry.GetString(RunKeyAutostart.RunKeyPath, RunKeyAutostart.ValueName));
        Assert.Equal(AutostartState.Off, Autostart(registry).Read());
    }

    // 0x03 in the first DWORD is Task Manager's disable; the remaining 8 bytes are a FILETIME.
    private static readonly byte[] Disabled = [0x03, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0];

    [Fact]
    public void Reads_blocked_when_task_manager_disabled_it()
    {
        var registry = new FakeRegistryKeys()
            .WithString(RunKeyAutostart.RunKeyPath, RunKeyAutostart.ValueName, $"\"{Exe}\" --tray")
            .WithBinary(RunKeyAutostart.ApprovedKeyPath, RunKeyAutostart.ValueName, Disabled);

        Assert.Equal(AutostartState.BlockedByTaskManager, Autostart(registry).Read());
    }

    [Theory]
    [InlineData(0x02)]
    [InlineData(0x06)]
    public void An_approval_record_that_permits_it_still_reads_on(byte first)
    {
        byte[] approval = [first, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0];

        var registry = new FakeRegistryKeys()
            .WithString(RunKeyAutostart.RunKeyPath, RunKeyAutostart.ValueName, $"\"{Exe}\" --tray")
            .WithBinary(RunKeyAutostart.ApprovedKeyPath, RunKeyAutostart.ValueName, approval);

        Assert.Equal(AutostartState.On, Autostart(registry).Read());
    }

    /// <summary>
    /// The heart of it. Writing the Run key while Task Manager holds a veto would produce a
    /// toggle that flips on, does nothing at next login, and flips back — which is worse than
    /// a toggle that honestly refuses.
    /// </summary>
    [Fact]
    public void Enabling_refuses_while_task_manager_holds_the_veto()
    {
        var registry = new FakeRegistryKeys()
            .WithBinary(RunKeyAutostart.ApprovedKeyPath, RunKeyAutostart.ValueName, Disabled);

        Assert.False(Autostart(registry).Enable());
        Assert.Null(registry.GetString(RunKeyAutostart.RunKeyPath, RunKeyAutostart.ValueName));
    }

    [Fact]
    public void A_blocked_entry_reads_blocked_even_with_no_run_entry()
    {
        var registry = new FakeRegistryKeys()
            .WithBinary(RunKeyAutostart.ApprovedKeyPath, RunKeyAutostart.ValueName, Disabled);

        Assert.Equal(AutostartState.BlockedByTaskManager, Autostart(registry).Read());
    }

    /// <summary>A short or empty approval record tells us nothing; do not read it as a veto.</summary>
    [Fact]
    public void A_malformed_approval_record_is_ignored()
    {
        var registry = new FakeRegistryKeys()
            .WithString(RunKeyAutostart.RunKeyPath, RunKeyAutostart.ValueName, $"\"{Exe}\" --tray")
            .WithBinary(RunKeyAutostart.ApprovedKeyPath, RunKeyAutostart.ValueName, []);

        Assert.Equal(AutostartState.On, Autostart(registry).Read());
    }

    [Fact]
    public void Enabling_is_idempotent()
    {
        var registry = new FakeRegistryKeys();
        var autostart = Autostart(registry);

        Assert.True(autostart.Enable());
        Assert.True(autostart.Enable());

        Assert.Equal(AutostartState.On, autostart.Read());
    }
}
