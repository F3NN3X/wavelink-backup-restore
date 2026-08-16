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

// ---------------------------------------------------------------------------------------
// Snapshot store (phase 2)
// ---------------------------------------------------------------------------------------

/// <summary>manifest.json could not be understood at all.</summary>
public sealed record MalformedManifest(string Detail)
    : CoreError($"This backup's manifest is unreadable: {Detail}");

/// <summary>
/// Written by a newer version of Wave Link Backup. Rejected outright rather than partially
/// read - understanding some fields of a format we do not know is how a store gets quietly
/// corrupted by an older build.
/// </summary>
public sealed record UnsupportedSnapshotSchema(int Found, int Supported)
    : CoreError($"This backup was made by a newer version of Wave Link Backup " +
                $"(format {Found}; this version understands {Supported}). Update to restore it.");

/// <summary>
/// The directory is not a snapshot we wrote. Replaces upstream's filename regex: the
/// assertion is about contents, not about a name, so custom names and locations stay
/// possible while a mistyped path still cannot write arbitrary bytes into a config file.
/// </summary>
public sealed record NotASnapshot(string Path, string Reason)
    : CoreError($"That folder is not a Wave Link Backup: {Reason}");

/// <summary>
/// The snapshot's recorded hashes no longer match its files - corrupted after it was
/// written, by a failed sync, a bad disk, or an edit. Something the filename regex could
/// never have caught.
/// </summary>
public sealed record SnapshotCorrupted(string Path, string Detail)
    : CoreError($"This backup is damaged and was not restored: {Detail}");

/// <summary>No snapshot with that id in the store.</summary>
public sealed record SnapshotNotFound(string Id)
    : CoreError($"No backup with id '{Id}' was found.");

/// <summary>The store directory could not be created, read or written.</summary>
public sealed record StoreUnavailable(string Path, string Reason)
    : CoreError($"The backup folder could not be used: {Reason}");
