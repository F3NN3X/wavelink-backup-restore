using System.Security.Cryptography;
using System.Text.Json;
using WaveLinkBackup.Core.Results;

namespace WaveLinkBackup.Core.Analysis;

/// <summary>What a settings file says about itself. Findings, not errors.</summary>
/// <param name="DuplicateKeys">
/// Empty for a healthy file. Non-empty marks the snapshot suspect - it does not block a
/// restore, because a suspect snapshot may be the only one there is.
/// </param>
public sealed record ValidationReport(IReadOnlyList<DuplicateKeyFinding> DuplicateKeys)
{
    public bool HasCaseInsensitiveDuplicateKeys => DuplicateKeys.Count > 0;
}

/// <param name="WaveLinkVersion">
/// From <c>Update.LastUpdateVersion</c>, e.g. "3.3.0.4108". Null when absent.
///
/// This is the version that last wrote the file, which is exactly the question worth
/// recording: when a restore fails, the first thing to know is whether the config is bad or
/// the validator changed. 3.3.0.4108 Beta rejected a file 3.2.9 accepted.
///
/// Read from the settings file rather than from the installed package because the package
/// install directory (C:\Program Files\WindowsApps) is not readable without elevation -
/// verified 2026-08-16 - and because WinRT PackageManager would force a Windows-only target
/// framework on a library that otherwise needs none.
/// </param>
/// <param name="ReferencedPlugins">
/// Every third-party plugin the configuration uses, deduplicated across channels. Empty when
/// the rig is all Elgato built-ins. This is what tier 2 is built from (ADR-006).
/// </param>
public sealed record SettingsAnalysisResult(
    ValidationReport Report,
    HealthFingerprint Fingerprint,
    string? WaveLinkVersion,
    IReadOnlyList<ReferencedPlugin> ReferencedPlugins);

/// <summary>
/// The pure heart of Core: bytes in, records out. No IO, no seams, no async, no state.
///
/// This class cannot write a file even by accident, which is how "capture is a byte copy"
/// stops being a convention someone has to remember and becomes a property of the type
/// system. Parsing exists for validation and the fingerprint; its output is metadata,
/// never a file.
/// </summary>
public static class SettingsAnalysis
{
    /// <summary>
    /// Parses once and derives everything. Three walks over one <see cref="JsonDocument"/>:
    /// the duplicate scan needs the whole tree; the fingerprint and the referenced-plugin
    /// set need InputSettings.
    /// </summary>
    public static Result<SettingsAnalysisResult> Analyse(ReadOnlySpan<byte> utf8Json)
    {
        if (utf8Json.IsEmpty) return new MalformedSettings("the file is empty");

        var bytes = utf8Json.ToArray();

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(bytes);
        }
        catch (JsonException ex)
        {
            return new MalformedSettings(ex.Message);
        }
        catch (ArgumentException ex)
        {
            // Audit finding 3b. JsonNode.Parse throws this on exact duplicate keys;
            // JsonDocument should not, but a dictionary's internal error must never reach
            // a user as "An item with the same key has already been added. Key: A".
            return new MalformedSettings(ex.Message);
        }

