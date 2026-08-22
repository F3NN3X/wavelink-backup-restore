using System.Text.Json;
using WaveLinkBackup.Core.Results;

namespace WaveLinkBackup.Core.Analysis;

/// <summary>
/// One effect on one channel, in the order the chain runs it.
/// </summary>
/// <param name="Position">
/// 1-based, and load-bearing: an EQ before a compressor is a different sound from the same two
/// the other way round, so the order is part of what a backup holds.
/// </param>
/// <param name="Name">
/// Wave Link's own name for the plug-in. Elgato's built-ins are spelt in the file as
/// <c>WaveCompressor</c>, <c>VoiceFocus</c> - one word, no spaces - and are left exactly as
/// written. Prettifying them here would invent a name the user cannot find in Wave Link.
/// </param>
/// <param name="CustomName">What the user renamed this instance to, or null. Wave Link allows it.</param>
/// <param name="Category">
/// The file's own <c>Category</c>: <c>Fx</c>, <c>EQ</c>, <c>Dynamics</c>, <c>Distortion</c>.
/// Not normalised - it is the plug-in's self-description, and a fixed vocabulary here would go
/// stale the first time a vendor picked a word we had not seen.
/// </param>
/// <param name="Bypassed">
/// The effect is in the chain and switched off. Worth showing rather than hiding: a bypassed
/// effect is still restored bypassed, and "why is my de-esser doing nothing" is answered here.
/// </param>
/// <param name="FilePath">
/// Null for an Elgato built-in, which carries an empty <c>FilePath</c> in the settings file and
/// ships with Wave Link ([[ADR-006]]). Non-null is a third-party VST3 - the ones tier 2 records
/// and tier 4 can copy.
/// </param>
public sealed record EffectDetail(
    int Position,
    string Name,
    string? CustomName,
    string? Vendor,
    string? Category,
    bool Bypassed,
    string? FilePath)
{
    /// <summary>Ships with Wave Link, so a restore on a new machine always finds it.</summary>
    public bool IsBuiltIn => FilePath is null;

    /// <summary>What to show: the user's own name for it when there is one.</summary>
    public string DisplayName => CustomName ?? Name;
}

/// <summary>
/// One input channel, as the settings file describes it.
/// </summary>
/// <param name="DeviceType">
/// The file's <c>WaveDeviceType</c>: <c>Wave3</c> for Elgato hardware, <c>NoWaveDevice</c> for
/// everything else (an application, a virtual channel, another interface). Kept verbatim for the
/// same reason as <see cref="EffectDetail.Category"/>.
/// </param>
/// <param name="Mixes">
/// The friendly names of the mixes this channel feeds, resolved from its <c>MixerIds</c> through
/// <c>MixSettings</c>. EMPTY IS MEANINGFUL: a channel routed nowhere is audible nowhere, which
/// is a thing a rig can be in without anything looking wrong in the list.
/// </param>
/// <param name="HiddenFromMixes">The channel exists but is hidden in Wave Link's own mixer view.</param>
public sealed record ChannelDetail(
    string Name,
    string? DeviceType,
    bool HiddenFromMixes,
    IReadOnlyList<string> Mixes,
    IReadOnlyList<EffectDetail> Effects)
{
    /// <summary>Routed to nothing. See <see cref="Mixes"/>.</summary>
    public bool IsInNoMix => Mixes.Count == 0;
}

/// <summary>A device a mix plays out of.</summary>
/// <param name="FriendlyName">
/// Windows' own label - "Headphones (Elgato Wave:3)" - which is the string the user recognises.
/// Null when the file carries only the short name.
/// </param>
public sealed record OutputDetail(string Name, string? FriendlyName, string? DeviceType)
{
    public string DisplayName => FriendlyName ?? Name;
}

/// <summary>
/// One of Wave Link's mixes, and what it plays out of.
/// </summary>
/// <param name="Outputs">
/// Empty for a mix with no device attached, which is normal: on a stock rig only the monitor mix
/// carries a hardware output and the rest are consumed by the stream software over the virtual
/// device. Empty is therefore not an error and is not drawn as one.
/// </param>
public sealed record MixDetail(
    string Name,
    bool IsMuted,
    IReadOnlyList<OutputDetail> Outputs);

