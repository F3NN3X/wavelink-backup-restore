using System.Windows;
using WaveLinkBackup.App.Windows;
using WaveLinkBackup.App.ViewModels;

namespace WaveLinkBackup.App.Views;

/// <summary>
/// The empty-trash confirmation's view. It renders an <see cref="EmptyTrashDialogModel"/> and
/// reports the user's decision back through <see cref="ShowDialog"/> - nothing here touches Core,
/// does I/O, or decides anything the model has not already decided.
///
/// There is exactly one shape (the irreversible case), so there is no variant flag to read back:
/// DialogResult true means "empty it", false means Cancel / Escape. Focus starts on Cancel - the
/// safe choice for an action with no undo - and Escape is equivalent to it.
/// </summary>
public partial class EmptyTrashDialog : Window
{
    public EmptyTrashDialog(EmptyTrashDialogModel model)
    {
        InitializeComponent();

        DataContext = model;

        // Cover the owner, dim it, frost it. Everything happens on SourceInitialized inside
        // Attach; a dialog with no owner (a standalone run, or a view test) keeps its own
        // geometry and gets no blur, because there is nothing to cover.
        DialogOverlay.Attach(this);

        // Focus starts on Cancel - the safe choice for an irreversible action - and Escape is the
        // same as clicking it. Confirm is IsDefault, so Enter confirms only when the user has
        // deliberately moved there or pressed it; both paths are wired below.
        ConfirmButton.Click += (_, _) => { DialogResult = true; };
        CancelButton.Click += (_, _) => { DialogResult = false; };

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
