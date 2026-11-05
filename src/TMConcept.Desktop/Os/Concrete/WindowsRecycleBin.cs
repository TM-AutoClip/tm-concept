namespace TMConcept.Desktop.Os;

/// <summary>Windows recycle bin through the shell (Microsoft.VisualBasic.FileIO).</summary>
public sealed class WindowsRecycleBin : IRecycleBin
{
    /// <inheritdoc />
    public Task MoveAsync(string folder, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("The Windows recycle bin is only available on Windows.");
        }

        return Task.Run(() => Microsoft.VisualBasic.FileIO.FileSystem.DeleteDirectory(folder, Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
            Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin), cancellationToken);
    }
}
