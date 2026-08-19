using WaveLinkBackup.Core.Analysis;

namespace WaveLinkBackup.Core.Snapshots;

/// <summary>A file gathered from outside the snapshot, and where it goes inside it.</summary>
/// <param name="RelativePath">Forward slashes; becomes a key in the manifest's <c>files</c> map.</param>
public sealed record CapturedFile(string RelativePath, byte[] Bytes);

/// <summary>
/// Everything a capture gathered beyond `Settings.json` itself: the tier 2 manifest, the bytes of
/// tiers 1-extra, 3 and 4, and the tier names those bytes earn.
///
/// **Null and empty mean different things**, and the restore warning depends on the difference. A
/// payload — even an entirely empty one — means a capture looked: it writes `plugins.json`, claims
/// the `plugin-manifest` tier, and a restore reading it can say "nothing is missing" and be
/// believed. **No payload means nobody looked**, which is every snapshot written before phase 6
/// and every caller that never wired a <see cref="Capture.TierCapture"/>.
/// </summary>
public sealed record SnapshotPayload(
    PluginManifest Plugins,
    IReadOnlyList<CapturedFile> Files,
    IReadOnlyList<string> Tiers)
{
    /// <summary>A capture that looked and found no third-party plugins and no extra files.</summary>
    public static SnapshotPayload Empty { get; } = new(PluginManifest.Empty, [], []);

    /// <summary>Tier 2 only — what a capture with no filesystem behind it can honestly claim.</summary>
    public static SnapshotPayload ForPlugins(IReadOnlyList<ResolvedPlugin> plugins) =>
        new(new PluginManifest(
                PluginManifest.CurrentSchemaVersion,
                [.. plugins.Select(p => new PluginManifestEntry(
                    p.Name, p.Vendor, p.Version, p.UniqueId, p.FilePath, Sha256: null, p.Channels))]),
            [],
            []);

    public bool Equals(SnapshotPayload? other) =>
        other is not null
        && Plugins == other.Plugins
        && Files.Count == other.Files.Count
        && Tiers.SequenceEqual(other.Tiers);

    public override int GetHashCode() => HashCode.Combine(Plugins, Files.Count, Tiers.Count);
}
