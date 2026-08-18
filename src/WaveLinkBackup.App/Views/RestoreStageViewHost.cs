using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using WaveLinkBackup.App.ViewModels;

namespace WaveLinkBackup.App.Views;

/// <summary>
/// Renders one stage of the restore in-progress strip (04-in-progress.md) with its three
/// treatments, driven entirely by the bound <see cref="RestoreStageView"/>:
///
///   done     14px check in WlOk + mono 500 11px ls .14em WlMuted
///   current  mono 500 11px ls .14em WlStrong, 4px bottom pad, 2px solid WlAccent bottom rule
///   pending  mono 500 11px ls .14em WlMuted at 40% opacity
///
/// The model owns the status; this control only maps each one to its visual. A separate user
/// control (rather than four inline DataTrigger blocks in MainWindow.xaml) keeps the treatment
/// logic in one place - the strip's XAML then just places four of these with their connectors.
/// </summary>
public partial class RestoreStageViewHost : UserControl
{
    public static readonly DependencyProperty StageProperty = DependencyProperty.Register(
        nameof(Stage), typeof(RestoreStageView), typeof(RestoreStageViewHost),
        new PropertyMetadata(null, OnStageChanged));

    /// <summary>The stage row to render: its label and its current status.</summary>
    public RestoreStageView? Stage
    {
        get => (RestoreStageView?)GetValue(StageProperty);
        set => SetValue(StageProperty, value);
    }

    public RestoreStageViewHost()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Map the bound stage's status to its visual treatment. The model raises Stages when the
    /// frontier moves (RestoreProgressModel.Advance/Complete replace the row record), so this
    /// fires on every transition - no timer, no polling.
    /// </summary>
    private static void OnStageChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var host = (RestoreStageViewHost)d;
        if (e.NewValue is not RestoreStageView stage) return;

        host.Label.Text = stage.Label;

        switch (stage.Status)
        {
            case StageStatus.Done:
                host.CheckMark.Visibility = Visibility.Visible;
                host.Rule.BorderThickness = new Thickness(0); // no accent rule when done
                SetLabel(host, "WlMuted", FontWeights.Normal);
                break;

            case StageStatus.Current:
                host.CheckMark.Visibility = Visibility.Collapsed;
                host.Rule.SetResourceReference(BorderBrushProperty, "WlAccent");
                host.Rule.BorderThickness = new Thickness(0, 0, 0, 2); // the 2px accent bottom rule
                SetLabel(host, "WlStrong", FontWeights.Medium);
                break;

            default: // Pending
                host.CheckMark.Visibility = Visibility.Collapsed;
                host.Rule.BorderThickness = new Thickness(0);
                SetLabel(host, "WlMuted", FontWeights.Medium);
                break;
        }

        // Only pending is dimmed at 40%; done and current are full opacity.
        host.Opacity = stage.Status == StageStatus.Pending ? 0.4 : 1.0;

        host.UpdateAutomationName(stage);
    }

    /// <summary>Point the label's foreground at a theme brush by key, keeping it live across themes.</summary>
    private static void SetLabel(RestoreStageViewHost host, string brushKey, FontWeight weight)
    {
        host.Label.SetResourceReference(TrackedText.ForegroundProperty, brushKey);
        host.Label.FontWeight = weight;
    }

    /// <summary>
    /// Give this stage an AutomationProperties.Name so a screen reader can announce it by name as
    /// the frontier advances - e.g. "WRITING SETTINGS, in progress" when it becomes current and
    /// "CLOSING WAVE LINK, done" once it is complete. Pending stages read just their label; they
    /// are not yet happening, so no status suffix would mislead. The name is recomputed on every
    /// OnStageChanged, which fires whenever the model replaces this row's record (Advance/Complete).
    /// </summary>
    private void UpdateAutomationName(RestoreStageView stage)
    {
        var suffix = stage.Status switch
        {
            StageStatus.Current => ", in progress",
            StageStatus.Done => ", done",
            _ => string.Empty, // Pending: just the label.
        };

        AutomationProperties.SetName(this, stage.Label + suffix);
    }
}
