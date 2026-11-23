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
        Read everything from `window.INPUT` (width, height, fps, durationSeconds, title, theme {background, accent, text}, style (the
        template, when there is one), scenes [{number, startSeconds, durationSeconds, visual, image, transition}]). Never hard-code texts or durations.
        Build the DOM inside `document.getElementById("root")` with createElement / createElementNS and textContent (never innerHTML).
        Images: only `scene.image` (a relative file name) when it is not empty.
        Create ONE paused timeline: `const tl = gsap.timeline({ paused: true });` place every animation at the scene's startSeconds, and register
        it with `window.__timelines = window.__timelines || {}; window.__timelines["concept"] = tl;`.
        No network, no timers, no randomness that changes between frames, no external fonts or addresses.
        """;

    /// <summary>
    /// The minimal file the repair request shows (20 Nov 2026: it shows each scene's image full frame with a slow zoom; the old one drew only
    /// a bar and the visual text, and a small local model copied it into a video without its image) and the install check renders.
    /// </summary>
    public override string SampleCode => """
        (function () {
          const input = window.INPUT;
          const root = document.getElementById("root");
          root.style.background = input.theme.background;
          const tl = gsap.timeline({ paused: true });
          input.scenes.forEach(function (scene) {
            const layer = document.createElement("div");
            layer.style.cssText = "position:absolute;left:0;top:0;width:100%;height:100%;overflow:hidden;opacity:0";
            layer.style.background = input.theme.background;
            root.appendChild(layer);
            tl.set(layer, { opacity: 1 }, scene.startSeconds);
            if (scene.image) {
              const picture = document.createElement("img");
              picture.src = scene.image;
              picture.style.cssText = "position:absolute;left:0;top:0;width:100%;height:100%;object-fit:cover";
              layer.appendChild(picture);
              tl.fromTo(picture, { scale: 1 }, { scale: 1.08, duration: scene.durationSeconds, ease: "none" }, scene.startSeconds);
            } else {
              const box = document.createElement("div");
              box.style.cssText = "position:absolute;left:8%;right:8%;top:40%;text-align:center;font-weight:700";
              box.style.color = input.theme.text;
              box.style.fontSize = Math.round(input.width * 0.06) + "px";
              box.textContent = scene.visual;
              layer.appendChild(box);
              tl.fromTo(box, { opacity: 0, y: 60 }, { opacity: 1, y: 0, duration: 0.4 }, scene.startSeconds);
            }
          });
          window.__timelines = window.__timelines || {};
          window.__timelines["concept"] = tl;
        })();
        """;

    /// <summary>
    /// The app's own scene code: each scene on its own full-frame layer, set at the scene's start (no transition of its own: the program fades
    /// where the input says so), its image slowly zooming; the title over the first scene, the visual idea over a scene without an image. The
    /// template's colours and title style through <c>window.TM</c> when the video has a template, the input's theme otherwise.
    /// </summary>
    public override string FallbackCode => """
        (function () {
          const input = window.INPUT;
          const root = document.getElementById("root");
          const tm = window.TM;
          const unit = Math.min(input.width, input.height);
          function paint(name) {
            if (tm) return tm.color(name);
            if (name === "text") return input.theme.text;
            if (name === "accent") return input.theme.accent;
            return input.theme.background;
          }
          function heading(words) {
            if (tm) return tm.title(words);
            const el = document.createElement("div");
            el.textContent = words;
            el.style.cssText = "position:absolute;left:8%;right:8%;top:12%;text-align:center;font-weight:700;line-height:1.15";
            el.style.color = paint("text");
            el.style.fontSize = Math.round(unit * 0.07) + "px";
            return el;
          }
          root.style.background = paint("background");
          const tl = gsap.timeline({ paused: true });
          input.scenes.forEach(function (scene, index) {
            const layer = document.createElement("div");
            layer.style.cssText = "position:absolute;left:0;top:0;width:100%;height:100%;overflow:hidden;opacity:0";
            layer.style.background = paint("background");
            root.appendChild(layer);
            tl.set(layer, { opacity: 1 }, scene.startSeconds);
            if (scene.image) {
              const picture = document.createElement("img");
              picture.src = scene.image;
              picture.style.cssText = "position:absolute;left:0;top:0;width:100%;height:100%;object-fit:cover";
              layer.appendChild(picture);
              tl.fromTo(picture, { scale: 1 }, { scale: 1.1, duration: scene.durationSeconds, ease: "none" }, scene.startSeconds);
            }
            const words = index === 0 ? input.title : (scene.image ? "" : scene.visual);
            if (words) {
              const title = heading(words);
              layer.appendChild(title);
              tl.fromTo(title, { opacity: 0, y: unit * 0.03 }, { opacity: 1, y: 0, duration: 0.5 }, scene.startSeconds + 0.2);
            }
          });
          window.__timelines = window.__timelines || {};
          window.__timelines["concept"] = tl;
        })();
        """;

    /// <summary>The template helper file (<see cref="TemplateHelpers.HyperFramesScript"/>), written when the video has a template.</summary>
    public const string HelperFile = "tm.js";

    /// <summary>
    /// The root page: size and length from the input; the input embedded so it cannot leave its script block. With a template
    /// (<see cref="RenderInput.Style"/>) also the template's font (from the project's fonts/ folder, preloaded), its colours as CSS variables
    /// (<c>--tm-&lt;name&gt;</c>, <c>--tm-font</c>), the title classes of <see cref="TemplateHelpers.HyperFramesScript"/> and that helper file
    /// before scene.js. A video without a template gets exactly the page it had.
    /// </summary>
    public static string IndexHtml(RenderInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var c = CultureInfo.InvariantCulture;
        var width = input.Width.ToString(c);
        var height = input.Height.ToString(c);
        var language = input.Language.All(char.IsAsciiLetter) ? input.Language : "en";
        var style = input.Style;
        var head = style is null ? string.Empty : TemplateHead(input, style);
        var helper = style is null ? string.Empty : "<script src=\"" + HelperFile + "\"></script>\n";

        // 0.1.33: a video drawn by the app with a map scene gets the world map before its scene file.
        if (Core.Plans.PlanDrawer.NeedsWorld(input))
        {
            helper += "<script src=\"" + Core.Plans.PlanDrawer.WorldFile + "\"></script>\n";
        }
        var rootFont = style is null ? "sans-serif" : "var(--tm-font)";
        // 0.1.32: the style's texture, the app's own still layer over the whole picture (nothing for a video without a template).
        var textureCss = StyleTextureCss.Css(style?.Texture);
        var texture = StyleTextureCss.Element(style?.Texture);
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
            #root { position: relative; width: 100%; height: 100%; overflow: hidden; font-family: {{rootFont}}; }
            {{head}}{{textureCss}}</style>
            </head>
            <body>
            <div id="root" data-composition-id="concept" data-start="0" data-duration="{{input.DurationSeconds.ToString("0.###", c)}}" data-fps="{{input.Fps.ToString(c)}}" data-width="{{width}}" data-height="{{height}}"></div>
            {{texture}}<script>window.INPUT = {{RenderInputBuilder.ToHtmlSafeJson(input)}};</script>
            {{helper}}<script src="scene.js"></script>
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
    protected override IReadOnlyDictionary<string, string> Skeleton(EngineRenderRequest request)
    {
        var files = new Dictionary<string, string> { ["index.html"] = IndexHtml(request.Input) };
        if (request.Input.Style is not null)
        {
            files[HelperFile] = TemplateHelpers.HyperFramesScript;
        }

        if (Core.Plans.PlanDrawer.NeedsWorld(request.Input))
        {
            files[Core.Plans.PlanDrawer.WorldFile] = Core.Plans.PlanDrawer.WorldScript;
        }

        return files;
    }

    /// <summary>
    /// The template's part of the page style: the font faces (a single-weight font claims every weight, so no fake bold is drawn), the colour
    /// and font variables and the title classes. Values are the app's own (palette, font table), never the model's.
    /// </summary>
    private static string TemplateHead(RenderInput input, RenderStyle style)
    {
        var c = CultureInfo.InvariantCulture;
        var family = Css(style.Font);
        var text = new System.Text.StringBuilder();
        var faces = RenderStyleBuilder.FacesOf(style);
        foreach (var face in faces)
        {
            text.Append(c, $"@font-face {{ font-family: \"{family}\"; src: url(\"{Css(face.File)}\") format(\"{Css(face.Format)}\"); font-weight: {Css(face.Weight)}; ")
                .Append(c, $"font-style: {Css(face.Style)}; font-display: block; }}\n");
        }

        var slant = style.TextStyle == "italic" ? "italic" : "normal";

        var colors = style.Colors;
        text.Append(c, $":root {{ --tm-background: {Css(colors.Background)}; --tm-surface: {Css(colors.Surface)}; --tm-accent: {Css(colors.Accent)}; ")
            .Append(c, $"--tm-accent2: {Css(colors.Accent2)}; --tm-text: {Css(colors.Text)}; --tm-muted: {Css(colors.Muted)}; ")
            .Append(c, $"--tm-font: \"{family}\", sans-serif; --tm-unit: {Math.Min(input.Width, input.Height)}px; }}\n")
            .Append(c, $"#root {{ color: var(--tm-text); background: var(--tm-background); font-weight: {style.TextWeight}; font-style: {slant}; }}\n")
            .Append(Readable(style.Outline, style.OutlineBoost))
            .Append(c, $".tm-title {{ font-family: var(--tm-font); font-weight: {style.TitleWeight}; font-style: {slant}; color: var(--tm-text); font-size: calc(var(--tm-unit) * 0.075); line-height: 1.15; }}\n")
            .Append(".tm-title-band { background: var(--tm-surface); padding-top: 0.35em; padding-bottom: 0.35em; }\n")
            .Append(".tm-title-outline { -webkit-text-stroke: 0.06em var(--tm-background); paint-order: stroke fill; }\n")
            .Append(".tm-title-underline::after { content: \"\"; display: block; width: 2.2em; height: 0.12em; margin-top: 0.25em; background: var(--tm-accent); }\n")
            .Append(".tm-title-underline.tm-align-center::after { margin-left: auto; margin-right: auto; }\n")
            .Append(".tm-title-underline.tm-align-right::after { margin-left: auto; }\n")
            .Append(".tm-title-boxed { background: var(--tm-accent); color: var(--tm-background); padding: 0.3em 0.6em; border-radius: 0.35em; }\n");
        foreach (var face in faces)
        {
            var type = face.Format == "opentype" ? "font/otf" : "font/ttf";
            text.Append(c, $"</style>\n<link rel=\"preload\" href=\"{Css(face.File)}\" as=\"font\" type=\"{type}\" crossorigin />\n<style>\n");
        }

        return text.ToString();
    }

    /// <summary>
    /// Readable text (the template's default since 0.1.24): every text on the page inherits an outline and a soft shadow in the colour
    /// opposite to the text colour (text-shadow is inherited, so the code's own text gets it too); nothing when the template has it off.
    /// <paramref name="boost"/> (0.1.25, <see cref="Core.Styles.TextBackdrop"/>) widens it over images with too little contrast; from
    /// <see cref="Core.Styles.TextBackdrop.BackdropBoost"/> on a soft halo behind the text works as a subtle backdrop.
    /// </summary>
    public static string Readable(string outline, double boost = 1)
    {
        ArgumentNullException.ThrowIfNull(outline);
        if (outline.Length == 0)
        {
            return string.Empty;
        }

        var o = Css(outline);
        var b = Math.Clamp(boost, 1, Core.Styles.TextBackdrop.BackdropBoost);
        string Em(double value) => (value * b).ToString("0.###", CultureInfo.InvariantCulture) + "em";
        var halo = b >= Core.Styles.TextBackdrop.BackdropBoost ? $", 0 0 0.5em {o}80" : string.Empty;
        return $"#root {{ --tm-outline: {o}; text-shadow: 0 0 {Em(0.05)} {o}, 0 0 {Em(0.05)} {o}, 0 0 {Em(0.1)} {o}, 0 {Em(0.04)} {Em(0.14)} {o}99{halo}; }}\n";
    }

    /// <summary>A value for the page's style: only characters a font name, file name or colour needs (the app's own values pass unchanged).</summary>
    private static string Css(string value) =>
        new(value.Where(ch => char.IsAsciiLetterOrDigit(ch) || ch is ' ' or '-' or '_' or '.' or '/' or '#').ToArray());
}