/// <summary>
/// The whole of a configuration, in the shape a person reads it: channels with their effect
/// chains, the mixes, and where the mixes go.
///
/// Separate from <see cref="SettingsAnalysisResult"/> on purpose. That record answers questions
/// the APP asks on every capture - is this healthy, what changed, which plug-ins must be copied -
/// and is computed for every snapshot written. This one answers a question a PERSON asks about
/// one snapshot at a time, is an order of magnitude larger, and is read on demand.
/// </summary>
public sealed record ConfigurationDetail(
    IReadOnlyList<ChannelDetail> Channels,
    IReadOnlyList<MixDetail> Mixes,
    OutputDetail? MainOutput)
{
    public int EffectCount => Channels.Sum(c => c.Effects.Count);

    public int ChannelsWithEffectsCount => Channels.Count(c => c.Effects.Count > 0);

    /// <summary>
    /// Bytes in, records out - the same rule as <see cref="SettingsAnalysis"/>, and for the same
    /// reason: a type that cannot touch the file system cannot damage a settings file by accident.
    ///
    /// TOLERANT PER FIELD, deliberately. Every property below is missing on some real file: an
    /// older Wave Link, a channel added by a beta, a key Elgato renamed. A detail view that
    /// refuses to open because one channel has no <c>WaveDeviceType</c> is worse than one that
    /// shows the channel with its type blank - so only a file that is not a settings file at all
    /// fails, and it fails with the same <see cref="MalformedSettings"/> the rest of Core uses.
    /// </summary>
    public static Result<ConfigurationDetail> Read(ReadOnlySpan<byte> utf8Json)
    {
        if (utf8Json.IsEmpty) return new MalformedSettings("the file is empty");

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(utf8Json.ToArray());
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException)
        {
            return new MalformedSettings(ex.Message);
        }

        using (document)
        {
            if (!TryGetObject(document.RootElement, "MixerConfiguration", out var mixer))
            {
                return new MalformedSettings("expected a MixerConfiguration object");
            }

            if (!TryGetObject(mixer, "InputSettings", out var inputs))
            {
                return new MalformedSettings(
                    "expected MixerConfiguration.InputSettings to be a JSON object");
            }

            _ = TryGetObject(mixer, "MixSettings", out var mixes);

            return new ConfigurationDetail(
                ReadChannels(inputs, mixes),
                ReadMixes(mixes),
                TryGetObject(mixer, "MainOutputDeviceSettings", out var main) ? ReadOutput(main) : null);
        }
    }

    private static IReadOnlyList<ChannelDetail> ReadChannels(JsonElement inputs, JsonElement mixes)
    {
        var channels = new List<ChannelDetail>();

        foreach (var input in inputs.EnumerateObject())
        {
            if (input.Value.ValueKind != JsonValueKind.Object)
            {
                // The key is the Core Audio endpoint id. Ugly, but losing the channel entirely
                // would under-report the rig - the same call SettingsAnalysis.ReadName makes.
                channels.Add(new ChannelDetail(input.Name, null, false, [], []));
                continue;
            }

            channels.Add(new ChannelDetail(
                Name: Text(input.Value, "InputName") ?? input.Name,
                DeviceType: Text(input.Value, "WaveDeviceType"),
                HiddenFromMixes: Flag(input.Value, "IsHiddenFromMixes"),
                Mixes: ReadChannelMixes(input.Value, mixes),
                Effects: ReadEffects(input.Value)));
        }

        return channels;
    }

    /// <summary>
    /// A channel's <c>MixerIds</c> are mix ids; the names live in <c>MixSettings</c>. An id with
    /// no entry there keeps the id, rather than being dropped: the channel IS routed somewhere,
    /// and "PCM_IN_01_V_04_SD3" at least matches what the file says.
    /// </summary>
    private static IReadOnlyList<string> ReadChannelMixes(JsonElement input, JsonElement mixes)
    {
        if (!input.TryGetProperty("MixerIds", out var ids) || ids.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var names = new List<string>();

        foreach (var id in ids.EnumerateArray())
        {
            if (id.ValueKind != JsonValueKind.String) continue;

            var key = id.GetString()!;

            names.Add(
                mixes.ValueKind == JsonValueKind.Object
                && mixes.TryGetProperty(key, out var mix)
                && Text(mix, "Name") is { } name
                    ? name
                    : key);
        }

        return names;
    }

    private static IReadOnlyList<EffectDetail> ReadEffects(JsonElement input)
    {
        if (!input.TryGetProperty("AudioPluginConfigurations", out var effects)
            || effects.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var chain = new List<EffectDetail>();
        var position = 0;

        foreach (var effect in effects.EnumerateArray())
        {
            if (effect.ValueKind != JsonValueKind.Object) continue;

            position++;

            chain.Add(new EffectDetail(
                Position: position,
                // A plug-in with neither a name nor a path is still a slot in the chain, and
                // saying so beats silently shortening a chain the user can count in Wave Link.
                Name: Text(effect, "Name") ?? "Unnamed effect",
                CustomName: Text(effect, "CustomName"),
                Vendor: Text(effect, "Vendor"),
                Category: Text(effect, "Category"),
                Bypassed: Flag(effect, "BypassState"),
                FilePath: Text(effect, "FilePath")));
        }

        return chain;
    }

    private static IReadOnlyList<MixDetail> ReadMixes(JsonElement mixes)
    {
        if (mixes.ValueKind != JsonValueKind.Object) return [];

        var all = new List<MixDetail>();

        foreach (var mix in mixes.EnumerateObject())
        {
            if (mix.Value.ValueKind != JsonValueKind.Object) continue;

            var outputs = new List<OutputDetail>();

            if (mix.Value.TryGetProperty("OutputDevices", out var devices)
                && devices.ValueKind == JsonValueKind.Array)
            {
                foreach (var device in devices.EnumerateArray())
                {
                    if (device.ValueKind != JsonValueKind.Object) continue;

                    if (ReadOutput(device) is { } output) outputs.Add(output);
                }
            }

            all.Add(new MixDetail(
                Name: Text(mix.Value, "Name") ?? mix.Name,
                IsMuted: Flag(mix.Value, "IsMuted"),
                Outputs: outputs));
        }

        return all;
    }

    private static OutputDetail? ReadOutput(JsonElement device)
    {
        var name = Text(device, "Name");
        var friendly = Text(device, "FriendlyName");

        if (name is null && friendly is null) return null;

        return new OutputDetail(
            Name: name ?? friendly!,
            FriendlyName: friendly,
            DeviceType: Text(device, "DeviceType") ?? Text(device, "AudioDeviceType"));
    }

    private static string? Text(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.String
        && !string.IsNullOrWhiteSpace(value.GetString())
            ? value.GetString()
            : null;

    private static bool Flag(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.True;

    private static bool TryGetObject(JsonElement parent, string name, out JsonElement value)
    {
        value = default;

        return parent.ValueKind == JsonValueKind.Object
            && parent.TryGetProperty(name, out value)
            && value.ValueKind == JsonValueKind.Object;
    }
}
