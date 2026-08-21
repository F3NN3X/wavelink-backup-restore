using System.Windows;
using WaveLinkBackup.App.ViewModels;
using WaveLinkBackup.App.Windows;

namespace WaveLinkBackup.App.Views;

/// <summary>
/// "What's in this backup": every channel, its effect chain in order, and the mixes with the
/// devices they play out of.
///
/// A pure renderer, like <see cref="RestoreDialog"/> - it takes a <see cref="SnapshotDetailsModel"/>
/// and binds to it. The file read and the parse both happen in App before this window exists, so
/// there is no IO here and no failure to handle: an unreadable backup arrives as a sentence to
/// show. Escape and both Close buttons are IsCancel, so dismissing needs no handler.
/// </summary>
public partial class SnapshotDetailsDialog : Window
{
    public SnapshotDetailsDialog(SnapshotDetailsModel model)
    {
        InitializeComponent();
        DataContext = model;

        // The same overlay every other dialog uses: cover the owner, dim it, frost it. Without an
        // owner (a view test) the window keeps its own geometry and gets no blur.
        DialogOverlay.Attach(this);
    }
}
