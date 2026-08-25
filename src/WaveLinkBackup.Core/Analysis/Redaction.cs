using System.Text.RegularExpressions;

namespace WaveLinkBackup.Core.Analysis;

/// <summary>
/// Removes the two things in this app's data that identify a PERSON or their HARDWARE, so a
/// diagnostic can be pasted into a public issue tracker.
///
/// The threat is not an attacker, it is helpfulness. Users attach whatever the app gives them
/// to a bug report, and they will not think about it. By the time anyone does, it is in a public
/// tracker with a permanent URL. So the rule here is that a redactor is only useful if it is the
/// ONLY way diagnostics leave the app, and if it fails closed: an ID this cannot parse is masked
/// wholesale rather than passed through in the hope that it is harmless.
///
/// Two things, named by SPEC.md §11's privacy note and technical-debt.md §6:
///
///   1. Hardware serial numbers, inside Core Audio endpoint IDs. `InputSettings` is keyed by
///      them, and the leading segment IS the device's serial:
///      <c>BS33J1A05009\PCM_IN_01_C_00_SD1</c>.
///   2. The Windows username, inside every absolute path the app records: settings paths,
///      plug-in paths, the store path, the log path.
///
/// PURE, and in Core, because the CLI and the shell must not each grow their own version of a
/// rule this consequential.
/// </summary>
public static partial class Redaction
{
    /// <summary>What a redacted value reads as. Visible, so nobody mistakes it for the real thing.</summary>
    public const string Mask = "[redacted]";

    /// <summary>
    /// A Core Audio endpoint ID with its serial removed:
    /// <c>BS33J1A05009\PCM_IN_01_C_00_SD1</c> → <c>[redacted]\PCM_IN_01_C_00_SD1</c>.
    ///
    /// The tail is KEPT on purpose. It says which physical port the channel is on, which is the
    /// part a support conversation is actually about, and it identifies nothing. Every Wave:3 on
    /// earth has the same one.
    ///
    /// An ID with no separator is masked ENTIRELY. It is not a shape this understands, and
    /// guessing that an unknown string is safe is the failure this type exists to prevent.
    /// </summary>
    public static string EndpointId(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return string.Empty;

        var separator = id.IndexOf('\\');

        return separator > 0 ? Mask + id[separator..] : Mask;
    }

    /// <summary>
    /// A path with the user's own name taken out of it:
    /// <c>C:\Users\joran\AppData\Local\…</c> → <c>C:\Users\[redacted]\AppData\Local\…</c>.
    ///
    /// The SHAPE is kept, drive, profile root, and everything below it, because "which folder"
    /// is the whole diagnostic value of a path, and it is the segment naming the person that
    /// carries none of it.
    ///
    /// <paramref name="userName"/> is replaced wherever it appears as well, which catches the
    /// cases the profile-root rule cannot: a store on <c>D:\joran-backups\</c>, a redirected
    /// Documents folder, a plug-in installed under a home-made path.
    /// </summary>
    public static string Path(string? path, string? userName = null)
    {
        if (string.IsNullOrWhiteSpace(path)) return string.Empty;

        // The profile root, whatever it is called on this Windows: \Users\<name>\ and the
        // localised equivalents keep the same shape, so the segment AFTER the root is the name.
        var redacted = ProfileSegment().Replace(path, $"$1{Mask}$3");

        if (!string.IsNullOrWhiteSpace(userName))
        {
            redacted = redacted.Replace(userName, Mask, StringComparison.OrdinalIgnoreCase);
        }

        return redacted;
    }

    /// <summary>
    /// Every input name, endpoint ID and path in one go, for a diagnostic that quotes a settings
    /// file's structure without quoting the file.
    ///
    /// Input names are NOT redacted. "Wave Mic 1", "Voice", "Browser" are what the user calls
    /// their own channels; they are the subject of nearly every support question, and they name a
    /// setup rather than a person. If someone has put their own name in a channel label, that is a
    /// thing they typed and can see.
    /// </summary>
    public static string Text(string? text, string? userName = null)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;

        var redacted = EndpointIdInText().Replace(text, match => EndpointId(match.Value));

        return Path(redacted, userName);
    }

    /// <summary>The current user's name, or empty when it cannot be read.</summary>
    public static string CurrentUserName
    {
        get
        {
            try
            {
                return Environment.UserName ?? string.Empty;
            }
            catch (InvalidOperationException)
            {
                return string.Empty;
            }
        }
    }

    /// <summary>
    /// <c>(prefix)(the name)(the rest)</c> for a Windows profile path. Case-insensitive: a path
    /// recorded as <c>c:\users\…</c> is the same path.
    /// </summary>
    [GeneratedRegex(@"([A-Za-z]:\\Users\\)([^\\]+)(\\|$)", RegexOptions.IgnoreCase)]
    private static partial Regex ProfileSegment();

    /// <summary>
    /// A Core Audio endpoint ID embedded in a longer string: a serial-looking run of capitals and
    /// digits, a backslash, then the port descriptor.
    /// </summary>
    [GeneratedRegex(@"\b[A-Z0-9]{6,}\\(?:PCM|SPDIF|WAVE)[A-Z0-9_]*", RegexOptions.IgnoreCase)]
    private static partial Regex EndpointIdInText();
}
