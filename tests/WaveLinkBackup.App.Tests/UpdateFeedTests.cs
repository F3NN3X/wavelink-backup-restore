using System.Net.Http;
using WaveLinkBackup.App.Updates;

namespace WaveLinkBackup.App.Tests;

/// <summary>
/// The release feed's parse, against payloads in GitHub's real shape. The fetch is separated from
/// the parse so this needs no network — the whole reason <c>Read</c> is public.
///
/// The rule every test here is really about: **a check that cannot be understood must report that
/// it failed, never that the app is up to date.** Reporting up-to-date wrongly means the user
/// never hears about a fix, and nothing in the app would ever say otherwise.
/// </summary>
public sealed class UpdateFeedTests
{
    private static readonly UpdateSource Source = new("f3nn3x", "wavelink-backup-restore");

    private static GitHubReleaseFeed Feed() => new(Source, new HttpClient());

    private static string Release(
        string tag = "v1.4.0",
        string asset = "WaveLinkBackup-1.4.0-app-win-x64.zip",
        long size = 4_300_000,
        bool checksum = true)
    {
        var checksumAsset = checksum
            ? $$"""
              ,
              {
                "name": "{{asset}}.sha256",
                "size": 96,
                "browser_download_url": "https://example.invalid/checksum.sha256"
              }
              """
            : string.Empty;

        return $$"""
        {
          "tag_name": "{{tag}}",
          "name": "{{tag}}",
          "html_url": "https://github.com/f3nn3x/wavelink-backup-restore/releases/tag/{{tag}}",
          "published_at": "2026-08-12T09:14:00Z",
          "assets": [
            {
              "name": "{{asset}}",
              "size": {{size}},
              "browser_download_url": "https://example.invalid/{{asset}}"
            }{{checksumAsset}}
          ]
        }
        """;
    }

    [Fact]
    public void A_newer_release_is_available_with_everything_the_row_prints()
    {
        var check = Feed().Read(Release(), new Version(1, 2, 3, 0));

        Assert.Equal(UpdateCheckResult.UpdateAvailable, check.Result);
        Assert.NotNull(check.Release);
        Assert.Equal(new Version(1, 4, 0, 0), check.Release.Version);
        Assert.Equal(4_300_000, check.Release.SizeBytes);
        Assert.NotNull(check.Release.PublishedAt);
        Assert.NotNull(check.Release.Sha256);
        Assert.NotNull(check.Release.NotesUrl);
    }

    [Fact]
    public void The_same_version_is_up_to_date()
    {
        Assert.Equal(
            UpdateCheckResult.UpToDate,
            Feed().Read(Release("v1.2.3"), new Version(1, 2, 3, 0)).Result);
    }

    [Fact]
    public void An_older_release_is_up_to_date_rather_than_a_downgrade()
    {
        Assert.Equal(
            UpdateCheckResult.UpToDate,
            Feed().Read(Release("v1.0.0"), new Version(1, 2, 3, 0)).Result);
    }

    /// <summary>
    /// The expensive silent failure: a tag this cannot read makes every release look older than
    /// the running build, and the app would report itself up to date forever.
    /// </summary>
    [Fact]
    public void An_unreadable_tag_is_a_failed_check_not_an_up_to_date()
    {
        var check = Feed().Read(Release("nightly"), new Version(1, 2, 3, 0));

        Assert.Equal(UpdateCheckResult.CheckFailed, check.Result);
        Assert.NotNull(check.FailureDetail);
    }

    [Fact]
    public void A_release_with_no_matching_asset_is_a_failed_check()
    {
        var check = Feed().Read(
            Release(asset: "WaveLinkBackup-1.4.0-linux-x64.tar.gz"), new Version(1, 2, 3, 0));

        Assert.Equal(UpdateCheckResult.CheckFailed, check.Result);
    }

