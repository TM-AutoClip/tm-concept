using Microsoft.Extensions.Logging;
using SherpaOnnx;
using TMConcept.Core.Speech;
using TMConcept.Core.Tools;

namespace TMConcept.Speech.Speech;

/// <summary>
/// A local sherpa-onnx voice: the model is loaded once (on the processor, never the GPU), one sentence block at a time; the result is
/// written as a WAV file. Speaking runs on a background thread.
/// </summary>
public abstract class SherpaSpeechEngineBase(SpeechModelInstaller installer, ToolArchive model, ILogger logger) : ISpeechEngine, IDisposable
{
    private readonly Lock _gate = new();
    private OfflineTts? _tts;

    /// <inheritdoc />
    public abstract string Code { get; }

    /// <inheritdoc />
    public abstract string Name { get; }

    /// <inheritdoc />
    public bool IsLocal => true;

    /// <inheritdoc />
    public bool Ready => installer.Ready(model);

    /// <summary>The model folder.</summary>
    protected string Folder => installer.Folder(model);

    /// <summary>Processor threads for speaking (leaves two cores free).</summary>
    protected static int Threads => Math.Clamp(Environment.ProcessorCount - 2, 1, 4);

    /// <inheritdoc />
    public abstract bool Supports(string language);

    /// <inheritdoc />
    public abstract IReadOnlyList<SpeechVoice> Voices(string language);

    /// <inheritdoc />
    public Task InstallAsync(bool agreed, IProgress<ToolProgress>? progress, CancellationToken cancellationToken) =>
        installer.InstallAsync(model, agreed, progress, cancellationToken);

    /// <inheritdoc />
    public Task<SpeechClip> SynthesizeAsync(SpeechRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!Ready)
        {
            throw new SpeechException($"{Name} is not installed.") { Code = "missing" };
        }

        return Task.Run(() =>
        {
            lock (_gate)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    _tts ??= new OfflineTts(Config());
                    var audio = _tts.GenerateWithConfig(request.Text, Generation(request), (_, _, _, _) => cancellationToken.IsCancellationRequested ? 0 : 1);
                    cancellationToken.ThrowIfCancellationRequested();
                    Directory.CreateDirectory(Path.GetDirectoryName(request.OutputWav)!);
                    if (!audio.SaveToWaveFile(request.OutputWav))
                    {
                        throw new SpeechException("The WAV file could not be written.") { Code = "engine" };
                    }

                    var ms = audio.SampleRate > 0 ? (long)Math.Round(audio.Samples.Length * 1000.0 / audio.SampleRate) : 0;
                    return new SpeechClip(request.OutputWav, ms, request.Text.Length, 0, Code);
                }
                catch (Exception ex) when (ex is not (SpeechException or OperationCanceledException))
                {
                    logger.LogError(ex, "{Engine} failed", Name);
                    throw new SpeechException($"{Name} failed: {ex.Message}", ex) { Code = "engine" };
                }
            }
        }, cancellationToken);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _tts?.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>The model configuration.</summary>
    protected abstract OfflineTtsConfig Config();

    /// <summary>The generation settings of a request.</summary>
    protected abstract OfflineTtsGenerationConfig Generation(SpeechRequest request);
}
