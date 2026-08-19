using System.Text;
using WaveLinkBackup.Core.Results;
using WaveLinkBackup.Core.Snapshots;

namespace WaveLinkBackup.Core.Tests;

/// <summary>
/// Field-by-field rejection. manifest.json is written by us, so a malformed one means it was
/// edited, truncated by a failed sync, or produced by a build we do not know about - and in
/// every one of those cases the right answer is a clear refusal, not a partial read.
/// </summary>
public sealed class ManifestFieldTests
{
    private const string Complete = """
        {
          "schemaVersion": 1,
          "displayName": "x",
          "notes": "",
          "createdUtc": "2026-08-15T23:07:11.0000000+00:00",
          "trigger": "manual",
          "settingsSha256": "a3f81c",
          "waveLinkVersion": "3.3.0.4108",
          "inputCount": 5,
          "inputNames": ["a"],
          "effectCount": 1,
          "effectChannelCount": 1,
          "hasDuplicateKeys": false,
          "tiers": ["settings"],
          "files": { "settings.json": { "sha256": "a3f81c", "sizeBytes": 43052 } }
        }
        """;

    private static Result<SnapshotManifest> Read(string json) =>
        ManifestSerializer.Read(Encoding.UTF8.GetBytes(json));

    private static Result<SnapshotManifest> Without(string field) =>
        Read(RemoveLineContaining(Complete, $"\"{field}\""));

    private static string RemoveLineContaining(string json, string needle) =>
        string.Join('\n', json.Split('\n').Where(l => !l.Contains(needle, StringComparison.Ordinal)));

    [Fact]
    public void The_complete_manifest_reads()
    {
        Assert.True(Read(Complete).IsSuccess);
    }

    [Theory]
    [InlineData("displayName")]
    [InlineData("createdUtc")]
    [InlineData("trigger")]
    [InlineData("settingsSha256")]
    [InlineData("inputCount")]
    [InlineData("inputNames")]
    [InlineData("effectCount")]
    [InlineData("effectChannelCount")]
    [InlineData("hasDuplicateKeys")]
    [InlineData("tiers")]
    [InlineData("files")]
    public void Every_required_field_is_actually_required(string field)
    {
        Assert.IsType<MalformedManifest>(Without(field).Error);
    }

    [Theory]
    [InlineData("notes")]
    [InlineData("waveLinkVersion")]
    public void Optional_fields_are_actually_optional(string field)
    {
        Assert.True(Without(field).IsSuccess);
    }

    [Fact]
    public void A_missing_schema_version_is_rejected_before_anything_else_is_read()
    {
        var error = Assert.IsType<MalformedManifest>(Without("schemaVersion").Error);
        Assert.Contains("schemaVersion", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_unknown_trigger_is_rejected_rather_than_defaulted()
    {
        // Defaulting would silently turn a pre-restore snapshot into a prunable one.
        var error = Assert.IsType<MalformedManifest>(
            Read(Complete.Replace("\"manual\"", "\"whatever\"", StringComparison.Ordinal)).Error);

        Assert.Contains("whatever", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_unparseable_timestamp_is_rejected()
    {
        Assert.IsType<MalformedManifest>(
            Read(Complete.Replace("2026-08-15T23:07:11.0000000+00:00", "yesterday", StringComparison.Ordinal)).Error);
    }

    [Fact]
    public void A_files_entry_missing_its_hash_is_rejected()
    {
        Assert.IsType<MalformedManifest>(
            Read(Complete.Replace("\"sha256\": \"a3f81c\", ", "", StringComparison.Ordinal)).Error);
    }

    [Fact]
    public void A_json_array_at_the_root_is_not_a_manifest()
    {
        Assert.IsType<MalformedManifest>(Read("[1,2,3]").Error);
    }

    [Fact]
    public void Wrong_types_are_rejected_rather_than_coerced()
    {
        Assert.IsType<MalformedManifest>(
            Read(Complete.Replace("\"inputCount\": 5", "\"inputCount\": \"five\"", StringComparison.Ordinal)).Error);

        Assert.IsType<MalformedManifest>(
            Read(Complete.Replace("\"inputNames\": [\"a\"]", "\"inputNames\": \"a\"", StringComparison.Ordinal)).Error);
    }

    [Fact]
    public void An_older_schema_version_is_accepted()
    {
        // Forward-only rejection: we can read what we used to write.
        Assert.True(Read(Complete.Replace("\"schemaVersion\": 1", "\"schemaVersion\": 0", StringComparison.Ordinal)).IsSuccess);
    }

    [Fact]
    public void Suspect_and_prunable_are_derived_from_the_stored_fields()
    {
        var suspect = Read(Complete.Replace("\"hasDuplicateKeys\": false", "\"hasDuplicateKeys\": true", StringComparison.Ordinal)).Value;
        Assert.True(suspect.IsSuspect);
        Assert.False(suspect.IsPrunable);

        var automatic = Read(Complete.Replace("\"manual\"", "\"automatic\"", StringComparison.Ordinal)).Value;
        Assert.True(automatic.IsPrunable);

        var preRestore = Read(Complete.Replace("\"manual\"", "\"preRestore\"", StringComparison.Ordinal)).Value;
        Assert.False(preRestore.IsPrunable);
    }

    [Fact]
    public void Manifests_compare_by_value_including_their_collections()
    {
        var a = Read(Complete).Value;
        var b = Read(Complete).Value;

        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
        Assert.NotEqual(a, a with { DisplayName = "different" });
        Assert.NotEqual(a, a with { InputNames = ["a", "b"] });
        Assert.NotEqual(a, a with { Tiers = [] });
        Assert.NotEqual(a, a with { Files = new Dictionary<string, SnapshotFile>() });
        Assert.False(a.Equals(null));
    }

    // ------------------------------------------- total size (technical-debt.md §4.11)

    /// <summary>
    /// One place, so the five callers that used to re-derive it cannot drift apart. It counts
    /// EVERY file, not the settings file: a snapshot's weight is what it occupies.
    /// </summary>
    [Fact]
    public void TotalSizeBytes_adds_up_every_file_the_snapshot_holds()
    {
        var manifest = Read(Complete).Value with
        {
            Files = new Dictionary<string, SnapshotFile>
            {
                ["settings.json"] = new("aa", 43_000),
                ["plugins.json"] = new("bb", 1_200),
                ["presets/appdata/FabFilter/one.ffp"] = new("cc", 800),
            },
        };

        Assert.Equal(45_000, manifest.TotalSizeBytes);
    }

    [Fact]
    public void TotalSizeBytes_is_zero_for_a_manifest_holding_nothing()
    {
        Assert.Equal(0, (Read(Complete).Value with { Files = new Dictionary<string, SnapshotFile>() }).TotalSizeBytes);
    }
}
