using WaveLinkBackup.Core.Abstractions;
using WaveLinkBackup.Core.Automation;
using WaveLinkBackup.Core.Io;
using WaveLinkBackup.Core.Snapshots;

namespace WaveLinkBackup.Core.Capture;

/// <summary>
/// What each tier would cost, measured rather than read. The Settings dialog prints these four
/// figures and must never hard-code them ([[ADR-006]]) — a number the user is asked to decide on
/// has to be their number.
/// </summary>
/// <param name="PresetBytes">
/// An upper bound: two plugins from one vendor that share a preset folder each count it, and it is
/// stored once. Over-estimating a tier the user is about to switch on is the safe direction.
/// </param>
public sealed record CaptureEstimate(
    long SettingsBytes,
    long WaveLinkBackupBytes,
    long PresetBytes,
    long PluginBinaryBytes)
{
    /// <summary>Tier 1 in full: the settings file plus Wave Link's own copies.</summary>
    public long TierOneBytes => SettingsBytes + WaveLinkBackupBytes;

    public static CaptureEstimate Nothing { get; } = new(0, 0, 0, 0);
}

/// <summary>
/// Gathers every tier beyond the settings file, and answers what each would cost.
///
/// This is where the tier rules live, deliberately in one class: <see cref="SnapshotStore"/> below
/// it writes bytes and knows nothing about tiers, and the shells above it render figures they were
/// handed. A tier that is claimed but empty, or captured but not claimed, is the kind of quiet
/// dishonesty that only stays fixed if one place decides it.
/// </summary>
/// <param name="appDataPath">
/// Roaming <c>%APPDATA%</c>, where tier 3 looks. Injected rather than read from the environment for
/// the same reason <see cref="Discovery.SettingsLocator.SystemLocalAppData"/> is: a test cannot
/// redirect a folder the code asks Windows for.
/// </param>
public sealed class TierCapture(IFileSystem fileSystem, string appDataPath)
{
    private readonly WaveLinkBackupFiles waveLinkBackups = new(fileSystem);
    private readonly PresetFiles presets = new(fileSystem, appDataPath);
    private readonly PluginBinaryFiles binaries = new(fileSystem);

    /// <summary>Roaming AppData, resolved through GetFolderPath — never a composed string.</summary>
    public static string SystemAppData =>
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

    public static TierCapture For(IFileSystem fileSystem) => new(fileSystem, SystemAppData);

    /// <summary>
    /// Everything a snapshot should carry beyond `Settings.json`, with the toggles applied.
    ///
    /// **Nothing here can fail a capture.** Tier 1's extras and tier 3 are best effort per file;
    /// tier 4 is all-or-nothing but its failure costs only tier 4. The settings file is the
    /// product, and no plugin, preset or stale copy of Wave Link's own is worth losing it over.
    /// </summary>
    public SnapshotPayload Gather(SettingsInspection live, BackupSettings settings)
    {
        var files = new List<CapturedFile>();
        var tiers = new List<string>();

        // ---- Tier 1's other half. Always on, like the settings file it sits beside.
        foreach (var file in waveLinkBackups.Discover(live.Location))
        {
            if (Read(file) is { } captured) files.Add(captured);
        }

        // ---- Tier 3, per plugin, so that an empty capture is visible in plugins.json rather
        //      than being a silence nobody can distinguish from "this vendor saves nothing".
        var presetsByPlugin = new Dictionary<string, PresetDiscovery>(StringComparer.OrdinalIgnoreCase);
        var capturedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (settings.IncludePresets)
        {
            foreach (var plugin in live.Plugins)
            {
                var found = presets.Discover(plugin);
                presetsByPlugin[plugin.FilePath] = found;

                foreach (var file in found.Files)
                {
                    // Two plugins from one vendor can resolve to the same folder. It is captured
                    // once; both record what their own lookup found.
                    if (!capturedPaths.Add(file.RelativePath)) continue;
                    if (Read(file) is { } captured) files.Add(captured);
                }
            }

            if (capturedPaths.Count > 0) tiers.Add(SnapshotManifest.PresetsTier);
        }

        // ---- Tier 4, all or nothing. "A plugin that cannot be captured must fail the tier, not
        //      quietly reduce it" — a snapshot claiming PLUGINS with one missing is a snapshot
        //      that cannot do the thing its badge promises.
        var captured4 = new List<CapturedFile>();
        var binaryRoots = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (settings.IncludePluginFiles && live.Plugins.Count > 0)
        {
            var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var complete = true;

            foreach (var plugin in live.Plugins)
            {
                var found = binaries.Discover(plugin, taken);
                if (!found.Found) { complete = false; break; }

                foreach (var file in found.Files)
                {
                    var bytes = Read(file);
                    if (bytes is null) { complete = false; break; }
                    captured4.Add(bytes);
                }

                if (!complete) break;

                // The relative ROOT, not the flag: restore has to map these back, and two
                // vendors can ship the same file name.
                binaryRoots[plugin.FilePath] = found.IsBundle
                    ? Root(found.Files[0].RelativePath)
                    : found.Files[0].RelativePath;
            }

            if (complete)
            {
                files.AddRange(captured4);
                tiers.Add(SnapshotManifest.PluginsTier);
            }
            else
            {
                binaryRoots.Clear();
            }
        }

        // ---- Tier 2 last: it records what the other tiers found.
        var entries = live.Plugins.Select(plugin =>
        {
            var preset = presetsByPlugin.GetValueOrDefault(plugin.FilePath, PresetDiscovery.Nothing);

            return new PluginManifestEntry(
                Name: plugin.Name,
                Vendor: plugin.Vendor,
                Version: plugin.Version,
                UniqueId: plugin.UniqueId,
                FilePath: plugin.FilePath,
                Sha256: Hash(plugin.FilePath),
                Channels: plugin.Channels,
                PresetSource: preset.Source,
                PresetFileCount: preset.Files.Count,
                PresetBytes: preset.Bytes,
                BinaryPath: binaryRoots.GetValueOrDefault(plugin.FilePath));
        });

        return new SnapshotPayload(
            new PluginManifest(PluginManifest.CurrentSchemaVersion, [.. entries]), files, tiers);
    }

    /// <summary>
    /// What a capture taken right now would cost, per tier, **ignoring the toggles** — the
    /// Settings dialog has to price a tier that is switched off, because pricing it is how the
    /// user decides.
    /// </summary>
    public CaptureEstimate Measure(SettingsInspection live) => new(
        SettingsBytes: live.Bytes.LongLength,
        WaveLinkBackupBytes: waveLinkBackups.Measure(live.Location),
        PresetBytes: live.Plugins.Sum(presets.Measure),
        PluginBinaryBytes: live.Plugins.Sum(binaries.Measure));

    /// <summary>
    /// Null for every way a read can fail. The caller decides what that costs: tier 4 gives up the
    /// whole tier, everything else drops the file.
    /// </summary>
    private CapturedFile? Read(SourceFile file)
    {
        try
        {
            return new CapturedFile(file.RelativePath, fileSystem.ReadSharedBytes(file.Path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private string? Hash(string filePath) => binaries.HashOf(filePath);

    /// <summary>
    /// `plugins/Name.vst3/Contents/x86_64-win/Name.vst3` back to `plugins/Name.vst3` - the
    /// bundle's own root, which is what a restore copies back.
    /// </summary>
    private static string Root(string relativePath)
    {
        var parts = relativePath.Split('/');
        return parts.Length >= 2 ? $"{parts[0]}/{parts[1]}" : relativePath;
    }
}
