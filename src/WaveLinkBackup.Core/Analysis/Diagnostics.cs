using System.Text;
using WaveLinkBackup.Core.Abstractions;
using WaveLinkBackup.Core.Automation;
using WaveLinkBackup.Core.Io;
using WaveLinkBackup.Core.Snapshots;

namespace WaveLinkBackup.Core.Analysis;

/// <summary>
/// What the app knows about itself, in a form that can be pasted into a public issue tracker.
///
/// This exists because the alternative is a user attaching their settings file, which carries
/// hardware serial numbers and their Windows username, into a tracker with a permanent URL
/// (SPEC.md §11, technical-debt.md §6). Giving them something better to paste is the only version
/// of this that works — telling people not to attach things does not.
///
/// Two rules, and they are the whole design:
///
///   1. Everything goes through <see cref="Redaction"/>. Not "paths do" — everything, including
///      strings this composes itself, because the next field somebody adds will be composed the
///      same way and will not think about it.
///   2. Nothing is ever sent anywhere. This returns a string. The caller puts it on the
///      clipboard. There is no upload, no telemetry and no opt-in that would create one.
///
/// It quotes STRUCTURE, never content: how many inputs, what they are called, which tiers a
/// snapshot claims, what the versions are. The settings file itself is never included, redacted or
/// otherwise — a redacted copy of a file is still a copy of a file, and the whole point is that
/// nobody needs one to answer a support question.
/// </summary>
public static class Diagnostics
{
    /// <param name="appVersion">The running build. Passed in: Core does not know which shell it is under.</param>
    /// <param name="snapshots">Newest first, as the store lists them. Only the newest few are described.</param>
    public static string Report(
        string appVersion,
        BackupSettings settings,
        SettingsInspection? live,
        IReadOnlyList<Snapshot> snapshots,
        DateTimeOffset now,
        string? userName = null,
        IReadOnlyList<AudioEndpoint>? endpoints = null) =>
        string.Join(
            Environment.NewLine,
            Lines(appVersion, settings, live, snapshots, now, userName, endpoints));

    /// <summary>
    /// The report as lines. The shape the CLI wants — its output seam writes one line at a time —
    /// and it spares every caller a split on a separator it would have to guess.
    /// </summary>
    public static IReadOnlyList<string> Lines(
        string appVersion,
        BackupSettings settings,
        SettingsInspection? live,
        IReadOnlyList<Snapshot> snapshots,
        DateTimeOffset now,
        string? userName = null,
        IReadOnlyList<AudioEndpoint>? endpoints = null)
    {
        var user = userName ?? Redaction.CurrentUserName;
        var report = new StringBuilder();

        void Line(string label, string? value) =>
            report.Append(label).Append(": ").AppendLine(Redaction.Text(value ?? "unknown", user));

        report.AppendLine("Wave Link Backup — diagnostics");
        report.AppendLine("Serial numbers and your Windows user name have been removed.");
        report.AppendLine();

        Line("Taken", now.ToString("u", System.Globalization.CultureInfo.InvariantCulture));
        Line("App", appVersion);
        Line("Windows", Environment.OSVersion.VersionString);
        report.AppendLine();

        report.AppendLine("Settings");
        Line("  Backup folder", settings.StorePath);
        Line("  Automatic backups", settings.AutoBackupEnabled ? "on" : "off");
        Line("  Keep", $"{settings.AutoBackupKeepCount} automatic backups");
        Line("  At most one every", $"{settings.AutoBackupIntervalMinutes} minutes");
        Line("  Daily backup", settings.DailyBackupAt?.ToString("HH:mm") ?? "off");
        Line("  Presets included", settings.IncludePresets ? "yes" : "no");
        Line("  Plug-in files included", settings.IncludePluginFiles ? "yes" : "no");
        Line("  Chosen Wave Link", settings.ChosenWaveLinkPath ?? "discovered automatically");
        report.AppendLine();

        report.AppendLine("Wave Link");
        if (live is null)
        {
            report.AppendLine("  Not found, or its settings could not be read.");
        }
        else
        {
            Line("  Version", live.Analysis.WaveLinkVersion);
            Line("  Settings file", live.Location.SettingsPath);
            Line("  Settings size", $"{live.Bytes.LongLength} bytes");
            Line("  Inputs", $"{live.Analysis.Fingerprint.InputCount}");

            // Input NAMES are kept: they are what the user calls their own channels, they are the
            // subject of nearly every support question, and they identify a setup rather than a
            // person. Redaction.Text still runs over them, so a path typed into a channel label
            // does not slip through.
            Line("  Input names", string.Join(", ", live.Analysis.Fingerprint.InputNames));
            Line("  Effects", $"{live.Analysis.Fingerprint.EffectCount} on {live.Analysis.Fingerprint.EffectChannelCount} channels");
            Line("  Duplicate keys", live.Analysis.Report.HasCaseInsensitiveDuplicateKeys ? "YES" : "no");
            Line("  Plug-ins referenced", $"{live.Plugins.Count}");
        }
        report.AppendLine();

        // COUNTS ONLY, and this is not a style choice. An endpoint id embeds a device serial
        // (technical-debt.md 3) and a friendly name is the hardware a person owns; neither belongs
        // in a file whose whole purpose is being safe to paste into a public tracker. How many
        // capture endpoints are active, and how many are dead, is the fact a support question
        // actually turns on - it is what separates "the input is gone" from "the input is fine".
        if (endpoints is not null)
        {
            report.AppendLine("Audio endpoints");

            if (endpoints.Count == 0)
            {
                report.AppendLine("  None reported. The audio service may not be running.");
            }
            else
            {
                foreach (var direction in new[] { EndpointDirection.Capture, EndpointDirection.Render })
                {
                    var inDirection = endpoints.Where(e => e.Direction == direction).ToArray();
                    if (inDirection.Length == 0) continue;

                    var byState = inDirection
                        .GroupBy(e => e.State)
                        .OrderBy(g => g.Key)
                        .Select(g => $"{g.Count()} {g.Key.ToString().ToLowerInvariant()}");

                    Line($"  {direction}", string.Join(", ", byState));
                }
            }

            report.AppendLine();
        }

        report.AppendLine("Backups");
        Line("  Count", $"{snapshots.Count}");
        Line("  Total size", $"{snapshots.Sum(s => s.Manifest.TotalSizeBytes)} bytes");

        foreach (var snapshot in snapshots.Take(NewestDescribed))
        {
            var manifest = snapshot.Manifest;

            // The DISPLAY NAME is left out. It is the one free-text field in a snapshot and people
            // put anything in it — "before Dave's session", a client's name. Nothing in a support
            // conversation needs it, and its absence costs the report nothing.
            Line(
                "  •",
                $"{manifest.CreatedUtc:u} · {manifest.Trigger} · {manifest.InputCount} inputs · "
                + $"{manifest.TotalSizeBytes} bytes · tiers {string.Join("+", manifest.Tiers)}"
                + (manifest.IsSuspect ? " · SUSPECT" : string.Empty));
        }

        // Normalised, so a caller writing one line at a time gets the same lines whatever
        // StringBuilder decided AppendLine meant on this platform.
        return report.ToString()
            .ReplaceLineEndings("\n")
            .TrimEnd('\n')
            .Split('\n');
    }

    /// <summary>
    /// How many snapshots are described individually. Enough to show a pattern — a run of
    /// automatics, a pre-restore, the gap where the watcher stopped — without turning a paste
    /// into a wall.
    /// </summary>
    public const int NewestDescribed = 10;
}
