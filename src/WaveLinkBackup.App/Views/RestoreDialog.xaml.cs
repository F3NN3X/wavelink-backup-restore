using System.Windows;
using WaveLinkBackup.App.Windows;
using WaveLinkBackup.App.ViewModels;

namespace WaveLinkBackup.App.Views;

/// <summary>
/// Screen 2's view: the restore confirmation. It renders a <see cref="RestoreDialogModel"/> and
/// reports the user's decision back through <see cref="ShowDialog"/> - nothing here touches Core,
/// does I/O, or decides anything the model has not already decided.
/// </summary>
public partial class RestoreDialog : Window
{
    public RestoreDialog(RestoreDialogModel model)
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
        RestoreButton.Click += (_, _) => { DialogResult = true; };

        // 10-decisions section 6: "Enter fires the primary button - except Delete and Restore,
        // where focus starts on Cancel and the destructive button must be reached deliberately
        // (Tab or click)." IsDefault would break exactly that: a default button takes Enter from
        // ANYWHERE in the dialog, focus on Cancel included, so the app's most destructive key was
        // the first one a user pressed. It is off, and Enter is wired to the button itself so it
        // still confirms once the user has actually tabbed onto it.
        RestoreButton.KeyDown += (_, e) =>
        {
            if (e.Key != System.Windows.Input.Key.Enter) return;

            DialogResult = true;
            e.Handled = true;
        };

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
