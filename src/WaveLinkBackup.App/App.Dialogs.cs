using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using H.NotifyIcon;
using H.NotifyIcon.Core;
using WaveLinkBackup.App.Hosting;
using WaveLinkBackup.App.Services;
using WaveLinkBackup.App.Startup;
using WaveLinkBackup.App.Updates;
using WaveLinkBackup.App.Theming;
using WaveLinkBackup.App.ViewModels;
using WaveLinkBackup.App.Views;
using WaveLinkBackup.App.Windows;
using WaveLinkBackup.Core.Abstractions;
using WaveLinkBackup.Core.Analysis;
using WaveLinkBackup.Core.Automation;
using WaveLinkBackup.Core.Capture;
using WaveLinkBackup.Core.Discovery;
using WaveLinkBackup.Core.Io;
using WaveLinkBackup.Core.Process;
using WaveLinkBackup.Core.Results;
using WaveLinkBackup.Core.Snapshots;

namespace WaveLinkBackup.App;

/// <summary>Opening the windows the tray owns, and the notifications it raises.</summary>
public partial class App
{

    /// <summary>
    /// Error 8's "Get the update": Settings, at UPDATES, with a check already running so the new
    /// version's row is showing by the time the user looks at it (the tray and updates spec).
    ///
    /// The check is the ONLY thing started automatically here. Installing is still a press. "It
    /// never installs anything without you" holds on this path exactly as it does on every other.
    /// </summary>
    internal void OpenSettingsAtUpdates() => OpenSettings(scrollToUpdates: true);

    /// <summary>
    /// Internal so MainWindow's own gear button can open the same dialog. Builds a fresh view-model
    /// each time (the file may have changed while the window was closed) and shows it modally over
    /// the main window when one is open, otherwise as a standalone modal.
    /// </summary>
    internal void OpenSettings() => OpenSettings(scrollToUpdates: false);

    /// <summary>
    /// "What's in this backup": read the snapshot's own settings file, describe it, show it.
    ///
    /// The READ lives here rather than in the window because the window has neither the store nor
    /// the file system. It is a single read of a file that is typically 47 KB and always local, on
    /// a press - so it is synchronous, and a store on a sleeping network drive is the only case
    /// where that is felt, which is the same trade every other row action already makes.
    ///
    /// Every failure - a snapshot that has gone, a file that cannot be read, one that is not
    /// settings at all - becomes a sentence INSIDE the dialog rather than a refusal to open it.
    /// A damaged backup is precisely when someone wants to know what was in it.
    /// </summary>
    internal void OpenSnapshotDetails(Window owner, string snapshotId)
    {
        if (store is null || fileSystem is null) return;

        var found = store.Get(snapshotId);
        if (!found.IsSuccess) return;

        var snapshot = found.Value;

        Result<ConfigurationDetail> read;
        try
        {
            read = ConfigurationDetail.Read(fileSystem.ReadSharedBytes(snapshot.SettingsPath));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            read = Result<ConfigurationDetail>.Fail(
                new SettingsUnreadable(snapshot.SettingsPath, ex.Message));
        }

        var dialog = new Views.SnapshotDetailsDialog(SnapshotDetailsModel.For(snapshot, read));

        if (owner.IsLoaded)
        {
            dialog.Owner = owner;
            dialog.ShowDialog();
        }
        else
        {
            dialog.Show();
        }
    }

    private void OpenSettings(bool scrollToUpdates)
    {
        var vm = BuildSettingsViewModel();
        var dialog = new Views.SettingsDialog(vm) { ScrollToUpdates = scrollToUpdates };
        ShowOverMainWindow(dialog);
    }

    /// <summary>
    /// Internal so MainWindow's own "?" button can open the same dialog. The content is static copy
    /// (HelpDialogModel.Default), so there is nothing to build fresh each time - but the owner
    /// handling is the same as Settings': modal over the main window when one is open, standalone
    /// otherwise (the tray menu's entry point runs with no window at all).
    /// </summary>
    internal void OpenHelp() => ShowOverMainWindow(new Views.HelpDialog(ViewModels.HelpDialogModel.Build()));

    /// <summary>
    /// Internal so MainWindow can open it if a future revision adds an entry point there. The model
    /// is built fresh each time because its version and links are read from the assembly and
    /// environment at construction - the same "build a fresh view-model each time" rule as Settings.
    /// </summary>
    internal void OpenAbout() => ShowOverMainWindow(new Views.AboutDialog(ViewModels.AboutDialogModel.Build()));

    /// <summary>
    /// Shows a dialog modally over the main window when one is open, standalone otherwise. The two
    /// lines every "open a dialog" seam used to repeat - Settings above, Help and About below - in
    /// one place, so a third dialog does not copy them again.
    /// </summary>
    private static void ShowOverMainWindow(Window dialog)
    {
        var owner = (Application.Current as App)?.MainWindow is { } main && main.IsLoaded ? main : null;
        if (owner is not null)
        {
            dialog.Owner = owner;
            dialog.ShowDialog();
        }
        else
        {
            dialog.Show();
        }
    }

    /// <summary>
    /// The two designed tray notifications, and their rules. Held rather than constructed per call
    /// because the nine-day notice fires ONCE per episode, which is state.
    /// </summary>
    private readonly TrayNotifications notifications = new();

    /// <summary>
    /// Show one, with its action wired.
    ///
    /// Its action is the whole notification, not a button. The design draws each notice with a
    /// labelled action; a classic tray balloon has no buttons, and Windows renders one as a toast
    /// that drops them. Real toast buttons need an AppUserModelID and a Start-menu shortcut - an
    /// installer concern this app does not have yet. So the label is stated in the body and
    /// clicking anywhere on the notice does the thing, which keeps the action reachable and says
    /// what it is. Recorded in technical-debt.md §4.21 item 6 rather than left as a silent
    /// difference from the design.
    /// </summary>
    private void Notify(TrayNotification? notification)
    {
        if (notification is null || tray is null) return;

        pendingNotificationAction = notification.Kind switch
        {
            TrayNotificationKind.NothingBackedUp => () =>
            {
                ShowMainWindow();
                OpenSettings();
            },
            TrayNotificationKind.WaveLinkReset => ShowMainWindow,
            TrayNotificationKind.UpdateAvailable or TrayNotificationKind.UpdateFailed => () =>
            {
                ShowMainWindow();
                OpenSettings();
            },
            _ => null,
        };

        tray.ShowNotification(
            notification.Title,
            $"{notification.Body}\n\n{notification.ActionLabel}");
    }

    /// <summary>
    /// The second designed notification: Wave Link rejected a restored backup and reset the live
    /// configuration. Raised by the window when a restore comes back Rejected, because the window
    /// may well be hidden at that point. A headless restore is a supported path, and the strip it
    /// draws is no use to somebody who is not looking at it.
    /// </summary>
    internal void NotifyWaveLinkReset() =>
        Notify(TrayNotifications.WaveLinkReset(Core.Restore.RestoreOrchestrator.PreRestoreName));
}
