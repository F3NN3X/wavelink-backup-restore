using WaveLinkBackup.App.ViewModels;
using WaveLinkBackup.Core.Snapshots;

namespace WaveLinkBackup.App.Tests;

/// <summary>
/// The three delete variants, in isolation from the window. 05-delete-dialogs.md is
/// authoritative for the copy and the variant rule; these tests assert exactly that behaviour
/// without standing up a Window, because the dialog binds to the strings this model exposes and
/// those are what must be right.
///
/// A Snapshot is built by hand rather than round-tripped through the store: the model only reads
/// the manifest's display name, trigger, created time and file sizes — it never touches disk.
/// </summary>
public sealed class DeleteDialogModelTests
{
    // Readable.Bytes truncates (does not round), so the size is chosen to pin under either:
    //   12_582_912 B / 1_048_576 = 12.0 MB exactly → "12.0 MB"
    // Pinned so a formatting change fails here, not in a screenshot.
    private const long SizeBytes = 12_582_912;

    // The design's own sample ("Before 3.3 beta", taken 11 Aug 21:36). The meta line and the
    // pre-restore block render in LOCAL time via ToLocalTime(), so an assertion that pins a
    // literal wall-clock string is only true on one machine — a fixed instant rendered 21:36 on
    // a UTC+2 box but 19:36 on a UTC runner. Instead the expected strings are derived from the
    // same ToLocalTime() conversion the model uses, so they hold in any zone the suite runs in.
    private static readonly DateTimeOffset Taken = new(2026, 8, 11, 21, 36, 0, TimeSpan.Zero);

    /// <summary>What the dialog renders for <see cref="Taken"/> on THIS machine — the test's expected values.</summary>
    private static DateTimeOffset TakenLocal => Taken.ToLocalTime();

    private static Snapshot Snapshot(
        string name,
        SnapshotTrigger trigger,
        long sizeBytes = SizeBytes) => new(
            "2026-08-11T2136-a3f81c",
            @"C:\Users\test\AppData\Local\WaveLinkBackup\2026-08-11T2136-a3f81c",
            new SnapshotManifest(
                SchemaVersion: SnapshotManifest.CurrentSchemaVersion,
                DisplayName: name,
                Notes: string.Empty,
                CreatedUtc: Taken,
                Trigger: trigger,
                SettingsSha256: new string('0', 64),
                WaveLinkVersion: null,
                InputCount: 3,
                InputNames: ["Wave Mic 1"],
                EffectCount: 0,
                EffectChannelCount: 0,
                HasDuplicateKeys: false,
                Tiers: [],
                Files: new Dictionary<string, SnapshotFile>
                {
                    [SnapshotManifest.SettingsFileName] = new(new string('0', 64), sizeBytes),
                }));

    // -------------------------------------------------------------- the meta line

    [Fact]
    public void Meta_line_is_size_taken_datetime_and_trigger()
    {
        var model = DeleteDialogModel.Build(Snapshot("Before 3.3 beta", SnapshotTrigger.Manual), totalBackups: 4);

        Assert.Equal(
            $"12 MB · TAKEN {TakenLocal.ToString("d MMM", System.Globalization.CultureInfo.InvariantCulture)} "
            + $"{TakenLocal.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture)} · MANUAL",
            model.MetaLine);
    }

    [Theory]
    [InlineData(SnapshotTrigger.Automatic, "AUTOMATIC")]
    [InlineData(SnapshotTrigger.PreRestore, "PRE-RESTORE")]
    public void Meta_line_names_the_trigger(SnapshotTrigger trigger, string expected)
    {
        var model = DeleteDialogModel.Build(Snapshot("Auto", trigger), totalBackups: 4);

        Assert.EndsWith($" · {expected}", model.MetaLine);
    }

    // -------------------------------------------------------------- 1 · Normal

    [Fact]
    public void Normal_variant_states_the_consequence_and_counts_the_others()
    {
        var model = DeleteDialogModel.Build(Snapshot("Before 3.3 beta", SnapshotTrigger.Manual), totalBackups: 4);

        Assert.Equal(DeleteVariant.Normal, model.Variant);
        Assert.Equal("Delete \"Before 3.3 beta\"?", model.Title);
        Assert.Equal(
            "It moves to the trash in your backup folder and stops showing in the list. "
            + "Your other 3 backups aren't affected.",
            model.Body);
        Assert.Null(model.Context);
        Assert.Null(model.BackUpNowInstead);
    }

