using WaveLinkBackup.Core.Results;

namespace WaveLinkBackup.Cli.CommandLine;

/// <summary>
/// Exit codes, because scripts are a real consumer and "non-zero" is not enough to branch on.
///
/// Mapped from the <see cref="CoreError"/> hierarchy, which was built for exactly this: each
/// expected failure is a distinct type, so each gets a distinct code without anyone parsing
/// message text.
///
/// The names here deliberately do NOT match the error type names. They collided when they did,
/// because a switch arm cannot tell a constant from a type of the same name — and the compiler
/// error for that is unhelpful enough to be worth avoiding by construction.
/// </summary>
public static class ExitCode
{
    public const int Success = 0;

    /// <summary>Something failed that has no more specific code.</summary>
    public const int Failure = 1;

    public const int NotInstalled = 2;
    public const int MultiplePackages = 3;
    public const int Unreadable = 4;
    public const int StillRunning = 5;
    public const int NotFound = 6;
    public const int Damaged = 7;
    public const int StoreFailed = 8;

    /// <summary>The user declined a confirmation. Not an error - nothing was attempted.</summary>
    public const int Declined = 9;

    /// <summary>Bad arguments. 64 is EX_USAGE from sysexits.h, which scripts already know.</summary>
    public const int Usage = 64;

    public static int For(CoreError error) => error switch
    {
        WaveLinkNotInstalled => NotInstalled,
        MultiplePackagesFound => MultiplePackages,
        SettingsUnreadable or MalformedSettings => Unreadable,
        WaveLinkStillRunning => StillRunning,
        SnapshotNotFound => NotFound,
        NotASnapshot or SnapshotCorrupted or MalformedManifest or UnsupportedSnapshotSchema => Damaged,
        StoreUnavailable => StoreFailed,
        _ => Failure,
    };
}
