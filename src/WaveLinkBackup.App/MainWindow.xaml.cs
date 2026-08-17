using System.ComponentModel;

namespace WaveLinkBackup.App;

public partial class MainWindow : System.Windows.Window
{
    public MainWindow() => InitializeComponent();

    /// <summary>
    /// Closing hides. The app is the process, not this window — if closing it stopped the
    /// backups, the app would fail its own promise.
    ///
    /// The setting that turns this off lives in the shell's own file, not in BackupSettings:
    /// Core has no window to hide (plan 3 builds the Settings UI for it).
    /// </summary>
    protected override void OnClosing(CancelEventArgs e)
    {
        // Except on the way out. Application.Shutdown closes every window, and a Closing that
        // always cancels would fight the one path that is genuinely meant to end the process.
        if (System.Windows.Application.Current is not App { IsShuttingDown: true })
        {
            e.Cancel = true;
            Hide();
        }

        base.OnClosing(e);
    }
}