        using (document)
        {
            if (!TryGetInputSettings(document.RootElement, out var inputs))
            {
                // Reporting zero inputs here would let an unrelated JSON file look exactly
                // like a collapsed configuration.
                return new MalformedSettings(
                    "expected MixerConfiguration.InputSettings to be a JSON object");
            }

            var findings = new List<DuplicateKeyFinding>();
            Scan(document.RootElement, "$", findings);

            return new SettingsAnalysisResult(
                new ValidationReport(findings),
                Fingerprint(inputs, bytes),
                ReadWaveLinkVersion(document.RootElement),
                ReadReferencedPlugins(inputs));
        }
    }

    private static HealthFingerprint Fingerprint(JsonElement inputs, byte[] bytes)
    {
        var names = new List<string>();
        var effectCount = 0;
        var effectChannelCount = 0;

        foreach (var input in inputs.EnumerateObject())
        {
            names.Add(ReadName(input));

            if (input.Value.ValueKind == JsonValueKind.Object &&
                input.Value.TryGetProperty("AudioPluginConfigurations", out var effects) &&
                effects.ValueKind == JsonValueKind.Array)
            {
                var count = effects.GetArrayLength();
                effectCount += count;
                if (count > 0) effectChannelCount++;
            }
        }

        return new HealthFingerprint(
            InputCount: names.Count,
            InputNames: names,
            EffectCount: effectCount,
            EffectChannelCount: effectChannelCount,
            SizeBytes: bytes.LongLength,
            Sha256: Convert.ToHexStringLower(SHA256.HashData(bytes)));
    }

    /// <summary>
    /// Every AudioPluginConfigurations entry carrying a non-empty FilePath, deduplicated by
    /// path in the order the channels appear.
    ///
    /// An empty FilePath means an Elgato built-in, which ships with Wave Link and is never
    /// captured. Deduplication is by path rather than by name because the same plugin sits on
    /// several channels of a real rig - the mic chain alone repeats a compressor - and tier 2
    /// describes a set of plugins, not a list of placements.
    /// </summary>
    private static IReadOnlyList<ReferencedPlugin> ReadReferencedPlugins(JsonElement inputs)
    {
        var plugins = new List<ReferencedPlugin>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var input in inputs.EnumerateObject())
        {
            if (input.Value.ValueKind != JsonValueKind.Object ||
                !input.Value.TryGetProperty("AudioPluginConfigurations", out var effects) ||
                effects.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var effect in effects.EnumerateArray())
            {
                if (effect.ValueKind != JsonValueKind.Object) continue;

                var path = ReadString(effect, "FilePath");
                if (path is null || !seen.Add(path)) continue;

                plugins.Add(new ReferencedPlugin(
                    // A plugin with a path but no name is still capturable, and the file name
                    // is a better label for it than an empty string.
                    Name: ReadString(effect, "Name") ?? Path.GetFileNameWithoutExtension(path),
                    Vendor: ReadString(effect, "Vendor"),
                    FilePath: path));
            }
        }

        return plugins;
    }

    /// <summary>A string property, or null when absent, wrong-typed or blank.</summary>
    private static string? ReadString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.String
        && !string.IsNullOrWhiteSpace(value.GetString())
            ? value.GetString()
            : null;

    /// <summary>
    /// Falls back to the key rather than throwing. The key is the Core Audio endpoint ID,
    /// which is worse than a friendly name but better than losing the channel from the
    /// count - and the count is the collapse signal.
    /// </summary>
    private static string ReadName(JsonProperty input) =>
        input.Value.ValueKind == JsonValueKind.Object &&
        input.Value.TryGetProperty("InputName", out var name) &&
        name.ValueKind == JsonValueKind.String &&
        !string.IsNullOrWhiteSpace(name.GetString())
            ? name.GetString()!
            : input.Name;

    private static string? ReadWaveLinkVersion(JsonElement root) =>
        root.TryGetProperty("Update", out var update)
        && update.ValueKind == JsonValueKind.Object
        && update.TryGetProperty("LastUpdateVersion", out var version)
        && version.ValueKind == JsonValueKind.String
        && !string.IsNullOrWhiteSpace(version.GetString())
            ? version.GetString()
            : null;

    private static bool TryGetInputSettings(JsonElement root, out JsonElement inputs)
    {
        inputs = default;

        return root.ValueKind == JsonValueKind.Object
            && root.TryGetProperty("MixerConfiguration", out var mixer)
            && mixer.ValueKind == JsonValueKind.Object
            && mixer.TryGetProperty("InputSettings", out inputs)
            && inputs.ValueKind == JsonValueKind.Object;
    }

    // Shares the already-parsed document rather than re-parsing through
    // DuplicateKeyScanner.Scan, which takes bytes.
    private static void Scan(JsonElement element, string path, List<DuplicateKeyFinding> findings)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                var names = new List<string>();
                foreach (var property in element.EnumerateObject()) names.Add(property.Name);

                foreach (var group in names.GroupBy(n => n, StringComparer.OrdinalIgnoreCase))
                {
                    if (group.Count() > 1) findings.Add(new DuplicateKeyFinding(path, [.. group]));
                }

                foreach (var property in element.EnumerateObject())
                {
                    Scan(property.Value, $"{path}.{property.Name}", findings);
                }
                break;

            case JsonValueKind.Array:
                var index = 0;
                foreach (var item in element.EnumerateArray())
                {
                    Scan(item, $"{path}[{index++}]", findings);
                }
                break;
        }
    }
}
