using System.IO;
using System.Net.Http;

namespace WaveLinkBackup.App.Updates;

/// <param name="Path">Where the verified archive landed, or null when it did not.</param>
/// <param name="FailureDetail">
/// The mono line under the failed-update block. Null on success. A failed download is NEUTRAL:
/// The tray and updates spec: "a failed update leaves a working app, so nothing is un-whole."
/// </param>
public sealed record UpdateDownload(string? Path, string? FailureDetail)
{
    public bool Succeeded => Path is not null;

    public static UpdateDownload Failed(string detail) => new(null, detail);
}

/// <summary>
/// Fetches a release archive and refuses to hand it back unless its bytes match the checksum the
/// release published.
///
/// Refusing is the point. An update that installs whatever arrived replaces a working program
/// with an unknown one, and this is the single most dangerous thing this app does. It is the only
/// code path that overwrites its own binaries. So: an update with no published checksum is
/// refused, a mismatch is refused, and a partial file is deleted rather than left where a later
/// run might find it.
///
/// What the checksum does and does not prove is spelt out on <see cref="UpdateRelease.Sha256"/>:
/// integrity, not authenticity. Signing is what would give the second, and it is owed before this
/// app is distributed to anyone.
/// </summary>
public sealed class UpdateDownloader(HttpClient http)
{
    /// <summary>
    /// Downloads to <paramref name="directory"/>, hashing as it streams. The archive is tens of
    /// megabytes and there is no reason for it to be resident.
    /// </summary>
    public async Task<UpdateDownload> DownloadAsync(
        UpdateRelease release,
        string directory,
        IProgress<double>? progress,
        CancellationToken ct)
    {
        if (release.Sha256 is not { Length: > 0 } checksumUrl)
        {
            return UpdateDownload.Failed("THE RELEASE PUBLISHED NO CHECKSUM · NOTHING WAS INSTALLED");
        }

        var expected = await ReadChecksumAsync(checksumUrl, ct).ConfigureAwait(false);
        if (expected is null)
        {
            return UpdateDownload.Failed("THE RELEASE'S CHECKSUM COULDN'T BE READ · NOTHING WAS INSTALLED");
        }

        var path = Path.Combine(directory, $"WaveLinkBackup-{ReleaseVersion.Display(release.Version)}.zip");

        try
        {
            Directory.CreateDirectory(directory);

            var actual = await StreamToFileAsync(release, path, progress, ct).ConfigureAwait(false);

            if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
            {
                // Deleted, not kept: a file that failed its checksum must not be sitting there for
                // a later run, or a person, to find and trust.
                Delete(path);
                return UpdateDownload.Failed("THE DOWNLOAD DIDN'T MATCH ITS CHECKSUM · NOTHING WAS INSTALLED");
            }

            return new UpdateDownload(path, null);
        }
        catch (OperationCanceledException)
        {
            Delete(path);
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or UnauthorizedAccessException)
        {
            Delete(path);
            return UpdateDownload.Failed($"THE DOWNLOAD FAILED · {ex.Message.ToUpperInvariant()}");
        }
    }

    private async Task<string> StreamToFileAsync(
        UpdateRelease release, string path, IProgress<double>? progress, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, release.DownloadUrl);
        request.Headers.Add("User-Agent", GitHubReleaseFeed.UserAgent);

        using var response = await http
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct)
            .ConfigureAwait(false);

        response.EnsureSuccessStatusCode();

        var total = response.Content.Headers.ContentLength ?? release.SizeBytes;

        using var source = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var destination = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        using var hash = System.Security.Cryptography.IncrementalHash.CreateHash(
            System.Security.Cryptography.HashAlgorithmName.SHA256);

        var buffer = new byte[1024 * 128];
        long written = 0;

        for (int read; (read = await source.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0;)
        {
            hash.AppendData(buffer, 0, read);
            await destination.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);

            written += read;
            if (total > 0) progress?.Report(Math.Clamp((double)written / total, 0, 1));
        }

        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    /// <summary>
    /// The published <c>.sha256</c> file. Both common shapes read: a bare hex digest, and
    /// <c>&lt;digest&gt;  &lt;filename&gt;</c> as <c>sha256sum</c> writes it.
    /// </summary>
    private async Task<string?> ReadChecksumAsync(string url, CancellationToken ct)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("User-Agent", GitHubReleaseFeed.UserAgent);

            using var response = await http.SendAsync(request, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return null;

            var text = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            return ParseChecksum(text);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException)
        {
            return null;
        }
    }

    /// <summary>Pure, so the two shapes are covered by a test rather than by a comment.</summary>
    public static string? ParseChecksum(string? text)
    {
        var first = text?
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault();

        var digest = first?.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();

        return digest is { Length: 64 } && digest.All(Uri.IsHexDigit) ? digest.ToLowerInvariant() : null;
    }

    private static void Delete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Nothing to say. A leftover file in the temp directory is a smaller problem than the
            // one being reported, and reporting two failures at once helps nobody.
        }
    }
}
