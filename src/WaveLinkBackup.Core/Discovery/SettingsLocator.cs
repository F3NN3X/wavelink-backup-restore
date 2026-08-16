using WaveLinkBackup.Core.Abstractions;
using WaveLinkBackup.Core.Results;

namespace WaveLinkBackup.Core.Discovery;

/// <summary>
/// Finds Wave Link's settings file, and - more importantly - does not find the decoy.
///
/// %APPDATA%\Elgato\WaveLink exists, looks authoritative, and is dead: nine months stale as
/// of 2026-08-15. Wave Link is an MSIX package, so its writes are redirected into
/// LocalState. A tool that resolves by vendor folder silently protects nothing.
///
/// Note this class NEVER looks at %APPDATA%. It only knows about Packages, which is what
/// makes the decoy unreachable rather than merely deprioritised.
/// See _docs/knowledge-base/gotchas/backup-succeeds-but-protects-nothing.md
/// </summary>
public sealed class SettingsLocator(IFileSystem fileSystem, string localAppDataPath)
{
    private const string PackageGlob = "Elgato.WaveLink_*";

    /// <summary>
    /// The real machine's LocalAppData. Call this at the composition root and pass the result
    /// in; there is deliberately NO constructor that reads it for you.
    ///
    /// There used to be, and it cost two debugging sessions: a convenience overload that
    /// silently reaches into the environment looks identical at the call site to one that does
    /// not, so tests wired against a fake filesystem quietly consulted the developer's real
    /// machine and reported "Wave Link not found". Making the dependency explicit makes the
    /// mistake unrepresentable.
    ///
    /// Uses GetFolderPath rather than a composed string - %LOCALAPPDATA% is redirected on some
    /// corporate and OneDrive setups.
    /// </summary>
    public static string SystemLocalAppData =>
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

    /// <param name="explicitSettingsPath">
    /// Bypasses discovery entirely when supplied. Diverges from upstream, which requires the
    /// override to match a discovered candidate - bypassing is the only thing that helps a
    /// user whose install we cannot find, including the possible non-MSIX case
    /// (technical-debt.md 2.2).
    /// </param>
    public Result<SettingsLocation> Locate(string? explicitSettingsPath = null)
    {
        if (explicitSettingsPath is not null) return LocateExplicit(explicitSettingsPath);

        var packages = Path.Combine(localAppDataPath, "Packages");

        var candidates = fileSystem
            .EnumerateDirectories(packages, PackageGlob)
            .Select(FromPackageDirectory)
            // Requiring Settings.json to exist is what disqualifies a stale or partial
            // package directory left behind by an uninstall.
            .Where(location => fileSystem.FileExists(location.SettingsPath))
            .ToArray();

        return candidates.Length switch
        {
            0 => new WaveLinkNotInstalled(),
            1 => candidates[0],
            // Never guess. Picking one silently would protect the wrong installation
            // while reporting success.
            _ => new MultiplePackagesFound([.. candidates.Select(c => c.SettingsPath)]),
        };
    }

    private Result<SettingsLocation> LocateExplicit(string path)
    {
        if (!fileSystem.FileExists(path))
        {
            return new SettingsUnreadable(path, "the file does not exist");
        }

        var localState = Path.GetDirectoryName(path) ?? string.Empty;

        // If the explicit path happens to sit inside a package, recover the family name so
        // relaunch still works. If not, CanRelaunch is false and callers say so.
        var packageDirectory = Path.GetFileName(Path.GetDirectoryName(localState) ?? string.Empty);
        var family = packageDirectory.StartsWith("Elgato.WaveLink_", StringComparison.OrdinalIgnoreCase)
            ? packageDirectory
            : string.Empty;

        return new SettingsLocation(path, family, localState, Path.Combine(localState, "Logs"));
    }

    private static SettingsLocation FromPackageDirectory(string packageDirectory)
    {
        var localState = Path.Combine(packageDirectory, "LocalState");

        return new SettingsLocation(
            SettingsPath: Path.Combine(localState, "Settings.json"),
            PackageFamilyName: Path.GetFileName(packageDirectory),
            LocalStatePath: localState,
            LogsPath: Path.Combine(localState, "Logs"));
    }
}
