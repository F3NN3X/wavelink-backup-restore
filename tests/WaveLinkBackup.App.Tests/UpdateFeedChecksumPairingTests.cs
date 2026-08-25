using System.Net.Http;
using WaveLinkBackup.App.Updates;

namespace WaveLinkBackup.App.Tests;

/// <summary>
/// The checksum must belong to the archive being downloaded.
///
/// <para>
/// <b>This is the shape of a REAL release, and the reason the bug survived.</b> Every payload in
/// <see cref="UpdateFeedTests"/> carries one archive and one <c>.sha256</c>, so "take any asset
/// ending .sha256" was indistinguishable from "take the right one". Since 0.7.2 a release has
/// carried two archives and two checksums — the app and the CLI — and the single-pass loop kept
/// the LAST checksum it saw, which is the CLI's. Every attempted update downloaded the app and
/// verified it against the CLI's digest.
/// </para>
///
/// <para>
/// It failed as a checksum error, which reads exactly like a corrupted download — so the one
/// symptom pointed away from the cause. Nothing in CI could catch it: the release workflow
/// publishes correct checksums for both archives, and it is the pairing in the client that was
/// wrong.
/// </para>
/// </summary>
public sealed class UpdateFeedChecksumPairingTests
{
    private static readonly UpdateSource Source = new("f3nn3x", "wavelink-backup-restore");

    private static GitHubReleaseFeed Feed() => new(Source, new HttpClient());

    private const string AppZip = "WaveLinkBackup-0.7.5-app-win-x64.zip";
    private const string CliZip = "WaveLinkBackup-CLI-0.7.5-win-x64.zip";

    private static string Asset(string name, string url, long size = 1000) => $$"""
        { "name": "{{name}}", "size": {{size}}, "browser_download_url": "{{url}}" }
        """;

    /// <param name="assets">In the order GitHub returns them.</param>
    private static string Release(params string[] assets) => $$"""
        {
          "tag_name": "v0.7.5",
          "name": "v0.7.5",
          "html_url": "https://github.com/f3nn3x/wavelink-backup-restore/releases/tag/v0.7.5",
          "published_at": "2026-08-25T12:00:00Z",
          "assets": [ {{string.Join(",", assets)}} ]
        }
        """;

    private static UpdateRelease Parse(string json)
    {
        var check = Feed().Read(json, new Version(0, 7, 4));

        Assert.Equal(UpdateCheckResult.UpdateAvailable, check.Result);
        Assert.NotNull(check.Release);
        return check.Release;
    }

    [Fact]
    public void A_release_with_two_archives_pairs_each_with_its_own_checksum()
    {
        // The exact asset list, in the exact order, that v0.7.5 publishes.
        var release = Parse(Release(
            Asset(AppZip, "https://example.invalid/app.zip", 8_061_810),
            Asset($"{AppZip}.sha256", "https://example.invalid/app.zip.sha256", 98),
            Asset(CliZip, "https://example.invalid/cli.zip", 377_000),
            Asset($"{CliZip}.sha256", "https://example.invalid/cli.zip.sha256", 94)));

        Assert.Equal("https://example.invalid/app.zip", release.DownloadUrl);
        Assert.Equal("https://example.invalid/app.zip.sha256", release.Sha256);
    }

    [Fact]
    public void The_order_the_assets_arrive_in_does_not_decide_the_answer()
    {
        // GitHub does not promise an order, and the old loop's answer depended on one.
        var release = Parse(Release(
            Asset($"{CliZip}.sha256", "https://example.invalid/cli.zip.sha256", 94),
            Asset(CliZip, "https://example.invalid/cli.zip", 377_000),
            Asset($"{AppZip}.sha256", "https://example.invalid/app.zip.sha256", 98),
            Asset(AppZip, "https://example.invalid/app.zip", 8_061_810)));

        Assert.Equal("https://example.invalid/app.zip", release.DownloadUrl);
        Assert.Equal("https://example.invalid/app.zip.sha256", release.Sha256);
    }

    [Fact]
    public void A_checksum_belonging_to_something_else_is_no_checksum_at_all()
    {
        // Not "use it anyway". A digest for a different file fails every time and reads as a
        // corrupted download; no checksum at least reports the honest thing, and UpdateDownloader
        // already refuses to install an archive it cannot verify.
        var release = Parse(Release(
            Asset(AppZip, "https://example.invalid/app.zip", 8_061_810),
            Asset($"{CliZip}.sha256", "https://example.invalid/cli.zip.sha256", 94)));

        Assert.Equal("https://example.invalid/app.zip", release.DownloadUrl);
        Assert.Null(release.Sha256);
    }

    [Fact]
    public void The_size_reported_is_the_archives_own()
    {
        // The progress bar reads this. Taking it from whichever asset happened to be last would
        // make the download appear to finish at 4,700% or stall at 2%.
        var release = Parse(Release(
            Asset(AppZip, "https://example.invalid/app.zip", 8_061_810),
            Asset($"{AppZip}.sha256", "https://example.invalid/app.zip.sha256", 98),
            Asset(CliZip, "https://example.invalid/cli.zip", 377_000),
            Asset($"{CliZip}.sha256", "https://example.invalid/cli.zip.sha256", 94)));

        Assert.Equal(8_061_810, release.SizeBytes);
    }
}
