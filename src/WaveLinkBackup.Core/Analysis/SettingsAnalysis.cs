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

public sealed record SettingsAnalysisResult(ValidationReport Report, HealthFingerprint Fingerprint);

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
    /// Parses once and derives everything. Two walks over one <see cref="JsonDocument"/>:
    /// the duplicate scan needs the whole tree, the fingerprint needs InputSettings.
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
                Fingerprint(inputs, bytes));
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
