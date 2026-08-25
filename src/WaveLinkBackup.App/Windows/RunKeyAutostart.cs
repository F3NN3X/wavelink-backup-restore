namespace WaveLinkBackup.App.Windows;

/// <summary>
/// Autostart through HKCU\...\Run. Per-user, never per-machine, and never a scheduled task
/// (screens/12-tray-autostart-update.md).
///
/// The complication is that whether the entry actually runs is decided somewhere else. Task
/// Manager's Startup tab does not delete the Run value. It writes an approval record under
/// StartupApproved, and Windows honours that. An app that only looked at the Run key would show
/// a toggle that reads on, does nothing at login, and looks like a bug in this app rather than
/// a choice the user made in Task Manager.
/// </summary>
public sealed class RunKeyAutostart(IRegistryKeys registry, string executablePath) : IAutostart
{
    public const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public const string ApprovedKeyPath =
        @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";

    public const string ValueName = "WaveLinkBackup";

    private string CommandLine => $"\"{executablePath}\" --tray";

    public AutostartState Read()
    {
        // The veto is checked FIRST and independently of the Run entry: Task Manager can hold
        // an approval record for an entry that is not currently present, and the toggle must
        // still show it as blocked rather than as a fresh off.
        if (IsVetoed()) return AutostartState.BlockedByTaskManager;

        return registry.GetString(RunKeyPath, ValueName) is null
            ? AutostartState.Off
            : AutostartState.On;
    }

    public bool Enable()
    {
        if (IsVetoed()) return false;

        registry.SetString(RunKeyPath, ValueName, CommandLine);
        return true;
    }

    public void Disable() => registry.DeleteValue(RunKeyPath, ValueName);

    /// <summary>
    /// The approval record is 12 bytes: a leading DWORD, then a FILETIME of when it was
    /// disabled. 0x02 and 0x06 mean enabled; 0x03 means the user disabled it. The low bit is
    /// the disable flag, which is why this tests the bit rather than listing the values.
    ///
    /// Anything shorter than a byte tells us nothing, and is NOT read as a veto: failing
    /// toward "the user may still turn this on".
    /// </summary>
    private bool IsVetoed()
    {
        var approval = registry.GetBinary(ApprovedKeyPath, ValueName);

        return approval is { Length: > 0 } && (approval[0] & 1) == 1;
    }
}
