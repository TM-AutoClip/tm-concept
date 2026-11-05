using TMConcept.Core.Engines;
using TMConcept.Core.Paths;
using TMConcept.Core.Render;

namespace TMConcept.Engines.Engines;

/// <summary>Install check without a model: the engine's hand-written sample code on a small sample input (540×960, three short scenes).</summary>
public sealed class SampleRender(IAppPaths paths)
{
    /// <summary>The sample input.</summary>
    public static RenderInput Input() => RenderInputBuilder.Build("TM Concept", "en",
    [
        new RenderSceneSource("Sample scene one", "This is a sample render.", string.Empty, 1200),
        new RenderSceneSource("Sample scene two", "No model and no network are used.", string.Empty, 1200),
        new RenderSceneSource("Done", "The engine works.", string.Empty, 1000),
    ], 540, 960, 30);

    /// <summary>Renders the sample and returns the video path.</summary>
    public async Task<string> RenderAsync(IVideoEngine engine, IProgress<EngineEvent>? progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(engine);
        var folder = Path.Combine(paths.WorkDirectory, "sample", engine.Kind.ToString().ToLowerInvariant());
        if (Directory.Exists(folder))
        {
            Directory.Delete(folder, recursive: true);
        }

        var request = new EngineRenderRequest(folder, engine.SampleCode, Input(), Path.Combine(folder, "sample.mp4"), 2);
        engine.WriteProject(request);
        await engine.RenderAsync(request, progress, cancellationToken).ConfigureAwait(false);
        return request.Output;
    }
}
