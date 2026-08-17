namespace WaveLinkBackup.App.Windows;

/// <summary>
/// Three states, not two. The design requires the toggle to READ BACK what Task Manager did
/// rather than fight it, and a boolean cannot express "off, and you may not turn it on".
/// </summary>
public enum AutostartState
{
    Off,
    On,
    BlockedByTaskManager,
}

public interface IAutostart
{
    AutostartState Read();

    /// <summary>Returns false when Task Manager holds a veto. Nothing is written in that case.</summary>
    bool Enable();

    void Disable();
}
