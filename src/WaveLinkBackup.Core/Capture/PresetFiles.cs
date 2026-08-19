using WaveLinkBackup.Core.Abstractions;
using WaveLinkBackup.Core.Analysis;

namespace WaveLinkBackup.Core.Capture;

/// <summary>
/// The two places a vendor keeps a user's presets. Recorded per file inside the snapshot, because
/// restore has to put each one back under the root it came from and the two are on different
/// volumes on any machine with a redirected Documents folder.
/// </summary>
public enum PresetRoot
{
    /// <summary>Roaming <c>%APPDATA%</c>. Interface defaults, MIDI maps, per-plugin state.</summary>
    AppData,

    /// <summary>
    /// The Documents folder, resolved through <c>GetFolderPath</c>. Where FabFilter — and every
    /// vendor that treats a preset as a document rather than a setting — keeps the actual
    /// <c>.ffp</c> files.
    /// </summary>
    Documents,
}

/// <param name="Root">Which root this folder sits under; decides where a restore writes it back.</param>
/// <param name="Path">The absolute folder that was read.</param>
public sealed record PresetSourceFolder(PresetRoot Root, string Path);

/// <param name="Sources">
/// Every folder that was found, at most one per root. Empty when the vendor keeps nothing anywhere
/// we look.
/// </param>
public sealed record PresetDiscovery(IReadOnlyList<PresetSourceFolder> Sources, IReadOnlyList<SourceFile> Files)
{
    public static PresetDiscovery Nothing { get; } = new([], []);

    public long Bytes => FileTree.TotalBytes(Files);

    /// <summary>The folder paths, in root order, as <c>plugins.json</c> records them.</summary>
    public IReadOnlyList<string> SourcePaths => [.. Sources.Select(s => s.Path)];
}

/// <summary>
/// Tier 3: the presets a plugin saved — the user's own irreplaceable work, and the only tier whose
/// contents cannot be re-downloaded from anyone.
///
/// **This is a heuristic and [[ADR-006]] says so.** Vendors agree on nothing, and the first version
/// of this class looked in one place only: <c>%APPDATA%\&lt;Vendor&gt;\</c>. Run against a real rig
/// (technical-debt.md §4.18) that turned out to capture the wrong thing quietly — for FabFilter it
/// found three files (an interface default, a MIDI map, a preset cache) while the 172 <c>.ffp</c>
/// presets the Settings dialog promises sat in <c>Documents\FabFilter\Presets\Pro-Q 4\</c>, and for
/// Supertone Clear it captured two crash reports and called them presets.
///
/// So it now reads **both roots**, records which folders it read, and refuses to capture files that
/// are plainly not presets. A heuristic whose result cannot be inspected is a heuristic nobody can
/// improve; one that cannot be checked against a real machine is one nobody knows is wrong.
/// </summary>
/// <param name="appDataPath">Roaming <c>%APPDATA%</c>.</param>
/// <param name="documentsPath">
/// The user's Documents folder. Injected, and resolved by the caller through
/// <c>GetFolderPath(MyDocuments)</c> rather than composed from <c>%USERPROFILE%</c> — the reference
/// rig has it redirected to another volume entirely, which is the same trap technical-debt.md §5
/// records for <c>%LOCALAPPDATA%</c>.
/// </param>
public sealed class PresetFiles(IFileSystem fileSystem, string appDataPath, string documentsPath)
{
    /// <summary>Where they sit inside a snapshot.</summary>
    public const string RelativeRoot = "presets";

    /// <summary>
    /// The path segment naming each root inside a snapshot: <c>presets/appdata/…</c> and
    /// <c>presets/documents/…</c>.
    ///
    /// Lower-case and unqualified, which is the one thing about this layout that could bite: a
    /// vendor literally named "AppData" would be indistinguishable from the root segment. No such
    /// vendor exists, and the alternative — a reserved prefix nobody can read — costs more than the
    /// risk. A snapshot written before this layout has neither segment, and
    /// <see cref="RootOf"/> reads it as AppData, which is where those files came from.
    /// </summary>
    public static string SegmentFor(PresetRoot root) =>
        root == PresetRoot.Documents ? "documents" : "appdata";

    /// <summary>
    /// Which root a snapshot-relative preset path belongs to, and the part of the path below it.
    /// The inverse of the capture mapping, and the only thing standing between a Documents preset
    /// and being restored into <c>%APPDATA%</c>.
    /// </summary>
    /// <param name="relative">A path under <c>presets/</c>, forward-slashed.</param>
    public static (PresetRoot Root, string Under) RootOf(string relative)
    {
        var under = relative.StartsWith(RelativeRoot + "/", StringComparison.OrdinalIgnoreCase)
            ? relative[(RelativeRoot.Length + 1)..]
            : relative;

        foreach (var root in new[] { PresetRoot.AppData, PresetRoot.Documents })
        {
            var segment = SegmentFor(root);
            if (under.StartsWith(segment + "/", StringComparison.OrdinalIgnoreCase))
            {
                return (root, under[(segment.Length + 1)..]);
            }
        }

        // Pre-§4.18 snapshots wrote presets/<Vendor>/… with no root segment, and everything they
        // hold came from %APPDATA%. Restoring them unchanged is the whole reason this returns a
        // root rather than throwing.
        return (PresetRoot.AppData, under);
    }

