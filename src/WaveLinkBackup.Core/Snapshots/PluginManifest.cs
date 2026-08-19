namespace WaveLinkBackup.Core.Snapshots;

/// <summary>
/// One plugin as a snapshot recorded it. Everything a person needs to put the rig back
/// together by hand, which is the whole point of tier 2 (ADR-006): a name and a version turn
/// "my effects are gone and I don't know why" into "install FabFilter Pro-Q 4 v4.x".
/// </summary>
/// <param name="Version">
/// Null when the plugin cache had nothing to say. Recorded as unknown rather than guessed -
/// an invented version answers the question wrongly, which is worse than not answering it.
/// </param>
/// <param name="Sha256">
/// Lowercase hex of the binary as it stood at capture time, or null when the path did not
/// resolve to a file. NOT verified by <see cref="SnapshotGuard"/> - the binary lives outside
/// the snapshot, and a plugin update is a legitimate thing to happen between capture and
/// restore. It is evidence for the drift check (phase 6 section 5), not an integrity claim.
/// </param>
/// <param name="Channels">
/// The channels this plugin sits on, so the restore warning can name them. Empty for a snapshot
/// written before phase 6 section 3.
/// </param>
/// <param name="PresetSource">
/// The folder tier 3 captured presets from, or null when it found none. Recorded because preset
/// discovery is a heuristic ([[ADR-006]]): when the numbers look wrong, the first question is
/// which folder was read, and a snapshot that cannot answer it makes the heuristic unimprovable.
/// </param>
/// <param name="PresetFileCount">
/// How many preset files were captured for this plugin. Zero with a non-null
/// <paramref name="PresetSource"/> means the folder was found and held nothing worth copying -
/// visible rather than silent, which is the whole point of recording it.
/// </param>
/// <param name="BinaryPath">
/// Where tier 4 put this plugin's `.vst3` inside the snapshot, or null when it did not copy it -
/// tier 4 was off, or it was on and this plugin is what stopped the tier being claimed.
///
/// The snapshot-relative path rather than a bare flag, because RESTORE needs the mapping back:
/// two vendors can ship `Clear.vst3` and the capture disambiguates them, so reversing that from
/// the file name would be a guess.
/// </param>
public sealed record PluginManifestEntry(
    string Name,
    string? Vendor,
    string? Version,
    string? UniqueId,
    string FilePath,
    string? Sha256,
    IReadOnlyList<string> Channels,
    string? PresetSource = null,
    int PresetFileCount = 0,
    long PresetBytes = 0,
    string? BinaryPath = null)
{
    /// <summary>Whether tier 4 holds this plugin's binary.</summary>
    public bool BinaryCaptured => BinaryPath is not null;

    /// <summary>False when the version could not be established. Restore reads this as drift.</summary>
    public bool VersionKnown => Version is not null;

    /// <summary>How the plugin should be named to a person: vendor first when there is one.</summary>
    public string DisplayName =>
        string.IsNullOrWhiteSpace(Vendor) ? Name : $"{Vendor} {Name}";

    public bool Equals(PluginManifestEntry? other) =>
        other is not null
        && Name == other.Name
        && Vendor == other.Vendor
        && Version == other.Version
        && UniqueId == other.UniqueId
        && FilePath == other.FilePath
        && Sha256 == other.Sha256
        && Channels.SequenceEqual(other.Channels)
        && PresetSource == other.PresetSource
        && PresetFileCount == other.PresetFileCount
        && PresetBytes == other.PresetBytes
        && BinaryPath == other.BinaryPath;

    public override int GetHashCode() =>
        HashCode.Combine(Name, Vendor, Version, UniqueId, FilePath, Sha256, PresetFileCount);
}

/// <summary>
/// plugins.json - the tier 2 payload, written beside settings.json in every snapshot the
/// capture path produces.
///
/// A COMPATIBILITY SURFACE, like <see cref="SnapshotManifest"/>, but with the opposite
/// failure rule: tier 2 is always on and cannot be switched off, so nothing here may ever
/// fail a snapshot. <see cref="PluginManifestSerializer.Read"/> therefore returns a manifest
/// under every condition, degrading field by field to unknown.
/// </summary>
public sealed record PluginManifest(int SchemaVersion, IReadOnlyList<PluginManifestEntry> Plugins)
{
    public const int CurrentSchemaVersion = 1;

    /// <summary>An empty capture - a rig running nothing but Elgato built-ins.</summary>
    public static PluginManifest Empty => new(CurrentSchemaVersion, []);

    public bool Equals(PluginManifest? other) =>
        other is not null
        && SchemaVersion == other.SchemaVersion
        && Plugins.SequenceEqual(other.Plugins);

    public override int GetHashCode() => HashCode.Combine(SchemaVersion, Plugins.Count);
}