    [Fact]
    public void Normal_variant_with_two_backups_counts_one_other()
    {
        var model = DeleteDialogModel.Build(Snapshot("Auto", SnapshotTrigger.Automatic), totalBackups: 2);

        Assert.Equal(DeleteVariant.Normal, model.Variant);
        Assert.EndsWith("Your other 1 backups aren't affected.", model.Body);
    }

    // -------------------------------------------------------------- 2 · The only backup

    [Fact]
    public void Only_backup_variant_is_neutral_and_names_what_remains()
    {
        var model = DeleteDialogModel.Build(Snapshot("Before 3.3 beta", SnapshotTrigger.Manual), totalBackups: 1);

        Assert.Equal(DeleteVariant.OnlyBackup, model.Variant);
        Assert.Equal("Delete \"Before 3.3 beta\"?", model.Title);
        Assert.Equal(
            "It moves to the trash in your backup folder. It is the only backup you have.",
            model.Body);
        Assert.NotNull(model.Context);
        Assert.Equal("WHAT YOU'D BE LEFT WITH", model.Context.Label);
        Assert.Equal(
            "Wave Link's own copies, which cover about three days. This one waits in the trash "
            + "until you empty it — after that it is gone.",
            model.Context.Body);
        Assert.Equal("Back up now instead", model.BackUpNowInstead);
    }

    [Fact]
    public void Only_backup_outranks_pre_restore_when_there_is_one()
    {
        // One backup, and it is a pre-restore copy: "it is the only backup you have" is the
        // load-bearing sentence, so the OnlyBackup variant wins.
        var model = DeleteDialogModel.Build(Snapshot("Before restore", SnapshotTrigger.PreRestore), totalBackups: 1);

        Assert.Equal(DeleteVariant.OnlyBackup, model.Variant);
        Assert.Contains("It is the only backup you have.", model.Body);
        Assert.NotNull(model.Context);
        Assert.Equal("WHAT YOU'D BE LEFT WITH", model.Context.Label);
    }

    // -------------------------------------------------------------- 3 · A pre-restore copy

    [Fact]
    public void Pre_restore_variant_carries_its_block_and_names_the_way_back()
    {
        var model = DeleteDialogModel.Build(Snapshot("Before restore", SnapshotTrigger.PreRestore), totalBackups: 4);

        Assert.Equal(DeleteVariant.PreRestore, model.Variant);
        Assert.Equal("Delete \"Before restore\"?", model.Title);
        Assert.Equal(
            "It moves to the trash in your backup folder and stops showing in the list. "
            + "Your other 3 backups aren't affected.",
            model.Body);
        Assert.NotNull(model.Context);
        Assert.Equal("WHAT THIS ONE IS", model.Context.Label);
        Assert.Equal(
            $"Taken automatically at {TakenLocal.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture)} on "
            + $"{TakenLocal.ToString("d MMM", System.Globalization.CultureInfo.InvariantCulture)}, just before you restored. "
            + "It is the way back from that restore.",
            model.Context.Body);
        Assert.Null(model.BackUpNowInstead);
    }

    // -------------------------------------------------------------- the Recycle-Bin rule

    [Fact]
    public void No_variant_ever_names_the_recycle_bin()
    {
        // 05 §"Why the dialog never says Recycle Bin": the backup folder is user-chosen and the
        // Recycle Bin does not exist on network shares, so the one destination true on every
        // volume — "the trash in your backup folder" — is named instead. The Recycle Bin appears
        // in exactly one place in the whole app: the empty-trash row (Task 6).
        foreach (var model in new[]
        {
            DeleteDialogModel.Build(Snapshot("A", SnapshotTrigger.Manual), totalBackups: 4),
            DeleteDialogModel.Build(Snapshot("B", SnapshotTrigger.Automatic), totalBackups: 2),
            DeleteDialogModel.Build(Snapshot("C", SnapshotTrigger.PreRestore), totalBackups: 3),
            DeleteDialogModel.Build(Snapshot("D", SnapshotTrigger.Manual), totalBackups: 1),
        })
        {
            Assert.DoesNotContain("Recycle Bin", model.Title, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Recycle Bin", model.Body, StringComparison.OrdinalIgnoreCase);
            if (model.Context is { } context)
            {
                Assert.DoesNotContain("Recycle Bin", context.Label, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("Recycle Bin", context.Body, StringComparison.OrdinalIgnoreCase);
            }
        }
    }
}
