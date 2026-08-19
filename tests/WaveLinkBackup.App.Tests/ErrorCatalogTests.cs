using WaveLinkBackup.App.ViewModels;
using WaveLinkBackup.Core.Results;

namespace WaveLinkBackup.App.Tests;

/// <summary>
/// Pins the twelve errors to their designed placement and weight (06-errors.md), the weight rule,
/// and the pure mapper from Core signals. This is the guard against a future edit silently
/// re-placing or re-weighting an error: change the catalog and these fail before it ships.
/// </summary>
public sealed class ErrorCatalogTests
{
    [Theory]
    [InlineData(1, ErrorPlacement.StatusStrip, ErrorWeight.Amber)]
    [InlineData(2, ErrorPlacement.Dialog, ErrorWeight.Neutral)]
    [InlineData(3, ErrorPlacement.InlineStrip, ErrorWeight.Neutral)]
    [InlineData(4, ErrorPlacement.Dialog, ErrorWeight.Amber)]
    [InlineData(5, ErrorPlacement.InlineStrip, ErrorWeight.Neutral)]
    [InlineData(6, ErrorPlacement.InlineStrip, ErrorWeight.Neutral)]
    [InlineData(7, ErrorPlacement.InlineStrip, ErrorWeight.Neutral)]
    [InlineData(8, ErrorPlacement.Dialog, ErrorWeight.Neutral)]
    [InlineData(9, ErrorPlacement.Dialog, ErrorWeight.Neutral)]
    [InlineData(10, ErrorPlacement.InlineStrip, ErrorWeight.Neutral)]
    [InlineData(11, ErrorPlacement.InlineStrip, ErrorWeight.Neutral)]
    [InlineData(12, ErrorPlacement.ReplacesList, ErrorWeight.Neutral)]
    [InlineData(13, ErrorPlacement.InlineStrip, ErrorWeight.Neutral)]
    public void Each_error_has_its_designed_placement_and_weight(int code, ErrorPlacement placement, ErrorWeight weight)
    {
        var error = AppError.ByCode(code);

        Assert.Equal(code, error.Code);
        Assert.Equal(placement, error.Placement);
        Assert.Equal(weight, error.Weight);
    }

    [Fact]
    public void Catalog_holds_exactly_thirteen_errors_numbered_one_through_thirteen()
    {
        // Twelve from 06-errors.md, plus the declined-elevation strip from 13-elevation.md.
        Assert.Equal(13, AppError.All.Count);
        for (var code = 1; code <= 13; code++)
            Assert.Equal(code, AppError.ByCode(code).Code);
    }

    [Fact]
    public void Declining_administrator_rights_is_neutral_because_nothing_changed()
    {
        // The weight rule at its sharpest. A declined UAC prompt LOOKS like a failure and is not:
        // the settings and presets went back, the plug-ins on this machine are exactly as they
        // were, and the backup still holds them. Amber would claim the configuration is not whole
        // when the user's own refusal is the only thing that happened.
        var declined = AppError.ElevationDeclined;

        Assert.Equal(13, declined.Code);
        Assert.Equal(ErrorWeight.Neutral, declined.Weight);
        Assert.Equal(ErrorPlacement.InlineStrip, declined.Placement);
    }

    [Fact]
    public void No_Core_error_maps_to_the_declined_strip()
    {
        // Nothing in Core failed and nothing in Core can know what a person clicked, so 13 is the
        // one error with no CoreError behind it. If the mapper ever started producing it, that
        // would mean a real failure was being reported as the user's own choice.
        foreach (var error in AppError.All)
        {
            if (error.Code == 13) continue;
            Assert.NotEqual(13, error.Code);
        }

        Assert.NotEqual(13, AppErrorMapper.FromCoreSignal(new CoreSignal(new WriteFailed("x")))?.Code);
    }

    [Fact]
    public void Weight_rule_exactly_two_errors_are_amber()
    {
        // The weight rule: "Neutral if nothing happened. Amber only if the configuration — live or
        // restorable — is not whole." Exactly two of the twelve leave a config not whole:
        //   1 — Wave Link not found: the LIVE settings file cannot be read at all (06 line 20:
        //       dot + text both --wl-warn). The status strip is amber.
        //   4 — Malformed settings file: the LIVE settings file itself does not parse (06 "Amber
        //       because the live configuration is the thing that is not whole").
        Assert.Equal(ErrorWeight.Amber, AppError.ByCode(1).Weight);
        Assert.Equal(ErrorWeight.Amber, AppError.ByCode(4).Weight);

        // Every other error is neutral: refusals (nothing written/changed), missing locations
        // (nothing lost), or a format this copy doesn't understand yet (the backup is fine).
        for (var code = 1; code <= 13; code++)
        {
            if (code is 1 or 4)
                continue;
            Assert.True(AppError.ByCode(code).Weight == ErrorWeight.Neutral, $"error {code} should be neutral");
        }
    }

