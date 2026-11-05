using System.Globalization;
using Microsoft.Extensions.Logging;
using TMConcept.Core.Engines;
using TMConcept.Core.Paths;
using TMConcept.Core.Processes;
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
        Allowed imports only: 'react', 'remotion' and `import type {ConceptInput, ConceptScene} from './input';`.
        The input type: {width, height, fps, totalFrames, durationSeconds, language, title, theme {background, accent, text},
        scenes [{number, startFrame, frameCount, startSeconds, durationSeconds, visual, caption, image}]}.
        Put each scene in `<Sequence from={scene.startFrame} durationInFrames={scene.frameCount}>`; animate with useCurrentFrame,
        interpolate and spring. Images: `<Img src={staticFile(scene.image)} />` only when scene.image is not empty.
        Never hard-code texts or durations. No network, no external fonts, no randomness that changes between frames.
        """;

    /// <inheritdoc />
    public override string SampleCode => """
        import React from 'react';
        import {AbsoluteFill, Sequence, interpolate, spring, useCurrentFrame, useVideoConfig} from 'remotion';
        import type {ConceptInput, ConceptScene} from './input';

        const Card: React.FC<{scene: ConceptScene; input: ConceptInput}> = ({scene, input}) => {
          const frame = useCurrentFrame();
          const {fps} = useVideoConfig();
          const enter = spring({frame, fps, config: {damping: 200}});
          const leave = interpolate(frame, [scene.frameCount - 8, scene.frameCount], [1, 0], {extrapolateLeft: 'clamp', extrapolateRight: 'clamp'});
          return (
            <AbsoluteFill style={{justifyContent: 'center', alignItems: 'center', opacity: leave}}>
              <div style={{color: input.theme.text, fontSize: input.width * 0.06, fontWeight: 700, textAlign: 'center',
                padding: '0 8%', transform: `translateY(${(1 - enter) * 60}px)`, opacity: enter}}>{scene.visual}</div>
              <div style={{position: 'absolute', bottom: '12%', left: '8%', right: '8%', color: input.theme.text,
                fontSize: input.width * 0.035, textAlign: 'center'}}>{scene.caption}</div>
              <div style={{position: 'absolute', top: '10%', left: '10%', height: 8, width: `${enter * 80}%`, background: input.theme.accent}} />
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
        };
        export type ConceptTheme = {background: string; accent: string; text: string};
        export type ConceptInput = {
          width: number;
          height: number;
          fps: number;
          totalFrames: number;
          durationSeconds: number;
          language: string;
          title: string;
          theme: ConceptTheme;
          scenes: ConceptScene[];
        };
        """;

    private const string IndexTs = """
        // The app's file (TM Concept); not changed by the model.
        import {registerRoot} from 'remotion';
        import {Root} from './Root';
        registerRoot(Root);
        """;

    private const string RootTsx = """
        // The app's file (TM Concept); length, fps and size come from input.json (calculated in C#).
        import React from 'react';
        import {Composition} from 'remotion';
        import {Scene} from './Scene';
        import type {ConceptInput} from './input';

        export const Root: React.FC = () => (
          <Composition
            id="Concept"
            component={Scene as unknown as React.FC<Record<string, unknown>>}
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
            "--concurrency", request.Workers.ToString(CultureInfo.InvariantCulture), "--gl", "swangle",
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
        ["public/.keep"] = string.Empty,
    };

    /// <summary>Remotion downloads its own Chrome Headless Shell (x64 and arm64).</summary>
    protected override async Task InstallBrowserAsync(IProgress<ToolProgress>? progress, CancellationToken cancellationToken)
    {
        if (Chrome.Find() is not null)
        {
            return;
        }

        progress?.Report(new ToolProgress(0.6, "chrome-headless-shell"));
        var result = await Package.RunNodeAsync([Path.Combine(Package.Root, "render.mjs"), "--ensure-browser"],
            line => Logger.LogDebug("remotion: {Line}", EnginePackage.Short(line)), cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0 || Chrome.Find() is null)
        {
            throw new ToolInstallException($"Chrome Headless Shell could not be installed ({result.ExitCode}): {EnginePackage.Short(result.LastLines)}")
            {
                Code = "download",
            };
        }
    }
}
