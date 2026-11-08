using TMConcept.Core.Components;
using TMConcept.Core.Tools;
using TMConcept.Speech.Speech;

namespace TMConcept.Speech.Components;

/// <summary>A local voice model (after the sherpa-onnx libraries, which every local voice needs).</summary>
public sealed class VoiceComponent(LocalSpeechEngineBase voice) : IComponent
{
    /// <inheritdoc />
    public string Code => ComponentCodes.VoicePrefix + voice.Code;

    /// <inheritdoc />
    public string Name => voice.Name;

    /// <inheritdoc />
    public IReadOnlyList<string> DependsOn { get; } = [ComponentCodes.SpeechRuntime];

    /// <inheritdoc />
    public bool Supported => true;

    /// <inheritdoc />
    public bool Ready => voice.Ready;

    /// <inheritdoc />
    public IReadOnlyList<ComponentDownload> Downloads() => voice.Downloads();

    /// <inheritdoc />
    public IReadOnlyList<string> Folders() => [voice.InstallFolder];

    /// <inheritdoc />
    public Task RefreshAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc />
    public Task InstallAsync(IProgress<ToolProgress>? progress, CancellationToken cancellationToken) =>
        voice.ModelInstalled ? Task.CompletedTask : voice.InstallAsync(agreed: true, progress, cancellationToken);
}
