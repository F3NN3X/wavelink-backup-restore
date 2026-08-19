using System.Buffers;
using System.Text.Json;
using WaveLinkBackup.Core.Results;

namespace WaveLinkBackup.Core.Snapshots;

/// <summary>
/// manifest.json in and out. PURE - bytes to records, records to bytes, no IO.
///
/// Hand-written with Utf8JsonWriter and JsonDocument rather than JsonSerializer, because
/// reflection-based serialization would close off NativeAOT for the CLI. The source-scan
/// guard fails the build if anyone reaches for the shortcut
/// (technical-debt.md 2.4, and see the guards-that-can-fail pattern).
/// </summary>
public static class ManifestSerializer
{
    public static byte[] Write(SnapshotManifest manifest)
    {
        var buffer = new ArrayBufferWriter<byte>();

        // Default encoder and indentation, matching what Wave Link itself writes. This is a
        // manifest rather than a settings file so byte-fidelity does not bite here, but
        // keeping one convention across the codebase means nobody has to remember which
        // file follows which rule.
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();

            writer.WriteNumber("schemaVersion", manifest.SchemaVersion);
            writer.WriteString("displayName", manifest.DisplayName);
            writer.WriteString("notes", manifest.Notes);
            writer.WriteString("createdUtc", manifest.CreatedUtc.ToUniversalTime().ToString("O"));
            writer.WriteString("trigger", TriggerToString(manifest.Trigger));
            writer.WriteString("settingsSha256", manifest.SettingsSha256);

            if (manifest.WaveLinkVersion is null) writer.WriteNull("waveLinkVersion");
            else writer.WriteString("waveLinkVersion", manifest.WaveLinkVersion);

            writer.WriteNumber("inputCount", manifest.InputCount);

            writer.WriteStartArray("inputNames");
            foreach (var name in manifest.InputNames) writer.WriteStringValue(name);
            writer.WriteEndArray();

            writer.WriteNumber("effectCount", manifest.EffectCount);
            writer.WriteNumber("effectChannelCount", manifest.EffectChannelCount);
            writer.WriteBoolean("hasDuplicateKeys", manifest.HasDuplicateKeys);

            writer.WriteStartArray("tiers");
            foreach (var tier in manifest.Tiers) writer.WriteStringValue(tier);
            writer.WriteEndArray();

            writer.WriteStartObject("files");
            foreach (var (name, file) in manifest.Files)
            {
                writer.WriteStartObject(name);
                writer.WriteString("sha256", file.Sha256);
                writer.WriteNumber("sizeBytes", file.SizeBytes);
                writer.WriteEndObject();
            }
            writer.WriteEndObject();

            writer.WriteEndObject();
        }

