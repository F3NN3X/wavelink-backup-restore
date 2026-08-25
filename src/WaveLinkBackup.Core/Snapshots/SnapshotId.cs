namespace WaveLinkBackup.Core.Snapshots;

/// <summary>
/// A snapshot's directory name. MACHINE-GENERATED, always.
///
/// The user's display name lives in manifest.json and never here, so renaming is a metadata
/// write: no file moves, no collisions, no sanitising user text into a path, and nothing
/// breaks when someone types `Mic chain 3/4"`. Upstream encodes identity in the filename and
/// pays for it three ways at once - custom names, custom locations, and a rename that is a
/// move. See ADR-003.
/// </summary>
public static class SnapshotId
{
    /// <summary>e.g. <c>2026-08-15T23the search spec</c>.</summary>
    public static string Create(DateTimeOffset createdUtc, string settingsSha256)
    {
        var stamp = createdUtc.ToUniversalTime().ToString("yyyy-MM-dd'T'HHmm");
        var shortHash = settingsSha256.Length >= 6 ? settingsSha256[..6] : settingsSha256;

        return $"{stamp}-{shortHash}";
    }

    /// <summary>
    /// Disambiguates a collision. Two snapshots in the same minute with the same content
    /// hash are the *same configuration*, so this is rare and meaningful rather than
    /// accidental - phase 3's dedup will usually skip the write entirely. Until then, both
    /// are kept.
    /// </summary>
    public static string WithSuffix(string id, int attempt) => $"{id}-{attempt}";

    // A LooksLikeSnapshotId(string) helper lived here for three phases and never acquired a
    // caller: SnapshotStore.List filters on "does this directory contain a manifest we can
    // read", which is both cheaper to reason about and authoritative in a way a name pattern
    // can never be. Deleted in phase 4 rather than carried a fourth time.
    //
    // If something ever does need to recognise an id by shape - a repair tool scanning a
    // half-deleted store, say - write it then, against that requirement.
}
