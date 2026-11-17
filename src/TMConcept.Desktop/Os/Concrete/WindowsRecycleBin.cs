using System.Globalization;
using System.Runtime.InteropServices;

namespace TMConcept.Desktop.Os;

/// <summary>
/// Windows recycle bin through the shell (<c>SHFileOperationW</c>, delete with undo). <c>FOF_WANTNUKEWARNING</c> is set: when a file or the
/// folder is bigger than the drive's recycle bin, or the bin is turned off on that drive, Windows asks before it deletes for good. Without
/// it (Microsoft.VisualBasic's <c>DeleteDirectory</c> with <c>OnlyErrorDialogs</c>, 0.1.16) the shell deleted several GB of image models
/// permanently without a word and the app said "moved to the recycle bin". A declined question or a partial move ends in an exception, so
/// the caller never reports a move that did not happen.
/// </summary>
public sealed partial class WindowsRecycleBin : IRecycleBin
{
    private const uint Delete = 3;
    private const ushort Silent = 0x0004;
    private const ushort NoConfirmation = 0x0010;
    private const ushort AllowUndo = 0x0040;
    private const ushort WantNukeWarning = 0x4000;

    /// <inheritdoc />
    public Task MoveAsync(string folder, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(folder);
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("The Windows recycle bin is only available on Windows.");
        }

        return Task.Run(() => Move(Path.GetFullPath(folder)), cancellationToken);
    }

    private static void Move(string folder)
    {
        // The source is a list of paths ending with an extra null: the marshalled string brings one null, the added one makes two.
        var from = Marshal.StringToHGlobalUni(folder + "\0");
        try
        {
            var operation = new FileOperation
            {
                Function = Delete,
                From = from,
                Flags = (ushort)(AllowUndo | WantNukeWarning | NoConfirmation | Silent),
            };
            var result = SHFileOperationW(ref operation);
            if (operation.AnyOperationsAborted != 0)
            {
                throw new OperationCanceledException("The move to the recycle bin was stopped (a permanent delete was declined).");
            }

            if (result != 0)
            {
                throw new IOException(string.Create(CultureInfo.InvariantCulture, $"The shell could not move the folder to the recycle bin (code 0x{result:X})."));
            }

            if (Directory.Exists(folder) || File.Exists(folder))
            {
                throw new IOException("The folder was only partly moved to the recycle bin.");
            }
        }
        finally
        {
            Marshal.FreeHGlobal(from);
        }
    }

    [LibraryImport("shell32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int SHFileOperationW(ref FileOperation operation);

    /// <summary>SHFILEOPSTRUCTW as laid out on 64-bit Windows (natural alignment; only 32-bit Windows packs it to 1 byte).</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct FileOperation
    {
        public nint Window;
        public uint Function;
        public nint From;
        public nint To;
        public ushort Flags;
        public int AnyOperationsAborted;
        public nint NameMappings;
        public nint ProgressTitle;
    }
}
