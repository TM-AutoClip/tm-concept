using Microsoft.Extensions.Logging;
using TMConcept.Core.Speech;
using TMConcept.Core.Tools;

namespace TMConcept.Speech.Speech;

/// <summary>
/// A local sherpa-onnx voice. Two downloads make it ready: the voice model (<see cref="SpeechModelInstaller"/>) and the sherpa-onnx
/// libraries (a tool, <see cref="ToolKind.SpeechRuntime"/>). Speaking happens in the speech worker process (<see cref="SpeechWorkerClient"/>),
/// on the processor, never the GPU; the window process never loads the libraries.
/// </summary>
public abstract class SherpaSpeechEngineBase(SpeechModelInstaller installer, IToolLocator tools, SpeechWorkerClient worker, ToolArchive model,
    ILogger logger) : ISpeechEngine
{
    /// <inheritdoc />
    public abstract string Code { get; }

    /// <inheritdoc />
    public abstract string Name { get; }

    /// <inheritdoc />
    public bool IsLocal => true;

    /// <inheritdoc />
    public bool Ready => ModelInstalled && tools.Status(ToolKind.SpeechRuntime).Ready;

    /// <summary>Whether the voice model itself is installed (the libraries may still be missing, as after an update from 0.1.2).</summary>
    public bool ModelInstalled => installer.Ready(model);

    /// <summary>The voice model's download.</summary>
    public ToolArchive Model => model;

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
    public async Task<SpeechClip> SynthesizeAsync(SpeechRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!installer.Ready(model))
        {
            throw new SpeechException($"{Name} is not installed.") { Code = "missing" };
        }

        var runtime = tools.Status(ToolKind.SpeechRuntime);
        if (!runtime.Ready)
        {
            throw new SpeechException("The speech libraries (sherpa-onnx) are not installed. Choose the local voice in Settings → Models and save: they are installed then.") { Code = "missing" };
        }

        var reply = await worker.SpeakAsync(Path.GetDirectoryName(runtime.ExecutablePath)!, installer.Folder(model), Threads, Request(request),
            cancellationToken).ConfigureAwait(false);
        if (!reply.Ok)
        {
            logger.LogError("{Engine} failed ({Code}): {Error}", Name, reply.Code, reply.Error);
            throw new SpeechException($"{Name} failed: {reply.Error}") { Code = "engine" };
        }

        return new SpeechClip(request.OutputWav, reply.DurationMs, request.Text.Length, 0, Code);
    }

    /// <summary>The worker request of a speech request (voice, speed, steps and language of this model).</summary>
    protected abstract SpeechWorkerRequest Request(SpeechRequest request);
}
