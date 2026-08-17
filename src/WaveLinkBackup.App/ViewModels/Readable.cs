using System.Globalization;

namespace WaveLinkBackup.App.ViewModels;

/// <summary>
/// Every mono readout on screen 1, as pure functions.
///
/// Here rather than in a converter because these ARE the design - "12.1 MB · 4 days ago" is a
/// specified string, not a formatting preference - and a rule in a converter is a rule nobody
/// can assert.
/// </summary>
public static class Readable
{
    private static readonly string[] Units = ["B", "KB", "MB", "GB", "TB"];

    /// <summary>
    /// 471 KB · 12.1 MB · 118 GB, matching README and 02 exactly.
    ///
    /// Bytes and KB never carry a decimal, and a three-digit figure drops it too: "118.0 GB" in
    /// a status strip is a number pretending to be a measurement.
    /// </summary>
    public static string Bytes(long bytes)
    {
        if (bytes < 0) bytes = 0;

        var unit = 0;
        double value = bytes;

        while (value >= 1024 && unit < Units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        var decimals = unit >= 2 && value < 100 ? 1 : 0;

        return string.Create(
            CultureInfo.CurrentCulture, $"{Math.Round(value, decimals)} {Units[unit]}");
    }

    /// <summary>
    /// The row's meta line: "4 days ago". A fragment, lowercase, because it sits after a size in
    /// a sentence-shaped readout rather than standing alone as a label.
    /// </summary>
    public static string RelativeTime(DateTimeOffset at, DateTimeOffset now)
    {
        var elapsed = now - at;

        if (elapsed < TimeSpan.FromMinutes(1)) return "just now";

        if (elapsed < TimeSpan.FromHours(1))
        {
            var minutes = (int)elapsed.TotalMinutes;
            return minutes == 1 ? "a minute ago" : $"{minutes} minutes ago";
        }

        if (elapsed < TimeSpan.FromDays(1))
        {
            var hours = (int)elapsed.TotalHours;
            return hours == 1 ? "an hour ago" : $"{hours} hours ago";
        }

        var days = (int)elapsed.TotalDays;

        if (days == 1) return "yesterday";
        if (days < 30) return $"{days} days ago";

        var months = days / 30;

        if (months == 1) return "a month ago";
        if (months < 12) return $"{months} months ago";

        var years = days / 365;

        return years <= 1 ? "a year ago" : $"{years} years ago";
    }

    /// <summary>
    /// The date-group header: TODAY, YESTERDAY, TUE 11 AUG.
    ///
    /// README shows TODAY and the weekday form; YESTERDAY is this app's own, and it matches the
    /// tray readout's qualifier so the two never disagree about the same backup.
    /// </summary>
    public static string DayGroup(DateTimeOffset at, DateTimeOffset now)
    {
        var day = at.Date;
        var today = now.Date;

        if (day == today) return "TODAY";
        if (day == today.AddDays(-1)) return "YESTERDAY";

        return Upper(at.ToString("ddd d MMM", CultureInfo.CurrentCulture));
    }

    /// <summary>The TAKEN column's upper line.</summary>
    public static string TimeOfDay(DateTimeOffset at) => at.ToString("HH:mm", CultureInfo.CurrentCulture);

    /// <summary>The TAKEN column's lower line, and the bottom bar's selected readout.</summary>
    public static string ShortDate(DateTimeOffset at) =>
        Upper(at.ToString("d MMM", CultureInfo.CurrentCulture));

    /// <summary>
    /// An input name shortened to fit a 57px slot: MIC 1 · VOICE · BROWSER · GAME · SYSTEM, and
    /// WAVE:3 for "Elgato Wave:3".
    ///
    /// ONE leading brand word is dropped, never two - "Elgato Wave Mic 1" is still a Wave device
    /// and losing that would make two different inputs read identically.
    /// </summary>
    public static string SlotLabel(string inputName)
    {
        if (string.IsNullOrWhiteSpace(inputName)) return "—";

        var name = inputName.Trim();

        foreach (var brand in (string[])["Elgato ", "Wave "])
        {
            if (!name.StartsWith(brand, StringComparison.OrdinalIgnoreCase)) continue;

            var rest = name[brand.Length..].Trim();
            if (rest.Length > 0) name = rest;

            break;
        }

        name = Upper(name);

        return name.Length <= 10 ? name : name[..9] + "…";
    }

    private static string Upper(string value) => value.ToUpper(CultureInfo.CurrentCulture);
}