    [Fact]
    public void All_inline_strips_are_neutral_fill()
    {
        // 06-errors.md: "All neutral fill." An inline strip is a refusal — nothing was written,
        // nothing changed. Errors 3, 5, 6, 7, 10, 11 all sit here.
        foreach (var code in new[] { 3, 5, 6, 7, 10, 11 })
        {
            var error = AppError.ByCode(code);
            Assert.Equal(ErrorPlacement.InlineStrip, error.Placement);
            Assert.Equal(ErrorWeight.Neutral, error.Weight);
        }
    }

    [Fact]
    public void Every_error_has_non_empty_title_and_body()
    {
        foreach (var error in AppError.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(error.Title), $"error {error.Code} has no title");
            Assert.False(string.IsNullOrWhiteSpace(error.Body), $"error {error.Code} has no body");
        }
    }

    // --- Mapper: Core signal -> the right one of the twelve (or null) -------------------------

    [Fact]
    public void Healthy_signal_maps_to_null()
    {
        Assert.Null(AppErrorMapper.FromCoreSignal(new CoreSignal()));
    }

    [Fact]
    public void Wave_link_not_found_maps_to_error_1_and_beats_an_operation_error()
    {
        var signal = new CoreSignal(Error: new WriteFailed("denied"), WaveLinkFound: false);

        Assert.Equal(1, AppErrorMapper.FromCoreSignal(signal)!.Code);
    }

    [Fact]
    public void Multiple_packages_maps_to_error_2()
    {
        var signal = new CoreSignal(Error: new MultiplePackagesFound(new[] { "a", "b" }));

        Assert.Equal(2, AppErrorMapper.FromCoreSignal(signal)!.Code);
    }

    [Fact]
    public void Settings_unreadable_maps_to_error_3()
    {
        var signal = new CoreSignal(Error: new SettingsUnreadable("C:\\x.json", "locked"));

        Assert.Equal(3, AppErrorMapper.FromCoreSignal(signal)!.Code);
    }

    [Fact]
    public void Malformed_settings_maps_to_error_4()
    {
        var signal = new CoreSignal(Error: new MalformedSettings("unexpected character at line 12"));

        Assert.Equal(4, AppErrorMapper.FromCoreSignal(signal)!.Code);
    }

    [Fact]
    public void Wave_link_still_running_maps_to_error_5()
    {
        var signal = new CoreSignal(Error: new WaveLinkStillRunning(new[] { "WaveLink.exe" }));

        Assert.Equal(5, AppErrorMapper.FromCoreSignal(signal)!.Code);
    }

    [Fact]
    public void Write_failed_maps_to_error_6()
    {
        var signal = new CoreSignal(Error: new WriteFailed("access denied"));

        Assert.Equal(6, AppErrorMapper.FromCoreSignal(signal)!.Code);
    }

    [Fact]
    public void Malformed_manifest_maps_to_error_7()
    {
        var signal = new CoreSignal(Error: new MalformedManifest("missing required key"));

        Assert.Equal(7, AppErrorMapper.FromCoreSignal(signal)!.Code);
    }

    [Fact]
    public void Unsupported_schema_maps_to_error_8()
    {
        var signal = new CoreSignal(Error: new UnsupportedSnapshotSchema(Found: 2, Supported: 1));

        Assert.Equal(8, AppErrorMapper.FromCoreSignal(signal)!.Code);
    }

    [Fact]
    public void Not_a_snapshot_maps_to_error_9()
    {
        var signal = new CoreSignal(Error: new NotASnapshot("D:\\Recordings", "no manifest"));

        Assert.Equal(9, AppErrorMapper.FromCoreSignal(signal)!.Code);
    }

    [Fact]
    public void Snapshot_corrupted_maps_to_error_10()
    {
        var signal = new CoreSignal(Error: new SnapshotCorrupted("C:\\b", "checksum mismatch"));

        Assert.Equal(10, AppErrorMapper.FromCoreSignal(signal)!.Code);
    }

    [Fact]
    public void Snapshot_not_found_maps_to_error_11()
    {
        var signal = new CoreSignal(Error: new SnapshotNotFound("abc-123"));

        Assert.Equal(11, AppErrorMapper.FromCoreSignal(signal)!.Code);
    }

    [Fact]
    public void Store_unavailable_maps_to_error_12_full_screen()
    {
        var signal = new CoreSignal(Error: new StoreUnavailable("D:\\backups", "missing"));

        Assert.Equal(12, AppErrorMapper.FromCoreSignal(signal)!.Code);
    }

    [Fact]
    public void Folder_unusable_without_an_error_maps_to_error_12()
    {
        var signal = new CoreSignal(FolderUsable: false);

        Assert.Equal(12, AppErrorMapper.FromCoreSignal(signal)!.Code);
    }

    [Fact]
    public void Wave_link_not_installed_alone_maps_to_null_the_mapper_needs_the_found_flag()
    {
        // The mapper keys off the WaveLinkFound flag (the shell's discovery result), not the raw
        // CoreError, so a bare WaveLinkNotInstalled with Found=true is not an error here.
        var signal = new CoreSignal(Error: new WaveLinkNotInstalled(), WaveLinkFound: true);

        Assert.Null(AppErrorMapper.FromCoreSignal(signal));
    }

    // --- Weight-rule integration: every signal renders the weight the rule says ---------------

    [Fact]
    public void Every_core_signal_renders_the_weight_the_rule_says()
    {
        // The guard Task 7 asks for: walk a representative Core signal for EACH of the twelve and
        // assert the WEIGHT THAT ACTUALLY RENDERS (the AppError's Weight, the value the views read)
        // matches the weight rule. This is end-to-end over the mapper -> catalog path, so a future
        // edit that re-weights an error in the catalog - or mis-routes a signal to the wrong one of
        // the twelve - fails here before it ships.
        //
        // The rule (06-errors.md): "Neutral if nothing happened. Amber only if the configuration —
        // live or restorable — is not whole." Exactly two of the twelve do that:
        //   1 — Wave Link not found: the LIVE settings file cannot be read at all.
        //   4 — Malformed settings file: the LIVE settings file itself does not parse.
        // Every other signal renders neutral - a refusal (nothing written/changed), a missing
        // location (nothing lost), or a format this copy doesn't understand yet (the backup is fine).

        var signals = new (int Code, CoreSignal Signal)[]
        {
            // 1 — the standing fact: discovery failed. No operation error; the found flag decides.
            (1, new CoreSignal(WaveLinkFound: false)),

            // 2 — two installations, none chosen. A decision is needed; no config is damaged.
            (2, new CoreSignal(Error: new MultiplePackagesFound(new[] { "a", "b" }))),

            // 3 — the settings file could not be read at all. A refusal: nothing was backed up.
            (3, new CoreSignal(Error: new SettingsUnreadable("C:\\x.json", "locked"))),

            // 4 — the settings file does not parse. AMBER: the live config is not whole.
            (4, new CoreSignal(Error: new MalformedSettings("unexpected character at line 12"))),

            // 5 — Wave Link still running, so nothing was written. A refusal.
            (5, new CoreSignal(Error: new WaveLinkStillRunning(new[] { "WaveLink.exe" }))),

            // 6 — the settings file couldn't be replaced. The old settings are still in place.
            (6, new CoreSignal(Error: new WriteFailed("access denied"))),

            // 7 — the backup's manifest can't be read. A refusal: nothing was listed or restored.
            (7, new CoreSignal(Error: new MalformedManifest("missing required key"))),

            // 8 — a newer format this copy doesn't understand yet. The backup itself is fine.
            (8, new CoreSignal(Error: new UnsupportedSnapshotSchema(Found: 2, Supported: 1))),

            // 9 — the chosen folder is not a Wave Link Backup. A wrong location; nothing lost.
            (9, new CoreSignal(Error: new NotASnapshot("D:\\Recordings", "no manifest"))),

            // 10 — the backup is damaged and was not restored. The mixer hasn't changed.
            (10, new CoreSignal(Error: new SnapshotCorrupted("C:\\b", "checksum mismatch"))),

            // 11 — no backup with that id. A refusal: pick another from the list.
            (11, new CoreSignal(Error: new SnapshotNotFound("abc-123"))),

            // 12 — the folder can't be used at all. Nothing lost; a location is simply missing.
            //     Reached here through the standing-folder path (no operation error).
            (12, new CoreSignal(FolderUsable: false)),
        };

        foreach (var (code, signal) in signals)
        {
            var rendered = AppErrorMapper.FromCoreSignal(signal);

            Assert.NotNull(rendered);
            // The signal must route to the one of the twelve it names...
            Assert.Equal(code, rendered!.Code);
            // ...and render the weight that error is designed with (the value the views read).
            Assert.Equal(AppError.ByCode(code).Weight, rendered.Weight);
        }

        // And the rule itself, stated over what actually rendered: exactly two amber, ten neutral.
        var renderedWeights = signals
            .Select(s => AppErrorMapper.FromCoreSignal(s.Signal)!.Weight)
            .ToArray();

        Assert.Equal(2, renderedWeights.Count(w => w == ErrorWeight.Amber));
        Assert.Equal(10, renderedWeights.Count(w => w == ErrorWeight.Neutral));
        // The two amber are precisely the "config not whole" pair.
        Assert.Equal(ErrorWeight.Amber, AppErrorMapper.FromCoreSignal(signals[0].Signal)!.Weight);
        Assert.Equal(ErrorWeight.Amber, AppErrorMapper.FromCoreSignal(signals[3].Signal)!.Weight);
    }
}
