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

    // -------------------------------------------------------------- the unbuilt tiers: locked, not just off

    [Fact]
    public void The_preset_tier_is_off_and_cannot_be_turned_on()
    {
        var (model, _, _) = Rig();

        model.IncludePresets = true;

        Assert.False(model.IncludePresets);
    }

    [Fact]
    public void The_plugin_tier_is_off_and_cannot_be_turned_on()
    {
        var (model, _, _) = Rig();

        model.IncludePluginFiles = true;

        Assert.False(model.IncludePluginFiles);
    }

    [Fact]
    public void A_programmatic_tier_set_writes_nothing_to_the_file()
    {
        var (model, repository, fs) = Rig(new BackupSettings(Store, AutoBackupKeepCount: 30));
        var before = fs.Replacements.Count;

        model.IncludePresets = true;
        model.IncludePluginFiles = true;

        Assert.Equal(before, fs.Replacements.Count);
        // And the stored value is untouched, not just unwritten: nothing in BackupSettings
        // has a field for these tiers yet.
        Assert.Equal(30, repository.Read().AutoBackupKeepCount);
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

    [Fact]
    public void Locked_and_zero_byte_tiers_contribute_nothing_to_the_bar()
    {
        // The effects list is enabled but rides inside the settings file (0 bytes of its own), and
        // both unbuilt tiers are locked off. None of them may appear as a segment - only real bytes count.
        var model = Bar(Setup(), new("Effect presets", "", 0, false, true), new("The effect plug-ins themselves", "", 0, false, true));

        Assert.Single(model.Segments);
        Assert.Equal("Your setup", model.Segments[0].Name);
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
}
