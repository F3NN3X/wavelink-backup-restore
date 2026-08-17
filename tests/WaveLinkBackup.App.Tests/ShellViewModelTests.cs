using WaveLinkBackup.App.Hosting;
using WaveLinkBackup.App.ViewModels;

namespace WaveLinkBackup.App.Tests;

/// <summary>
/// The status strip and the bottom bar - the two places the app states a standing fact about
/// the machine, and the place it says what may be done to the selection.
/// </summary>
public sealed class ShellViewModelTests
{
    private static readonly DateTimeOffset SavedAt = new(2026, 8, 15, 23, 7, 0, TimeSpan.Zero);

    private static ShellViewModel Shell(
        bool waveLinkRunning = true,
        bool waveLinkFound = true,
        bool folderMissing = false,
        bool autoBackup = true,
        long? freeBytes = 126701535232,
        string storePath = @"C:\Users\t\AppData\Local\WaveLinkBackup")
    {
        // The harness the plan builds in Step 3; it wraps the five facts the strip reports so a
        // test does not have to stand up a store, a process and an inspector to assert a string.
        return ShellViewModelHarness.Build(
            waveLinkRunning, waveLinkFound, folderMissing, autoBackup, freeBytes, storePath, SavedAt);
    }

    // -- the status strip -------------------------------------------------------------------

    // README section Screen 1 item 2, verbatim.
    [Fact]
    public void The_strip_reports_wave_link_the_save_time_and_the_switch()
    {
        Assert.Equal(
            "WAVE LINK RUNNING · SETTINGS LAST SAVED 23:07 · AUTOMATIC BACKUP ON",
            Shell().StatusStrip);
    }

    [Fact]
    public void The_strip_is_green_when_everything_is_as_it_should_be()
    {
        Assert.Equal(StripTone.Ok, Shell().StatusTone);
    }

    [Fact]
    public void Wave_link_not_running_is_stated_rather_than_hidden()
    {
        Assert.StartsWith("WAVE LINK NOT RUNNING", Shell(waveLinkRunning: false).StatusStrip,
            StringComparison.Ordinal);
    }

    [Fact]
    public void The_switch_being_off_is_stated_too()
    {
        Assert.EndsWith("AUTOMATIC BACKUP OFF", Shell(autoBackup: false).StatusStrip,
            StringComparison.Ordinal);
    }

    // 06's status strip (1). The "Choose the settings file…" button beside it is error 1 and is
    // a later session; the sentence is not.
    [Fact]
    public void Wave_link_not_found_is_amber_and_says_so()
    {
        var shell = Shell(waveLinkFound: false);

        Assert.Equal("WAVE LINK NOT FOUND ON THIS COMPUTER", shell.StatusStrip);
        Assert.Equal(StripTone.Warn, shell.StatusTone);
    }

    // 08: "status strip ok dot: WAVE LINK RUNNING · 5 INPUTS · BACKUP FOLDER UNAVAILABLE", and
    // "Neutral, not amber: nothing is broken and nothing is lost - a location is missing."
    [Fact]
    public void A_missing_folder_replaces_the_last_segment_and_is_neutral()
    {
        var shell = Shell(folderMissing: true);

        Assert.EndsWith("BACKUP FOLDER UNAVAILABLE", shell.StatusStrip, StringComparison.Ordinal);
        Assert.DoesNotContain("AUTOMATIC BACKUP", shell.StatusStrip, StringComparison.Ordinal);
        Assert.Equal(StripTone.Neutral, shell.StatusTone);
    }

    // 10-decisions section 6: "Automatic backup while the folder is missing does nothing at all,
    // and the status strip says so. It must not fail silently every hour and it must not queue."
    [Fact]
    public void A_missing_folder_outranks_the_automatic_backup_switch_in_the_strip()
    {
        Assert.Equal(Shell(folderMissing: true).StatusStrip, Shell(folderMissing: true, autoBackup: false).StatusStrip);
    }

    // -- the bottom bar ---------------------------------------------------------------------

    // README: "4 BACKUPS · 12.4 MB IN %LOCALAPPDATA%\WaveLinkBackup · 118 GB FREE".
    //
    // Deviation from the brief: storePath is built from THIS machine's own
    // Environment.GetFolderPath(LocalApplicationData) rather than the brief's literal
    // @"C:\Users\t\AppData\Local\WaveLinkBackup". ShellViewModel.ShortStorePath asks Windows
    // for %LOCALAPPDATA% and shortens only a path that is genuinely under it - which is the
    // correct behaviour (a false match on a merely similar-looking path would print
    // %LOCALAPPDATA% for a location that is not it). A fixture path only matches that check on
    // an account literally named "t"; building it from the real value here is what makes the
    // test pass on any machine and any account while still pinning real behaviour rather than a
    // string shape.
    [Fact]
    public void The_summary_counts_backups_names_the_folder_and_reports_free_space()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var shell = Shell(storePath: localAppData + @"\WaveLinkBackup");
        shell.List.Refresh();

