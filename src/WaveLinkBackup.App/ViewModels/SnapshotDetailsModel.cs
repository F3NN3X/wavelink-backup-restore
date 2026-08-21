using System.Globalization;
using WaveLinkBackup.Core.Analysis;
using WaveLinkBackup.Core.Results;
using WaveLinkBackup.Core.Snapshots;

namespace WaveLinkBackup.App.ViewModels;

/// <summary>One effect of one channel's chain, as the details dialog draws it.</summary>
/// <param name="Position">1-based, and shown: the chain's order is part of the configuration.</param>
public sealed record EffectRow(
    int Position,
    string Name,
    string Meta,
    bool Bypassed,
    bool IsBuiltIn)
{
    /// <summary>The mono number in front of the name, zero-padded to two so a chain stays aligned.</summary>
    public string PositionLabel => Position.ToString("00", CultureInfo.InvariantCulture);
}

/// <summary>
/// One channel: what it is, where it goes, and what sits on it.
/// </summary>
/// <param name="DeviceLabel">
/// The chip beside the name, for ELGATO HARDWARE ONLY - WAVE:3, WAVE XLR. Null for everything
/// else, because "NoWaveDevice" is the file's way of saying "an application or a virtual channel",
/// which is the ordinary case and does not earn a badge.
/// </param>
/// <param name="RoutingLine">
/// Where the channel is heard: the mixes it feeds, or the fact that it feeds none.
/// </param>
public sealed record ChannelRow(
    string Name,
    string? DeviceLabel,
    string RoutingLine,
    bool IsInNoMix,
    bool IsHidden,
    string EffectsLabel,
    IReadOnlyList<EffectRow> Effects);

/// <summary>One mix and where it plays out.</summary>
public sealed record MixRow(string Name, string OutputLine, bool IsMuted);

/// <summary>
/// "What's in this backup" - the whole configuration a snapshot holds, read from the snapshot's
/// own settings file rather than from its manifest.
///
/// **The manifest was not extended to carry any of this.** It records what the LIST needs on every
/// row - counts, names, tiers - and it is written once and read forever, so adding a channel-by-
/// channel structure to it would answer this question only for backups taken after the change and
/// leave every existing one blank. Reading the settings file on demand answers it for every
/// snapshot already on disk, at the cost of one file read when a dialog opens ([[ADR-015]]).
/// </summary>
public sealed class SnapshotDetailsModel
{
    private SnapshotDetailsModel(
        string title,
        string metaLine,
        string? unreadable,
        string summaryLine,
        IReadOnlyList<ChannelRow> channels,
        IReadOnlyList<MixRow> mixes,
        string? mainOutputLine)
    {
        Title = title;
        MetaLine = metaLine;
        Unreadable = unreadable;
        SummaryLine = summaryLine;
        Channels = channels;
        Mixes = mixes;
        MainOutputLine = mainOutputLine;
    }

    /// <summary>The dialog title: the backup's own name, in the app's plain voice.</summary>
    public string Title { get; }

    /// <summary>Mono meta under the title: <c>MANUAL · 20 AUG 10:41 · 3.4 MB</c>.</summary>
    public string MetaLine { get; }

    /// <summary>
    /// Why there is nothing to show, or null when there is. Non-null for a damaged backup and for
    /// one whose settings file has gone - both of which are worth OPENING rather than refusing,
    /// because "what was in it" is exactly what someone asks when a backup has gone wrong.
    /// </summary>
    public string? Unreadable { get; }

    public bool IsReadable => Unreadable is null;

    /// <summary>
    /// <c>9 CHANNELS · 11 EFFECTS ON 1 CHANNEL · 5 MIXES</c>. The same sentence the selected row's
    /// expansion shows, extended - so the dialog opens saying what the row already said.
    /// </summary>
    public string SummaryLine { get; }

    public IReadOnlyList<ChannelRow> Channels { get; }

    public IReadOnlyList<MixRow> Mixes { get; }

    /// <summary>Where Wave Link itself plays out, or null when the file does not say.</summary>
    public string? MainOutputLine { get; }

