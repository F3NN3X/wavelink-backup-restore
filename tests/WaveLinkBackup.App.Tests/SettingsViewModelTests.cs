using WaveLinkBackup.App.Startup;
using WaveLinkBackup.App.ViewModels;
using WaveLinkBackup.Core.Automation;
using WaveLinkBackup.Core.Tests.Fakes;

namespace WaveLinkBackup.App.Tests;

/// <summary>
/// The settings dialog's model, in isolation from the Window. 08-settings-persistence.md is
/// authoritative: there is no Save button, every control commits on change, and a command-line
/// flag overrides the file for one run and is never saved. These tests pin that behaviour
/// against a real <see cref="SettingsRepository"/> over a fake file system, so "writes through
/// immediately" means bytes on disk, not a field flip.
/// </summary>
public sealed class SettingsViewModelTests
{
    private const string Directory = @"C:\Users\t\AppData\Local\WaveLinkBackup";
    private const string Store = @"C:\Users\t\Backups";

    // The fake file system's GetLastWriteTimeUtc is a constant, so "did it write" is asserted
    // through Replacements instead: the first save creates the file (a plain WriteBytes), and
    // every save after that goes through File.Replace - one entry per atomic rewrite.
    private static (SettingsViewModel Model, SettingsRepository Repository, FakeFileSystem Fs) Rig(
        BackupSettings? settings = null,
        WhichWaveLinkModel? whichWaveLink = null)
    {
        var fileSystem = new FakeFileSystem();
        var repository = new SettingsRepository(fileSystem, Directory);

        if (settings is not null)
            Assert.True(repository.Save(settings).IsSuccess);

        var model = SettingsViewModel.Build(
            repository.Read(),
            s => repository.Save(s).IsSuccess,
            new WhereSettingsLiveModel(repository.FilePath, "43 KB"),
            whichWaveLink);

        return (model, repository, fileSystem);
    }

    // -------------------------------------------------------------- in-place commit: the whole point

    [Fact]
    public void Toggling_auto_backup_writes_the_file_immediately()
    {
        var (model, repository, _) = Rig(new BackupSettings(Store, AutoBackupEnabled: false));

        model.AutoBackupEnabled = true;

        Assert.True(repository.Read().AutoBackupEnabled);
    }

    [Fact]
    public void Toggling_auto_backup_off_writes_the_file_immediately_too()
    {
        var (model, repository, _) = Rig(new BackupSettings(Store, AutoBackupEnabled: true));

        model.AutoBackupEnabled = false;

        Assert.False(repository.Read().AutoBackupEnabled);
    }

    [Fact]
    public void Changing_the_keep_count_writes_the_file_immediately()
    {
        var (model, repository, _) = Rig(new BackupSettings(Store, AutoBackupKeepCount: 30));

        model.AutoBackupKeepCount = 12;

        Assert.Equal(12, repository.Read().AutoBackupKeepCount);
    }

    [Fact]
    public void Setting_the_same_value_writes_nothing()
    {
        var (model, _, fs) = Rig(new BackupSettings(Store, AutoBackupEnabled: true));
        var before = fs.Replacements.Count;

        model.AutoBackupEnabled = true; // unchanged

        Assert.Equal(before, fs.Replacements.Count);
    }

    [Fact]
    public void Changing_the_backup_folder_persists_and_updates_the_display()
    {
        var (model, repository, _) = Rig(new BackupSettings(Store));

        Assert.True(model.ChangeBackupFolder(@"D:\Backups"));

        Assert.Equal(@"D:\Backups", repository.Read().StorePath);
        Assert.Equal(@"D:\Backups", model.BackupFolder);
    }

    [Fact]
    public void Choosing_a_wave_link_persists_the_path_and_updates_the_section()
    {
        var (model, repository, _) = Rig(new BackupSettings(Store));
        var chosen = new WhichWaveLinkModel("3.2.1", @"D:\WL\Settings.json",
            new DateTimeOffset(2026, 8, 19, 12, 0, 0, TimeSpan.Zero), Visible: true);

        Assert.True(model.ChooseWaveLink(chosen));

        Assert.Equal(@"D:\WL\Settings.json", model.WhichWaveLink!.Path);
        Assert.Equal(@"D:\WL\Settings.json", repository.Read().ChosenWaveLinkPath);
    }

    // -------------------------------------------------------------- the keep-count bounds

