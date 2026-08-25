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
/// <param name="PresetSources">
/// The folders tier 3 captured presets from - at most one per root, so up to two. Empty when it
/// found none. Recorded because preset discovery is a heuristic ([[ADR-006]]): when the numbers
/// look wrong, the first question is which folder was read, and a snapshot that cannot answer it
/// makes the heuristic unimprovable. It is also what caught §4.18 - three files where a hundred
/// and seventy-two were expected is only visible if the folder is written down beside the count.
/// </param>
/// <param name="PresetFileCount">
/// How many preset files were captured for this plugin. Zero with a non-empty
/// <paramref name="PresetSources"/> means the folders were found and held nothing worth copying -
/// visible rather than silent, which is the whole point of recording it. Supertone Clear is the
/// real case: its folder exists and holds only crash reports, which tier 3 refuses to capture.
/// </param>
/// <param name="BinaryPath">
/// Where tier 4 put this plugin's `.vst3` inside the snapshot, or null when it did not copy it -
/// tier 4 was off, or it was on and this plugin is what stopped the tier being claimed.
///
/// The snapshot-relative path rather than a bare flag, because RESTORE needs the mapping back:
/// two vendors can ship `Clear.vst3` and the capture disambiguates them, so reversing that from
/// the file name would be a guess.
/// </param>
/// <param name="BinarySizeBytes">
/// The binary's length when <paramref name="Sha256"/> was computed, or 0 when it did not resolve
/// to a file. Half of the pair that says whether the hash is still current.
/// </param>
/// <param name="BinaryLastWriteUtc">
/// The binary's last-write time when <paramref name="Sha256"/> was computed, or null.
///
/// Recorded so the NEXT capture can skip re-reading the file: tier 2 is always on and runs on
/// every automatic capture, and hashing ~40 MB of plug-in binaries to re-derive a value that
/// changes only when the user updates a plug-in is the largest avoidable cost in a capture
/// (technical-debt.md §4.16). Size AND time together, because either alone is easy to match by
/// accident.
/// </param>
public sealed record PluginManifestEntry(
    string Name,
    string? Vendor,
    string? Version,
    string? UniqueId,
    string FilePath,
    string? Sha256,
    IReadOnlyList<string> Channels,
    IReadOnlyList<string>? PresetSources = null,
    int PresetFileCount = 0,
    long PresetBytes = 0,
    string? BinaryPath = null,
    long BinarySizeBytes = 0,
    DateTime? BinaryLastWriteUtc = null)
{
    private readonly IReadOnlyList<string> presetSources = PresetSources ?? [];

    /// <summary>
    /// Never null, so every reader can enumerate it without a guard. The parameter is optional
    /// because most call sites have nothing to say about presets, and "nothing was found" and
    /// "nobody looked" are the same thing to everything downstream of the capture.
    /// </summary>
    public IReadOnlyList<string> PresetSources
    {
        get => presetSources;
        init => presetSources = value ?? [];
    }

    /// <summary>
    /// The first folder tier 3 read, or null when it read none. Kept because most of what asks
    /// this question wants one answer — a diagnostic line, a log — and only restore needs all of
    /// them.
    /// </summary>
    public string? PresetSource => PresetSources.Count > 0 ? PresetSources[0] : null;

    /// <summary>Whether tier 4 holds this plugin's binary.</summary>
    public bool BinaryCaptured => BinaryPath is not null;

    /// <summary>
    /// Whether this entry's <see cref="Sha256"/> can be reused for a binary that measures
    /// <paramref name="sizeBytes"/> and was last written at <paramref name="lastWriteUtc"/>.
    ///
    /// Conservative in every direction. No hash, no recorded size or no recorded time means
    /// no — an entry from a snapshot written before schema 3 has none of them, and the honest
    /// answer for it is to hash. This is a cache with an invalidation rule, so it says yes only
    /// when all three agree.
    /// </summary>
    public bool BinaryMatches(long sizeBytes, DateTime lastWriteUtc) =>
        Sha256 is not null
        && BinarySizeBytes > 0
        && BinaryLastWriteUtc is { } recorded
        && BinarySizeBytes == sizeBytes
        && recorded == lastWriteUtc;

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
        && PresetSources.SequenceEqual(other.PresetSources)
        && PresetFileCount == other.PresetFileCount
        && PresetBytes == other.PresetBytes
        && BinaryPath == other.BinaryPath
        && BinarySizeBytes == other.BinarySizeBytes
        && BinaryLastWriteUtc == other.BinaryLastWriteUtc;

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
    /// <summary>
    /// 1 · the original: one <c>presetSource</c> string per plugin, presets stored at
    /// <c>presets/&lt;Vendor&gt;/…</c>.
    /// 2 · <c>presetSources</c> is an array and the stored path names its root
    /// (<c>presets/appdata/…</c>, <c>presets/documents/…</c>). Both are still read; only the newer
    /// one is written.
    /// 3 · each entry records <c>binarySizeBytes</c> and <c>binaryLastWriteUtc</c> beside its
    /// <c>sha256</c>, so the next capture can tell a hash that is still current from one that
    /// is not. Purely additive: a schema-2 file reads as an entry that always needs rehashing.
    /// </summary>
    public const int CurrentSchemaVersion = 3;

    /// <summary>An empty capture - a rig running nothing but Elgato built-ins.</summary>
    public static PluginManifest Empty => new(CurrentSchemaVersion, []);

    public bool Equals(PluginManifest? other) =>
        other is not null
        && SchemaVersion == other.SchemaVersion
        && Plugins.SequenceEqual(other.Plugins);

    public override int GetHashCode() => HashCode.Combine(SchemaVersion, Plugins.Count);
}
