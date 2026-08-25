using WaveLinkBackup.Core.Abstractions;
using WaveLinkBackup.Core.Analysis;
using WaveLinkBackup.Core.Snapshots;

namespace WaveLinkBackup.Core.Restore;

/// <summary>What a restore would find for one plugin the snapshot names.</summary>
public enum PluginPresence
{
    /// <summary>It is here, at the recorded version. Nothing to say.</summary>
    Installed,

    /// <summary>
    /// Not at its recorded path and not in the scanner cache. This is the failure tier 2 exists
    /// to make visible: restore the settings and that channel loads with the effect switched
    /// off, looking like an incomplete backup rather than a missing plugin.
    /// </summary>
    Missing,

    /// <summary>Here, at a different version. `ParameterState` usually survives that. Usually.</summary>
    VersionDrift,

    /// <summary>Here, but one of the two versions is unknown. Never a hard failure.</summary>
    VersionUnknown,
}

public sealed record PluginPresenceResult(
    string DisplayName,
    string FilePath,
    string? RecordedVersion,
    string? CurrentVersion,
    IReadOnlyList<string> Channels,
    PluginPresence Presence);

/// <summary>
/// What a restore would find on this machine for the plugins a snapshot recorded, and the two
/// clauses the restore dialog prints when the answer is "not all of them".
///
/// The sentences live here rather than in the shell for the same reason
/// <see cref="RestorePlan.VersionWarning"/> does: the wording is a property of the finding, and
/// two shells rendering the same finding differently is how a warning becomes untrustworthy.
/// </summary>
public sealed record PluginRestoreCheck(IReadOnlyList<PluginPresenceResult> Plugins)
{
    /// <summary>Nothing to compare against, a snapshot written before tier 2 existed.</summary>
    public static PluginRestoreCheck Unknown { get; } = new([]);

    public IReadOnlyList<PluginPresenceResult> Missing =>
        [.. Plugins.Where(p => p.Presence == PluginPresence.Missing)];

    public IReadOnlyList<PluginPresenceResult> Drifted =>
        [.. Plugins.Where(p => p.Presence == PluginPresence.VersionDrift)];

    public bool HasMissing => Missing.Count > 0;

    /// <summary>
    /// The naming clause, in strong text: *"FabFilter Pro-Q 4 isn't installed on this computer."*
    /// Null when nothing is missing, which is what hides the whole amber block.
    /// </summary>
    public string? MissingLead => Missing.Count switch
    {
        0 => null,
        1 => $"{Missing[0].DisplayName} isn't installed on this computer.",
        _ => $"{Join([.. Missing.Select(p => p.DisplayName)])} aren't installed on this computer.",
    };

    /// <summary>
    /// The consequence and the way out: *"The Voice channel will load with that effect switched
    /// off. Install it and restore again to get it back."*
    /// </summary>
    public string? MissingRest
    {
        get
        {
            if (Missing.Count == 0) return null;

            var one = Missing.Count == 1;
            var effect = one ? "that effect" : "those effects";
            var it = one ? "it" : "them";

            var channels = Missing
                .SelectMany(p => p.Channels)
                .Distinct(StringComparer.Ordinal)
                .ToList();

            // A snapshot from before channels were recorded knows what is missing but not where
            // it sat. Saying so beats naming a channel we do not have.
            var where = channels.Count == 0
                ? $"The channels using {effect} will load with {(one ? "it" : "them")} switched off."
                : channels.Count == 1
                    ? $"The {channels[0]} channel will load with {effect} switched off."
                    : $"The {Join(channels)} channels will load with {effect} switched off.";

            return $"{where} Install {it} and restore again to get {it} back.";
        }
    }

    /// <summary>
    /// The quiet line for plugins that ARE installed at a different version. Never a failure and
    /// never amber: `ParameterState` is written by a specific plugin version and normally survives
    /// an update, because plugins version their own state, but it is not guaranteed, and this is
    /// the one place the user can find out that it happened.
    /// </summary>
    public string? DriftNote
    {
        get
        {
            if (Drifted.Count == 0) return null;

            var named = Drifted
                .Select(p => $"{p.DisplayName} {p.RecordedVersion} → {p.CurrentVersion}")
                .ToList();

            return $"Plug-in versions have changed since this backup: {Join(named)}. "
                 + "Effect settings usually survive a plug-in update, but not always.";
        }
    }

    private static string Join(IReadOnlyList<string> parts) => parts.Count switch
    {
        0 => string.Empty,
        1 => parts[0],
        _ => $"{string.Join(", ", parts.Take(parts.Count - 1))} and {parts[^1]}",
    };
}

/// <summary>
/// Does each plugin a snapshot names still resolve on this machine, and at which version?
/// [[ADR-006]], SPEC §9.
/// </summary>
public sealed class PluginResolution(IFileSystem fileSystem)
{
    /// <param name="snapshot">The snapshot's plugins.json, already read.</param>
    /// <param name="installed">
    /// The LIVE plugin cache. Used as the second chance: a plugin the user moved is still
    /// installed, and reporting it missing would send them to reinstall something they have.
    /// </param>
    public PluginRestoreCheck Check(PluginManifest snapshot, IReadOnlyList<CachedPlugin> installed)
    {
        var byPath = new Dictionary<string, CachedPlugin>(StringComparer.OrdinalIgnoreCase);
        var byName = new Dictionary<string, CachedPlugin>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in installed)
        {
            if (entry.FilePath.Length > 0) byPath.TryAdd(Normalise(entry.FilePath), entry);
            if (entry.Name.Length > 0) byName.TryAdd(entry.Name, entry);
        }

        var results = new List<PluginPresenceResult>(snapshot.Plugins.Count);

        foreach (var plugin in snapshot.Plugins)
        {
            if (!byPath.TryGetValue(Normalise(plugin.FilePath), out var match))
            {
                byName.TryGetValue(plugin.Name, out match);
            }

            // On disk beats in the cache: the cache is rebuilt by scanning and can name a plugin
            // that has since been uninstalled. Directory as well as file. A bundle is a directory
            // ([[vst3-backs-up-as-nothing]]), and testing only for a file reports every bundled
            // plugin missing.
            var onDisk = plugin.FilePath.Length > 0
                && (fileSystem.FileExists(plugin.FilePath) || fileSystem.DirectoryExists(plugin.FilePath));

            var current = match?.Version;

            results.Add(new PluginPresenceResult(
                DisplayName: plugin.DisplayName,
                FilePath: plugin.FilePath,
                RecordedVersion: plugin.Version,
                CurrentVersion: current,
                Channels: plugin.Channels,
                Presence: Presence(onDisk, match is not null, plugin.Version, current)));
        }

        return new PluginRestoreCheck(results);
    }

    /// <summary>
    /// Drift is only claimed when both versions are known. An unknown version on either side
    /// is reported as unknown rather than as a change, because a warning that fires on every
    /// restore is a warning nobody reads by the third time.
    /// </summary>
    private static PluginPresence Presence(bool onDisk, bool inCache, string? recorded, string? current)
    {
        if (!onDisk && !inCache) return PluginPresence.Missing;
        if (recorded is null || current is null) return PluginPresence.VersionUnknown;

        return string.Equals(recorded, current, StringComparison.OrdinalIgnoreCase)
            ? PluginPresence.Installed
            : PluginPresence.VersionDrift;
    }

    /// <summary>Separator direction and a trailing slash, exactly as <see cref="PluginReferences"/>.</summary>
    private static string Normalise(string path) => path.Replace('/', '\\').TrimEnd('\\');
}