    [Fact]
    public void The_keep_count_will_not_go_below_one()
    {
        var (model, repository, _) = Rig(new BackupSettings(Store, AutoBackupKeepCount: 2));

        model.AutoBackupKeepCount = 0;

        Assert.Equal(1, model.AutoBackupKeepCount);
        Assert.Equal(1, repository.Read().AutoBackupKeepCount);
    }

    [Fact]
    public void The_keep_count_will_not_go_above_999()
    {
        var (model, repository, _) = Rig(new BackupSettings(Store, AutoBackupKeepCount: 900));

        model.AutoBackupKeepCount = 10_000;

        Assert.Equal(999, model.AutoBackupKeepCount);
        Assert.Equal(999, repository.Read().AutoBackupKeepCount);
    }

    // -------------------------------------------------------------- the tier toggles (phase 6)

    [Fact]
    public void The_two_switchable_tiers_start_where_the_settings_file_left_them()
    {
        var (model, _, _) = Rig(new BackupSettings(Store, IncludePresets: false, IncludePluginFiles: true));

        Assert.False(model.IncludePresets);
        Assert.True(model.IncludePluginFiles);
    }

    [Fact]
    public void Switching_a_tier_commits_immediately_like_every_other_control()
    {
        var (model, repository, _) = Rig();

        model.IncludePluginFiles = true;

        Assert.True(model.IncludePluginFiles);
        Assert.True(repository.Read().IncludePluginFiles);
    }

    [Fact]
    public void Switching_presets_off_persists_that_too()
    {
        var (model, repository, _) = Rig();

        model.IncludePresets = false;

        Assert.False(repository.Read().IncludePresets);
    }

    [Fact]
    public void The_row_toggle_is_what_switches_the_tier()
    {
        // The design puts the control IN the row, so the row is where the user changes it. The
        // view binds to the row; nothing would persist if the row and the setting were not wired.
        var (model, repository, _) = Rig();
        model.WhatGoesIn = new WhatGoesInModel(
            setup: new WhatGoesInRow("Your setup", "", 470_000, true, true),
            effectsList: new WhatGoesInRow("A list of your effects", "", 0, true, true),
            presets: new WhatGoesInRow("Effect presets", "", 10_000_000, true, false),
            pluginFiles: new WhatGoesInRow("The effect plug-ins themselves", "", 40_000_000, false, false));

        model.WhatGoesIn.PluginFiles.Enabled = true;

        Assert.True(model.IncludePluginFiles);
        Assert.True(repository.Read().IncludePluginFiles);
    }

    [Fact]
    public void A_locked_row_refuses_a_set_even_from_a_binding()
    {
        // The settings file and the effects list have no switch, deliberately (ADR-006).
        var row = new WhatGoesInRow("Your setup", "", 470_000, true, true);

        row.Enabled = false;

        Assert.True(row.Enabled);
    }

    [Fact]
    public void The_proportion_bar_recomputes_when_a_tier_is_switched()
    {
        // "Recompute from the enabled tiers, never hard-code the percentages" is only true if it
        // recomputes when one is switched - otherwise it is a hard-coded percentage with steps.
        var model = new WhatGoesInModel(
            setup: new WhatGoesInRow("Your setup", "", 470_000, true, true),
            effectsList: new WhatGoesInRow("A list of your effects", "", 0, true, true),
            presets: new WhatGoesInRow("Effect presets", "", 10_000_000, false, false),
            pluginFiles: new WhatGoesInRow("The effect plug-ins themselves", "", 40_000_000, false, false));

        Assert.Equal(470_000, model.TotalBytes);
        Assert.Single(model.Segments);

        model.Presets.Enabled = true;

        Assert.Equal(10_470_000, model.TotalBytes);
        Assert.Equal(2, model.Segments.Count);
        Assert.Equal("EACH BACKUP: ABOUT 10 MB", model.EachBackupLabel);
        Assert.Equal("+ 38.1 MB IF YOU ADD THE PLUG-IN FILES", model.IfYouAddLabel);
    }

    // -------------------------------------------------------------- command-line flags: override, never save

