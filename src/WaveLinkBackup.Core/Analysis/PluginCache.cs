using System.Xml.Linq;

namespace WaveLinkBackup.Core.Analysis;

/// <summary>
/// One plugin as Wave Link's scanner recorded it, from
/// <c>AudioPluginCache\AvailablePlugins.cache</c>.
/// </summary>
/// <param name="FilePath">
/// The <c>file</c> attribute. This is what ties a cache entry back to a settings reference -
/// names collide across vendors and formats, paths do not.
/// </param>
/// <param name="Version">
/// Null when the attribute is missing or blank. The version is the whole reason to read this
/// file: ParameterState is written by a specific plugin version (ADR-006), so a snapshot that
/// cannot name the version it was captured against cannot report drift on restore.
/// </param>
public sealed record CachedPlugin(
    string Name,
    string? Manufacturer,
    string? Version,
    string FilePath,
    string? UniqueId);

/// <summary>
/// Reads the JUCE <c>&lt;KNOWNPLUGINS&gt;</c> XML that Wave Link's plugin scanner maintains.
/// Pure: XML in, records out.
///
/// This file is a cache, rebuilt by rescanning, and it is never a payload (SPEC.md 8). It is
/// read only to attach a version and uniqueId to plugins the settings already name, so every
/// failure here degrades to "fewer known versions" rather than to a failed capture - see
/// <see cref="Parse"/>.
/// </summary>
public static class PluginCache
{
    /// <summary>
    /// ReadSharedText decodes the bytes verbatim, so a UTF-8 BOM survives into the string,
    /// where XDocument reads it as content sitting before the declaration and throws.
    /// </summary>
    private const char ByteOrderMark = (char)0xFEFF;

    /// <summary>
    /// Never throws and never fails: an unreadable cache yields an empty list.
    ///
    /// Tier 2 is always on (ADR-006), so a malformed cache must not be able to fail a
    /// snapshot. The settings file's <c>FilePath</c> is authoritative on its own; the cache
    /// only enriches it, and a plugin with no cache entry is still captured with its version
    /// left unknown.
    /// </summary>
    public static IReadOnlyList<CachedPlugin> Parse(string xml)
    {
        if (string.IsNullOrWhiteSpace(xml)) return [];

        XDocument document;
        try
        {
            // DTD processing is prohibited by XDocument.Parse's default reader settings,
            // which is what keeps a planted cache file from becoming an entity-expansion
            // problem rather than merely an unparseable one.
            document = XDocument.Parse(xml.TrimStart(ByteOrderMark));
        }
        catch (System.Xml.XmlException)
        {
            return [];
        }

        var plugins = new List<CachedPlugin>();

        foreach (var element in document.Descendants())
        {
            if (!element.Name.LocalName.Equals("PLUGIN", StringComparison.OrdinalIgnoreCase))
                continue;

            var file = Attribute(element, "file");
            var name = Attribute(element, "name");

            // An entry naming neither a file nor a plugin cannot be matched against anything.
            if (file is null && name is null) continue;

            plugins.Add(new CachedPlugin(
                Name: name ?? string.Empty,
                Manufacturer: Attribute(element, "manufacturer"),
                Version: Attribute(element, "version"),
                FilePath: file ?? string.Empty,
                // uniqueId is the current JUCE spelling; uid is what older scanners wrote.
                UniqueId: Attribute(element, "uniqueId") ?? Attribute(element, "uid")));
        }

        return plugins;
    }

    /// <summary>Case-insensitive by attribute name, and blank reads as absent.</summary>
    private static string? Attribute(XElement element, string name)
    {
        foreach (var attribute in element.Attributes())
        {
            if (!attribute.Name.LocalName.Equals(name, StringComparison.OrdinalIgnoreCase))
                continue;

            return string.IsNullOrWhiteSpace(attribute.Value) ? null : attribute.Value;
        }

        return null;
    }
}
