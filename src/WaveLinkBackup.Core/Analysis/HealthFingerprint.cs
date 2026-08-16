namespace WaveLinkBackup.Core.Analysis;

/// <summary>
/// Enough to tell a real configuration from a reset one, cheap enough to compute on every
/// capture, and small enough to live in a snapshot manifest.
///
/// There is deliberately no <c>IsHealthy</c> property. Five inputs and 43 KB is ONE user's
/// rig; an absolute threshold is a bug waiting for the first user with three inputs. Health
/// is decided by <see cref="CompareTo"/> against that user's own previous snapshot.
/// SPEC.md 11.
/// </summary>
public sealed record HealthFingerprint(
    int InputCount,
    IReadOnlyList<string> InputNames,
    int EffectCount,
    int EffectChannelCount,
    long SizeBytes,
    string Sha256)
{
    /// <summary>How this configuration compares with an earlier one from the same machine.</summary>
    public FingerprintComparison CompareTo(HealthFingerprint previous) => new(
        InputsLost: Math.Max(0, previous.InputCount - InputCount),
        InputsGained: Math.Max(0, InputCount - previous.InputCount),
        NamesLost: [.. previous.InputNames.Except(InputNames, StringComparer.Ordinal)],
        EffectsLost: Math.Max(0, previous.EffectCount - EffectCount),
        SizeDeltaBytes: SizeBytes - previous.SizeBytes,
        ContentChanged: !string.Equals(Sha256, previous.Sha256, StringComparison.Ordinal));
}

/// <param name="InputsLost">Channels present before and gone now. The collapse signal.</param>
/// <param name="NamesLost">Named channels that disappeared - louder than a bare count.</param>
/// <param name="ContentChanged">False means these are the same bytes: the dedup decision.</param>
public sealed record FingerprintComparison(
    int InputsLost,
    int InputsGained,
    IReadOnlyList<string> NamesLost,
    int EffectsLost,
    long SizeDeltaBytes,
    bool ContentChanged)
{
    /// <summary>
    /// True when this configuration lost inputs relative to the earlier one. Advisory:
    /// a collapsed snapshot is still restorable, and sometimes it is all there is.
    /// </summary>
    public bool LooksCollapsed => InputsLost > 0;
}
