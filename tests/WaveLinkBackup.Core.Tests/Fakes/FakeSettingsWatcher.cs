using WaveLinkBackup.Core.Automation;

namespace WaveLinkBackup.Core.Tests.Fakes;

/// <summary>
/// Raises "the file changed" on demand. The seam that makes every timing test in this suite
/// instantaneous — there is no filesystem event to provoke and no delay to wait through.
/// </summary>
public sealed class FakeSettingsWatcher : ISettingsWatcher
{
    public event EventHandler? SettingsChanged;

    public bool Started { get; private set; }
    public bool Disposed { get; private set; }

    public void Start() => Started = true;

    public void Stop() => Started = false;

    /// <summary>One write. Call it several times to model a burst.</summary>
    public void RaiseChange() => SettingsChanged?.Invoke(this, EventArgs.Empty);

    public void Dispose() => Disposed = true;
}
