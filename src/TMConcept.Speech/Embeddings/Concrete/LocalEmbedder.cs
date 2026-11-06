using Microsoft.Extensions.Logging;
using TMConcept.Core.Duplicates;
using TMConcept.Core.Paths;
using TMConcept.Core.Providers;
using TMConcept.Core.Speech;
using TMConcept.Core.Tools;
using TMConcept.Speech.Speech;

namespace TMConcept.Speech.Embeddings;

/// <summary>
/// The local semantic duplicate check (<see cref="LocalEmbeddingModels"/>): its two files in <c>&lt;tools&gt;/embeddings/&lt;id&gt;/</c>
/// (a file is there only after its SHA-256 matched), run in the worker process with the sherpa-onnx libraries' ONNX Runtime. Free, no key;
/// the texts never leave this computer.
/// </summary>
public sealed class LocalEmbedder(IAppPaths paths, IArchiveDownloader downloader, IToolLocator tools, SpeechWorkerClient worker, ILogger<LocalEmbedder> logger)
    : ILocalEmbedder
{
    /// <inheritdoc />
    public string ModelId => LocalEmbeddingModels.Id;

    /// <summary>The model's folder.</summary>
    public string Folder => Path.Combine(paths.ToolsDirectory, "embeddings", LocalEmbeddingModels.Id);

    /// <summary>Files that are not there yet.</summary>
    public IReadOnlyList<ToolArchive> Missing => LocalEmbeddingModels.Files.Where(f => !File.Exists(Path.Combine(Folder, f.ExecutablePath))).ToList();

    /// <summary>Whether the files and the libraries are installed.</summary>
    public bool Ready => Missing.Count == 0 && tools.Status(ToolKind.SpeechRuntime).Ready;

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
            logger.LogInformation("Embedding model {Model}: {File} downloaded and verified", LocalEmbeddingModels.Id, missing[i].ExecutablePath);
        }
    }

    /// <inheritdoc />
    public async Task<EmbeddingResult> EmbedAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(texts);
        var runtime = tools.Status(ToolKind.SpeechRuntime);
        if (Missing.Count > 0 || !runtime.Ready)
        {
            throw new ProviderException(LocalEmbeddingModels.Name + " is not installed. It is installed when you start with it (after a confirmation).")
            {
                Code = "model",
            };
        }

        try
        {
            var vectors = await worker.EmbedAsync(Path.GetDirectoryName(runtime.ExecutablePath)!, Folder, Math.Clamp(Environment.ProcessorCount - 2, 1, 4), texts,
                cancellationToken).ConfigureAwait(false);
            return new EmbeddingResult(vectors, 0);
        }
        catch (SpeechException ex)
        {
            // The concept generator skips the semantic layer of the batch on a provider error and tells the user; the cause is in the log.
            logger.LogError(ex, "The local embedding model failed ({Code})", ex.Code);
            throw new ProviderException("The local embedding model failed: " + ex.Message, ex) { Code = "model" };
        }
    }
}
