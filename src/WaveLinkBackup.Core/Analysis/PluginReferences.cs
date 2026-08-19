namespace WaveLinkBackup.Core.Analysis;

/// <summary>
/// A third-party plugin the settings file actually uses, as the settings file describes it.
///
/// "Referenced, not installed" is the whole economy of ADR-006: the referenced set is 39.8 MB
/// where the VST3 tree is 4,887 MB. Elgato's built-in effects carry an empty
/// <c>FilePath</c> and are never members - they ship with Wave Link, so capturing them would
/// be paying to back up the installer.
/// </summary>
/// <param name="FilePath">
/// Absolute, and authoritative. <c>C:\Program Files\Common Files\VST3</c> is a default, not a
/// location (ADR-006): the standard directories are a fallback for a path that no longer
/// resolves, never the primary answer.
/// </param>
public sealed record ReferencedPlugin(
    string Name,
    string? Vendor,
    string FilePath);

/// <summary>
/// A referenced plugin after the plugin cache has been consulted - the row that becomes one
/// entry of tier 2's <c>plugins.json</c>.
/// </summary>
/// <param name="Version">
/// Null when the cache has nothing to say about this plugin. Recorded as unknown rather than
/// guessed: tier 2 exists to turn "my effects are gone" into "install Pro-Q 4 v4.x", and an
/// invented version answers that question wrongly, which is worse than not answering it.
/// </param>
public sealed record ResolvedPlugin(
    string Name,
    string? Vendor,
    string FilePath,
    string? Version,
    string? UniqueId)
{
    /// <summary>
    /// False when the plugin is absent from the cache. Restore treats an unknown version as
    /// drift rather than as a match, and never as a hard failure (ADR-006).
    /// </summary>
    public bool VersionKnown => Version is not null;
}

/// <summary>
/// Cross-references what the settings use against what the scanner knows. Pure: two record
/// lists in, one out.
/// </summary>
public static class PluginReferences
{
    /// <summary>
    /// Attaches version and uniqueId to each referenced plugin.
    ///
    /// A plugin present in the settings but absent from the cache is still returned, with its
    /// version left null. The cache is rebuilt by rescanning and can legitimately be stale or
    /// missing; the settings file's <c>FilePath</c> is the authority on what is in use.
    /// </summary>
    public static IReadOnlyList<ResolvedPlugin> Resolve(
        IReadOnlyList<ReferencedPlugin> referenced,
        IReadOnlyList<CachedPlugin> cached)
    {
        var byPath = new Dictionary<string, CachedPlugin>(StringComparer.OrdinalIgnoreCase);
        var byName = new Dictionary<string, CachedPlugin>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in cached)
        {
            if (entry.FilePath.Length > 0) byPath.TryAdd(Normalise(entry.FilePath), entry);
            if (entry.Name.Length > 0) byName.TryAdd(entry.Name, entry);
        }

        var resolved = new List<ResolvedPlugin>(referenced.Count);

        foreach (var plugin in referenced)
        {
            // Path first. Two builds of the same plugin can share a name, and a name match
            // that picks the wrong one records a version the ParameterState was not written
            // by - the exact failure tier 2 exists to make visible.
            if (!byPath.TryGetValue(Normalise(plugin.FilePath), out var match))
            {
                byName.TryGetValue(plugin.Name, out match);
            }

            resolved.Add(new ResolvedPlugin(
                Name: plugin.Name,
                Vendor: plugin.Vendor ?? match?.Manufacturer,
                FilePath: plugin.FilePath,
                Version: match?.Version,
                UniqueId: match?.UniqueId));
        }

        return resolved;
    }

    /// <summary>
    /// Enough to survive the difference between how Wave Link writes a path and how the
    /// scanner writes the same path: separator direction and a trailing slash. Deliberately
    /// not a full-path resolve - that would touch the disk, and this class does not.
    /// </summary>
    private static string Normalise(string path) =>
        path.Replace('/', '\\').TrimEnd('\\');
}
