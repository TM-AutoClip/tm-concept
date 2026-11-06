using Microsoft.Extensions.Logging;
using TMConcept.Core.Paths;
using TMConcept.Core.Tools;

namespace TMConcept.Engines.Images;

/// <summary>
/// The weight files of the local image models in <c>&lt;tools&gt;/images/&lt;model&gt;/</c>. A file is there only after its SHA-256 matched (the
/// downloader writes a <c>.part</c> file and moves it into place after the check).
/// </summary>
public sealed class LocalImageStore(IAppPaths paths, IArchiveDownloader downloader, ILogger<LocalImageStore> logger)
{
    /// <summary>The model's folder.</summary>
    public string Folder(LocalImageModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        return Path.Combine(paths.ToolsDirectory, "images", model.Id);
    }

    /// <summary>The path of one weights file.</summary>
    public string File(LocalImageModel model, ToolArchive file)
    {
        ArgumentNullException.ThrowIfNull(file);
        return Path.Combine(Folder(model), file.ExecutablePath);
    }

    /// <summary>Weight files that are not there yet.</summary>
    public IReadOnlyList<ToolArchive> Missing(LocalImageModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        return model.Files.Where(f => !System.IO.File.Exists(File(model, f))).ToList();
    }

    /// <summary>Downloads the missing files (network; only after the user agreed).</summary>
    public async Task InstallAsync(LocalImageModel model, IProgress<ToolProgress>? progress, CancellationToken cancellationToken)
    {
        var missing = Missing(model);
        Directory.CreateDirectory(Folder(model));
        for (var i = 0; i < missing.Count; i++)
        {
            var index = i;
            var inner = new Progress<ToolProgress>(p => progress?.Report(new ToolProgress((index + p.Share) / missing.Count, p.Text)));
            await downloader.DownloadAsync(missing[i], File(model, missing[i]), inner, cancellationToken).ConfigureAwait(false);
            logger.LogInformation("Image model {Model}: {File} downloaded and verified", model.Id, missing[i].ExecutablePath);
        }
    }
}
