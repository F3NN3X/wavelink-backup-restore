using System.Windows;
using System.Windows.Controls;
using WaveLinkBackup.App.ViewModels;

namespace WaveLinkBackup.App.Views;

/// <summary>
/// The view for the three decision dialogs (06-errors.md "Dialogs"): error 2 (two installations,
/// chooser), error 4 (malformed settings, amber) and error 8 (newer version). It renders an
/// <see cref="ErrorDialogModel"/> and reports the user's decision back through <see cref="ShowDialog"/>
/// - nothing here touches Core, does I/O, or decides anything the model has not already decided.
///
/// Outcomes, read by the caller after <see cref="ShowDialog"/> returns:
///   <see cref="Confirmed"/>          true when the primary button was clicked (Use this one / Try again / Get the update).
///   <see cref="SelectedInstallPath"/> error 2 only - the path of the radio the user picked; null otherwise.
///   <see cref="RememberChosen"/>      error 2 only - whether "Remember this one and stop asking" was ticked.
///   <see cref="ClickedGhost"/>        true when the ghost footer action (error 4 "Open the folder") was clicked.
/// Cancel / Close / Escape all leave Confirmed false.
/// </summary>
public partial class ErrorDialog : Window
{
    private bool confirmed;
    private bool clickedGhost;

    /// <summary>True when the primary button was clicked.</summary>
    public bool Confirmed => confirmed;

    /// <summary>The installation path the user picked (error 2 only); null for errors 4 and 8.</summary>
    public string? SelectedInstallPath
    {
        get
        {
            if (ChooserList.ItemsSource is not System.Collections.IEnumerable options) return null;

            // The ItemsControl has no container lookup (that is a Selector thing), so the radios are
            // reached by walking its visual tree. They come back in item order, which matches the
            // Options list order - each radio's row binds to the option at the same index.
            var radios = CollectRadioButtons(ChooserList);

            var index = 0;
            foreach (var item in options)
            {
                if (item is ErrorInstallOption option &&
                    index < radios.Count &&
                    radios[index].IsChecked == true)
                    return option.Path;

                index++;
            }

            return null;
        }
    }

    /// <summary>Whether "Remember this one and stop asking" was ticked (error 2 only).</summary>
    public bool RememberChosen => RememberCheckbox.IsChecked == true;

    /// <summary>True when the ghost footer action (error 4 "Open the folder") was clicked.</summary>
    public bool ClickedGhost => clickedGhost;

    public ErrorDialog(ErrorDialogModel model)
    {
        InitializeComponent();

        DataContext = model;

        // The note block's fill is a DECISION, not a trigger: error 4 (malformed settings) is the
        // only amber of the three - there the live configuration file is the thing that cannot be
        // read. A DataTrigger swapping brushes here is unreliable when the window is shown without a
        // full render pass (it never fires, leaving both brushes neutral), and it would make the
        // variant untestable. Setting them from the model's weight at construction is deterministic:
        // the decision lives where the weight lives, and a test can assert the resulting brushes.
        if (model.Weight == ErrorWeight.Amber)
        {
            NoteBlock.Background = (System.Windows.Media.Brush)FindResource("WlWarnSoft");
            NoteBlock.BorderBrush = (System.Windows.Media.Brush)FindResource("WlWarn");
        }

        PrimaryButton.Click += (_, _) => { confirmed = true; DialogResult = true; };
        SecondaryButton.Click += (_, _) => { DialogResult = false; };
        GhostButton.Click += (_, _) =>
        {
            clickedGhost = true;
            DialogResult = false;
        };

        Loaded += (_, _) => SecondaryButton.Focus();
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

    /// <summary>
    /// Collects the chooser's RadioButtons in item order. A plain ItemsControl generates a
    /// ContentPresenter per item with no stable x:Name inside the template, so the radios are found
    /// by a depth-first visual walk; WPF materialises the rows top to bottom, which is the same
    /// order as <see cref="ErrorDialogModel.Options"/>.
    /// </summary>
    private static System.Collections.Generic.List<RadioButton> CollectRadioButtons(DependencyObject node)
    {
        var found = new System.Collections.Generic.List<RadioButton>();
        Gather(node, found);
        return found;

        static void Gather(DependencyObject current, System.Collections.Generic.List<RadioButton> into)
        {
            var count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(current);

            for (var i = 0; i < count; i++)
            {
                var child = System.Windows.Media.VisualTreeHelper.GetChild(current, i);

                if (child is RadioButton radio) into.Add(radio);

                Gather(child, into);
            }
        }
    }
}