        return buffer.WrittenSpan.ToArray();
    }

    public static Result<SnapshotManifest> Read(ReadOnlySpan<byte> utf8Json)
    {
        if (utf8Json.IsEmpty) return new MalformedManifest("the manifest is empty");

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(utf8Json.ToArray());
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException)
        {
            return new MalformedManifest(ex.Message);
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return new MalformedManifest("not an object");

            // Version FIRST. A manifest from a newer schema is rejected outright rather than
            // partially read - reading the fields we happen to recognise from a format we do
            // not understand is how a store gets silently corrupted by an older build.
            if (!TryInt(root, "schemaVersion", out var schemaVersion))
            {
                return new MalformedManifest("schemaVersion is missing");
            }

            if (schemaVersion > SnapshotManifest.CurrentSchemaVersion)
            {
                return new UnsupportedSnapshotSchema(schemaVersion, SnapshotManifest.CurrentSchemaVersion);
            }

            try
            {
                return new SnapshotManifest(
                    SchemaVersion: schemaVersion,
                    DisplayName: RequiredString(root, "displayName"),
                    Notes: OptionalString(root, "notes") ?? string.Empty,
                    CreatedUtc: DateTimeOffset.Parse(RequiredString(root, "createdUtc"),
                                                     System.Globalization.CultureInfo.InvariantCulture),
                    Trigger: ParseTrigger(RequiredString(root, "trigger")),
                    SettingsSha256: RequiredString(root, "settingsSha256"),
                    WaveLinkVersion: OptionalString(root, "waveLinkVersion"),
                    InputCount: RequiredInt(root, "inputCount"),
                    InputNames: StringArray(root, "inputNames"),
                    EffectCount: RequiredInt(root, "effectCount"),
                    EffectChannelCount: RequiredInt(root, "effectChannelCount"),
                    HasDuplicateKeys: RequiredBool(root, "hasDuplicateKeys"),
                    Tiers: StringArray(root, "tiers"),
                    Files: ReadFiles(root));
            }
            catch (Exception ex) when (ex is ManifestFieldException or FormatException)
            {
                return new MalformedManifest(ex.Message);
            }
        }
    }

    private static string TriggerToString(SnapshotTrigger trigger) => trigger switch
    {
        SnapshotTrigger.Manual => "manual",
        SnapshotTrigger.Automatic => "automatic",
        SnapshotTrigger.PreRestore => "preRestore",
        _ => throw new ArgumentOutOfRangeException(nameof(trigger), trigger, "Unknown trigger."),
    };

    private static SnapshotTrigger ParseTrigger(string value) => value switch
    {
        "manual" => SnapshotTrigger.Manual,
        "automatic" => SnapshotTrigger.Automatic,
        "preRestore" => SnapshotTrigger.PreRestore,
        _ => throw new ManifestFieldException($"unknown trigger '{value}'"),
    };

    private static IReadOnlyDictionary<string, SnapshotFile> ReadFiles(JsonElement root)
    {
        if (!root.TryGetProperty("files", out var files) || files.ValueKind != JsonValueKind.Object)
        {
            throw new ManifestFieldException("files is missing or not an object");
        }

        var result = new Dictionary<string, SnapshotFile>(StringComparer.Ordinal);
        foreach (var entry in files.EnumerateObject())
        {
            result[entry.Name] = new SnapshotFile(
                RequiredString(entry.Value, "sha256"),
                RequiredLong(entry.Value, "sizeBytes"));
        }
        return result;
    }

    private static IReadOnlyList<string> StringArray(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var array) || array.ValueKind != JsonValueKind.Array)
        {
            throw new ManifestFieldException($"{name} is missing or not an array");
        }

        return [.. array.EnumerateArray().Select(e => e.GetString() ?? string.Empty)];
    }

    private static string RequiredString(JsonElement root, string name) =>
        OptionalString(root, name) ?? throw new ManifestFieldException($"{name} is missing");

    private static string? OptionalString(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static int RequiredInt(JsonElement root, string name) =>
        TryInt(root, name, out var value) ? value : throw new ManifestFieldException($"{name} is missing");

    /// <summary>
    /// Sizes are read as 64-bit. Tier 4 puts real binaries in the manifest - a 24 MB plugin is
    /// nowhere near the 32-bit ceiling, but a file size read as an int is a trap that only springs
    /// once, on someone else's machine.
    /// </summary>
    private static long RequiredLong(JsonElement root, string name) =>
        root.TryGetProperty(name, out var element)
        && element.ValueKind == JsonValueKind.Number
        && element.TryGetInt64(out var value)
            ? value
            : throw new ManifestFieldException($"{name} is missing");

    private static bool TryInt(JsonElement root, string name, out int value)
    {
        value = 0;
        return root.TryGetProperty(name, out var element)
            && element.ValueKind == JsonValueKind.Number
            && element.TryGetInt32(out value);
    }

    private static bool RequiredBool(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value)
        && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : throw new ManifestFieldException($"{name} is missing");

    private sealed class ManifestFieldException(string message) : Exception(message);
}
