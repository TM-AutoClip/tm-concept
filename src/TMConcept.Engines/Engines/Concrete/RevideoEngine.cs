using System.Globalization;
using Microsoft.Extensions.Logging;
using TMConcept.Core.Engines;
using TMConcept.Core.Paths;
using TMConcept.Core.Processes;
using TMConcept.Engines.Tools;

namespace TMConcept.Engines.Engines;

/// <summary>
/// Revideo (MIT, a Motion Canvas fork), experimental: no release for months, two Windows pitfalls (handled in render.mjs) and no ARM64
/// browser from Puppeteer. A TypeScript generator scene rendered with Vite + headless Chrome; the browser has no network and no GPU.
/// Work folder: src/project.ts + src/input.ts (the app's), src/scene.tsx (the model's), input.json.
/// </summary>
public sealed class RevideoEngine(IAppPaths paths, ToolInstaller tools, ChromeLocator chrome, IProcessRunner runner, ILogger<RevideoEngine> logger)
    : VideoEngineBase(new EnginePackage("revideo", paths, tools, runner, logger), chrome, tools, runner, logger)
{
    private static readonly string Renderer = Path.Combine("node_modules", "@revideo", "renderer", "package.json");

    /// <inheritdoc />
    public override EngineKind Kind => EngineKind.Revideo;

    /// <inheritdoc />
    public override string Name => "Revideo";

    /// <inheritdoc />
    public override bool Experimental => true;

    /// <inheritdoc />
    public override string CodeFile => "src/scene.tsx";

    /// <inheritdoc />
    protected override string PackageProbe => Renderer;

    /// <inheritdoc />
    public override string Contract => """
        Engine: Revideo (TypeScript generator scene, Motion Canvas style).
        Write ONE file (src/scene.tsx): `export default makeScene2D('scene', function* (view) { … });`.
        Allowed imports only: '@revideo/2d', '@revideo/core' and `import type {ConceptInput} from './input';`.
        Read the input with `const input = useScene().variables.get<ConceptInput>('input', {} as ConceptInput)();`
        (fields: width, height, fps, durationSeconds, title, theme {background, accent, text},
        scenes [{number, startSeconds, durationSeconds, visual, caption, image, transition}]).
        Play the scenes in order with yield* animations and waitFor so each scene lasts exactly its durationSeconds.
        Never hard-code texts or durations. No network, no external fonts.
        """;

    /// <inheritdoc />
    public override string SampleCode => """
        import {makeScene2D, Rect, Txt} from '@revideo/2d';
        import {all, createRef, useScene, waitFor} from '@revideo/core';
        import type {ConceptInput} from './input';

        export default makeScene2D('scene', function* (view) {
          const input = useScene().variables.get<ConceptInput>('input', {} as ConceptInput)();
          view.fill(input.theme.background);
          const bar = createRef<Rect>();
          view.add(<Rect ref={bar} width={0} height={8} fill={input.theme.accent} y={-input.height * 0.4} />);
          yield* bar().width(input.width * 0.8, 0.6);
          for (const scene of input.scenes) {
            const title = createRef<Txt>();
            const caption = createRef<Txt>();
            view.add(<Txt ref={title} text={scene.visual} fill={input.theme.text} fontSize={input.width * 0.06} fontWeight={700} opacity={0} y={40} />);
            view.add(<Txt ref={caption} text={scene.caption} fill={input.theme.text} fontSize={input.width * 0.035} opacity={0} y={input.height * 0.35} />);
            const hold = Math.max(0, scene.durationSeconds - 0.65 - (scene.number === 1 ? 0.6 : 0));
            yield* all(title().opacity(1, 0.4), title().y(0, 0.4), caption().opacity(1, 0.4));
            yield* waitFor(hold);
            yield* all(title().opacity(0, 0.25), caption().opacity(0, 0.25));
            title().remove();
            caption().remove();
          }
        });
        """;

    /// <inheritdoc />
    public override async Task RenderAsync(EngineRenderRequest request, IProgress<EngineEvent>? progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        RequireReady();
        Package.Extract();
        var c = CultureInfo.InvariantCulture;
        string[] arguments =
        [
            Path.Combine(Package.Root, "render.mjs"), "--work", request.Folder, "--out", request.Output, "--width", request.Input.Width.ToString(c),
            "--height", request.Input.Height.ToString(c), "--workers", request.Workers.ToString(c), "--chrome", Chrome.Find()!,
        ];
        Logger.LogInformation("Revideo render: {Width}×{Height}, {Frames} frames", request.Input.Width, request.Input.Height, request.Input.TotalFrames);
        await RunNodeAsync(arguments, Package.Root, request.Folder, null, progress, requireDone: true, cancellationToken).ConfigureAwait(false);
        if (!File.Exists(request.Output))
        {
            throw new EngineException("Revideo finished without an output file.") { Code = "render" };
        }
    }

    /// <inheritdoc />
    protected override IReadOnlyDictionary<string, string> Skeleton(EngineRenderRequest request) => new Dictionary<string, string>
    {
        ["src/project.ts"] = $$$"""
            // The app's file (TM Concept); not changed by the model. The input comes from renderVideo's variables ('input').
            import {makeProject} from '@revideo/core';
            import scene from './scene';

            export default makeProject({
              scenes: [scene],
              settings: {rendering: {fps: {{{request.Input.Fps.ToString(CultureInfo.InvariantCulture)}}}}},
            });
            """,
        ["src/input.ts"] = RemotionEngine.InputTs,
    };
}
