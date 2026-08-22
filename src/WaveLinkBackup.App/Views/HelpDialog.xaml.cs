using System.Windows;
using WaveLinkBackup.App.ViewModels;
using WaveLinkBackup.App.Windows;

namespace WaveLinkBackup.App.Views;

/// <summary>
/// The help dialog's view. It renders a <see cref="HelpDialogModel"/> and nothing else - every
/// element in the XAML binds to it and computes nothing, so there is no logic here to test beyond
/// the one browser launch the footer link performs. Close is IsCancel, so dismissing needs no
/// handler at all: WPF closes the window on its own for Escape, Alt+F4 and the button alike.
/// </summary>
public partial class HelpDialog : Window
{
    public HelpDialog(HelpDialogModel model)
    {
        InitializeComponent();

        DataContext = model;

        // Cover the owner, dim it, frost it - the same overlay every other dialog uses. Without an
        // owner (a standalone run, or a view test) this keeps its own geometry and gets no blur.
        DialogOverlay.Attach(this);

        // The documentation link opens the user's browser. Nothing is reported when it fails: the
        // link is a convenience, and a second error about a browser that would not start buries the
        // help text the user was actually reading (the same rule Settings' "What changed" follows).
        DocumentationHyperlink.RequestNavigate += (_, e) => OpenInBrowser(e.Uri?.AbsoluteUri);

        Loaded += (_, _) =>
        {
            // A null URL means none is configured, and a link that points nowhere is worse than no
            // link (the same rule as App.ReleaseSource's IsConfigured). Collapsing here rather than
            // in the XAML keeps the model free of WPF types.
            if ((model.DocumentationUrl is null)) DocumentationLink.Visibility = Visibility.Collapsed;

            // Focus starts on Close - the only action this dialog has, so a keyboard user who hits
            // Enter immediately dismisses rather than does nothing.
            CloseButton.Focus();
        };
    }

    /// <summary>
    /// Opens a page in the user's browser. Swallows a browser that would not start - see the note
    /// on the RequestNavigate handler above for why.
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
}
