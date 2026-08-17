using WaveLinkBackup.App.Windows;

namespace WaveLinkBackup.App.Tests.Fakes;

public sealed class FakeRegistryKeys : IRegistryKeys
{
    private readonly Dictionary<string, object> values = new(StringComparer.OrdinalIgnoreCase);

    private static string Key(string keyPath, string name) => $"{keyPath}::{name}";

    public FakeRegistryKeys WithString(string keyPath, string name, string value)
    {
        values[Key(keyPath, name)] = value;
        return this;
    }

    public FakeRegistryKeys WithBinary(string keyPath, string name, byte[] value)
    {
        values[Key(keyPath, name)] = value;
        return this;
    }

    public string? GetString(string keyPath, string name) =>
        values.TryGetValue(Key(keyPath, name), out var value) ? value as string : null;

    public byte[]? GetBinary(string keyPath, string name) =>
        values.TryGetValue(Key(keyPath, name), out var value) ? value as byte[] : null;

    public void SetString(string keyPath, string name, string value) =>
        values[Key(keyPath, name)] = value;

    public void DeleteValue(string keyPath, string name) => values.Remove(Key(keyPath, name));
}
