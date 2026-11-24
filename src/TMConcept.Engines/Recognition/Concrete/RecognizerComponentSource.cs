using TMConcept.Core.Components;
using TMConcept.Core.Tools;
using TMConcept.Engines.Components;
using TMConcept.Engines.Tools;

namespace TMConcept.Engines.Recognition;

/// <summary>The components of the subtitles' word times (0.1.48, S3): the whisper.cpp program and its multilingual model.</summary>
public sealed class RecognizerComponentSource(ToolInstaller installer, RecognizerModelStore store) : IComponentSource
{
    private readonly Lazy<IReadOnlyList<IComponent>> _components = new(() =>
    {
        var program = new ToolComponent(installer, ComponentCodes.SpeechRecognizer, "whisper.cpp", [ToolKind.SpeechRecognizer], []);
        return [program, new RecognizerModelComponent(store, program)];
    });

    /// <inheritdoc />
    public IReadOnlyList<IComponent> Components => _components.Value;
}
