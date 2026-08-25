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

            // Added in phase 6 with NO schema bump. Adding a field whose absence means its
            // default is exactly what the tolerant read already handles; the version exists for
            // a field whose MEANING changes, which is a different and much rarer event.
            writer.WriteBoolean("includePresets", settings.IncludePresets);
            writer.WriteBoolean("includePluginFiles", settings.IncludePluginFiles);

            // Same argument, same absence of a schema bump: a missing interval means the hour it
            // always was, and a missing daily time means the daily backup nobody had before.
            writer.WriteNumber("autoBackupIntervalMinutes", settings.AutoBackupIntervalMinutes);

            if (settings.DailyBackupMinutes is { } daily) writer.WriteNumber("dailyBackupMinutes", daily);
            else writer.WriteNull("dailyBackupMinutes");

            // The UPDATES section (the tray and updates spec). Additive again, and again with no schema bump: a
            // missing switch means the weekly check that is on by default, and a missing stamp
            // means one has never run - which is exactly what an older settings file describes.
            writer.WriteBoolean("checkForUpdates", settings.CheckForUpdates);

            if (settings.LastUpdateCheckUtc is { } checkedAt)
            {
                writer.WriteString(
                    "lastUpdateCheckUtc",
                    checkedAt.ToUniversalTime().ToString("O", System.Globalization.CultureInfo.InvariantCulture));
            }
            else
            {
                writer.WriteNull("lastUpdateCheckUtc");
            }

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
                ChosenWaveLinkPath: String(root, "chosenWaveLinkPath"),
                IncludePresets: Bool(root, "includePresets") ?? defaults.IncludePresets,
                IncludePluginFiles: Bool(root, "includePluginFiles") ?? defaults.IncludePluginFiles,
                AutoBackupIntervalMinutes:
                    Int(root, "autoBackupIntervalMinutes") ?? defaults.AutoBackupIntervalMinutes,
                DailyBackupMinutes: Int(root, "dailyBackupMinutes"),
                CheckForUpdates: Bool(root, "checkForUpdates") ?? defaults.CheckForUpdates,
                LastUpdateCheckUtc: Timestamp(root, "lastUpdateCheckUtc"));
        }
    }

    /// <summary>
    /// A round-trip UTC timestamp, or null for anything that does not parse. Null is the safe
    /// answer here: it means "never checked", which makes the next check happen. The failure
    /// direction is one extra request, not a check that never runs again.
    /// </summary>
    private static DateTimeOffset? Timestamp(JsonElement root, string name) =>
        String(root, name) is { } text
        && DateTimeOffset.TryParse(
            text,
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.RoundtripKind,
            out var parsed)
            ? parsed
            : null;

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
