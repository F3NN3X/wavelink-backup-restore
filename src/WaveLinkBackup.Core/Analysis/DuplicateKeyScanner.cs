using System.Text.Json;

namespace WaveLinkBackup.Core.Analysis;

/// <summary>One object holding property names that collide when compared case-insensitively.</summary>
/// <param name="Path">JSONPath-ish location, e.g. <c>$.MixerConfiguration.InputSettings</c>.</param>
/// <param name="Names">The colliding names as written, in document order.</param>
public sealed record DuplicateKeyFinding(string Path, IReadOnlyList<string> Names);

/// <summary>
/// Detects the defect that motivated this project: Wave Link's
/// <c>SettingsJsonNormalizer.HasCaseInsensitiveDuplicateProperties</c> rejects a file with
/// case-insensitively duplicated property names and resets to defaults.
///
/// Built on <see cref="JsonDocument"/> deliberately, and it is the only type that works:
///
///   - JsonDocument      preserves both forms of duplicate. Measured 2026-08-16.
///   - JsonNode.Parse    preserves case-insensitive duplicates but THROWS ArgumentException
///                       on exact ones, so it cannot survey an untrusted file.
///   - ConvertFrom-Json  collapses case-insensitive duplicates silently, which is why the
///                       original incident looked like a healthy file for so long.
///
/// See _docs/knowledge-base/gotchas/file-parses-but-wave-link-resets.md
/// </summary>
public static class DuplicateKeyScanner
{
    /// <summary>
    /// Walks the whole document. Throws <see cref="JsonException"/> on unparseable input -
    /// callers reaching this through <see cref="SettingsAnalysis"/> get that as a
    /// <c>MalformedSettings</c> instead.
    /// </summary>
    public static IReadOnlyList<DuplicateKeyFinding> Scan(ReadOnlySpan<byte> utf8Json)
    {
        using var document = JsonDocument.Parse(utf8Json.ToArray());

        var findings = new List<DuplicateKeyFinding>();
        Walk(document.RootElement, "$", findings);
        return findings;
    }

    private static void Walk(JsonElement element, string path, List<DuplicateKeyFinding> findings)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                // EnumerateObject yields BOTH duplicates. That is the property the whole
                // check rests on, and the reason JsonNode cannot substitute here.
                var names = new List<string>();
                foreach (var property in element.EnumerateObject()) names.Add(property.Name);

                foreach (var group in names.GroupBy(n => n, StringComparer.OrdinalIgnoreCase))
                {
                    if (group.Count() > 1) findings.Add(new DuplicateKeyFinding(path, [.. group]));
                }

                foreach (var property in element.EnumerateObject())
                {
                    Walk(property.Value, $"{path}.{property.Name}", findings);
                }
                break;

            case JsonValueKind.Array:
                var index = 0;
                foreach (var item in element.EnumerateArray())
                {
                    Walk(item, $"{path}[{index++}]", findings);
                }
                break;
        }
    }
}
