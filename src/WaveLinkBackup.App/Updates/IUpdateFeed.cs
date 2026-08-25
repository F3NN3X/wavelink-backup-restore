using System.IO;
using System.Net.Http;
using System.Text.Json;

namespace WaveLinkBackup.App.Updates;

/// <summary>
/// Where releases come from. A seam for the same reason <c>IFileSystem</c> is one: the alternative
/// is a test suite that either reaches the network or does not test this at all.
/// </summary>
public interface IUpdateFeed
{
    Task<UpdateCheck> CheckAsync(Version current, CancellationToken ct);
}

/// <summary>
/// GitHub's <c>releases/latest</c>, read with <see cref="JsonDocument"/>.
///
/// Nothing here throws. A failed check is one of the four designed outcomes, not an error the
/// app has to handle: no network, a rate limit, a repository that has published no release yet and
/// a feed whose shape changed all arrive as <see cref="UpdateCheckResult.CheckFailed"/> with a
/// mono line saying which.
///
/// Hand-parsed rather than deserialised into a type, matching every other JSON reader in this
/// codebase — reflection-based serialization is what would close off NativeAOT ([[ADR-004]],
/// technical-debt.md §2.4), and a source-scan guard fails the build on it.
/// </summary>
public sealed class GitHubReleaseFeed(UpdateSource source, HttpClient http) : IUpdateFeed
{
    /// <summary>
    /// GitHub refuses a request with no User-Agent, with a 403 that reads like a rate limit. Worth
    /// stating once here rather than rediscovering it from a support thread.
    /// </summary>
    public const string UserAgent = "WaveLinkBackup";

    public async Task<UpdateCheck> CheckAsync(Version current, CancellationToken ct)
    {
        if (!source.IsConfigured) return UpdateCheck.Failed("NO RELEASE FEED IS CONFIGURED");

        string body;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, source.LatestReleaseApiUrl);
            request.Headers.Add("User-Agent", UserAgent);
            request.Headers.Add("Accept", "application/vnd.github+json");

            using var response = await http.SendAsync(request, ct).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                return UpdateCheck.Failed(
                    $"COULDN'T REACH THE RELEASE FEED · HTTP {(int)response.StatusCode}");
            }

            body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
        {
            return UpdateCheck.Failed("COULDN'T REACH THE RELEASE FEED · NO CONNECTION");
        }

        return Read(body, current);
    }

    /// <summary>
    /// The parse, separated from the fetch so it can be tested against real GitHub payloads
    /// without a network. Public for that reason and no other.
    /// </summary>
    public UpdateCheck Read(string json, Version current)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException)
        {
            return UpdateCheck.Failed("THE RELEASE FEED COULDN'T BE READ");
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return UpdateCheck.Failed("THE RELEASE FEED COULDN'T BE READ");
            }

            if (ReleaseVersion.Parse(String(root, "tag_name") ?? String(root, "name")) is not { } version)
            {
                return UpdateCheck.Failed("THE NEWEST RELEASE HAS NO VERSION WE CAN READ");
            }

            // Older or equal is the ordinary answer, and it is reported before the assets are
            // examined: a release we are not going to install need not be well-formed.
            if (version <= current) return UpdateCheck.UpToDate;

            if (!root.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
            {
                return UpdateCheck.Failed($"RELEASE {ReleaseVersion.Display(version)} HAS NO DOWNLOADS");
            }

            // Collected first, PAIRED second. This used to be a single pass that took any asset
            // ending ".sha256" as the checksum, last one winning - which was right while a release
            // carried one archive, and silently wrong from 0.7.2, when §8.5 split the CLI into its
            // own artifact. A release then has two archives and two checksums, so the app's zip was
            // being verified against the CLI's digest and every update failed its checksum. The
            // download and its digest have to be matched BY NAME, not by shape.
            var found = new List<(string Name, string Url, long Size)>();

            foreach (var asset in assets.EnumerateArray())
            {
                if (asset.ValueKind != JsonValueKind.Object) continue;
                if (String(asset, "name") is not { } name) continue;
                if (String(asset, "browser_download_url") is not { } url) continue;

                found.Add((name, url, Number(asset, "size")));
            }

            var download = found.FirstOrDefault(
                a => a.Name.EndsWith(source.AssetSuffix, StringComparison.OrdinalIgnoreCase));

            if (download.Url is null)
            {
                return UpdateCheck.Failed(
                    $"RELEASE {ReleaseVersion.Display(version)} HAS NO {source.AssetSuffix.ToUpperInvariant()}");
            }

            var downloadUrl = download.Url;
            var size = download.Size;

            // "<the archive we are downloading>.sha256", and nothing else. A checksum belonging to
            // some other asset is worse than none: it turns every update into a failure that reads
            // like a corrupted download. The checksum's own URL only - the file is fetched when
            // the download starts, so a check still costs one request.
            var sha256 = found
                .FirstOrDefault(a => a.Name.Equals(
                    download.Name + ".sha256", StringComparison.OrdinalIgnoreCase))
                .Url;

            return UpdateCheck.Available(new UpdateRelease(
                version,
                Timestamp(root, "published_at"),
                downloadUrl,
                size,
                sha256,
                String(root, "html_url") ?? source.ReleasesUrl));
        }
    }

    private static string? String(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.String
        && !string.IsNullOrWhiteSpace(value.GetString())
            ? value.GetString()
            : null;

    private static long Number(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.Number
        && value.TryGetInt64(out var number)
            ? number
            : 0;

    private static DateTimeOffset? Timestamp(JsonElement element, string name) =>
        String(element, name) is { } text
        && DateTimeOffset.TryParse(
            text,
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.RoundtripKind,
            out var parsed)
            ? parsed
            : null;
}