    [Fact]
    public void A_flag_override_is_not_written_back_when_the_user_changes_something_else()
    {
        // The flag said --keep 12 for this run; the file says 30. The dialog is built from the
        // overlaid value (12), and when the user flips auto-backup the save must carry the FILE's
        // 30 - not the flag's 12. "A command-line flag overrides this file for that one run and
        // isn't saved" means a control change commits the field the control owns, merged over the
        // persisted record, so the overlay can never leak into settings.json through an unrelated
        // commit.
        var (model, repository, _) = Rig(new BackupSettings(Store, AutoBackupKeepCount: 30));

        // Apply the flag exactly as App does at startup: overlay the file's settings for this run.
        var overlaid = ShellArguments.Parse(["--keep", "12"]).ApplyTo(repository.Read());
        var flagged = SettingsViewModel.Build(overlaid, s => repository.Save(s).IsSuccess,
            new WhereSettingsLiveModel(repository.FilePath, "43 KB"));

        Assert.Equal(12, flagged.AutoBackupKeepCount); // the flag's value, for this run

        flagged.AutoBackupEnabled = true;

        Assert.True(repository.Read().AutoBackupEnabled);   // the change committed...
        Assert.Equal(30, repository.Read().AutoBackupKeepCount); // ...without saving the flag.
    }

    [Fact]
    public void A_control_change_to_the_flagged_field_does_save_that_change()
    {
        // The mirror image: when the user moves the keep-count stepper itself, that IS a choice
        // and it is saved - even though a flag had overridden the same field for this run.
        var (_, repository, _) = Rig(new BackupSettings(Store, AutoBackupKeepCount: 30));

        var overlaid = ShellArguments.Parse(["--keep", "12"]).ApplyTo(repository.Read());
        var flagged = SettingsViewModel.Build(overlaid, s => repository.Save(s).IsSuccess,
            new WhereSettingsLiveModel(repository.FilePath, "43 KB"));

        flagged.AutoBackupKeepCount = 50;

        Assert.Equal(50, repository.Read().AutoBackupKeepCount);
    }

    [Fact]
    public void The_overlay_alone_writes_nothing_to_the_file()
    {
        // Parsing and applying flags is a pure overlay; the file must be untouched no matter how
        // many flags were given.
        var (_, repository, fs) = Rig(new BackupSettings(Store, AutoBackupKeepCount: 30));
        var before = fs.Replacements.Count;

        ShellArguments.Parse(["--store", @"D:\flag", "--keep", "12"])
            .ApplyTo(repository.Read());

        Assert.Equal(before, fs.Replacements.Count);
        Assert.Equal(30, repository.Read().AutoBackupKeepCount);
    }

    // -------------------------------------------------------------- WHICH WAVE LINK visibility

    [Fact]
    public void The_which_wave_link_section_hides_for_a_single_installation()
    {
        var (model, _, _) = Rig(whichWaveLink: null);

        Assert.Null(model.WhichWaveLink);
    }

    [Fact]
    public void The_which_wave_link_section_shows_when_there_is_a_choice_to_make()
    {
        var chosen = new WhichWaveLinkModel("3.2.1", @"C:\Program Files\Elgato\Wave Link\Settings.json",
            new DateTimeOffset(2026, 8, 1, 9, 0, 0, TimeSpan.Zero), Visible: true);
        var (model, _, _) = Rig(whichWaveLink: chosen);

        Assert.NotNull(model.WhichWaveLink);
        Assert.True(model.WhichWaveLink!.Visible);
    }

    // -------------------------------------------------------------- the WHERE THESE SETTINGS LIVE block

    [Fact]
    public void The_where_settings_live_block_carries_the_path_and_size()
    {
        var (model, repository, _) = Rig();

        Assert.Equal(repository.FilePath, model.WhereSettingsLive.FilePath);
        Assert.Equal("43 KB", model.WhereSettingsLive.SizeText);
    }

    // -------------------------------------------------------------- WHICH WAVE LINK: the CHOSEN date line

    [Fact]
    public void The_chosen_date_line_prints_the_local_date_upper_cased()
    {
        // "CHOSEN 14 AUG" - the mono micro-label convention. The date is local (the moment the
        // user made the choice), upper-cased, and formatted d MMM. A UTC input at midnight on a
        // positive-offset machine lands on the same calendar day, so the assertion is stable.
        var model = new WhichWaveLinkModel(
            "3.2.1", @"C:\WL\Settings.json",
            new DateTimeOffset(2026, 8, 14, 0, 0, 0, TimeSpan.Zero), Visible: true);

        Assert.StartsWith("CHOSEN ", model.ChosenAtText);
        Assert.Equal(model.ChosenAtText, model.ChosenAtText.ToUpperInvariant());
    }

