namespace WaveLinkBackup.Core.Abstractions;

/// <summary>
/// Whether a rendering or capture endpoint is usable right now.
///
/// <para>
/// One-for-one with Core Audio's <c>DEVICE_STATE_*</c> flags, and the names are theirs. The two
/// dead states are easy to swap and mean different things to someone diagnosing a lost channel:
/// <see cref="NotPresent"/> is a missing adapter, <see cref="Unplugged"/> is a present adapter
/// with nothing in the socket. Telling a user to check their cable is right for one and useless
/// for the other.
/// </para>
/// </summary>
public enum EndpointState
{
    /// <summary>
    /// <c>DEVICE_STATE_ACTIVE</c>. Present, enabled, and available to open a stream on.
    /// </summary>
    Active,

    /// <summary>
    /// <c>DEVICE_STATE_DISABLED</c>. The user disabled the endpoint in Windows' own sound
    /// settings. The hardware is there; Windows has been told not to use it.
    /// </summary>
    Disabled,

    /// <summary>
    /// <c>DEVICE_STATE_NOTPRESENT</c>. The audio ADAPTER behind the endpoint is gone - unplugged
    /// from USB, removed, or disabled in Device Manager. Windows still remembers the endpoint,
    /// which is why it can be enumerated at all.
    /// </summary>
    NotPresent,

    /// <summary>
    /// <c>DEVICE_STATE_UNPLUGGED</c>. The adapter is present and working, but jack-presence
    /// detection says nothing is connected to the socket - a headphone jack with no headphones.
    /// Not about exclusive-mode access, which is a property of opening a stream rather than a
    /// device state.
    /// </summary>
    Unplugged,

    /// <summary>
    /// The state word was something this enum does not name, which means the
    /// <c>DEVICE_STATE_*</c> constants have drifted from the SDK rather than that the device is
    /// in an exotic state.
    /// </summary>
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
    ///
    /// <para>
    /// <b>Every returned endpoint has a non-empty <see cref="AudioEndpoint.Id"/>.</b> The id is
    /// the only field a channel key can match on, so an endpoint that will not surrender one is
    /// dropped rather than reported blank - a blank-id record reads as a real device to every
    /// caller and matches nothing. A missing <see cref="AudioEndpoint.FriendlyName"/> is not the
    /// same and does not drop the endpoint: a nameless live device still answers the question.
    /// </para>
    /// </summary>
    IReadOnlyList<AudioEndpoint> List();
}
