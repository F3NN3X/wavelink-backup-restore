using System.Windows;
using WaveLinkBackup.App.ViewModels;
using WaveLinkBackup.App.Windows;

namespace WaveLinkBackup.App.Views;

/// <summary>
/// The about dialog's view. It renders an <see cref="AboutDialogModel"/> and nothing else - every
/// element in the XAML binds to it and computes nothing, so there is no logic here to test beyond
/// the two browser launches the footer links perform. OK is IsCancel, so dismissing needs no
/// handler at all: WPF closes the window on its own for Escape, Alt+F4 and the button alike.
/// </summary>
public partial class AboutDialog : Window
{
    public AboutDialog(AboutDialogModel model)
    {
        InitializeComponent();

        DataContext = model;

        // Cover the owner, dim it, frost it - the same overlay every other dialog uses. Without an
        // owner (a standalone run, or a view test) this keeps its own geometry and gets no blur.
        DialogOverlay.Attach(this);

        // The links open the user's browser. Nothing is reported when one fails: they are a
        // convenience, and a second error about a browser that would not start buries the version
        // line the user was actually reading (the same rule Settings' "What changed" follows). A
        // link whose URL is null never renders at all, so these only fire with a real target.
        ReleasesHyperlink.RequestNavigate += (_, e) => OpenInBrowser(e.Uri?.AbsoluteUri);
        RepositoryHyperlink.RequestNavigate += (_, e) => OpenInBrowser(e.Uri?.AbsoluteUri);

        Loaded += (_, _) =>
        {
            // A null URL means none is configured, and a link that points nowhere is worse than no
            // link (the same rule as App.ReleaseSource's IsConfigured). Collapsing here rather than
            // in the XAML keeps the model free of WPF types.
            if (model.ReleasesUrl is null) ReleasesLink.Visibility = Visibility.Collapsed;
            if (model.RepositoryUrl is null) RepositoryLink.Visibility = Visibility.Collapsed;

            // Focus starts on OK - the only action this dialog has, so a keyboard user who hits
            // Enter immediately dismisses rather than does nothing.
            OkButton.Focus();
        };
    }

    /// <summary>
    /// Opens a page in the user's browser. Swallows a browser that would not start - see the note
    /// on the RequestNavigate handlers above for why.
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