    [Fact]
    public void The_chosen_date_line_carries_the_day_and_month()
    {
        var model = new WhichWaveLinkModel(
            "3.2.1", @"C:\WL\Settings.json",
            new DateTimeOffset(2026, 8, 14, 12, 0, 0, TimeSpan.Zero), Visible: true);

        Assert.Contains("AUG", model.ChosenAtText);
    }

    // -------------------------------------------------------------- WHICH WAVE LINK: ChooseWaveLink persists + updates

    [Fact]
    public void Choosing_a_wave_link_updates_the_visible_section_immediately()
    {
        var (model, _, _) = Rig(whichWaveLink: null);
        var chosen = new WhichWaveLinkModel("3.2.1", @"D:\WL\Settings.json",
            new DateTimeOffset(2026, 8, 19, 12, 0, 0, TimeSpan.Zero), Visible: true);

        Assert.True(model.ChooseWaveLink(chosen));

        Assert.NotNull(model.WhichWaveLink);
        Assert.Equal("3.2.1", model.WhichWaveLink!.Version);
        Assert.Equal(@"D:\WL\Settings.json", model.WhichWaveLink.Path);
    }

    // -------------------------------------------------------------- WHAT GOES IN A BACKUP: the proportion bar

    // The bar is a pure projection of the enabled tiers (Task 3 step 2): it recomputes from what
    // is actually in a backup, never a hard-coded percentage. These tests drive WhatGoesInModel
    // directly with synthetic sizes so each rule - reflow, exclusion, labels - is asserted on its
    // own, exactly the way FreeSpaceText is tested through Readable rather than through a window.

    private static WhatGoesInRow Setup(long bytes = 43 * 1024) =>
        new("Your setup", "Every channel, routing and effect chain - the whole file.", bytes, true, false);

    private static WhatGoesInRow EffectsList() =>
        new("A list of your effects", "The names of the effects in use. Travels inside the settings file above.", 0, true, false);

    private static WhatGoesInModel Bar(WhatGoesInRow setup, WhatGoesInRow presets, WhatGoesInRow pluginFiles) =>
        new(setup, EffectsList(), presets, pluginFiles);

    [Fact]
    public void A_single_enabled_tier_fills_the_whole_bar()
    {
        // Today's honest state: only the settings file is in a backup, so it takes 100% of the bar.
        var model = Bar(Setup(), new("Effect presets", "", 0, false, true), new("The effect plug-ins themselves", "", 0, false, true));

        Assert.Equal(43 * 1024, model.TotalBytes);
        Assert.Single(model.Segments);
        Assert.Equal(1.0, model.Segments[0].Fraction);
        Assert.Equal("EACH BACKUP: ABOUT 43 KB", model.EachBackupLabel);
    }

    [Fact]
    public void Enabling_a_tier_reflows_the_bar_from_the_enabled_sizes()
    {
        // The rule the spec calls out by name: enabling/disabling a tier changes the computed
        // widths. Two equal enabled tiers split the bar evenly - neither keeps a fixed share.
        var off = Bar(Setup(), new("Effect presets", "", 0, false, true), new("The effect plug-ins themselves", "", 0, false, true));
        Assert.Equal(1.0, off.Segments[0].Fraction);

        // Turn on the plug-in tier at the same size as the settings file: the bar recomputes and
        // both enabled tiers now hold half each. The disabled/locked presets contribute nothing.
        var on = Bar(Setup(), new("Effect presets", "", 0, false, true),
            new("The effect plug-ins themselves", "", 43 * 1024, true, false));

        Assert.Equal(2, on.Segments.Count);
        Assert.All(on.Segments, s => Assert.Equal(0.5, s.Fraction));
    }

    [Fact]
    public void A_larger_enabled_tier_takes_a_wider_share()
    {
        // The share is proportional to bytes, not a fixed slot: 3x the size takes 3/4 of the bar.
        var model = Bar(Setup(1 * 1024), new("Effect presets", "", 0, false, true),
            new("The effect plug-ins themselves", "", 3 * 1024, true, false));

        Assert.Equal(0.25, model.Segments[0].Fraction); // the settings file: 1 of 4 KB
        Assert.Equal(0.75, model.Segments[1].Fraction); // the plug-ins: 3 of 4 KB
    }

