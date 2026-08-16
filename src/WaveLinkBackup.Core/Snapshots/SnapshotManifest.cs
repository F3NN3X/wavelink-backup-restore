namespace WaveLinkBackup.Core.Snapshots;

/// <summary>Why a snapshot was taken. Rendered distinctly in the UI.</summary>
public enum SnapshotTrigger
{
    /// <summary>The user asked for it. Never pruned.</summary>
    Manual,

    /// <summary>The watcher noticed a change (phase 3). Pruned to the configured count.</summary>
    Automatic,

    /// <summary>
    /// Taken automatically before a restore. Never pruned - it is what the user comes back
    /// to when the restore was a mistake.
    /// </summary>
    PreRestore,
}

/// <param name="Sha256">Lowercase hex. Checked by <see cref="SnapshotGuard"/> against disk.</param>
public sealed record SnapshotFile(string Sha256, long SizeBytes);

/// <summary>
/// Everything known about a snapshot, and the entire restore UI: every column in the main
/// window's list reads from here, so nothing has to open a snapshot to render a row.
///
/// This is a COMPATIBILITY SURFACE from the first write. The store outlives the application,
/// in a location the user chose and may sync, move, or restore from a backup of their own.
/// Adding a field is safe; changing or removing one needs a schema bump.
/// </summary>
public sealed record SnapshotManifest(
    int SchemaVersion,
    string DisplayName,
    string Notes,
    DateTimeOffset CreatedUtc,
    SnapshotTrigger Trigger,
    string SettingsSha256,
    string? WaveLinkVersion,
    int InputCount,
    IReadOnlyList<string> InputNames,
    int EffectCount,
    int EffectChannelCount,
    bool HasDuplicateKeys,
    IReadOnlyList<string> Tiers,
    IReadOnlyDictionary<string, SnapshotFile> Files)
{
    public const int CurrentSchemaVersion = 1;

    /// <summary>The file every snapshot has. Tiers 2-4 add more (phase 6).</summary>
    public const string SettingsFileName = "settings.json";

    public const string ManifestFileName = "manifest.json";

    /// <summary>
    /// True when validation found case-insensitively duplicated keys. Marks the entry suspect
    /// in the UI; does NOT block a restore, because a suspect snapshot may be the only one
    /// there is.
    /// </summary>
    public bool IsSuspect => HasDuplicateKeys;

    /// <summary>Manual and pre-restore snapshots are never pruned, at any count (phase 3).</summary>
    public bool IsPrunable => Trigger == SnapshotTrigger.Automatic;

    public bool Equals(SnapshotManifest? other) =>
        other is not null
        && SchemaVersion == other.SchemaVersion
        && DisplayName == other.DisplayName
        && Notes == other.Notes
        && CreatedUtc == other.CreatedUtc
        && Trigger == other.Trigger
        && SettingsSha256 == other.SettingsSha256
        && WaveLinkVersion == other.WaveLinkVersion
        && InputCount == other.InputCount
        && InputNames.SequenceEqual(other.InputNames)
        && EffectCount == other.EffectCount
        && EffectChannelCount == other.EffectChannelCount
        && HasDuplicateKeys == other.HasDuplicateKeys
        && Tiers.SequenceEqual(other.Tiers)
        && Files.Count == other.Files.Count
        && Files.All(f => other.Files.TryGetValue(f.Key, out var g) && f.Value == g);

    public override int GetHashCode() => HashCode.Combine(
        SchemaVersion, DisplayName, CreatedUtc, Trigger, SettingsSha256, InputCount, EffectCount);
}
