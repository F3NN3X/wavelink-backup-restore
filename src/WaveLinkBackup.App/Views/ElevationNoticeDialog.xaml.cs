using System.Windows;
using WaveLinkBackup.App.Windows;

namespace WaveLinkBackup.App.Views;

/// <summary>
/// The inform-then-elevate notice (screens/13-elevation.md). A borderless modal that explains WHY
/// Windows is about to ask for administrator rights before the elevated restore copy is launched -
/// the UAC prompt stays the consent gate, but it no longer appears unexplained.
///
/// It renders nothing from a model: the text is fixed by the situation (a running Wave Link above
/// this process's integrity level), so there is no ViewModel to set up. The caller reads the
/// decision from <see cref="ShowDialog"/> - true means "continue with administrator rights", false
/// means Cancel, Escape, or the window closed without a choice, and nothing was touched.
/// </summary>
public partial class ElevationNoticeDialog : Window
{
    public ElevationNoticeDialog()
    {
        InitializeComponent();

        // Cover the owner, dim it, frost it. Everything happens on SourceInitialized inside Attach;
        // a dialog with no owner (a standalone run, or a view test) keeps its own geometry and gets
        // no blur, because there is nothing to cover.
        DialogOverlay.Attach(this);

        ContinueButton.Click += (_, _) => { DialogResult = true; };

        // Enter confirms only once the user has actually tabbed onto Continue - IsDefault would take
        // Enter from anywhere in the dialog, focus on Cancel included, which is exactly the trap the
        // destructive buttons avoid (10-decisions section 6). Wired here rather than via IsDefault.
        ContinueButton.KeyDown += (_, e) =>
        {
            if (e.Key != System.Windows.Input.Key.Enter) return;

            DialogResult = true;
            e.Handled = true;
        };

        CancelButton.Click += (_, _) => { DialogResult = false; };

        // Focus starts on the safe choice: declining changes nothing, continuing asks Windows for
        // rights. Escape is the same as clicking it.
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