    // README Screen 3 colours the bar in ROW order - ok, warn, then accent at 75% - and the view
    // picks the brush off Tier. The view used to match one hard-coded English row label instead,
    // which painted every other segment ok; nothing catches that but the number reaching the view.
    [Fact]
    public void Each_segment_carries_the_tier_it_came_from()
    {
        var model = Bar(Setup(1 * 1024), new("Effect presets", "", 2 * 1024, true, false),
            new("The effect plug-ins themselves", "", 3 * 1024, true, false));

        Assert.Equal([1, 3, 4], model.Segments.Select(s => s.Tier));
    }

    [Fact]
    public void Locked_and_zero_byte_tiers_contribute_nothing_to_the_bar()
    {
        // The effects list is enabled but rides inside the settings file (0 bytes of its own), and
        // both unbuilt tiers are locked off. None of them may appear as a segment - only real bytes count.
        var model = Bar(Setup(), new("Effect presets", "", 0, false, true), new("The effect plug-ins themselves", "", 0, false, true));

        Assert.Single(model.Segments);
        Assert.Equal("Your setup", model.Segments[0].Name);
    }

    // screens/14: the keep-count row's title is the value read back, exactly as the interval
    // row's is. The XAML carried the sentence with the number deleted out of it.
    [Fact]
    public void The_keep_count_label_reads_the_value_back()
    {
        var (model, _, _) = Rig();

        model.AutoBackupKeepCount = 30;
        Assert.Equal("Keep the last 30 automatic backups", model.KeepCountLabel);

        model.StepKeepCount(-1);
        Assert.Equal("Keep the last 29 automatic backups", model.KeepCountLabel);
    }

    [Fact]
    public void The_each_backup_label_prints_the_enabled_total()
    {
        var model = Bar(Setup(), new("Effect presets", "", 0, false, true),
            new("The effect plug-ins themselves", "", 43 * 1024, true, false));

        Assert.Equal("EACH BACKUP: ABOUT 86 KB", model.EachBackupLabel);
    }

    [Fact]
    public void The_if_you_add_label_shows_the_cost_of_the_left_out_tiers()
    {
        // The right-hand figure is the honest answer to "what would turning this on cost" - the sum
        // of every disabled tier that carries bytes. With the plug-ins off at 40 MB, it prints them.
        var model = Bar(Setup(), new("Effect presets", "", 0, false, true),
            new("The effect plug-ins themselves", "", 40 * 1024 * 1024, false, true));

        Assert.Equal("+ 40 MB IF YOU ADD THE PLUG-IN FILES", model.IfYouAddLabel);
    }

    [Fact]
    public void The_if_you_add_label_is_empty_when_nothing_is_left_out()
    {
        // When every tier with bytes is already in a backup, there is nothing to add - the label
        // must be empty rather than print "+ 0 B".
        var model = Bar(Setup(), new("Effect presets", "", 0, false, true),
            new("The effect plug-ins themselves", "", 43 * 1024, true, false));

        Assert.Equal(string.Empty, model.IfYouAddLabel);
    }

    [Fact]
    public void A_row_with_no_bytes_of_its_own_prints_a_dash()
    {
        // The effects list and the unbuilt tiers carry no separate number - they print "—", not 0 B.
        Assert.Equal("—", EffectsList().SizeText);
        Assert.Equal("43 KB", Setup().SizeText);
    }

    // ------------------------------------------------- when to back up (screens/14-backup-timing)

    [Fact]
    public void The_interval_stepper_moves_along_the_ladder_and_commits()
    {
        // A ladder rather than free-form minutes, so every position is a number a person would
        // actually choose.
        var (vm, saved) = Model();

        vm.StepInterval(-1);
        Assert.Equal(30, vm.AutoBackupIntervalMinutes);
        Assert.Equal(30, saved[^1].AutoBackupIntervalMinutes);

        vm.StepInterval(+1);
        vm.StepInterval(+1);
        Assert.Equal(120, vm.AutoBackupIntervalMinutes);
    }

    [Fact]
    public void The_interval_stepper_stops_at_both_ends_rather_than_wrapping()
    {
        // A stepper that jumps from 24 h to 15 min on one press is a stepper that mis-sets itself.
        var (vm, _) = Model();

        for (var i = 0; i < 10; i++) vm.StepInterval(-1);
        Assert.Equal(15, vm.AutoBackupIntervalMinutes);

        for (var i = 0; i < 20; i++) vm.StepInterval(+1);
        Assert.Equal(1440, vm.AutoBackupIntervalMinutes);
    }

