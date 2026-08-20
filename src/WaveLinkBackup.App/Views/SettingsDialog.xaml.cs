using System.Windows;
using WaveLinkBackup.App.Windows;
using WaveLinkBackup.App.ViewModels;

namespace WaveLinkBackup.App.Views;

/// <summary>
/// The settings dialog's view. It renders a <see cref="SettingsViewModel"/> and nothing else - every
/// element in the XAML binds to it and computes nothing, so there is no logic here to test: the
/// commit-on-change behaviour lives on the view model (SettingsViewModelTests) and the folder-picker /
/// open-folder actions live on App (the one place this view crosses the VM boundary).
///
/// The three buttons call back into App through code-behind rather than commands, for the same reason
/// MainWindow's gear button does: each is a two-line wrapper around an App seam, and a command object
/// would just add an indirection with no behaviour to test. Escape and both Close buttons are IsCancel,
/// so dismissing needs no handler at all - WPF closes the window on its own.
/// </summary>
public partial class SettingsDialog : Window
{
    private readonly SettingsViewModel model;

    /// <summary>
    /// Open scrolled to UPDATES, with a check already running. Set by error 8's "Get the update"
    /// (screens/12: it deep-links here "with the new version's row already showing").
    /// </summary>
    public bool ScrollToUpdates { get; init; }

    public SettingsDialog(SettingsViewModel model)
    {
        this.model = model;
        InitializeComponent();
        DataContext = model;

        // Cover the owner, dim it, frost it - the same overlay every other dialog uses. Without an
        // owner (a standalone run, or a view test) this keeps its own geometry and gets no blur.
        DialogOverlay.Attach(this);

        // Change folder… opens the picker and, on a pick, re-points the live store and re-detects the
        // trash row's volume + free space for the new folder (App.ChangeBackupFolder owns all of that).
        ChangeFolderButton.Click += (_, _) =>
            (Application.Current as App)?.ChangeBackupFolder(this, model);

        // Open launches Explorer at the backup folder. Reuses the same seam the tray menu's
        // "Open folder" item uses, so both entry points agree on where the folder is.
        OpenFolderButton.Click += (_, _) =>
            (Application.Current as App)?.OpenStoreFolder();

        // Empty trash: the Plan-6 action. The button is already IsEnabled=false when the trash is
        // empty (ActionEnabled), so this only runs with items to remove. Local volumes run straight
        // through; network/removable confirm first (RequiresConfirmation).
        EmptyTrashButton.Click += (_, _) =>
            (Application.Current as App)?.EmptyTrash(this, model);

        // Change… re-opens the error-2 chooser (the same dialog that fires at startup when two
        // installations are found and none is chosen). App.ChangeWaveLink owns the whole flow:
        // re-inspect, show the chooser, persist the pick. The section itself is only visible when
        // more than one installation exists, so this button always has a real choice to offer.
        ChangeWaveLinkButton.Click += (_, _) =>
            (Application.Current as App)?.ChangeWaveLink(this);

        // The three steppers. Two lines each, calling a method on the view model that owns the
        // ladder, the clamp and the wrap - the same reason the buttons above are code-behind rather
        // than commands.
        //
        // The keep-count pair had NO handler at all until the other two were added beside it: the
        // buttons rendered, the readout bound, and pressing either did nothing. Nothing caught it
        // because the view model's clamp was unit-tested and the wiring never was, which is what
        // SettingsDialogViewTests now asserts by clicking them.
        DecrementKeepCountButton.Click += (_, _) => model.StepKeepCount(-1);
        IncrementKeepCountButton.Click += (_, _) => model.StepKeepCount(+1);

        DecrementIntervalButton.Click += (_, _) => model.StepInterval(-1);
        IncrementIntervalButton.Click += (_, _) => model.StepInterval(+1);

        DecrementDailyTimeButton.Click += (_, _) => model.StepDailyTime(-1);
        IncrementDailyTimeButton.Click += (_, _) => model.StepDailyTime(+1);

        // Error 9's two actions, sitting in place under "Change folder…" (06-errors.md §9).
        // "Choose another…" is the same picker the row above uses; "Keep the current folder" only
        // clears the block - the store never moved, so there is nothing to undo.
        ChooseAnotherFolderButton.Click += (_, _) =>
            (Application.Current as App)?.ChangeBackupFolder(this, model);
        KeepCurrentFolderButton.Click += (_, _) => model.ClearNotABackupFolder();

        // Error 8's deep link, and the weekly check. Both are LOOKS, never installs.
        Loaded += async (_, _) =>
        {
            if (model.Updates is not { } updates) return;

            if (ScrollToUpdates)
            {
                UpdatesSection.BringIntoView();
                await updates.CheckAsync(DateTimeOffset.Now);
            }
            else if (updates.ShouldAutoCheck(DateTimeOffset.Now))
            {
                await updates.CheckAsync(DateTimeOffset.Now);
            }
        };

        // The Run key can change behind us - Task Manager's Startup tab is the reason
        // AutostartState has three values rather than two - so the toggle re-reads on open rather
        // than trusting what it was constructed with.
        Loaded += (_, _) => model.RefreshAutostart();

        // UPDATES (screens/12). The three actions that reach the network go through the view
        // model, which owns the busy flag and the failure line; the two that open a browser go
        // through App, which is where every other shell-out lives.
        CheckForUpdatesButton.Click += async (_, _) => await model.Updates!.CheckAsync(DateTimeOffset.Now);
        InstallUpdateButton.Click += async (_, _) => await model.Updates!.InstallAsync();
        RetryUpdateButton.Click += async (_, _) => await model.Updates!.RetryAsync();

        WhatChangedButton.Click += (_, _) => OpenInBrowser(model.Updates?.NotesUrl);
        DownloadUpdateButton.Click += (_, _) => OpenInBrowser(App.ReleaseSource.ReleasesUrl);

        // Focus the Close button when the dialog opens: it is the safe action and the only one that
        // needs no thought, so a keyboard user who hits Enter immediately dismisses rather than
        // changes something.
        Loaded += (_, _) => FooterCloseButton.Focus();

        // When the dialog closes (Escape, either Close button, or the window being dismissed), hand
        // focus back to the main window's list - the same seam every other dialog uses after it
        // returns (MainWindow.RestoreFocusToList). For a modal owner WPF already re-activates the
        // owner on close; this just makes sure the LIST holds the focus, not some leftover control.
        Closed += (_, _) => RestoreFocus();
    }

    /// <summary>
    /// Opens a release page in the user's browser. Nothing is reported when it fails: the button
    /// is a convenience, and a second error about a browser that would not start buries whichever
    /// update state the user was actually reading.
    /// </summary>
    private static void OpenInBrowser(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return;

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url)
            {
                UseShellExecute = true,
            });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
        }
    }

    /// <summary>
    /// Returns keyboard focus to the main window's list after the dialog closes. No-op when the
    /// dialog was shown standalone (no owner) - there is no list to return to in that case, and the
    /// window is going away anyway.
    /// </summary>
    internal void RestoreFocus()
    {
        if (Owner is MainWindow main)
            main.RestoreFocusToList();
    }
}
