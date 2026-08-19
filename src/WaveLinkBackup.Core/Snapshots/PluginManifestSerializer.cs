using System.Buffers;
using System.Text.Json;

namespace WaveLinkBackup.Core.Snapshots;

/// <summary>
/// plugins.json in and out. PURE - bytes to records, records to bytes, no IO.
///
/// Hand-written with Utf8JsonWriter and JsonDocument for the same reason
/// <see cref="ManifestSerializer"/> is: reflection-based serialization would close off
/// NativeAOT for the CLI, and the source-scan guard fails the build if anyone reaches for the
/// shortcut (technical-debt.md 2.4).
///
/// **Where it differs from <see cref="ManifestSerializer"/> is <see cref="Read"/>.** The
/// manifest is what makes a directory a snapshot, so a manifest that cannot be understood is
/// a failure. plugins.json is a description of what the settings referenced; tier 2 is always
/// on and cannot be switched off (ADR-006), so a file that cannot be understood must degrade
/// to "unknown" rather than take a restorable snapshot down with it.
/// </summary>
public static class PluginManifestSerializer
{
    public static byte[] Write(PluginManifest manifest)
    {
        var buffer = new ArrayBufferWriter<byte>();

        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            writer.WriteNumber("schemaVersion", manifest.SchemaVersion);

            writer.WriteStartArray("plugins");
            foreach (var plugin in manifest.Plugins)
            {
                writer.WriteStartObject();
                writer.WriteString("name", plugin.Name);
                WriteNullable(writer, "vendor", plugin.Vendor);
                WriteNullable(writer, "version", plugin.Version);
                WriteNullable(writer, "uniqueId", plugin.UniqueId);
                writer.WriteString("filePath", plugin.FilePath);
                WriteNullable(writer, "sha256", plugin.Sha256);

                writer.WriteStartArray("channels");
                foreach (var channel in plugin.Channels) writer.WriteStringValue(channel);
                writer.WriteEndArray();

                writer.WriteStartArray("presetSources");
                foreach (var source in plugin.PresetSources) writer.WriteStringValue(source);
                writer.WriteEndArray();

                writer.WriteNumber("presetFileCount", plugin.PresetFileCount);
                writer.WriteNumber("presetBytes", plugin.PresetBytes);
                WriteNullable(writer, "binaryPath", plugin.BinaryPath);

                writer.WriteEndObject();
            }
            writer.WriteEndArray();

            writer.WriteEndObject();
        }

        return buffer.WrittenSpan.ToArray();
    }

    /// <summary>
    /// Never fails and never throws. Every unreadable shape - empty bytes, truncated JSON, an
    /// array where an object belongs, a plugin entry that is a number - yields a manifest,
    /// with the parts that could not be understood left null.
    ///
    /// <paramref name="utf8Json"/> from a NEWER schema is read rather than rejected, which is
    /// the opposite of what <see cref="ManifestSerializer.Read"/> does. Reading fields we
    /// recognise from a format we do not fully understand is dangerous when the result decides
    /// what gets written to disk; here the result decides what a warning says, and a partial
    /// warning beats none.
    /// </summary>
    public static PluginManifest Read(ReadOnlySpan<byte> utf8Json)
    {
        if (utf8Json.IsEmpty) return Unreadable;

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(utf8Json.ToArray());
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException)
        {
            return Unreadable;
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return Unreadable;

            var plugins = new List<PluginManifestEntry>();

            if (root.TryGetProperty("plugins", out var array) && array.ValueKind == JsonValueKind.Array)
            {
                foreach (var element in array.EnumerateArray())
                {
                    if (element.ValueKind != JsonValueKind.Object) continue;

                    var path = String(element, "filePath");
                    var name = String(element, "name");

                    // An entry naming neither a file nor a plugin describes nothing anyone
                    // could act on, and a row of blanks in the restore warning is worse than
                    // no row at all.
                    if (path is null && name is null) continue;

                    plugins.Add(new PluginManifestEntry(
                        Name: name ?? Path.GetFileNameWithoutExtension(path!),
                        Vendor: String(element, "vendor"),
                        Version: String(element, "version"),
                        UniqueId: String(element, "uniqueId"),
                        FilePath: path ?? string.Empty,
                        Sha256: String(element, "sha256"),
                        Channels: Strings(element, "channels"),
                        PresetSources: PresetSources(element),
                        PresetFileCount: (int)Number(element, "presetFileCount"),
                        PresetBytes: Number(element, "presetBytes"),
                        BinaryPath: String(element, "binaryPath")));
                }
            }

            return new PluginManifest(Version(root), plugins);
        }
    }

    /// <summary>
    /// Schema version 0 - "the file said nothing we could read". Distinct from
    /// <see cref="PluginManifest.Empty"/>, which is a rig with no third-party plugins.
    /// </summary>
    private static PluginManifest Unreadable => new(0, []);

    /// <summary>
    /// The folders tier 3 read, from either shape of the file.
    ///
    /// Schema 1 wrote a single `presetSource` string, because tier 3 only ever looked in one
    /// place. Schema 2 writes a `presetSources` array, because it looks in two (technical-debt.md
    /// §4.18). An old snapshot is read through the old key rather than losing the answer, which
    /// matters more here than anywhere else in this file: the folder it names is the evidence for
    /// why the heuristic changed.
    /// </summary>
    private static IReadOnlyList<string> PresetSources(JsonElement element)
    {
        var many = Strings(element, "presetSources");
        if (many.Count > 0) return many;

        return String(element, "presetSource") is { } one ? [one] : [];
    }

    private static void WriteNullable(Utf8JsonWriter writer, string name, string? value)
    {
        if (value is null) writer.WriteNull(name);
        else writer.WriteString(name, value);
    }

    private static int Version(JsonElement root) =>
        root.TryGetProperty("schemaVersion", out var value)
        && value.ValueKind == JsonValueKind.Number
        && value.TryGetInt32(out var version)
            ? version
            : 0;

    /// <summary>A string array, or empty for anything else. Non-string members are skipped.</summary>
    private static IReadOnlyList<string> Strings(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var array) || array.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return [.. array.EnumerateArray()
            .Where(e => e.ValueKind == JsonValueKind.String)
            .Select(e => e.GetString()!)];
    }

    /// <summary>A number property, or 0. A count that cannot be read is not a count.</summary>
    private static long Number(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.Number
        && value.TryGetInt64(out var number)
            ? number
            : 0;

    /// <summary>A string property, or null when absent, wrong-typed or blank.</summary>
    private static string? String(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.String
        && !string.IsNullOrWhiteSpace(value.GetString())
            ? value.GetString()
            : null;
}
