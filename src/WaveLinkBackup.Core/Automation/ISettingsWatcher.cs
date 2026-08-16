namespace WaveLinkBackup.Core.Automation;

/// <summary>
/// Notices that Wave Link wrote its settings file.
///
/// A seam because a test must be able to say "the file changed" without a real filesystem
/// event, and because the watcher has to be startable and stoppable from a shell that does
/// not exist yet.
/// </summary>
public interface ISettingsWatcher : IDisposable
{
    /// <summary>Raised on every write. Bursts are expected; debouncing is not this type's job.</summary>
    event EventHandler? SettingsChanged;

    void Start();

    void Stop();
}