    /// <summary>
    /// Everything the dialog shows, from a snapshot and the read of its settings file. A failed
    /// read is a STATE of this model rather than a failure to build it: the header still names the
    /// backup, and the body says why it cannot describe it.
    /// </summary>
    public static SnapshotDetailsModel For(Snapshot snapshot, Result<ConfigurationDetail> read)
    {
        var manifest = snapshot.Manifest;
        var title = $"What's in “{manifest.DisplayName}”";

        var meta = string.Join(
            " · ",
            Why(manifest.Trigger),
            Readable.ShortDate(manifest.CreatedUtc.ToLocalTime()).ToUpperInvariant()
                + " " + Readable.TimeOfDay(manifest.CreatedUtc.ToLocalTime()),
            Readable.Bytes(manifest.TotalSizeBytes).ToUpperInvariant());

        if (!read.IsSuccess)
        {
            return new SnapshotDetailsModel(
                title, meta, read.Error!.Message, string.Empty, [], [], null);
        }

        var detail = read.Value;

        return new SnapshotDetailsModel(
            title,
            meta,
            null,
            Summary(detail),
            [.. detail.Channels.Select(Channel)],
            [.. detail.Mixes.Select(Mix)],
            detail.MainOutput is { } main ? $"WAVE LINK PLAYS OUT OF {main.DisplayName.ToUpperInvariant()}" : null);
    }

    private static string Summary(ConfigurationDetail detail)
    {
        var channels = Count(detail.Channels.Count, "CHANNEL");
        var mixes = Count(detail.Mixes.Count, "MIX", "MIXES");

        var effects = detail.EffectCount == 0
            ? "NO EFFECTS"
            : $"{Count(detail.EffectCount, "EFFECT")} ON "
              + Count(detail.ChannelsWithEffectsCount, "CHANNEL");

        return string.Join(" · ", channels, effects, mixes);
    }

    private static ChannelRow Channel(ChannelDetail channel) => new(
        Name: channel.Name,
        DeviceLabel: DeviceLabel(channel.DeviceType),
        RoutingLine: channel.IsInNoMix
            // Not an error and not drawn as one - but it is the one fact about a channel that
            // nothing else in the app would ever tell you, and a channel nobody can hear is
            // usually a surprise.
            ? "NOT IN ANY MIX"
            : "IN " + string.Join(", ", channel.Mixes.Select(m => m.ToUpperInvariant())),
        IsInNoMix: channel.IsInNoMix,
        IsHidden: channel.HiddenFromMixes,
        EffectsLabel: channel.Effects.Count == 0 ? "NO EFFECTS" : Count(channel.Effects.Count, "EFFECT"),
        Effects: [.. channel.Effects.Select(Effect)]);

    private static EffectRow Effect(EffectDetail effect) => new(
        Position: effect.Position,
        Name: effect.DisplayName,
        // Vendor and category as the plug-in describes itself, plus the one thing this app knows
        // and the plug-in does not: whether a restore on another machine would find it.
        Meta: string.Join(
            " · ",
            new[]
            {
                effect.Vendor?.ToUpperInvariant(),
                effect.Category?.ToUpperInvariant(),
                effect.IsBuiltIn ? "BUILT IN" : "VST3",
            }.Where(part => !string.IsNullOrEmpty(part))),
        Bypassed: effect.Bypassed,
        IsBuiltIn: effect.IsBuiltIn);

    private static MixRow Mix(MixDetail mix) => new(
        Name: mix.Name,
        OutputLine: mix.Outputs.Count == 0
            // Normal on a stock rig: only the monitor mix carries a hardware output and the rest
            // are read by the stream software over the virtual device.
            ? "NO OUTPUT DEVICE"
            : string.Join(", ", mix.Outputs.Select(o => o.DisplayName.ToUpperInvariant())),
        IsMuted: mix.IsMuted);

    /// <summary>
    /// Elgato hardware only. <c>Wave3</c> is the file's spelling of a Wave:3, and every other value
    /// it uses for hardware starts the same way; <c>NoWaveDevice</c> means an application or a
    /// virtual channel and earns no badge.
    /// </summary>
    private static string? DeviceLabel(string? deviceType)
    {
        if (deviceType is null) return null;
        if (!deviceType.StartsWith("Wave", StringComparison.OrdinalIgnoreCase)) return null;

        return deviceType.Equals("Wave3", StringComparison.OrdinalIgnoreCase)
            ? "WAVE:3"
            : deviceType.ToUpperInvariant();
    }

    /// <summary>
    /// The row's own WHY vocabulary, so a backup is called the same thing in the list and in the
    /// dialog the list opens. PRE-RESTORE is hyphenated here for the same reason it is there:
    /// PRERESTORE is not a word.
    /// </summary>
    private static string Why(SnapshotTrigger trigger) => trigger switch
    {
        SnapshotTrigger.Manual => "MANUAL",
        SnapshotTrigger.Automatic => "AUTOMATIC",
        SnapshotTrigger.PreRestore => "PRE-RESTORE",
        _ => "UNKNOWN",
    };

    private static string Count(int count, string singular, string? plural = null) =>
        count == 1 ? $"1 {singular}" : $"{count} {plural ?? singular + "S"}";
}
