using System.Globalization;
using Microsoft.Extensions.Logging;
using TMConcept.Core.Engines;
using TMConcept.Core.Paths;
using TMConcept.Core.Processes;
using TMConcept.Core.Render;
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
        Allowed imports only: '@revideo/2d', '@revideo/core', `import type {ConceptInput} from './input';` and the helpers of './tm'.
        Read the input with `const input = useScene().variables.get<ConceptInput>('input', {} as ConceptInput)();`
        (fields: width, height, fps, durationSeconds, title, theme {background, accent, text}, style? (the template, when there is one),
        scenes [{number, startSeconds, durationSeconds, visual, image, transition}]).
        Play the scenes in order with yield* animations and waitFor so each scene lasts exactly its durationSeconds.
        Never hard-code texts or durations. No network, no external fonts.
        """;

    /// <summary>
    /// The minimal file the repair request shows and the install check renders; also the app's own animation of this engine. 20 Nov 2026: it
    /// shows each scene's image full frame (served from public/, so its address starts with "/") with a slow zoom.
    /// </summary>
    public override string SampleCode => """
        import {Img, makeScene2D, Txt} from '@revideo/2d';
        import {all, createRef, useScene, waitFor} from '@revideo/core';
        import type {ConceptInput} from './input';

        export default makeScene2D('scene', function* (view) {
          const input = useScene().variables.get<ConceptInput>('input', {} as ConceptInput)();
          view.fill(input.theme.background);
          for (const scene of input.scenes) {
            if (scene.image) {
              const picture = createRef<Img>();
              view.add(<Img ref={picture} src={'/' + scene.image} width={input.width} height={input.height} />);
              yield* picture().scale(1.08, scene.durationSeconds);
              picture().remove();
            } else {
              const title = createRef<Txt>();
              view.add(<Txt ref={title} text={scene.visual} fill={input.theme.text} fontSize={input.width * 0.06} fontWeight={700} opacity={0} y={40} />);
              yield* all(title().opacity(1, 0.4), title().y(0, 0.4));
              yield* waitFor(Math.max(0, scene.durationSeconds - 0.4));
              title().remove();
            }
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
            {{{FontLoader(request.Input)}}}
            export default makeProject({
              scenes: [scene],
              settings: {rendering: {fps: {{{request.Input.Fps.ToString(CultureInfo.InvariantCulture)}}}}},
            });
            """,
        ["src/input.ts"] = RemotionEngine.InputTs,
        ["src/tm.ts"] = TemplateHelpers.RevideoHelpers,
    };

    /// <summary>
    /// The template's font for Revideo (experimental): the files are served from <c>public/fonts</c> and loaded with the FontFace API before
    /// the project is exported (a top-level await in <c>project.ts</c>, an ES module served by Vite), so no frame is measured or drawn in a
    /// fallback font. A face that cannot load throws, and the render fails with that error instead of drawing another font (the template is
    /// enforced, not hoped for). Nothing without a template.
    /// </summary>
    public static string FontLoader(RenderInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (input.Style is not { } style)
        {
            return string.Empty;
        }

        static string Quote(string value) => "'" + new string(value.Where(ch => ch is not ('\'' or '\\' or '\n' or '\r')).ToArray()) + "'";
        var faces = RenderStyleBuilder.FacesOf(style).Select(f => $"[{Quote(f.File)}, {Quote(f.Weight)}, {Quote(f.Style)}, {Quote(f.Format)}]").ToList();

        return $$"""

            // The template's font (served from public/fonts): loaded before the project starts, so every frame uses it.
            if (typeof document !== 'undefined') {
              const faces: [string, string, string, string][] = [{{string.Join(", ", faces)}}];
              await Promise.all(faces.map(async ([file, weight, style, format]) => {
                try {
                  document.fonts.add(await new FontFace({{Quote(style.Font)}}, `url('/${file}') format('${format}')`, {weight, style}).load());
                } catch (error: unknown) {
                  throw new Error(`TM Concept: the template font ${file} could not be loaded: ${String(error)}`);
                }
              }));
            }

            """;
    }
}
