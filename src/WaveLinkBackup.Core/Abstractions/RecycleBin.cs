using System.Runtime.InteropServices;
using WaveLinkBackup.Core.Results;

namespace WaveLinkBackup.Core.Abstractions;

/// <summary>
/// The real Recycle Bin, via <c>SHFileOperationW</c>.
///
/// LIVES IN CORE, which departs from the note in technical-debt.md 7.1 that said "implemented
/// per shell". That note was written expecting interop to need the Windows Desktop ref pack.
/// It does not: <see cref="LibraryImportAttribute"/> P/Invoke works from plain
/// <c>net10.0</c>, and <c>GuardNoDesktopFramework</c> guards the ref pack, not P/Invoke.
///
/// The alternative, implementing it in both shells, would duplicate the interop, and a
/// fourth project for one class is worse than either. Core already contains Windows-specific
/// behaviour for the same reason: <c>WaveLinkProcess.LaunchByAppId</c> shells out to
/// <c>explorer.exe</c> with an MSIX AppID. Core is Windows-only in behaviour (ADR-008) and
/// headless in dependencies; this is the second and not the first.
///
/// Uses <c>DllImport</c> rather than the newer <c>LibraryImport</c> deliberately: the source
/// generator requires <c>AllowUnsafeBlocks</c> for the whole project, and granting unsafe to a
/// carefully conservative library for one call that runs when someone clicks *Empty trash* is
/// a poor trade. Both are equally NativeAOT-compatible; the difference is marshalling cost on
/// a call that happens by hand, at most, once a session.
/// </summary>
public sealed class RecycleBin : IRecycleBin
{
    private const uint FoDelete = 0x0003;

    private const ushort FofSilent = 0x0004;
    private const ushort FofNoConfirmation = 0x0010;
    private const ushort FofAllowUndo = 0x0040;      // the whole point
    private const ushort FofNoErrorUi = 0x0400;
    private const ushort FofNoConfirmMkDir = 0x0200;

    /// <summary>
    /// Whether this path has a Recycle Bin at all.
    ///
    /// UNC paths and non-fixed volumes do not, and the backup store is user-chosen, so a
    /// person keeping backups on a NAS is a normal case, not an edge one. Asking first is what
    /// lets the UI promise an undo only when it can keep the promise.
    /// </summary>
    public bool IsAvailableFor(string path)
    {
        try
        {
            var full = Path.GetFullPath(path);

            // \\server\share has no drive root and no Recycle Bin.
            if (full.StartsWith(@"\\", StringComparison.Ordinal)) return false;

            var root = Path.GetPathRoot(full);
            if (string.IsNullOrEmpty(root)) return false;

            // Removable and network volumes either lack a Recycle Bin or bypass it silently.
            return new DriveInfo(root).DriveType == DriveType.Fixed;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        {
            // Cannot tell: assume not, so the caller says "permanent" rather than promising
            // an undo that may not exist. Failing toward the honest message.
            return false;
        }
    }

    public Result Send(string path)
    {
        // pFrom is a double-null-terminated list, not a plain string. A single terminator
        // reads past the end of the intended value.
        var from = path + '\0' + '\0';
        var buffer = Marshal.StringToHGlobalUni(from);

        try
        {
            var operation = new ShFileOpStruct
            {
                Hwnd = IntPtr.Zero,
                Func = FoDelete,
                From = buffer,
                To = IntPtr.Zero,
                Flags = FofAllowUndo | FofNoConfirmation | FofNoErrorUi | FofSilent | FofNoConfirmMkDir,
                AnyOperationsAborted = 0,
                NameMappings = IntPtr.Zero,
                ProgressTitle = IntPtr.Zero,
            };

            var result = ShFileOperation(ref operation);

            if (result != 0)
            {
                return new StoreUnavailable(path, $"the Recycle Bin refused it (code {result}).");
            }

            return operation.AnyOperationsAborted != 0
                ? new StoreUnavailable(path, "the operation was aborted.")
                : Result.Ok();
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    [DllImport("shell32.dll", EntryPoint = "SHFileOperationW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int ShFileOperation(ref ShFileOpStruct fileOp);

    [StructLayout(LayoutKind.Sequential)]
    private struct ShFileOpStruct
    {
        public IntPtr Hwnd;
        public uint Func;
        public IntPtr From;
        public IntPtr To;
        public ushort Flags;
        public int AnyOperationsAborted;
        public IntPtr NameMappings;
        public IntPtr ProgressTitle;
    }
}
