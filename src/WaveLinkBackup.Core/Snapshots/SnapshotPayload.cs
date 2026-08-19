using WaveLinkBackup.Core.Analysis;

namespace WaveLinkBackup.Core.Snapshots;

/// <summary>
/// A file the capture decided to take, and where it goes inside the snapshot.
///
/// **A reference, not the bytes.** It used to carry a <c>byte[]</c>, which put every preset and
/// every plug-in binary on the heap at once — ~40 MB on the reference rig, and unbounded on a rig
/// with a sample-library instrument on a channel (technical-debt.md §4.19). The store copies from
/// <see cref="Path"/> through <see cref="Abstractions.IFileSystem.CopyFile"/>, which streams and
/// hashes in one pass, so the peak is a buffer.
/// </summary>
/// <param name="RelativePath">Forward slashes; becomes a key in the manifest's <c>files</c> map.</param>
/// <param name="Path">Where it is being copied FROM, on the real filesystem.</param>
/// <param name="SizeBytes">
/// What the size was when the capture looked. Advisory: the manifest records what the copy
/// actually wrote, because the two can differ if the file changed underneath.
/// </param>
public sealed record CapturedFile(string RelativePath, string Path, long SizeBytes);

/// <summary>
/// Everything a capture gathered beyond `Settings.json` itself: the tier 2 manifest, the bytes of
/// tiers 1-extra, 3 and 4, and the tier names those bytes earn.
///
/// **Null and empty mean different things**, and the restore warning depends on the difference. A
/// payload - even an entirely empty one - means a capture looked: it writes `plugins.json`, claims
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
