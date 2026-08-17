using WaveLinkBackup.App.ViewModels;

namespace WaveLinkBackup.App.Tests;

/// <summary>
/// Every mono readout on screen 1. The expected strings are README's and 02's own sample data,
/// so a change here is a change to the design rather than to a format string.
/// </summary>
public sealed class ReadableTests
{
    // README: "12.1 MB · 4 days ago", "471 KB", "118 GB FREE", "43 KB".
    [Theory]
    [InlineData(0, "0 B")]
    [InlineData(512, "512 B")]
    [InlineData(1024, "1 KB")]
    [InlineData(44032, "43 KB")]
    [InlineData(482304, "471 KB")]
    [InlineData(12687769, "12.1 MB")]
    [InlineData(75091968, "71.6 MB")]
    [InlineData(126701535232, "118 GB")]
    public void Bytes_read_the_way_the_design_writes_them(long bytes, string expected)
    {
        Assert.Equal(expected, Readable.Bytes(bytes));
    }

    // KB never carries a decimal; MB and GB carry one until they reach three digits, where the
    // decimal is noise. 118.0 GB in a status strip is a number pretending to be a measurement.
    [Fact]
    public void Three_digit_figures_drop_the_decimal()
    {
        Assert.Equal("118 GB", Readable.Bytes(126701535232));
        Assert.Equal("99.6 MB", Readable.Bytes(104438169));
    }

    [Theory]
    [InlineData(0, "just now")]
    [InlineData(45, "just now")]
    [InlineData(60 * 5, "5 minutes ago")]
    [InlineData(60 * 60, "an hour ago")]
    [InlineData(60 * 60 * 5, "5 hours ago")]
    [InlineData(60 * 60 * 24, "yesterday")]
    [InlineData(60 * 60 * 24 * 4, "4 days ago")]
    [InlineData(60 * 60 * 24 * 30, "a month ago")]
    public void Relative_time_is_a_sentence_fragment_not_a_timestamp(int secondsAgo, string expected)
    {
        var now = new DateTimeOffset(2026, 8, 15, 23, 7, 0, TimeSpan.Zero);

        Assert.Equal(expected, Readable.RelativeTime(now.AddSeconds(-secondsAgo), now));
    }

    // README's date-group headers: TODAY, TUE 11 AUG, TUE 4 AUG.
    [Fact]
    public void Day_groups_name_today_and_yesterday_and_otherwise_use_the_weekday()
    {
        var now = new DateTimeOffset(2026, 8, 15, 23, 7, 0, TimeSpan.Zero);

        Assert.Equal("TODAY", Readable.DayGroup(now, now));
        Assert.Equal("YESTERDAY", Readable.DayGroup(now.AddDays(-1), now));
        Assert.Equal("TUE 11 AUG", Readable.DayGroup(now.AddDays(-4), now));
        Assert.Equal("TUE 4 AUG", Readable.DayGroup(now.AddDays(-11), now));
    }

    [Fact]
    public void The_taken_column_is_a_time_over_a_date()
    {
        var at = new DateTimeOffset(2026, 8, 11, 21, 36, 0, TimeSpan.Zero);

        Assert.Equal("21:36", Readable.TimeOfDay(at));
        Assert.Equal("11 AUG", Readable.ShortDate(at));
    }

    // "Label is the input name shortened to fit: MIC 1 · VOICE · BROWSER · GAME · SYSTEM" -
    // README, and "WAVE:3" for Elgato Wave:3 in the collapsed case. One leading brand word goes;
    // the rest is the user's.
    [Theory]
    [InlineData("Wave Mic 1", "MIC 1")]
    [InlineData("Voice", "VOICE")]
    [InlineData("Browser", "BROWSER")]
    [InlineData("Game", "GAME")]
    [InlineData("System", "SYSTEM")]
    [InlineData("Elgato Wave:3", "WAVE:3")]
    public void Slot_labels_are_the_design_s_own(string inputName, string expected)
    {
        Assert.Equal(expected, Readable.SlotLabel(inputName));
    }

    [Fact]
    public void A_name_that_is_only_a_brand_word_keeps_it()
    {
        Assert.Equal("WAVE", Readable.SlotLabel("Wave"));
        Assert.Equal("ELGATO", Readable.SlotLabel("Elgato"));
    }

    [Fact]
    public void Only_one_leading_brand_word_is_dropped()
    {
        Assert.Equal("WAVE MIC 1", Readable.SlotLabel("Elgato Wave Mic 1"));
    }

    [Fact]
    public void A_very_long_name_is_truncated_rather_than_overflowing_its_cell()
    {
        var label = Readable.SlotLabel("Podcast Guest Return Feed");

        Assert.True(label.Length <= 10, label);
        Assert.EndsWith("…", label, StringComparison.Ordinal);
    }

    [Fact]
    public void An_empty_name_reads_as_the_missing_dash()
    {
        Assert.Equal("—", Readable.SlotLabel(""));
        Assert.Equal("—", Readable.SlotLabel("   "));
    }
}
