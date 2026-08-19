using WaveLinkBackup.Core.Abstractions;
using WaveLinkBackup.Core.Analysis;
using WaveLinkBackup.Core.Discovery;

namespace WaveLinkBackup.Core.Io;

/// <summary>
/// Puts <see cref="PluginCache"/> behind the filesystem seam.
///
/// Returns a list rather than a <c>Result</c> on purpose. Tier 2 is always on (ADR-006), so
/// every outcome here has to be survivable: the cache is absent on a rig with no third-party
/// plugins, it is locked while Wave Link rescans, and it is rebuilt from a scan rather than
/// authored, so a truncated one says nothing about whether the settings are sound. Each of
/// those degrades to "no versions known", which the manifest records honestly as unknown.
/// </summary>
public sealed class PluginCacheReader(IFileSystem fileSystem)
{
    public IReadOnlyList<CachedPlugin> Read(SettingsLocation location) =>
        Read(location.PluginCachePath);

    public IReadOnlyList<CachedPlugin> Read(string path)
    {
        if (!fileSystem.FileExists(path)) return [];

        try
        {
            return PluginCache.Parse(fileSystem.ReadSharedText(path));
        }
        catch (IOException)
        {
            return [];
        }
        catch (UnauthorizedAccessException)
        {
            return [];
        }
    }
}
