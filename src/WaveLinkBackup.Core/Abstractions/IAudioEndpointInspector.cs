namespace WaveLinkBackup.Core.Abstractions;

/// <summary>Whether a rendering or capture endpoint is usable right now.</summary>
public enum EndpointState
{
    /// <summary>Present and usable.</summary>
    Active,

    /// <summary>The device exists but the user turned it off in Sound settings.</summary>
    Disabled,

    /// <summary>The driver is installed but the hardware is unplugged.</summary>
    NotPresent,

    /// <summary>Present, but another application holds it exclusively.</summary>
    Unplugged,

    /// <summary>The state word was something this enum does not name.</summary>
    Unknown,
}

/// <summary>Which direction an endpoint carries audio.</summary>
public enum EndpointDirection
{
    /// <summary>A playback device.</summary>
    Render,

    /// <summary>A recording device - which is what a Wave Link input channel points at.</summary>
    Capture,
}

/// <param name="Id">
/// The Core Audio endpoint id, and the same string Wave Link uses as a channel key in
/// <c>MixerConfiguration.InputSettings</c>. THIS IS WHY SNAPSHOTS ARE MACHINE-LOCAL: it embeds a
/// device serial (technical-debt.md 3).
/// </param>
public sealed record AudioEndpoint(
    string Id,
    string FriendlyName,
    EndpointDirection Direction,
    EndpointState State);

/// <summary>
/// Reads the machine's live audio endpoints.
///
/// <para>
/// This is what tells <em>"the input is dead"</em> from <em>"the input is fine"</em>: a channel key
/// in a snapshot is an endpoint id, and whether that id is Active on this machine right now is a
/// fact only the audio stack has. Nothing in a settings file can answer it.
/// </para>
///
/// <para>
/// READ-ONLY, deliberately. Repointing a dead channel at a working device is an EDITING feature and
/// is out of 1.0 (see <c>dev-phases/post-1.0.md</c>): SPEC section 3 warns that rewriting a device
/// id means walking the whole tree and rewriting both the bare and <c>id|suffix</c> forms, and
/// handling a destination key that already exists. Enumeration carries none of that risk and
/// answers the question technical-debt.md 2.4 asks on its own.
/// </para>
/// </summary>
public interface IAudioEndpointInspector
{
    /// <summary>
    /// Every endpoint the audio stack knows about, in whatever order it reports them.
    ///
    /// <para>
    /// Returns an empty list rather than throwing when the audio service is not running, which is
    /// a real state on a machine with no sound hardware and on some server SKUs. A caller asking
    /// "is this channel's device alive" gets "no" either way, and an exception here would take
    /// down a capture that has nothing to do with endpoints.
    /// </para>
    /// </summary>
    IReadOnlyList<AudioEndpoint> List();
}
