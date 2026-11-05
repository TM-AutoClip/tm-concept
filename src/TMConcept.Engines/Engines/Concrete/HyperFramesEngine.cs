using System.Globalization;
using Microsoft.Extensions.Logging;
using TMConcept.Core.Engines;
using TMConcept.Core.Paths;
using TMConcept.Core.Processes;
using TMConcept.Core.Render;
using TMConcept.Engines.Tools;

namespace TMConcept.Engines.Engines;

/// <summary>
/// HyperFrames (HeyGen, Apache-2.0), the default engine: HTML + a GSAP timeline captured frame by frame by a headless Chrome. Work folder:
/// <c>index.html</c> (the app's: root element, size and length from the input, the input embedded as <c>window.INPUT</c>, a Content
/// Security Policy allowing only the folder itself), <c>gsap.min.js</c> (copied from the packages, not a CDN) and <c>scene.js</c> (the
/// model's: registers its timeline in <c>window.__timelines["concept"]</c>). Rendered with telemetry, update checks and optional installs
/// off and <c>--no-browser-gpu</c>.
/// </summary>
public sealed class HyperFramesEngine(IAppPaths paths, ToolInstaller tools, ChromeLocator chrome, IProcessRunner runner, ILogger<HyperFramesEngine> logger)
    : VideoEngineBase(new EnginePackage("hyperframes", paths, tools, runner, logger), chrome, tools, runner, logger)
{
    private static readonly string Cli = Path.Combine("node_modules", "hyperframes", "bin", "hyperframes.mjs");
    private static readonly string Gsap = Path.Combine("node_modules", "gsap", "dist", "gsap.min.js");

    /// <inheritdoc />
    public override EngineKind Kind => EngineKind.HyperFrames;

    /// <inheritdoc />
    public override string Name => "HyperFrames";

    /// <inheritdoc />
    public override string CodeFile => "scene.js";

    /// <inheritdoc />
    protected override string PackageProbe => Cli;

    /// <inheritdoc />
    public override string Contract => """
        Engine: HyperFrames (HTML + GSAP 3, captured frame by frame).
        Write ONE plain JavaScript file (scene.js) that runs in the page. GSAP is already loaded as the global `gsap`.
        Read everything from `window.INPUT` (width, height, fps, durationSeconds, title, theme {background, accent, text},
        scenes [{number, startSeconds, durationSeconds, visual, caption, image}]). Never hard-code texts or durations.
        Build the DOM inside `document.getElementById("root")` with createElement / createElementNS and textContent (never innerHTML).
        Images: only `scene.image` (a relative file name) when it is not empty.
        Create ONE paused timeline: `const tl = gsap.timeline({ paused: true });` place every animation at the scene's startSeconds, and register
        it with `window.__timelines = window.__timelines || {}; window.__timelines["concept"] = tl;`.
        No network, no timers, no randomness that changes between frames, no external fonts or addresses.
        """;

    /// <inheritdoc />
    public override string SampleCode => """
        (function () {
          const input = window.INPUT;
          const root = document.getElementById("root");
          root.style.background = input.theme.background;
          const tl = gsap.timeline({ paused: true });
          const bar = document.createElement("div");
          bar.style.cssText = "position:absolute;top:10%;left:10%;height:8px;width:0;background:" + input.theme.accent;
          root.appendChild(bar);
          tl.to(bar, { width: "80%", duration: 0.8 }, 0);
          input.scenes.forEach(function (scene) {
            const box = document.createElement("div");
            box.style.cssText = "position:absolute;left:8%;right:8%;top:40%;text-align:center;font-weight:700;opacity:0;color:" + input.theme.text +
              ";font-size:" + Math.round(input.width * 0.06) + "px";
            box.textContent = scene.visual;
            const caption = document.createElement("div");
            caption.style.cssText = "position:absolute;left:8%;right:8%;bottom:12%;text-align:center;opacity:0;color:" + input.theme.text +
              ";font-size:" + Math.round(input.width * 0.035) + "px";
            caption.textContent = scene.caption;
            root.appendChild(box);
            root.appendChild(caption);
            tl.fromTo(box, { opacity: 0, y: 60 }, { opacity: 1, y: 0, duration: 0.4 }, scene.startSeconds);
            tl.to(caption, { opacity: 1, duration: 0.3 }, scene.startSeconds + 0.1);
            tl.to([box, caption], { opacity: 0, duration: 0.25 }, scene.startSeconds + scene.durationSeconds - 0.25);
          });
          window.__timelines = window.__timelines || {};
          window.__timelines["concept"] = tl;
        })();
        """;

    /// <summary>The root page: size and length from the input; the input embedded so it cannot leave its script block.</summary>
    public static string IndexHtml(RenderInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var c = CultureInfo.InvariantCulture;
        var width = input.Width.ToString(c);
        var height = input.Height.ToString(c);
        var language = input.Language.All(char.IsAsciiLetter) ? input.Language : "en";
        return $$"""
            <!doctype html>
            <!-- The app's file (TM Concept); not changed by the model. The model's code is only in scene.js. -->
            <html lang="{{language}}">
            <head>
            <meta charset="UTF-8" />
            <meta http-equiv="Content-Security-Policy" content="default-src 'self' 'unsafe-inline' data: blob:; connect-src 'self' data: blob:; img-src 'self' data: blob:; font-src 'self' data:; frame-src 'none'; object-src 'none'; base-uri 'none'; form-action 'none'" />
            <meta name="viewport" content="width={{width}}, height={{height}}" />
            <script src="gsap.min.js"></script>
            <style>
            * { margin: 0; padding: 0; box-sizing: border-box; }
            html, body { width: {{width}}px; height: {{height}}px; overflow: hidden; background: #000; }
            #root { position: relative; width: 100%; height: 100%; overflow: hidden; font-family: sans-serif; }
            </style>
            </head>
            <body>
            <div id="root" data-composition-id="concept" data-start="0" data-duration="{{input.DurationSeconds.ToString("0.###", c)}}" data-fps="{{input.Fps.ToString(c)}}" data-width="{{width}}" data-height="{{height}}"></div>
            <script>window.INPUT = {{RenderInputBuilder.ToHtmlSafeJson(input)}};</script>
            <script src="scene.js"></script>
            </body>
            </html>
            """;
    }

    /// <inheritdoc />
    public override async Task RenderAsync(EngineRenderRequest request, IProgress<EngineEvent>? progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        RequireReady();
        File.Copy(Path.Combine(Package.Root, Gsap), Path.Combine(request.Folder, "gsap.min.js"), overwrite: true);
        var c = CultureInfo.InvariantCulture;
        string[] arguments =
        [
            Path.Combine(Package.Root, Cli), "render", request.Folder, "-o", request.Output, "--format", "mp4", "--fps", request.Input.Fps.ToString(c),
            "--quality", "standard", "--workers", request.Workers.ToString(c), "--no-browser-gpu",
        ];
        var extra = new Dictionary<string, string>
        {
            ["HYPERFRAMES_BROWSER_PATH"] = Chrome.Find()!,
            ["HYPERFRAMES_EXTRACT_CACHE_DIR"] = Path.Combine(request.Folder, "tmp", "frames"),
        };
        Logger.LogInformation("HyperFrames render: {Width}×{Height}, {Seconds:0.#} s", request.Input.Width, request.Input.Height, request.Input.DurationSeconds);
        await RunNodeAsync(arguments, request.Folder, request.Folder, extra, progress, requireDone: false, cancellationToken).ConfigureAwait(false);
        if (!File.Exists(request.Output))
        {
            throw new EngineException("HyperFrames finished without an output file.") { Code = "render" };
        }

        progress?.Report(new EngineEvent(1, "done", Done: true));
    }

    /// <inheritdoc />
    protected override IReadOnlyDictionary<string, string> Skeleton(EngineRenderRequest request) =>
        new Dictionary<string, string> { ["index.html"] = IndexHtml(request.Input) };
}
