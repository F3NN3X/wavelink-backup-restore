using WaveLinkBackup.Core.Abstractions;
using WaveLinkBackup.Core.Analysis;
using WaveLinkBackup.Core.Discovery;
using WaveLinkBackup.Core.Results;

namespace WaveLinkBackup.Core.Io;

/// <param name="Bytes">
/// The source bytes, verbatim. What a capture stores - never a re-serialized version.
/// </param>
/// <param name="Plugins">
/// The third-party plugins the settings reference, with version and uniqueId attached from
/// the plugin cache where it had them. Empty for a rig running nothing but Elgato built-ins,
/// and empty - not absent - when the cache could not be read, which is a version left unknown
/// rather than a plugin left out (ADR-006).
/// </param>
public sealed record SettingsInspection(
    SettingsLocation Location,
    byte[] Bytes,
    SettingsAnalysisResult Analysis,
    IReadOnlyList<ResolvedPlugin> Plugins);

/// <summary>
/// Locate, read, analyse - the composition callers actually use, so that no caller has to
/// unwrap three results by hand.
/// </summary>
/// <param name="pluginCache">
/// Null skips the cross-reference entirely, leaving every referenced plugin's version
/// unknown. Optional rather than required because the cache only ENRICHES what the settings
/// already say: the settings file's FilePath is the authority on what is in use, and a
/// capture taken without a cache is a real capture with fewer known versions, not a broken
/// one. <see cref="For"/> always supplies one.
/// </param>
public sealed class SettingsInspector(
    SettingsLocator locator, SettingsReader reader, PluginCacheReader? pluginCache = null)
{
    /// <summary>
    /// Builds one against a given LocalAppData. Named rather than a constructor overload so
    /// that the environment dependency is visible at every call site — see
    /// <see cref="SettingsLocator.SystemLocalAppData"/> for why that matters.
    /// </summary>
    public static SettingsInspector For(IFileSystem fileSystem, string localAppDataPath) =>
        new(new SettingsLocator(fileSystem, localAppDataPath),
            new SettingsReader(fileSystem),
            new PluginCacheReader(fileSystem));

    public Result<SettingsInspection> Inspect(string? explicitSettingsPath = null)
    {
        var location = locator.Locate(explicitSettingsPath);
        if (!location.IsSuccess) return location.Propagate<SettingsInspection>();

        var read = ReadAndAnalyse(location.Value.SettingsPath);
        if (!read.IsSuccess) return read.Propagate<SettingsInspection>();

        return new SettingsInspection(
            location.Value,
            read.Value.Bytes,
            read.Value.Analysis,
            PluginReferences.Resolve(
                read.Value.Analysis.ReferencedPlugins,
                pluginCache?.Read(location.Value) ?? []));
    }

    private Result<(byte[] Bytes, SettingsAnalysisResult Analysis)> ReadAndAnalyse(string path)
    {
        // Retry ONCE, and only on a parse failure.
        //
        // A single read is not atomic against Wave Link's own save, so a capture taken
        // mid-write can catch a torn file - that is a retry, not a broken config. A read
        // that fails outright is NOT retried: the lock is Wave Link's steady state, not a
        // window, and a retry loop would turn an immediate clearly-worded failure into a
        // slow one reported as a timeout.
        for (var attempt = 1; ; attempt++)
        {
            var read = reader.Read(path);
            if (!read.IsSuccess) return read.Propagate<(byte[], SettingsAnalysisResult)>();

            var analysis = SettingsAnalysis.Analyse(read.Value);
            if (analysis.IsSuccess) return (read.Value, analysis.Value);

            if (attempt == 2) return analysis.Propagate<(byte[], SettingsAnalysisResult)>();
        }
    }
}
