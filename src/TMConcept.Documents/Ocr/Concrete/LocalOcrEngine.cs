using Microsoft.Extensions.Logging;
using TMConcept.Core.Paths;
using TMConcept.Core.Tools;

namespace TMConcept.Documents.Ocr;

/// <summary>
/// The local OCR as installed on this computer (0.1.25): its two model files (<see cref="LocalOcrModels"/>) in <c>&lt;tools&gt;/ocr/pp-ocr-latin/</c>
/// (a file is there only after its SHA-256 matched), downloaded after the user agreed (<see cref="InstallAsync"/>), and a new engine for
/// each add or read (<see cref="Create"/>: the models are loaded for it, about 13 MB on disk and some 100 MB of memory while it works, and let
/// go with it).
/// </summary>
public sealed class LocalOcrEngine(IAppPaths paths, IArchiveDownloader downloader, IPageImageDecoder decoder, ILoggerFactory loggers)
{
    private readonly ILogger _logger = loggers.CreateLogger<LocalOcrEngine>();

    /// <summary>The models' folder.</summary>
    public string Folder => Path.Combine(paths.ToolsDirectory, "ocr", Core.Documents.PageTextEngines.LocalOcrId);

    /// <summary>Files that are not there yet.</summary>
    public IReadOnlyList<ToolArchive> Missing => LocalOcrModels.Files.Where(f => !File.Exists(Path.Combine(Folder, f.ExecutablePath))).ToList();

    /// <summary>Whether both models are installed.</summary>
    public bool Ready => Missing.Count == 0;

    /// <summary>Downloads the missing files (network; only after the user agreed).</summary>
    public async Task InstallAsync(IProgress<ToolProgress>? progress, CancellationToken cancellationToken)
    {
        var missing = Missing;
        Directory.CreateDirectory(Folder);
        for (var i = 0; i < missing.Count; i++)
        {
            var index = i;
            var inner = new Progress<ToolProgress>(p => progress?.Report(new ToolProgress((index + p.Share) / missing.Count, p.Text)));
            await downloader.DownloadAsync(missing[i], Path.Combine(Folder, missing[i].ExecutablePath), inner, cancellationToken).ConfigureAwait(false);
            _logger.LogInformation("Local OCR: {File} downloaded and verified", missing[i].ExecutablePath);
        }
    }

    /// <summary>A new engine with both models loaded; <see cref="InvalidOperationException"/> when they are not installed or cannot be loaded.</summary>
    public PaddleOcrRecognizer Create()
    {
        if (!Ready)
        {
            throw new InvalidOperationException("The local OCR models are not installed.");
        }

        IOcrNetwork? detector = null;
        try
        {
            detector = new OnnxOcrNetwork(Path.Combine(Folder, LocalOcrModels.DetectorFile));
            var recognizer = new OnnxOcrNetwork(Path.Combine(Folder, LocalOcrModels.RecognizerFile));
            _logger.LogInformation("Local OCR loaded ({Name}, {Threads} processor threads)", LocalOcrModels.Name,
                Core.Documents.PageTextEngines.OcrThreads(Environment.ProcessorCount));
            return new PaddleOcrRecognizer(decoder, detector, recognizer, OcrDictionary.Latin(), loggers.CreateLogger<PaddleOcrRecognizer>());
        }
        catch (Exception ex) when (ex is InvalidDataException or TypeInitializationException or DllNotFoundException)
        {
            detector?.Dispose();
            _logger.LogError(ex, "The local OCR models could not be loaded");
            throw new InvalidOperationException("The local OCR could not start: " + ex.Message, ex);
        }
    }
}
