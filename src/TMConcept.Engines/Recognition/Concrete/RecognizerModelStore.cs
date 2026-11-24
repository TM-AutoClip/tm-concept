using Microsoft.Extensions.Logging;
using TMConcept.Core.Paths;
using TMConcept.Core.Tools;

namespace TMConcept.Engines.Recognition;

/// <summary>
/// The whisper.cpp model file in <c>&lt;tools&gt;/whisper-models/</c> (0.1.48). It is there only after its SHA-256 matched (the downloader
/// writes a <c>.part</c> file and moves it into place after the check).
/// </summary>
public sealed class RecognizerModelStore(IAppPaths paths, IArchiveDownloader downloader, ILogger<RecognizerModelStore> logger)
{
    /// <summary>The models' folder.</summary>
    public string Folder => Path.Combine(paths.ToolsDirectory, "whisper-models");

    /// <summary>The model file's path.</summary>
    public string File => Path.Combine(Folder, RecognizerModels.File.ExecutablePath);

    /// <summary>Whether the model file is there.</summary>
    public bool Ready => System.IO.File.Exists(File);

    /// <summary>Downloads the model file (network; only after the user agreed).</summary>
    public async Task InstallAsync(IProgress<ToolProgress>? progress, CancellationToken cancellationToken)
    {
        if (Ready)
        {
            return;
        }

        Directory.CreateDirectory(Folder);
        await downloader.DownloadAsync(RecognizerModels.File, File, progress, cancellationToken).ConfigureAwait(false);
        logger.LogInformation("whisper.cpp model {Model} downloaded and verified", RecognizerModels.Name);
    }
}
