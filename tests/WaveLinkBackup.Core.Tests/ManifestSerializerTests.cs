using System.Text;
using WaveLinkBackup.Core.Results;
using WaveLinkBackup.Core.Snapshots;

namespace WaveLinkBackup.Core.Tests;

/// <summary>
/// manifest.json is a compatibility surface from the first write. The store outlives the
/// application that wrote it, in a location the user chose and may sync, move, or restore
/// from a backup of their own.
/// </summary>
public sealed class ManifestSerializerTests
{
    private static SnapshotManifest Sample(string name = "Before 3.3 beta") => new(
        SchemaVersion: SnapshotManifest.CurrentSchemaVersion,
        DisplayName: name,
        Notes: "",
        CreatedUtc: new DateTimeOffset(2026, 8, 15, 23, 7, 11, TimeSpan.Zero),
        Trigger: SnapshotTrigger.Manual,
        SettingsSha256: "a3f81c",
        WaveLinkVersion: "3.3.0.4108",
        InputCount: 5,
        InputNames: ["Wave Mic 1", "Voice", "Browser", "Music", "System"],
        EffectCount: 17,
        EffectChannelCount: 4,
        HasDuplicateKeys: false,
        Tiers: ["settings"],
        Files: new Dictionary<string, SnapshotFile> { ["settings.json"] = new("a3f81c", 43052) });

    private static SnapshotManifest RoundTrip(SnapshotManifest manifest)
    {
        var result = ManifestSerializer.Read(ManifestSerializer.Write(manifest));
        Assert.True(result.IsSuccess);
        return result.Value;
    }

    [Fact]
    public void Round_trips_every_field()
    {
        Assert.Equal(Sample(), RoundTrip(Sample()));
    }

    [Theory]
    [InlineData(SnapshotTrigger.Manual)]
    [InlineData(SnapshotTrigger.Automatic)]
    [InlineData(SnapshotTrigger.PreRestore)]
    public void Round_trips_every_trigger(SnapshotTrigger trigger)
    {
        Assert.Equal(trigger, RoundTrip(Sample() with { Trigger = trigger }).Trigger);
    }

    [Fact]
    public void A_display_name_that_would_be_illegal_in_a_path_round_trips_unharmed()
    {
        // The reason the display name lives here and never in a directory name.
        const string awkward = """Mic chain 3/4" <hot> & "loud" \ trailing """;

        Assert.Equal(awkward, RoundTrip(Sample(awkward)).DisplayName);
    }

    [Fact]
    public void Names_with_non_ascii_survive_without_escaping_mangling()
    {
        Assert.Equal("Røros — mikrofon ✓", RoundTrip(Sample("Røros — mikrofon ✓")).DisplayName);
    }

    [Fact]
    public void A_future_schema_version_is_rejected_with_a_readable_message()
    {
        // Never partially read. A manifest we do not fully understand is not usable.
        var future = ManifestSerializer.Write(
            Sample() with { SchemaVersion = SnapshotManifest.CurrentSchemaVersion + 1 });

        var result = ManifestSerializer.Read(future);

        var error = Assert.IsType<UnsupportedSnapshotSchema>(result.Error);
        Assert.Contains("newer version", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_manifest_that_is_not_json_fails_rather_than_throwing()
    {
        Assert.IsType<MalformedManifest>(ManifestSerializer.Read(Encoding.UTF8.GetBytes("{ nope")).Error);
    }

    [Fact]
    public void A_manifest_missing_required_fields_fails()
    {
        Assert.IsType<MalformedManifest>(
            ManifestSerializer.Read(Encoding.UTF8.GetBytes("""{"schemaVersion":1}""")).Error);
    }

    [Fact]
    public void Empty_bytes_fail_rather_than_producing_an_empty_manifest()
    {
        Assert.IsType<MalformedManifest>(ManifestSerializer.Read([]).Error);
    }

    [Fact]
    public void The_written_json_is_indented_so_a_human_can_read_it_in_the_store()
    {
        var json = Encoding.UTF8.GetString(ManifestSerializer.Write(Sample()));

        Assert.Contains("\n", json, StringComparison.Ordinal);
        Assert.Contains("\"schemaVersion\"", json, StringComparison.Ordinal);
        Assert.Contains("\"displayName\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void A_null_wave_link_version_round_trips_as_null()
    {
        // Absent from Settings.json on a fresh install that has never updated.
        Assert.Null(RoundTrip(Sample() with { WaveLinkVersion = null }).WaveLinkVersion);
    }
}
