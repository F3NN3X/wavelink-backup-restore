using System.Windows;
using WaveLinkBackup.App.Windows;
using WaveLinkBackup.App.ViewModels;

namespace WaveLinkBackup.App.Views;

/// <summary>
/// Screen 5's view: the delete confirmation, in its three variants. It renders a
/// <see cref="DeleteDialogModel"/> and reports the user's decision back through
/// <see cref="ShowDialog"/> - nothing here touches Core, does I/O, or decides anything the model
/// has not already decided.
///
/// The OnlyBackup variant exposes a third outcome - "Back up now instead" - which the caller reads
/// from <see cref="ClickedBackUpNowInstead"/> after the dialog closes with DialogResult false.
/// </summary>
public partial class DeleteDialog : Window
{
    private bool backUpNowInstead;

    /// <summary>
    /// True when the user chose "Back up now instead" (OnlyBackup variant only). Read by the caller
    /// after <see cref="ShowDialog"/> returns false - that path is Cancel, Escape, or this button.
    /// </summary>
    public bool ClickedBackUpNowInstead => backUpNowInstead;

    public DeleteDialog(DeleteDialogModel model)
    {
        InitializeComponent();

        DataContext = model;

        // Cover the owner, dim it, frost it. Everything happens on SourceInitialized inside
        // Attach; a dialog with no owner (a standalone run, or a view test) keeps its own
        // geometry and gets no blur, because there is nothing to cover.
        DialogOverlay.Attach(this);

        // Focus starts on Cancel - the safe choice for an irreversible action - and Escape is the
        // same as clicking it. Delete is IsDefault, so Enter confirms only when the user has
        // deliberately moved there or pressed it; both paths are wired below.
        DeleteButton.Click += (_, _) => { DialogResult = true; };
        CancelButton.Click += (_, _) => { DialogResult = false; };
        BackUpNowButton.Click += (_, _) =>
        {
            backUpNowInstead = true;
            DialogResult = false;
        };

        Loaded += (_, _) => CancelButton.Focus();
    }

    /// <summary>Escape cancels, matching the keyboard rule in 10-decisions (section 6).</summary>
    protected override void OnKeyDown(System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Escape)
        {
            DialogResult = false;
            e.Handled = true;
        }

        base.OnKeyDown(e);
    }
}
