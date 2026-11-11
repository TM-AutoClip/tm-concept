using Microsoft.Extensions.Logging;
using TMConcept.Desktop.Localization;
using TMConcept.Desktop.Os;
using TMConcept.Desktop.ViewModels;

namespace TMConcept.Desktop.Videos;

/// <summary>
/// A made video's files opened outside the app, for every page that lists videos (0.1.13: the Produced page had these for itself; the
/// Concept → video page lists the made videos too): the video in the system's player, its folder in the file manager. Each returns what
/// the page should say when it could not (empty when it did).
/// </summary>
public sealed class VideoFiles(ShellLauncher shell, Localizer text, ILogger<VideoFiles> logger)
{
    /// <summary>Opens the video in the system's player.</summary>
    public string Play(VideoRowViewModel row)
    {
        ArgumentNullException.ThrowIfNull(row);
        return row.Missing ? text["produced.file_missing"] : Open(row.File);
    }

    /// <summary>Opens the video's folder.</summary>
    public string OpenFolder(VideoRowViewModel row)
    {
        ArgumentNullException.ThrowIfNull(row);
        return Directory.Exists(row.Folder) ? Open(row.Folder) : text["produced.file_missing"];
    }

    private string Open(string path)
    {
        try
        {
            shell.OpenPath(path);
            return string.Empty;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            logger.LogWarning(ex, "Could not open the file");
            return text.Format("error.generic", ex.Message);
        }
    }
}
