namespace WaveLinkBackup.Core.Automation;

/// <summary>
/// The real watcher. Watch, don't poll: polling is worse on both latency and cost, with no
/// compensating simplicity, since the file sits in one known directory. ADR-007.
///
/// Deliberately dumb. It raises an event per write and takes no view on whether that write
/// matters - debouncing, rate limiting and dedup all live elsewhere, where they are testable
/// without a filesystem.
/// </summary>
public sealed class FileSystemSettingsWatcher : ISettingsWatcher
{
    private readonly FileSystemWatcher watcher;
    private bool disposed;

    public FileSystemSettingsWatcher(string localStatePath, string fileName = "Settings.json")
    {
        watcher = new FileSystemWatcher(localStatePath, fileName)
        {
            // LastWrite covers the ordinary save. CreationTime and FileName cover Wave Link's
            // atomic-save path, which REPLACES the file rather than writing through it - a
            // watcher filtered to LastWrite alone would miss exactly the saves that matter.
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.CreationTime | NotifyFilters.FileName,
            IncludeSubdirectories = false,
        };

        watcher.Changed += Raise;
        watcher.Created += Raise;
        watcher.Renamed += Raise;

        // A buffer overflow under load means events were dropped. That is a LATENCY problem,
        // not data loss: the next write, the next shutdown or the next launch reconciles by
        // content hash. Raising here turns a missed burst into one extra evaluation.
        watcher.Error += (_, _) => SettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    public event EventHandler? SettingsChanged;

    public void Start() => watcher.EnableRaisingEvents = true;

    public void Stop() => watcher.EnableRaisingEvents = false;

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;

        watcher.EnableRaisingEvents = false;
        watcher.Dispose();
    }

    private void Raise(object sender, FileSystemEventArgs e) =>
        SettingsChanged?.Invoke(this, EventArgs.Empty);
}
