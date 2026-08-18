using System.Windows;
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

        // Focus starts on Cancel - the safe choice for an irreversible action - and Escape is the
        // same as clicking it. The destructive button is IsDefault, so Enter confirms only when the
        // user has deliberately moved there or pressed it; both paths are wired below.
        RestoreButton.Click += (_, _) => { DialogResult = true; };
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
