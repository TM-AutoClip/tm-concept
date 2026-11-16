using TMConcept.Core.Components;
using TMConcept.Core.Tools;
using TMConcept.Speech.Python;

namespace TMConcept.Speech.Components;

/// <summary>
/// The model files of a Python voice (after its environment, and after the voice it builds on: Ahenk after VoxCPM2). Ready only when the
/// whole voice can speak, so a voice whose environment went missing (another PyTorch build after a new graphics card) pulls it in again. A
/// non-commercial licence is named in the download line, so the install window says it too (the step asks for it before).
/// </summary>
public sealed class PythonVoiceComponent(PythonSpeechEngine engine, PythonModelStore models, string license, bool nonCommercial) : IComponent
{
    /// <inheritdoc />
    public string Code => ComponentCodes.VoicePrefix + engine.Code;

    /// <inheritdoc />
    public string Name => engine.Name;

    /// <inheritdoc />
    public IReadOnlyList<string> DependsOn { get; } = engine.BaseVoice is { } basis
        ? [PythonEnvironmentComponent.CodeOf(engine.Environment.Key), ComponentCodes.VoicePrefix + basis.Code]
        : [PythonEnvironmentComponent.CodeOf(engine.Environment.Key)];

    /// <inheritdoc />
    public bool Supported => true;

    /// <inheritdoc />
    public bool Ready => engine.Ready;

    /// <inheritdoc />
    public IReadOnlyList<ComponentDownload> Downloads()
    {
        var missing = models.Missing(engine.Voice);
        if (missing.Count == 0)
        {
            return [];
        }

        var name = engine.Name + " model" + (license.Length == 0 ? string.Empty : nonCommercial ? $" ({license}: NON-COMMERCIAL use only)" : $" ({license})");
        return [new ComponentDownload(name, missing[0].Url.Host, missing.Sum(f => f.ApproximateBytes))];
    }

    /// <inheritdoc />
    public IReadOnlyList<string> Folders() => [models.Folder(engine.Voice)];

    /// <inheritdoc />
    public Task RefreshAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc />
    public Task InstallAsync(IProgress<ToolProgress>? progress, CancellationToken cancellationToken) => models.InstallAsync(engine.Voice, progress, cancellationToken);
}
