using System.Globalization;
using System.Text;
using WaveLinkBackup.Core.Restore;
using WaveLinkBackup.Core.Snapshots;

namespace WaveLinkBackup.Cli.Output;

/// <summary>
/// Turns Core records into text. PURE, so every output rule - including the privacy one - is
/// testable without a console.
///
/// NOTHING HERE EVER PRINTS A DEVICE ID. InputSettings keys are Core Audio endpoint IDs and
/// they embed hardware serial numbers; friendly names are both safer and what a person
/// actually recognises. technical-debt.md 6.
/// </summary>
public static class Format
{
    public static string SnapshotLine(Snapshot snapshot)
    {
        var m = snapshot.Manifest;
        var health = $"{m.InputCount} input{(m.InputCount == 1 ? "" : "s")}";
        var names = m.InputNames.Count > 0 ? string.Join(", ", m.InputNames) : "none";
        var flags = m.IsSuspect ? "  SUSPECT" : "";

        return string.Create(CultureInfo.InvariantCulture,
            $"{snapshot.Id}  {m.CreatedUtc.ToLocalTime():yyyy-MM-dd HH:mm}  {Trigger(m.Trigger),-11}  {health,-9}  {m.DisplayName}{flags}\n" +
            $"    {names}");
    }

    public static string SnapshotJson(Snapshot snapshot)
    {
        var m = snapshot.Manifest;
        var sb = new StringBuilder();

        sb.Append("{\"id\":").Append(Json(snapshot.Id));
        sb.Append(",\"name\":").Append(Json(m.DisplayName));
        sb.Append(",\"createdUtc\":").Append(Json(m.CreatedUtc.ToUniversalTime().ToString("O")));
        sb.Append(",\"trigger\":").Append(Json(Trigger(m.Trigger)));
        sb.Append(",\"inputCount\":").Append(m.InputCount.ToString(CultureInfo.InvariantCulture));
        sb.Append(",\"inputNames\":[")
          .Append(string.Join(",", m.InputNames.Select(Json)))
          .Append(']');
        sb.Append(",\"effectCount\":").Append(m.EffectCount.ToString(CultureInfo.InvariantCulture));
        sb.Append(",\"waveLinkVersion\":").Append(m.WaveLinkVersion is null ? "null" : Json(m.WaveLinkVersion));
        sb.Append(",\"suspect\":").Append(m.IsSuspect ? "true" : "false");
        sb.Append('}');

        return sb.ToString();
    }

    public static IEnumerable<string> PlanLines(RestorePlan plan)
    {
        yield return $"Restore \"{plan.SnapshotName}\", taken {plan.SnapshotTakenUtc.ToLocalTime():yyyy-MM-dd HH:mm}?";
        yield return "";
        yield return $"  {"",-16}{"NOW",-34}AFTER";

        foreach (var row in plan.Rows)
        {
            var marker = row.Changes ? "*" : " ";
            yield return $"{marker} {row.Label,-16}{Truncate(row.Now, 32),-34}{Truncate(row.After, 32)}";
        }

        if (plan.LosesInputs)
        {
            yield return "";
            yield return $"  WARNING: this loses {string.Join(", ", plan.InputNamesLost)}.";
        }

        if (plan.SnapshotIsSuspect)
        {
            yield return "  WARNING: this backup failed validation when it was taken.";
        }

        if (plan.VersionWarning is not null)
        {
            yield return $"  NOTE: {plan.VersionWarning}";
        }

        yield return "";
        yield return "Your current settings are saved as \"Before restore\" first.";
    }

    private static string Trigger(SnapshotTrigger trigger) => trigger switch
    {
        SnapshotTrigger.Manual => "manual",
        SnapshotTrigger.Automatic => "automatic",
        SnapshotTrigger.PreRestore => "pre-restore",
        _ => "unknown",
    };

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..(max - 1)] + "…";

    /// <summary>Minimal JSON string escaping. Enough for names, paths and versions.</summary>
    private static string Json(string value)
    {
        var sb = new StringBuilder("\"");
        foreach (var c in value)
        {
            sb.Append(c switch
            {
                '"' => "\\\"",
                '\\' => "\\\\",
                '\n' => "\\n",
                '\r' => "\\r",
                '\t' => "\\t",
                < ' ' => $"\\u{(int)c:x4}",
                _ => c.ToString(),
            });
        }
        return sb.Append('"').ToString();
    }
}
