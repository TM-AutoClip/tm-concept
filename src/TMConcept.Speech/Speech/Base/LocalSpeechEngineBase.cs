using Microsoft.Extensions.Logging;
using TMConcept.Core.Components;
using TMConcept.Core.Speech;
using TMConcept.Core.Tools;

namespace TMConcept.Speech.Speech;

/// <summary>
/// A local voice spoken in the speech worker process (<see cref="SpeechWorkerClient"/>), on the processor: its model (downloaded after a
/// confirmation, pinned with SHA-256) plus the sherpa-onnx libraries (a tool, <see cref="ToolKind.SpeechRuntime"/>, whose ONNX Runtime also
/// runs the models that are not sherpa-onnx ones). The window process never loads the native libraries. A voice is one component of the
/// install window (<see cref="Components.VoiceComponent"/>).
/// </summary>
public abstract class LocalSpeechEngineBase(IToolLocator tools, SpeechWorkerClient worker, ILogger logger) : ISpeechEngine
{
    /// <inheritdoc />
    public abstract string Code { get; }

    /// <inheritdoc />
    public abstract string Name { get; }

    /// <inheritdoc />
    public bool IsLocal => true;

    /// <inheritdoc />
    public bool Ready => ModelInstalled && tools.Status(ToolKind.SpeechRuntime).Ready;

    /// <summary>Whether the voice model itself is installed (the libraries may still be missing).</summary>
    public abstract bool ModelInstalled { get; }

    /// <summary>The folder the worker starts in (the model's files).</summary>
    public abstract string ModelFolder { get; }

    /// <summary>The folder the install window shows and removes (the model's own folder).</summary>
    public abstract string InstallFolder { get; }

    /// <summary>What is still to download for the model (empty when it is installed).</summary>
    public abstract IReadOnlyList<ComponentDownload> Downloads();

    /// <summary>Processor threads for speaking (leaves two cores free).</summary>
    protected virtual int Threads => Math.Clamp(Environment.ProcessorCount - 2, 1, 4);

    /// <inheritdoc />
    public abstract bool Supports(string language);

    /// <inheritdoc />
    public abstract IReadOnlyList<SpeechVoice> Voices(string language);

    /// <inheritdoc />
    public abstract Task InstallAsync(bool agreed, IProgress<ToolProgress>? progress, CancellationToken cancellationToken);

    /// <inheritdoc />
    public async Task<SpeechClip> SynthesizeAsync(SpeechRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!ModelInstalled)
        {
            throw new SpeechException($"{Name} is not installed.") { Code = "missing" };
        }

        var runtime = tools.Status(ToolKind.SpeechRuntime);
        if (!runtime.Ready)
        {
            throw new SpeechException("The speech libraries (sherpa-onnx) are not installed. They are installed when a process that uses a local voice starts (after a confirmation).") { Code = "missing" };
        }

        var workerRequest = await RequestAsync(request, cancellationToken).ConfigureAwait(false);
        SpeechWorkerReply reply;
        try
        {
            reply = await worker.SpeakAsync(Path.GetDirectoryName(runtime.ExecutablePath)!, ModelFolder, Threads, workerRequest, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            Done(workerRequest);
        }

        if (!reply.Ok)
        {
            logger.LogError("{Engine} failed ({Code}): {Error}", Name, reply.Code, reply.Error);
            throw new SpeechException($"{Name} failed: {reply.Error}") { Code = "engine" };
        }

        return new SpeechClip(request.OutputWav, reply.DurationMs, request.Text.Length, 0, Code) { SkippedWords = reply.Skipped ?? [] };
    }

    /// <summary>The worker request of a speech request (voice, speed, steps and language of this model).</summary>
    protected abstract SpeechWorkerRequest Request(SpeechRequest request);

    /// <summary>The worker request, with what must be prepared first (a voice that copies a voice writes its sample to a work file).</summary>
    protected virtual Task<SpeechWorkerRequest> RequestAsync(SpeechRequest request, CancellationToken cancellationToken) => Task.FromResult(Request(request));

    /// <summary>After the worker answered (or failed): removes what <see cref="RequestAsync"/> prepared.</summary>
    protected virtual void Done(SpeechWorkerRequest request)
    {
    }
}
