namespace TMConcept.Core.Components;

/// <summary>
/// Whether a component's folder holds an install: at least one file that is not an unfinished download (<c>.part</c>). An empty folder
/// (an image model's folder is made before its download starts) or one with only a partial download never counts as installed: 0.1.16
/// counted every existing folder, so a failed 9 GB download was offered again as "installed before" after every new version.
/// </summary>
public static class ComponentFolders
{
    /// <summary>The extension of an unfinished download.</summary>
    public const string PartExtension = ".part";

    /// <summary>Whether <paramref name="folder"/> exists and holds at least one finished file (in any sub folder).</summary>
    public static bool HasContent(string folder)
    {
        ArgumentNullException.ThrowIfNull(folder);
        if (!Directory.Exists(folder))
        {
            return false;
        }

        try
        {
            return Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
                .Any(f => !f.EndsWith(PartExtension, StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A folder that cannot be read is something on disk: the update offers it again rather than forgetting it.
            return true;
        }
    }
}
