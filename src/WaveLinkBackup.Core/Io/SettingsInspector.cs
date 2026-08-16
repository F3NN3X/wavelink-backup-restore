using WaveLinkBackup.Core.Abstractions;
using WaveLinkBackup.Core.Analysis;
using WaveLinkBackup.Core.Discovery;
using WaveLinkBackup.Core.Results;

namespace WaveLinkBackup.Core.Io;

/// <param name="Bytes">
/// The source bytes, verbatim. What a capture stores - never a re-serialized version.
/// </param>
public sealed record SettingsInspection(
    SettingsLocation Location,
    byte[] Bytes,
    SettingsAnalysisResult Analysis);

/// <summary>
/// Locate, read, analyse - the composition callers actually use, so that no caller has to
/// unwrap three results by hand.
/// </summary>
public sealed class SettingsInspector(SettingsLocator locator, SettingsReader reader)
{
    public SettingsInspector(IFileSystem fileSystem)
        : this(new SettingsLocator(fileSystem), new SettingsReader(fileSystem))
    {
    }

    public Result<SettingsInspection> Inspect(string? explicitSettingsPath = null)
    {
        var location = locator.Locate(explicitSettingsPath);
        if (!location.IsSuccess) return location.Propagate<SettingsInspection>();

        var read = ReadAndAnalyse(location.Value.SettingsPath);
        if (!read.IsSuccess) return read.Propagate<SettingsInspection>();

        return new SettingsInspection(location.Value, read.Value.Bytes, read.Value.Analysis);
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