    /// <summary>
    /// Directory names that are never presets, matched case-insensitively at any depth.
    ///
    /// Supertone Clear is why this exists: <c>%APPDATA%\Supertone\Clear\Reports\</c> holds crash
    /// dumps and nothing else, and the first version of this class captured them, counted them and
    /// reported them to the user as saved presets. Excluding them makes Clear report a folder it
    /// found with zero files in it — "we looked and there is nothing here", which is a state
    /// <see cref="Snapshots.PluginManifestEntry.PresetFileCount"/> is explicitly designed to show.
    /// </summary>
    public static IReadOnlySet<string> NeverPresets { get; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Reports", "Report", "CrashReports", "Crashes", "Crash",
            "Logs", "Log", "Diagnostics", "Telemetry",
        };

    /// <summary>
    /// Every root's first matching folder, walked in full.
    ///
    /// Within a root the candidates run narrowest first, so a vendor that separates its plugins is
    /// captured per plugin rather than wholesale. Across roots they are additive: FabFilter keeps
    /// the MIDI map in one and the presets in the other, and a rule that took only the first would
    /// have to choose which half of the user's work to lose.
    ///
    /// A plugin with no vendor recorded finds nothing: guessing a vendor folder from a plugin name
    /// would capture some other vendor's work.
    /// </summary>
    public PresetDiscovery Discover(ResolvedPlugin plugin)
    {
        if (string.IsNullOrWhiteSpace(plugin.Vendor)) return PresetDiscovery.Nothing;

        var sources = new List<PresetSourceFolder>();
        var files = new List<SourceFile>();

        foreach (var root in new[] { PresetRoot.AppData, PresetRoot.Documents })
        {
            var rootPath = root == PresetRoot.Documents ? documentsPath : appDataPath;
            if (string.IsNullOrWhiteSpace(rootPath)) continue;

            var found = FirstExisting(Candidates(root, rootPath, plugin));
            if (found is null) continue;

            sources.Add(new PresetSourceFolder(root, found));
            files.AddRange(Walk(root, rootPath, found));
        }

        return sources.Count == 0 ? PresetDiscovery.Nothing : new PresetDiscovery(sources, files);
    }

    /// <summary>
    /// Where to look under one root, narrowest first.
    ///
    /// The two roots do NOT get the same fallbacks, and the difference is deliberate.
    /// <c>%APPDATA%\&lt;Vendor&gt;</c> ends the AppData list because a vendor folder there is
    /// config-sized whatever it holds. <c>Documents\&lt;Vendor&gt;</c> does not end the Documents
    /// list, because a vendor folder in Documents is as likely to be a project library — sessions,
    /// renders, sample packs — as a preset folder, and a fallback that wide would turn a
    /// ten-megabyte tier into a hundred-gigabyte one on somebody's machine. Documents stops at
    /// <c>&lt;Vendor&gt;\Presets</c>: a folder that says what it is.
    /// </summary>
    private static IEnumerable<string> Candidates(PresetRoot root, string rootPath, ResolvedPlugin plugin)
    {
        var vendor = Path.Combine(rootPath, plugin.Vendor!);

        // The settings file calls it "Pro-Q 4" and the installer calls the file "FabFilter Pro-Q
        // 4.vst3"; both spellings are tried before anything wider.
        var fileName = Path.GetFileNameWithoutExtension(plugin.FilePath);

        if (root == PresetRoot.Documents)
        {
            var presets = Path.Combine(vendor, "Presets");

            yield return Path.Combine(presets, plugin.Name);
            if (fileName.Length > 0) yield return Path.Combine(presets, fileName);
            yield return Path.Combine(vendor, plugin.Name);
            if (fileName.Length > 0) yield return Path.Combine(vendor, fileName);
            yield return presets;
            yield break;
        }

        yield return Path.Combine(vendor, plugin.Name);
        if (fileName.Length > 0) yield return Path.Combine(vendor, fileName);
        yield return vendor;
    }

    private string? FirstExisting(IEnumerable<string> candidates) =>
        candidates.FirstOrDefault(fileSystem.DirectoryExists);

    /// <summary>
    /// What tier 3 would cost, per plugin, without reading anything.
    ///
    /// Two plugins from one vendor that both fall back to the same folder each report its size here
    /// and are stored **once**. The estimate is therefore an upper bound, which is the safe
    /// direction for a figure the Settings dialog prints before the user opts in.
    /// </summary>
    public long Measure(ResolvedPlugin plugin) => Discover(plugin).Bytes;

    private IReadOnlyList<SourceFile> Walk(PresetRoot root, string rootPath, string directory)
    {
        var walked = FileTree.Walk(fileSystem, directory, Relative(root, rootPath, directory));

        // Filtered after the walk rather than during it, so the excluded names live in one readable
        // set instead of inside the tree walker every tier shares.
        return [.. walked.Where(f => !IsNoise(f.RelativePath))];
    }

    private static bool IsNoise(string relativePath) =>
        relativePath.Split('/').SkipLast(1).Any(NeverPresets.Contains);

    /// <summary>
    /// <c>presets/&lt;root&gt;/&lt;path below that root&gt;</c>, so a snapshot's presets directory
    /// reads like the two folders it came from and restore can tell them apart.
    /// </summary>
    private static string Relative(PresetRoot root, string rootPath, string directory)
    {
        var trimmed = directory.TrimEnd('\\', '/');
        var start = rootPath.TrimEnd('\\', '/');

        var under = trimmed.StartsWith(start + "\\", StringComparison.OrdinalIgnoreCase)
            ? trimmed[(start.Length + 1)..]
            : Path.GetFileName(trimmed);

        return $"{RelativeRoot}/{SegmentFor(root)}/{under.Replace('\\', '/')}";
    }
}
