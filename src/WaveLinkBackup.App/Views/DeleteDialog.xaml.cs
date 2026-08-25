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
        // same as clicking it. Enter is handled on the button itself rather than through
        // IsDefault - see the note below.
        DeleteButton.Click += (_, _) => { DialogResult = true; };

        // The design decisions log, section 6: "Enter fires the primary button - except Delete and Restore,
        // where focus starts on Cancel and the destructive button must be reached deliberately
        // (Tab or click)." IsDefault would break exactly that: a default button takes Enter from
        // ANYWHERE in the dialog, focus on Cancel included, so the app's most destructive key was
        // the first one a user pressed. It is off, and Enter is wired to the button itself so it
        // still confirms once the user has actually tabbed onto it.
        DeleteButton.KeyDown += (_, e) =>
        {
            if (e.Key != System.Windows.Input.Key.Enter) return;

            DialogResult = true;
            e.Handled = true;
        };

        CancelButton.Click += (_, _) => { DialogResult = false; };
        BackUpNowButton.Click += (_, _) =>
        {
            backUpNowInstead = true;
            DialogResult = false;
        };

        Loaded += (_, _) => CancelButton.Focus();
    }

    /// <summary>Escape cancels, matching the keyboard rule in the design decisions log (section 6).</summary>
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
