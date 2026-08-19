using System.Windows;
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

    public SettingsDialog(SettingsViewModel model)
    {
        this.model = model;
        InitializeComponent();
        DataContext = model;

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

        // Focus the Close button when the dialog opens: it is the safe action and the only one that
        // needs no thought, so a keyboard user who hits Enter immediately dismisses rather than
        // changes something.
        Loaded += (_, _) => FooterCloseButton.Focus();
    }
}
