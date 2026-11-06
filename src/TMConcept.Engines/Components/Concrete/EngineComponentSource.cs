using Microsoft.Extensions.Logging;
using TMConcept.Core.Components;
using TMConcept.Core.Engines;
using TMConcept.Core.Processes;
using TMConcept.Core.Tools;
using TMConcept.Engines.Engines;
using TMConcept.Engines.Images;
using TMConcept.Engines.Tools;

namespace TMConcept.Engines.Components;

/// <summary>The components of this project: Node.js, ffmpeg, the local voice's libraries, the engines and their browser, stable-diffusion.cpp and the local image models.</summary>
public sealed class EngineComponentSource(ToolInstaller installer, IEnumerable<IVideoEngine> engines, ChromeLocator chrome, LocalImageStore images,
    IProcessRunner runner, ILoggerFactory loggers) : IComponentSource
{
    private readonly Lazy<IReadOnlyList<IComponent>> _components = new(() =>
    [
        new NodeComponent(installer, runner, loggers.CreateLogger<NodeComponent>()),
        new ToolComponent(installer, ComponentCodes.Ffmpeg, "ffmpeg", [ToolKind.Ffmpeg], []),
        new ToolComponent(installer, ComponentCodes.SpeechRuntime, "sherpa-onnx", [ToolKind.SpeechRuntime], []),
        .. engines.OrderBy(e => e.Kind).Select(e => new EngineComponent(e)),
        new BrowserComponent(chrome),
        new ToolComponent(installer, ComponentCodes.ImageRuntime, "stable-diffusion.cpp",
            installer.Archive(ToolKind.ImageRuntimeCuda) is null ? [ToolKind.ImageRuntime] : [ToolKind.ImageRuntime, ToolKind.ImageRuntimeCuda], []),
        .. LocalImageModels.All.Select(m => new ImageModelComponent(m, images, installer)),
    ]);

    /// <inheritdoc />
    public IReadOnlyList<IComponent> Components => _components.Value;
}
