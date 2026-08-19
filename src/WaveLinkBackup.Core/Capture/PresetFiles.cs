using WaveLinkBackup.Core.Abstractions;
using WaveLinkBackup.Core.Analysis;

namespace WaveLinkBackup.Core.Capture;

/// <param name="Source">The folder that was found, or null when none of the candidates existed.</param>
public sealed record PresetDiscovery(string? Source, IReadOnlyList<SourceFile> Files)
{
    public static PresetDiscovery Nothing { get; } = new(null, []);

    public long Bytes => FileTree.TotalBytes(Files);
}

/// <summary>
/// Tier 3: the presets a plugin saved under <c>%APPDATA%\&lt;Vendor&gt;\</c> - the user's own
/// irreplaceable work, and the only tier whose contents cannot be re-downloaded from anyone.
///
/// **This is a heuristic and [[ADR-006]] says so.** Vendors agree on nothing: some write
/// <c>%APPDATA%\FabFilter\Pro-Q 4\</c>, some write straight into the vendor folder, and at least
/// one (<c>%APPDATA%\Supertone\Clear</c>) keeps crash reports there and nothing else. So the rule
/// is to look in the narrowest place first and record **which folder was read**, because a
/// heuristic whose result cannot be inspected is a heuristic nobody can improve.
/// </summary>
public sealed class PresetFiles(IFileSystem fileSystem, string appDataPath)
{
    /// <summary>Where they sit inside a snapshot.</summary>
    public const string RelativeRoot = "presets";

    /// <summary>
    /// The first candidate folder that exists, walked in full:
    ///
    /// <list type="number">
    /// <item><c>&lt;AppData&gt;\&lt;Vendor&gt;\&lt;plugin name&gt;</c> - what the spec describes</item>
    /// <item><c>&lt;AppData&gt;\&lt;Vendor&gt;\&lt;file name without .vst3&gt;</c> - the settings file
    /// calls it "Pro-Q 4" and the installer calls the folder "FabFilter Pro-Q 4"</item>
    /// <item><c>&lt;AppData&gt;\&lt;Vendor&gt;</c> - vendors that keep presets flat</item>
    /// </list>
    ///
    /// Narrowest first, so a vendor that separates its plugins is captured per plugin rather than
    /// wholesale. A plugin with no vendor recorded finds nothing: guessing a vendor folder from a
    /// plugin name would capture some other vendor's work.
    /// </summary>
    public PresetDiscovery Discover(ResolvedPlugin plugin)
    {
        if (string.IsNullOrWhiteSpace(plugin.Vendor)) return PresetDiscovery.Nothing;

        var vendor = Path.Combine(appDataPath, plugin.Vendor);
        var fileName = Path.GetFileNameWithoutExtension(plugin.FilePath);

        string[] candidates =
        [
            Path.Combine(vendor, plugin.Name),
            fileName.Length > 0 ? Path.Combine(vendor, fileName) : vendor,
            vendor,
        ];

        foreach (var candidate in candidates)
        {
            if (!fileSystem.DirectoryExists(candidate)) continue;

            // The relative path mirrors the tree from the vendor folder down, so a snapshot's
            // presets/ directory reads like the %APPDATA% it came from.
            var relative = Relative(candidate);
            return new PresetDiscovery(candidate, FileTree.Walk(fileSystem, candidate, relative));
        }

        return PresetDiscovery.Nothing;
    }

    /// <summary>
    /// What tier 3 would cost, per plugin, without reading anything.
    ///
    /// Two plugins from one vendor that both fall back to the vendor folder each report its size
    /// here and are stored **once**. The estimate is therefore an upper bound, which is the safe
    /// direction for a figure the Settings dialog prints before the user opts in.
    /// </summary>
    public long Measure(ResolvedPlugin plugin) => Discover(plugin).Bytes;

    private string Relative(string directory)
    {
        var trimmed = directory.TrimEnd('\\', '/');
        var root = appDataPath.TrimEnd('\\', '/');

        var under = trimmed.StartsWith(root + "\\", StringComparison.OrdinalIgnoreCase)
            ? trimmed[(root.Length + 1)..]
            : Path.GetFileName(trimmed);

        return $"{RelativeRoot}/{under.Replace('\\', '/')}";
    }
}
