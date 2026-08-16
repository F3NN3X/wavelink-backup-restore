namespace WaveLinkBackup.Core.Discovery;

/// <summary>
/// A resolved Wave Link installation.
/// </summary>
/// <param name="SettingsPath">The 43 KB file that is the entire product.</param>
/// <param name="PackageFamilyName">
/// e.g. <c>Elgato.WaveLink_g54w8ztgkx496</c>. Needed to relaunch via shell AppID. Empty
/// when the location came from an explicit path outside a package, in which case relaunch
/// is not possible and callers must say so rather than fail obscurely.
/// </param>
/// <param name="LocalStatePath">Where Wave Link's own backups and logs live.</param>
/// <param name="LogsPath">The only trustworthy place to confirm a restore worked.</param>
public sealed record SettingsLocation(
    string SettingsPath,
    string PackageFamilyName,
    string LocalStatePath,
    string LogsPath)
{
    /// <summary>False for an explicitly-supplied path with no package around it.</summary>
    public bool CanRelaunch => !string.IsNullOrEmpty(PackageFamilyName);
}
