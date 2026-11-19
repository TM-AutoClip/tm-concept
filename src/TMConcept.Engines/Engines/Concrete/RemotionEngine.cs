using System.Globalization;
using Microsoft.Extensions.Logging;
using TMConcept.Core.Engines;
using TMConcept.Core.Paths;
using TMConcept.Core.Processes;
using TMConcept.Core.Render;
using TMConcept.Core.Tools;
using TMConcept.Engines.Tools;

namespace TMConcept.Engines.Engines;

/// <summary>
/// Remotion (source-available, conditional license: free for individuals, companies of up to 3 people and non-profits; others need a
/// company license). Installed only after the user accepted that license; we do not distribute it, npm does. Work folder: src/index.ts,
/// src/Root.tsx, src/input.ts (the app's), src/Scene.tsx (the model's), input.json, public/. Packages resolve from the engine folder
/// (webpack restrictions). Rendered with <c>--gl swangle</c> (software drawing, no GPU). Remotion brings its own Chrome Headless Shell
/// (x64 and arm64).
/// </summary>
public sealed class RemotionEngine(IAppPaths paths, ToolInstaller tools, ChromeLocator chrome, IProcessRunner runner, ILogger<RemotionEngine> logger)
    : VideoEngineBase(new EnginePackage("remotion", paths, tools, runner, logger), chrome, tools, runner, logger)
{
    private static readonly string Renderer = Path.Combine("node_modules", "@remotion", "renderer", "package.json");

    /// <inheritdoc />
    public override EngineKind Kind => EngineKind.Remotion;

    /// <inheritdoc />
    public override string Name => "Remotion";

    /// <inheritdoc />
    public override bool NeedsLicenseConsent => true;

    /// <inheritdoc />
    public override string CodeFile => "src/Scene.tsx";

    /// <inheritdoc />
    protected override string PackageProbe => Renderer;

    /// <inheritdoc />
    public override string Contract => """
        Engine: Remotion (React + TypeScript).
        Write ONE file (src/Scene.tsx) that exports `export const Scene: React.FC<ConceptInput> = (input) => …`.
        Allowed imports only: 'react', 'remotion', `import type {ConceptInput, ConceptScene} from './input';` and the helpers of './tm'.
        The input type: {width, height, fps, totalFrames, durationSeconds, language, title, theme {background, accent, text},
        style? (the template, when there is one), scenes [{number, startFrame, frameCount, startSeconds, durationSeconds, visual, image, transition}]}.
        Put each scene in `<Sequence from={scene.startFrame} durationInFrames={scene.frameCount}>`; animate with useCurrentFrame,
        interpolate and spring. Images: `<Img src={staticFile(scene.image)} />` only when scene.image is not empty.
        Never hard-code texts or durations. No network, no external fonts, no randomness that changes between frames.
        """;

    /// <summary>
    /// The minimal file the repair request shows and the install check renders; also the app's own animation of this engine (there is no
    /// other <see cref="IVideoEngine.FallbackCode"/>). 20 Nov 2026: it shows each scene's image full frame with a slow zoom.
    /// </summary>
    public override string SampleCode => """
        import React from 'react';
        import {AbsoluteFill, Img, Sequence, interpolate, spring, staticFile, useCurrentFrame, useVideoConfig} from 'remotion';
        import type {ConceptInput, ConceptScene} from './input';

        const Card: React.FC<{scene: ConceptScene; input: ConceptInput}> = ({scene, input}) => {
          const frame = useCurrentFrame();
          const {fps} = useVideoConfig();
          const enter = spring({frame, fps, config: {damping: 200}});
          const zoom = interpolate(frame, [0, scene.frameCount], [1, 1.08], {extrapolateLeft: 'clamp', extrapolateRight: 'clamp'});
          return (
            <AbsoluteFill style={{background: input.theme.background, justifyContent: 'center', alignItems: 'center', overflow: 'hidden'}}>
              {scene.image ? (
                <Img src={staticFile(scene.image)} style={{position: 'absolute', width: '100%', height: '100%', objectFit: 'cover',
                  transform: `scale(${zoom})`}} />
              ) : (
                <div style={{color: input.theme.text, fontSize: input.width * 0.06, fontWeight: 700, textAlign: 'center',
                  padding: '0 8%', transform: `translateY(${(1 - enter) * 60}px)`, opacity: enter}}>{scene.visual}</div>
              )}
            </AbsoluteFill>
          );
        };

        export const Scene: React.FC<ConceptInput> = (input) => (
          <AbsoluteFill style={{background: input.theme.background, fontFamily: 'sans-serif'}}>
            {input.scenes.map((scene) => (
              <Sequence key={scene.number} from={scene.startFrame} durationInFrames={scene.frameCount}>
                <Card scene={scene} input={input} />
              </Sequence>
            ))}
          </AbsoluteFill>
        );
        """;

    /// <summary>The input's TypeScript type (same fields as RenderInput, camelCase). Shared with Revideo.</summary>
    public const string InputTs = """
        // The app's file (TM Concept); matches RenderInput.
        export type ConceptScene = {
          number: number;
          startFrame: number;
          frameCount: number;
          startSeconds: number;
          durationSeconds: number;
          visual: string;
          caption: string;
          image: string;
          transition: string;
        };
        export type ConceptTheme = {background: string; accent: string; text: string};
        export type ConceptColors = {background: string; surface: string; accent: string; accent2: string; text: string; muted: string};
        export type ConceptStyle = {
          colors: ConceptColors;
          font: string;
          fontRegular: string;
          fontBold: string;
          outline?: string;
          outlineBoost?: number;
          faces?: {file: string; weight: string; style: string; format: string}[];
          textWeight?: number;
          titleWeight?: number;
          textStyle?: string;
          titleStyle: string;
          titleX: number;
          titleY: number;
          drawStyle: string;
          motion: string;
          subtitles: boolean;
          progressBar: boolean;
          safeMargin: number;
          safeBottom: number;
          endCardSeconds: number;
        };
        export type ConceptInput = {
          width: number;
          height: number;
          fps: number;
          totalFrames: number;
          durationSeconds: number;
          language: string;
          title: string;
          theme: ConceptTheme;
          style?: ConceptStyle;
          scenes: ConceptScene[];
        };
        """;

    /// <summary>
    /// <c>src/fonts.ts</c>, imported by Root.tsx: loads the template's font files from <c>public/fonts</c> (<c>staticFile</c>) before any frame
    /// is drawn (<c>delayRender</c>); a font that cannot load stops the render (<c>cancelRender</c>) instead of drawing in another font.
    /// Without a template the list is empty and nothing is loaded.
    /// </summary>
    public static string FontsTs(RenderInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var faces = new List<string>();
        if (input.Style is { } style)
        {
            var family = Quote(style.Font);
            faces.AddRange(RenderStyleBuilder.FacesOf(style).Select(f =>
                $"[{family}, {Quote(f.File)}, {Quote(f.Weight)}, {Quote(f.Style)}, {Quote(f.Format)}]"));
        }

        return $$"""
            // The app's file (TM Concept): loads the template's font before the frames are drawn; not changed by the model.
            import {cancelRender, continueRender, delayRender, staticFile} from 'remotion';

            const faces: [string, string, string, string, string][] = [{{string.Join(", ", faces)}}];

            if (typeof document !== 'undefined' && faces.length > 0) {
              const handle = delayRender('Loading the template font');
              Promise.all(faces.map(([family, file, weight, style, format]) => new FontFace(family, `url('${staticFile(file)}') format('${format}')`, {weight, style}).load()
                .then((face) => {
                  document.fonts.add(face);
                })))
                .then(() => continueRender(handle))
                .catch((error: unknown) => cancelRender(error instanceof Error ? error : new Error(String(error))));
            }
            """;
    }

    /// <summary>A TypeScript string literal of an app value (font names and file names only; quotes and backslashes are dropped).</summary>
    private static string Quote(string value) => "'" + new string(value.Where(ch => ch is not ('\'' or '\\' or '\n' or '\r')).ToArray()) + "'";

    private const string IndexTs = """
        // The app's file (TM Concept); not changed by the model.
        import {registerRoot} from 'remotion';
        import {Root} from './Root';
        registerRoot(Root);
        """;

    private const string RootTsx = """
        // The app's file (TM Concept); length, fps and size come from input.json (calculated in C#).
        import React from 'react';
        import {AbsoluteFill, Composition} from 'remotion';
        import './fonts';
        import {Scene} from './Scene';
        import type {ConceptInput} from './input';

        const SceneView = Scene as unknown as React.FC<Record<string, unknown>>;

        // The template's text weight and slant, and readable text (its default: every text inherits an outline and a soft shadow in the
        // colour opposite to the text), wider over images with too little contrast (outlineBoost; from 2.2 on a soft halo as a backdrop).
        const Framed: React.FC<Record<string, unknown>> = (props) => {
          const s = (props as unknown as ConceptInput).style;
          const o = s?.outline;
          const b = Math.min(Math.max(s?.outlineBoost ?? 1, 1), 2.2);
          const e = (v: number) => `${Number((v * b).toFixed(3))}em`;
          const halo = b >= 2.2 ? `, 0 0 0.5em ${o}80` : '';
          return s ? (
            <AbsoluteFill style={{fontWeight: s.textWeight ?? 400, fontStyle: s.textStyle ?? 'normal',
              textShadow: o ? `0 0 ${e(0.05)} ${o}, 0 0 ${e(0.05)} ${o}, 0 0 ${e(0.1)} ${o}, 0 ${e(0.04)} ${e(0.14)} ${o}99${halo}` : undefined}}>
              <SceneView {...props} />
            </AbsoluteFill>
          ) : <SceneView {...props} />;
        };

        export const Root: React.FC = () => (
          <Composition
            id="Concept"
            component={Framed}
            durationInFrames={1}
            fps={30}
            width={1080}
            height={1920}
            defaultProps={{} as Record<string, unknown>}
            calculateMetadata={({props}) => {
              const input = props as unknown as ConceptInput;
              return {durationInFrames: input.totalFrames, fps: input.fps, width: input.width, height: input.height};
            }}
          />
        );
        """;

    /// <inheritdoc />
    public override async Task RenderAsync(EngineRenderRequest request, IProgress<EngineEvent>? progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        RequireReady();
        Package.Extract();
        string[] arguments =
        [
            Path.Combine(Package.Root, "render.mjs"), "--work", request.Folder, "--out", request.Output, "--crf", "20", "--jpeg", "90",
            "--concurrency", request.Workers.ToString(CultureInfo.InvariantCulture), "--gl", "swangle", "--browser", Browser()!,
        ];
        Logger.LogInformation("Remotion render: {Width}×{Height}, {Frames} frames", request.Input.Width, request.Input.Height, request.Input.TotalFrames);
        await RunNodeAsync(arguments, Package.Root, request.Folder, null, progress, requireDone: true, cancellationToken).ConfigureAwait(false);
        if (!File.Exists(request.Output))
        {
            throw new EngineException("Remotion finished without an output file.") { Code = "render" };
        }
    }

    /// <inheritdoc />
    protected override IReadOnlyDictionary<string, string> Skeleton(EngineRenderRequest request) => new Dictionary<string, string>
    {
        ["src/index.ts"] = IndexTs,
        ["src/Root.tsx"] = RootTsx,
        ["src/input.ts"] = InputTs,
        ["src/fonts.ts"] = FontsTs(request.Input),
        ["src/tm.tsx"] = TemplateHelpers.RemotionHelpers,
        ["public/.keep"] = string.Empty,
    };

    /// <summary>Remotion renders with its own Chrome Headless Shell (the version it was tested with), passed to it by path.</summary>
    protected override string? Browser() => Chrome.FindRemotion();

    /// <summary>
    /// Remotion downloads its own Chrome Headless Shell (x64 and arm64) at install time, also when another engine's browser is already there:
    /// the render must never download one.
    /// </summary>
    protected override async Task InstallBrowserAsync(IProgress<ToolProgress>? progress, CancellationToken cancellationToken)
    {
        if (Chrome.FindRemotion() is not null)
        {
            return;
        }

        progress?.Report(new ToolProgress(0.6, "chrome-headless-shell"));
        var result = await Package.RunNodeAsync([Path.Combine(Package.Root, "render.mjs"), "--ensure-browser"],
            line => Logger.LogDebug("remotion: {Line}", EnginePackage.Short(line)), cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0 || Chrome.FindRemotion() is null)
        {
            throw new ToolInstallException($"Chrome Headless Shell could not be installed ({result.ExitCode}): {Core.Processes.ChildOutput.Summary(result.LastLines)}")
            {
                Code = "download",
            };
        }
    }
}
