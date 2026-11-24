using TMConcept.Core.Components;
using TMConcept.Core.Tools;

namespace TMConcept.Engines.Recognition;

/// <summary>
/// The whisper.cpp model as a component (0.1.48, rule 18: downloaded only after the user's yes, apart from the program). Ready only with
/// the program (<paramref name="runtime"/>); supported where the program is.
/// </summary>
public sealed class RecognizerModelComponent(RecognizerModelStore store, IComponent runtime) : IComponent
{
    /// <inheritdoc />
    public string Code => ComponentCodes.RecognizerModel;

    /// <inheritdoc />
    public string Name => "whisper.cpp " + RecognizerModels.Name + " (multilingual)";

    /// <inheritdoc />
    public IReadOnlyList<string> DependsOn { get; } = [ComponentCodes.SpeechRecognizer];

    /// <inheritdoc />
    public bool Supported => runtime.Supported;

    /// <inheritdoc />
    public bool Ready => store.Ready && runtime.Ready;

    /// <inheritdoc />
    public IReadOnlyList<ComponentDownload> Downloads() => store.Ready
        ? []
        : [new ComponentDownload(Name + " · " + RecognizerModels.File.ExecutablePath, RecognizerModels.File.Url.Host, RecognizerModels.File.ApproximateBytes)];

    /// <inheritdoc />
    public IReadOnlyList<string> Folders() => [store.Folder];

    /// <inheritdoc />
    public Task RefreshAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc />
    public Task InstallAsync(IProgress<ToolProgress>? progress, CancellationToken cancellationToken) => store.InstallAsync(progress, cancellationToken);
}