    /// <summary>
    /// The release carries BOTH the app and the CLI archives; the updater must pick the app's.
    /// A bare <c>win-x64.zip</c> suffix would match both — and the last one in the array wins —
    /// which is how an update would install the wrong bytes. The <c>app-</c> marker is what keeps
    /// the two apart, so this test exists to fail if it ever goes away.
    /// </summary>
    [Fact]
    public void A_release_with_both_app_and_cli_assets_picks_the_app()
    {
        var json = $$"""
        {
          "tag_name": "v1.4.0",
          "name": "v1.4.0",
          "html_url": "https://github.com/f3nn3x/wavelink-backup-restore/releases/tag/v1.4.0",
          "published_at": "2026-08-12T09:14:00Z",
          "assets": [
            {
              "name": "WaveLinkBackup-CLI-1.4.0-win-x64.zip",
              "size": 1500000,
              "browser_download_url": "https://example.invalid/cli.zip"
            },
            {
              "name": "WaveLinkBackup-CLI-1.4.0-win-x64.zip.sha256",
              "size": 96,
              "browser_download_url": "https://example.invalid/cli.sha256"
            },
            {
              "name": "WaveLinkBackup-1.4.0-app-win-x64.zip",
              "size": 4300000,
              "browser_download_url": "https://example.invalid/app.zip"
            },
            {
              "name": "WaveLinkBackup-1.4.0-app-win-x64.zip.sha256",
              "size": 96,
              "browser_download_url": "https://example.invalid/app.sha256"
            }
          ]
        }
        """;

        var check = Feed().Read(json, new Version(1, 2, 3, 0));

        Assert.Equal(UpdateCheckResult.UpdateAvailable, check.Result);
        Assert.NotNull(check.Release);
        Assert.EndsWith("app.zip", check.Release.DownloadUrl);
        Assert.Equal(4_300_000, check.Release.SizeBytes);
        Assert.EndsWith("app.sha256", check.Release.Sha256);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("{}")]
    public void Anything_unreadable_is_a_failed_check(string json)
    {
        Assert.Equal(UpdateCheckResult.CheckFailed, Feed().Read(json, new Version(1, 2, 3, 0)).Result);
    }

    // ------------------------------------------------------------------ tag parsing

    [Theory]
    [InlineData("v1.4.0", 1, 4, 0)]
    [InlineData("1.4.0", 1, 4, 0)]
    [InlineData("V1.4.0", 1, 4, 0)]
    [InlineData("1.4", 1, 4, 0)]
    [InlineData("1.4.0-beta.2", 1, 4, 0)]
    [InlineData("1.4.0+abc123", 1, 4, 0)]
    [InlineData(" v1.4.0 ", 1, 4, 0)]
    public void Every_shape_a_tag_comes_in_reads(string tag, int major, int minor, int build)
    {
        Assert.Equal(new Version(major, minor, build, 0), ReleaseVersion.Parse(tag));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("nightly")]
    [InlineData("release-candidate")]
    [InlineData("v")]
    public void A_tag_that_is_not_a_version_reads_as_null(string? tag)
    {
        Assert.Null(ReleaseVersion.Parse(tag));
    }

    /// <summary>
    /// A parsed <c>1.4.0</c> and an assembly's <c>1.4.0.0</c> have to compare EQUAL, or the app
    /// would see every release as older than itself.
    /// </summary>
    [Fact]
    public void A_three_part_tag_equals_a_four_part_assembly_version()
    {
        Assert.Equal(new Version(1, 4, 0, 0), ReleaseVersion.Parse("1.4.0"));
        Assert.Equal(0, ReleaseVersion.Current.Revision);
    }

    [Fact]
    public void A_version_is_printed_as_the_design_writes_it()
    {
        Assert.Equal("1.2.3", ReleaseVersion.Display(new Version(1, 2, 3, 0)));
    }

    // ------------------------------------------------------------------ configuration

    [Fact]
    public void An_unconfigured_source_is_not_usable_and_says_so()
    {
        var source = new UpdateSource(string.Empty, string.Empty);

        Assert.False(source.IsConfigured);
    }

    // ------------------------------------------------------------------ the checksum file

    [Theory]
    [InlineData("a3f81c0000000000000000000000000000000000000000000000000000000000")]
    [InlineData("a3f81c0000000000000000000000000000000000000000000000000000000000  app.zip")]
    [InlineData("  A3F81C0000000000000000000000000000000000000000000000000000000000\n")]
    public void Both_shapes_of_a_sha256_file_read(string text)
    {
        Assert.Equal(
            "a3f81c0000000000000000000000000000000000000000000000000000000000",
            UpdateDownloader.ParseChecksum(text));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-digest")]
    [InlineData("a3f81c")]
    [InlineData("zzz81c0000000000000000000000000000000000000000000000000000000000")]
    public void Anything_that_is_not_a_digest_reads_as_null(string? text)
    {
        Assert.Null(UpdateDownloader.ParseChecksum(text));
    }
}
