using Microsoft.Win32;

namespace WaveLinkBackup.App.Windows;

/// <summary>HKEY_CURRENT_USER only. Per-user, never per-machine (screens/12).</summary>
public sealed class WindowsRegistryKeys : IRegistryKeys
{
    public string? GetString(string keyPath, string name)
    {
        using var key = Registry.CurrentUser.OpenSubKey(keyPath);
        return key?.GetValue(name) as string;
    }

    public byte[]? GetBinary(string keyPath, string name)
    {
        using var key = Registry.CurrentUser.OpenSubKey(keyPath);
        return key?.GetValue(name) as byte[];
    }

    public void SetString(string keyPath, string name, string value)
    {
        using var key = Registry.CurrentUser.CreateSubKey(keyPath, writable: true);
        key?.SetValue(name, value, RegistryValueKind.String);
    }

    public void DeleteValue(string keyPath, string name)
    {
        using var key = Registry.CurrentUser.OpenSubKey(keyPath, writable: true);
        key?.DeleteValue(name, throwOnMissingValue: false);
    }
}
