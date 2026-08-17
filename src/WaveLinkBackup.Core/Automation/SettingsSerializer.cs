using System.Buffers;
using System.Text.Json;

namespace WaveLinkBackup.Core.Automation;

/// <summary>
/// settings.json in and out. PURE - bytes to a record, a record to bytes, no IO.
///
/// Hand-written with Utf8JsonWriter and JsonDocument rather than JsonSerializer, matching
/// <see cref="Snapshots.ManifestSerializer"/>: reflection-based serialization would close off
/// NativeAOT for the CLI, and the source-scan guard fails the build if anyone reaches for the
/// shortcut (technical-debt.md 2.4).
///
/// <see cref="Read"/> is deliberately TOLERANT. Every field falls back to its default
/// independently, and a document that cannot be parsed at all yields
/// <see cref="BackupSettings.Default"/>. This is a preferences file, not a backup: refusing to
/// start because it is corrupt would be worse than starting with defaults, and one broken
/// field must not cost the user the other three. It is also why no CoreError is defined here -
/// there is no failure for a caller to handle.
/// </summary>
public static class SettingsSerializer
{
    public const int CurrentSchemaVersion = 1;

    public static byte[] Write(BackupSettings settings)
    {
        var buffer = new ArrayBufferWriter<byte>();

        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();

            writer.WriteNumber("schemaVersion", CurrentSchemaVersion);
            writer.WriteString("storePath", settings.StorePath);
            writer.WriteBoolean("autoBackupEnabled", settings.AutoBackupEnabled);
            writer.WriteNumber("autoBackupKeepCount", settings.AutoBackupKeepCount);

            if (settings.ChosenWaveLinkPath is null) writer.WriteNull("chosenWaveLinkPath");
            else writer.WriteString("chosenWaveLinkPath", settings.ChosenWaveLinkPath);

            writer.WriteEndObject();
        }

        return buffer.WrittenSpan.ToArray();
    }

    public static BackupSettings Read(ReadOnlySpan<byte> utf8Json)
    {
        var defaults = BackupSettings.Default;

        if (utf8Json.IsEmpty) return defaults;

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(utf8Json.ToArray());
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException)
        {
            return defaults;
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return defaults;

            return new BackupSettings(
                StorePath: String(root, "storePath") ?? defaults.StorePath,
                AutoBackupEnabled: Bool(root, "autoBackupEnabled") ?? defaults.AutoBackupEnabled,
                AutoBackupKeepCount: Int(root, "autoBackupKeepCount") ?? defaults.AutoBackupKeepCount,
                ChosenWaveLinkPath: String(root, "chosenWaveLinkPath"));
        }
    }

    private static string? String(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static bool? Bool(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : null;

    private static int? Int(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.Number
        && value.TryGetInt32(out var number)
            ? number
            : null;
}
