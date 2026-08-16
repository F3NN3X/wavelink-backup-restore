namespace WaveLinkBackup.Core.Results;

/// <summary>
/// An expected failure. Genuine faults - a broken invariant, a violated seam - throw.
/// The split matters because a GUI has to render every expected failure as a message, and
/// catch-and-hope at each UI boundary is how error handling rots.
/// </summary>
public abstract record CoreError(string Message);

/// <summary>No <c>Elgato.WaveLink_*</c> package with a Settings.json was found.</summary>
public sealed record WaveLinkNotInstalled()
    : CoreError("Wave Link was not found on this computer.");

/// <summary>
/// More than one candidate. Deliberately never guesses - picking one silently would
/// protect the wrong installation while reporting success.
/// </summary>
public sealed record MultiplePackagesFound(IReadOnlyList<string> Candidates)
    : CoreError($"Found {Candidates.Count} Wave Link installations. Choose one explicitly.");

/// <summary>The file could not be read at all - missing, or denied even in shared mode.</summary>
public sealed record SettingsUnreadable(string Path, string Reason)
    : CoreError($"Could not read the settings file: {Reason}");

/// <summary>
/// Not valid settings. Distinct from a validation *finding*: a file with duplicate keys
/// parses fine and analyses successfully, because a suspect snapshot may be the only one
/// there is. This is for a file that cannot be understood at all.
/// </summary>
public sealed record MalformedSettings(string Detail)
    : CoreError($"This settings file is malformed: {Detail}");

/// <summary>
/// Wave Link had not exited when a write was attempted. A graceful exit flushes in-memory
/// config on the way out, so a write racing it is silently overwritten seconds later.
/// See _docs/knowledge-base/gotchas/restored-settings-revert-seconds-later.md
/// </summary>
public sealed record WaveLinkStillRunning(IReadOnlyList<string> ProcessNames)
    : CoreError($"Wave Link is still running ({string.Join(", ", ProcessNames)}); nothing was written.");

/// <summary>The write itself failed. The target is unchanged - <c>File.Replace</c> is atomic.</summary>
public sealed record WriteFailed(string Reason)
    : CoreError($"The settings file could not be replaced: {Reason}");
