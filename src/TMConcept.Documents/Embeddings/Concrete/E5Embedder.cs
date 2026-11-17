using Microsoft.Extensions.Logging;
using TMConcept.Core.Components;
using TMConcept.Core.Documents;
using TMConcept.Core.Paths;
using TMConcept.Core.Tools;

namespace TMConcept.Documents.Embeddings;

/// <summary>
/// The documents' meaning model (<see cref="E5Model"/>): its two files in <c>&lt;tools&gt;/embeddings/multilingual-e5-small/</c> (a file is
/// there only after its SHA-256 matched), run in this process with ONNX Runtime on ONE processor thread of lowered priority, so the computer
/// stays usable while a long document is embedded. The model is loaded for each run and let go afterwards (about 120 MB of memory only while it
/// works). No graphics card, no network; the texts never leave this computer.
/// </summary>
public sealed class E5Embedder(IAppPaths paths, IArchiveDownloader downloader, ILogger<E5Embedder> logger) : DocumentEmbedderBase
{
    /// <inheritdoc />
    public override string ModelId => E5Model.Id;

    /// <inheritdoc />
    public override string ComponentCode => ComponentCodes.EmbeddingPrefix + E5Model.Id;

    /// <summary>The model's folder.</summary>
    public string Folder => Path.Combine(paths.ToolsDirectory, "embeddings", E5Model.Id);

    /// <summary>Files that are not there yet.</summary>
    public IReadOnlyList<ToolArchive> Missing => E5Model.Files.Where(f => !File.Exists(Path.Combine(Folder, f.ExecutablePath))).ToList();

    /// <inheritdoc />
    public override bool Ready => Missing.Count == 0;

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
            logger.LogInformation("Documents meaning model: {File} downloaded and verified", missing[i].ExecutablePath);
        }
    }

    /// <inheritdoc />
    protected override Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken)
    {
        var done = new TaskCompletionSource<IReadOnlyList<float[]>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                using var encoder = new OnnxTextEncoder(Path.Combine(Folder, E5Model.ModelFile), Path.Combine(Folder, E5Model.TokenizerFile), E5Model.MaxTokens);
                var vectors = new List<float[]>(texts.Count);
                foreach (var text in texts)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    vectors.Add(encoder.Encode(text));
                }

                done.SetResult(vectors);
            }
            catch (OperationCanceledException)
            {
                done.SetCanceled(cancellationToken);
            }
            catch (Exception ex)
            {
                // ONNX Runtime reports a damaged model or a missing native library with its own exception types: one meaning here.
                logger.LogError(ex, "The documents meaning model failed");
                done.SetException(new InvalidOperationException("The meaning model failed: " + ex.Message, ex));
            }
        })
        {
            IsBackground = true,
            Priority = ThreadPriority.BelowNormal,
            Name = "Documents meaning model",
        };
        thread.Start();
        return done.Task;
    }
}
