using Microsoft.Extensions.Logging;
using TMConcept.Core.Components;
using TMConcept.Core.Tools;

namespace TMConcept.Speech.Speech;

/// <summary>
/// A local sherpa-onnx voice. Two downloads make it ready: the voice model (an archive, <see cref="SpeechModelInstaller"/>) and the
/// sherpa-onnx libraries (a tool, <see cref="ToolKind.SpeechRuntime"/>). Speaking happens in the speech worker process, on the processor.
/// </summary>
public abstract class SherpaSpeechEngineBase(SpeechModelInstaller installer, IToolLocator tools, SpeechWorkerClient worker, ToolArchive model,
    ILogger logger) : LocalSpeechEngineBase(tools, worker, logger)
{
    /// <inheritdoc />
    public override bool ModelInstalled => installer.Ready(model);

    /// <summary>The voice model's download.</summary>
    public ToolArchive Model => model;

    /// <inheritdoc />
    public override string ModelFolder => installer.Folder(model);

    /// <inheritdoc />
    public override string InstallFolder => Path.GetDirectoryName(installer.Folder(model))!;

    /// <inheritdoc />
    public override IReadOnlyList<ComponentDownload> Downloads() =>
        ModelInstalled ? [] : [new ComponentDownload(Name + " " + model.Version, model.Url.Host, model.ApproximateBytes)];

    /// <inheritdoc />
    public override Task InstallAsync(bool agreed, IProgress<ToolProgress>? progress, CancellationToken cancellationToken) =>
        installer.InstallAsync(model, agreed, progress, cancellationToken);
}