        Assert.Matches(
            @"^\d+ BACKUPS · [\d.]+ [KMG]?B IN %LOCALAPPDATA%\\WaveLinkBackup · 118 GB FREE$",
            shell.SummaryLine);
    }

    // "Null rather than 0 or a throw ... omitting the figure is honest where printing 0 would
    // quietly claim a full disk." - IFileSystem.GetAvailableFreeBytes.
    [Fact]
    public void Unknown_free_space_is_omitted_rather_than_printed_as_zero()
    {
        var shell = Shell(freeBytes: null);
        shell.List.Refresh();

        Assert.DoesNotContain("FREE", shell.SummaryLine, StringComparison.Ordinal);
        Assert.DoesNotContain("0 B", shell.SummaryLine, StringComparison.Ordinal);
    }

    [Fact]
    public void A_store_outside_localappdata_is_printed_in_full()
    {
        var shell = Shell(storePath: @"\\NAS\streaming\WaveLinkBackup");
        shell.List.Refresh();

        Assert.Contains(@"\\NAS\streaming\WaveLinkBackup", shell.SummaryLine, StringComparison.Ordinal);
        Assert.DoesNotContain("%LOCALAPPDATA%", shell.SummaryLine, StringComparison.Ordinal);
    }

    // Not in the brief - added per coordinator review of Task 9. The half that actually catches
    // a wrong match: a store path that is not under %LOCALAPPDATA% at all (a different drive,
    // not merely a different machine's account) must come back byte-for-byte, not just "any
    // path that fails to start with a hardcoded prefix".
    [Fact]
    public void A_store_on_a_different_drive_is_printed_in_full()
    {
        var shell = Shell(storePath: @"D:\Backups\WaveLinkBackup");
        shell.List.Refresh();

        Assert.Contains(@"D:\Backups\WaveLinkBackup", shell.SummaryLine, StringComparison.Ordinal);
        Assert.DoesNotContain("%LOCALAPPDATA%", shell.SummaryLine, StringComparison.Ordinal);
    }

    [Fact]
    public void With_nothing_selected_there_is_no_selected_line()
    {
        var shell = Shell();
        shell.List.Refresh();

        Assert.Null(shell.SelectedLine);
    }

    // README: "SELECTED · BEFORE 3.3 BETA · 11 AUG 21:36".
    [Fact]
    public void The_selected_line_names_the_backup_and_when_it_was_taken()
    {
        var shell = Shell();
        shell.List.Refresh();
        shell.List.Selected = shell.List.Groups[0].Rows[0];

        Assert.StartsWith("SELECTED · ", shell.SelectedLine!, StringComparison.Ordinal);
        Assert.Equal(shell.SelectedLine, shell.SelectedLine!.ToUpperInvariant());
    }

    // 02's bottom bar for a damaged selection, line 2.
    [Fact]
    public void A_damaged_selection_says_restore_is_off_before_it_counts_anything()
    {
        var shell = Shell();
        shell.List.Refresh();

        var row = shell.List.Groups[0].Rows[0];
        row.ApplyVerdict(new HealthVerdict(SnapshotHealth.Damaged, 1, 1, SavedAt));
        shell.List.Selected = row;

        Assert.StartsWith("DAMAGED — RESTORE IS OFF FOR THIS ONE · ", shell.SummaryLine,
            StringComparison.Ordinal);
    }

    // -- what the buttons may do ------------------------------------------------------------

    [Fact]
    public void With_no_selection_only_back_up_now_is_live()
    {
        var shell = Shell();
        shell.List.Refresh();

        Assert.False(shell.CanRename);
        Assert.False(shell.CanDelete);
        Assert.False(shell.CanRestore);
        Assert.True(shell.CanBackUpNow);
    }

    [Fact]
    public void A_healthy_selection_lights_all_four()
    {
        var shell = Shell();
        shell.List.Refresh();
        shell.List.Selected = shell.List.Groups[0].Rows[0];

        Assert.True(shell.CanRename);
        Assert.True(shell.CanDelete);
        Assert.True(shell.CanRestore);
        Assert.True(shell.CanBackUpNow);
    }

    [Fact]
    public void A_damaged_selection_leaves_only_delete_and_back_up_now()
    {
        var shell = Shell();
        shell.List.Refresh();

        var row = shell.List.Groups[0].Rows[0];
        row.ApplyVerdict(new HealthVerdict(SnapshotHealth.Damaged, 1, 1, SavedAt));
        shell.List.Selected = row;

        Assert.False(shell.CanRename);
        Assert.False(shell.CanRestore);
        Assert.True(shell.CanDelete);
        Assert.True(shell.CanBackUpNow);
    }

    // 08: "all four action buttons at 40% opacity, INCLUDING Back up now" - there is nowhere to
    // put a backup.
    [Fact]
    public void A_missing_folder_turns_every_action_off_including_back_up_now()
    {
        var shell = Shell(folderMissing: true);
        shell.List.Refresh();

        Assert.False(shell.CanBackUpNow);
        Assert.False(shell.CanDelete);
    }

    // -- high contrast ----------------------------------------------------------------------

    // Design section C: structural differences are "template switches driven by a flag on the
    // shell view model". This is that flag, and it is the only place high contrast lives outside
    // the theme dictionaries.
    [Fact]
    public void High_contrast_is_a_flag_the_templates_can_switch_on()
    {
        var shell = Shell();

        Assert.False(shell.IsHighContrast);

        shell.IsHighContrast = true;

        Assert.True(shell.IsHighContrast);
    }
}
