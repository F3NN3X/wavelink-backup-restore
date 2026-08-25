namespace WaveLinkBackup.App.Windows;

/// <summary>
/// The registry, narrowed to what autostart needs. A seam rather than direct Microsoft.Win32
/// calls because the interesting behaviour, the Task Manager veto, is otherwise only testable
/// by writing to the developer's real HKCU and hoping.
/// </summary>
public interface IRegistryKeys
{
    string? GetString(string keyPath, string name);

    byte[]? GetBinary(string keyPath, string name);

    void SetString(string keyPath, string name, string value);

    void DeleteValue(string keyPath, string name);
}
