using WaveLinkBackup.Core.Abstractions;
using WaveLinkBackup.Core.Snapshots;

namespace WaveLinkBackup.Core.Tests;

public sealed class SnapshotIdTests
{
    private static readonly DateTimeOffset Aug15 = new(2026, 8, 15, 23, 7, 11, TimeSpan.Zero);

    [Fact]
    public void An_id_is_a_timestamp_and_a_short_hash()
    {
        Assert.Equal("2026-08-15T2307-a3f81c", SnapshotId.Create(Aug15, "a3f81cdeadbeef"));
    }

    [Fact]
    public void Local_times_are_normalised_to_utc_so_ids_sort_consistently()
    {
        var sameInstantInOslo = new DateTimeOffset(2026, 8, 16, 1, 7, 11, TimeSpan.FromHours(2));

        Assert.Equal(SnapshotId.Create(Aug15, "a3f81c"), SnapshotId.Create(sameInstantInOslo, "a3f81c"));
    }

    [Fact]
    public void A_short_hash_is_used_whole_rather_than_throwing()
    {
        Assert.Equal("2026-08-15T2307-abc", SnapshotId.Create(Aug15, "abc"));
        Assert.Equal("2026-08-15T2307-", SnapshotId.Create(Aug15, ""));
    }

    [Fact]
    public void Suffixes_disambiguate_a_collision()
    {
        Assert.Equal("2026-08-15T2307-a3f81c-2", SnapshotId.WithSuffix("2026-08-15T2307-a3f81c", 2));
    }

    [Theory]
    [InlineData("2026-08-15T2307-a3f81c", true)]
    [InlineData("2026-08-15T2307-a3f81c-2", true)]
    [InlineData("random-folder", false)]
    [InlineData("2026_08_15T2307-a3f81c", false)]
    [InlineData("2026-08-15X2307-a3f81c", false)]
    [InlineData("short", false)]
    [InlineData("", false)]
    public void Obviously_unrelated_directory_names_are_recognised_as_such(string name, bool looksLike)
    {
        // Only a cheap filter for listing. SnapshotGuard verifying the manifest and its
        // hashes is what actually protects a restore.
        Assert.Equal(looksLike, SnapshotId.LooksLikeSnapshotId(name));
    }

    [Fact]
    public void Ids_from_successive_minutes_sort_chronologically_as_text()
    {
        var earlier = SnapshotId.Create(Aug15, "aaaaaa");
        var later = SnapshotId.Create(Aug15.AddMinutes(1), "aaaaaa");

        Assert.True(string.CompareOrdinal(earlier, later) < 0);
    }

    [Fact]
    public void The_system_clock_reports_utc_and_moves_forward()
    {
        var clock = new SystemClock();
        var first = clock.UtcNow;

        Assert.Equal(TimeSpan.Zero, first.Offset);
        Assert.True(clock.UtcNow >= first);
        Assert.InRange(first, DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddMinutes(1));
    }
}