    [Fact]
    public void A_hand_edited_interval_snaps_onto_the_ladder()
    {
        // The settings file is a text file someone can edit. 47 minutes is not a rung, and the
        // stepper has to know where it is standing before it can move.
        var (vm, _) = Model(BackupSettings.Default with { AutoBackupIntervalMinutes = 47 });

        vm.StepInterval(+1);

        Assert.Contains(vm.AutoBackupIntervalMinutes, BackupSettings.IntervalLadder);
    }

    [Theory]
    [InlineData(15, "15 MIN", "At most one automatic backup every 15 minutes")]
    [InlineData(60, "1 H", "At most one automatic backup an hour")]
    [InlineData(240, "4 H", "At most one automatic backup every 4 hours")]
    [InlineData(1440, "24 H", "At most one automatic backup a day")]
    public void The_row_title_is_the_value_read_back(int minutes, string readout, string label)
    {
        // The label and the control cannot drift, because the label IS the control's value. The old
        // copy said "at most one an hour" beside a constant nobody could change, and that was the
        // whole problem.
        var (vm, _) = Model(BackupSettings.Default with { AutoBackupIntervalMinutes = minutes });

        Assert.Equal(readout, vm.IntervalText);
        Assert.Equal(label, vm.IntervalLabel);
    }

    [Fact]
    public void Switching_the_daily_backup_on_starts_at_three_in_the_morning()
    {
        var (vm, saved) = Model();

        Assert.False(vm.DailyBackupEnabled);

        vm.DailyBackupEnabled = true;

        Assert.Equal("03:00", vm.DailyTimeText);
        Assert.Equal(180, saved[^1].DailyBackupMinutes);
    }

    [Fact]
    public void Switching_it_off_forgets_the_time_rather_than_keeping_a_dead_value()
    {
        // null IS "off" in the settings file, and two ways to say off is one too many.
        var (vm, saved) = Model(BackupSettings.Default with { DailyBackupMinutes = 5 * 60 });

        vm.DailyBackupEnabled = false;

        Assert.Null(saved[^1].DailyBackupMinutes);
        Assert.Equal(string.Empty, vm.DailyTimeText);
    }

    [Fact]
    public void The_daily_time_steps_by_half_an_hour_and_wraps_at_midnight()
    {
        // Wrapping is right here and wrong for the interval: a clock is a circle, a duration ladder
        // has two ends.
        var (vm, _) = Model(BackupSettings.Default with { DailyBackupMinutes = 23 * 60 + 30 });

        Assert.Equal("23:30", vm.DailyTimeText);

        vm.StepDailyTime(+1);
        Assert.Equal("00:00", vm.DailyTimeText);

        vm.StepDailyTime(-1);
        Assert.Equal("23:30", vm.DailyTimeText);
    }

    [Fact]
    public void Stepping_the_time_while_the_daily_backup_is_off_does_nothing()
    {
        var (vm, saved) = Model();
        var before = saved.Count;

        vm.StepDailyTime(+1);

        Assert.Equal(before, saved.Count);
        Assert.Null(vm.DailyBackupMinutes);
    }

    [Fact]
    public void The_keep_count_stepper_moves_the_value()
    {
        // Its two buttons had no handler at all until the interval and daily steppers were added
        // beside them: the control rendered, the readout bound, and pressing either did nothing.
        var (vm, saved) = Model();
        var start = vm.AutoBackupKeepCount;

        vm.StepKeepCount(+1);
        Assert.Equal(start + 1, vm.AutoBackupKeepCount);
        Assert.Equal(start + 1, saved[^1].AutoBackupKeepCount);

        vm.StepKeepCount(-1);
        Assert.Equal(start, vm.AutoBackupKeepCount);
    }

    /// <summary>A view model over the given settings, plus every value it commits.</summary>
    private static (SettingsViewModel Vm, List<BackupSettings> Saved) Model(BackupSettings? settings = null)
    {
        var saved = new List<BackupSettings>();

        var vm = SettingsViewModel.Build(
            settings ?? BackupSettings.Default,
            s => { saved.Add(s); return true; },
            new WhereSettingsLiveModel(@"C:\s.json", "1 KB"));

        return (vm, saved);
    }
}
