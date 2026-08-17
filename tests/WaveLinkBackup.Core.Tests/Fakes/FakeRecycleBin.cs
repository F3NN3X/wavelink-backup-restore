using WaveLinkBackup.Core.Abstractions;
using WaveLinkBackup.Core.Results;

namespace WaveLinkBackup.Core.Tests.Fakes;

/// <summary>
/// Stands in for the shell.
///
/// It takes the fake filesystem because <c>SHFileOperation</c> with <c>FOF_ALLOWUNDO</c>
/// **moves** the directory — sending it to the Recycle Bin IS the removal, and
/// <c>EmptyTrash</c> must not delete afterwards. A fake that only recorded the call would let
/// a double-delete through unnoticed.
///
/// The interesting setting is <see cref="Available"/>: the Recycle Bin does not exist on
/// network shares or many removable volumes, and the store is user-chosen, so "unavailable" is
/// a normal condition rather than an error.
/// </summary>
public sealed class FakeRecycleBin(FakeFileSystem? fileSystem = null) : IRecycleBin
{
    /// <summary>False models a network or removable store, where deletion is permanent.</summary>
    public bool Available { get; set; } = true;

    public List<string> Recycled { get; } = [];

    public bool IsAvailableFor(string path)
    {
        _ = path;
        return Available;
    }

    public Result Send(string path)
    {
        Recycled.Add(path);
        fileSystem?.DeleteDirectory(path);
        return Result.Ok();
    }
}
