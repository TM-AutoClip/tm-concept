namespace TMConcept.Core.DiskSpace;

/// <summary>Bytes on disk of folders and files (what cannot be read counts as 0).</summary>
public static class DiskSizes
{
    /// <summary>The bytes of every file under <paramref name="path"/> (a folder or a file); 0 when it does not exist or cannot be read.</summary>
    public static long Of(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        try
        {
            if (File.Exists(path))
            {
                return new FileInfo(path).Length;
            }

            if (!Directory.Exists(path))
            {
                return 0;
            }

            var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint };
            return new DirectoryInfo(path).EnumerateFiles("*", options).Sum(f => f.Length);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return 0;
        }
    }

    /// <summary>The bytes of all of <paramref name="paths"/>.</summary>
    public static long Of(IEnumerable<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        return paths.Sum(Of);
    }
}
