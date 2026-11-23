using TMConcept.Core.Templates;

namespace TMConcept.Core.Styles;

/// <summary>
/// The definitions of the drawing styles (0.1.32: every place that needs a style's words reads them here, so a new style is one entry). The
/// material and motion rules were written for this app after reading MIT projects as recipes, without taking their code or prompt text:
/// ersinkoc/AI-Video-Factory (entrance length caps, paper slide-in with a fixed shadow, isometric outline-then-fill, texture layers),
/// deenukhan/animation-styles and cth9191/animate (blueprint, hologram, terminal and pencil-sketch looks) and kaomei/stickman-video-director
/// (a stick-figure presenter, setup → action → reaction, a visual change every two to three seconds).
/// </summary>
public static class DrawStyles
{
    private static readonly LocalizedText NoGuidance = new();

    /// <summary>Every style in the order of the template editor (the AI choice first).</summary>
    public static readonly IReadOnlyList<DrawStyleDefinition> All =
    [
        new(DrawStyle.Auto, "auto", string.Empty, string.Empty, string.Empty, StyleTexture.None, string.Empty, NoGuidance),
        new(DrawStyle.Flat, "flat",
            "flat vector illustration, solid colours, simple shapes",
            "flat shapes and simple icons in solid colours",
            "Shapes pop in (at most 0.45 s, a little overshoot) and settle; no outlines.",
            StyleTexture.None, string.Empty, NoGuidance),
        new(DrawStyle.StickFigure, "stick-figure",
            "minimal stick-figure illustration, simple line characters with round heads, plain background, generous empty space on one side",
            "a stick-figure presenter (round head, line body) at one side who acts out and points at the scene's subject, next to simple line "
            + "drawings or diagrams of it",
            "The presenter changes pose on key words (point, walk, think, cheer); something changes every 2–3 s; drawings appear as reactions to it.",
            StyleTexture.None, "paper", new LocalizedText
            {
                Tr = "Bu video bir çöp adam anlatıcıyla çizilecek: anlatıcı sahnede durur, konuşur, gösterir, yürür ve olanlara tepki verir. Her sahnenin "
                    + "\"ekranda\" satırını anlatıcının yaptığı somut bir hareketle yaz (ör. \"çöp adam haritada rotayı parmağıyla çiziyor\", \"çöp adam "
                    + "dev bir çipin yanında küçük kalıyor\"). Kur, eylem, tepki zinciri kur: bir şey olur, anlatıcı bir şey yapar, sonuç görünür. "
                    + "Soyut fikirleri görsel benzetmeyle anlat (ör. bellek = çekmeceli dolap). Her bölümde tanınır yeni bir yer olsun. Konu teknik ya da "
                    + "tarihî olsa da anlatıcı yanındaki çizimleri, şemaları ve nesneleri gösterir; seslendirme anlatıcının ağzından, izleyiciye konuşur gibi.",
                En = "This video is drawn with a stick-figure narrator: the narrator stands in the scene, talks, shows, walks and reacts. Write every scene's "
                    + "\"ekranda\" line as a concrete action of the narrator (e.g. \"the stick figure traces the route on the map with a finger\", \"the "
                    + "stick figure looks tiny next to a giant chip\"). Build setup, action, reaction chains: something happens, the narrator does "
                    + "something, the result shows. Explain abstract ideas with a visual metaphor (memory = a chest of drawers). Every segment gets a new "
                    + "recognisable place. Even for a technical or historical subject the narrator points at the drawings, diagrams and objects beside "
                    + "it; the narration is the narrator speaking to the viewer.",
            }),
        new(DrawStyle.LineArt, "line-art",
            "clean line art, thin ink lines, minimal shading",
            "thin one-colour line drawings without fills",
            "Lines draw on along their length (stroke-dashoffset, 0.6–1.2 s) before anything is filled.",
            StyleTexture.Paper, string.Empty, NoGuidance),
        new(DrawStyle.PaperCut, "paper-cut",
            "layered paper cut-out illustration, soft shadows, craft paper texture",
            "layered paper cut-out shapes with soft shadows",
            "Paper pieces slide in horizontally to their place (at most 0.9 s) and keep one fixed soft shadow; paper never fades, morphs or bends.",
            StyleTexture.Paper, "paper", NoGuidance),
        new(DrawStyle.Infographic, "infographic",
            "clean infographic-style illustration with simple icons and diagrams (no text)",
            "an infographic: big numbers, simple charts, icons and arrows",
            "Bars grow from zero, numbers count up and land with a small scale, connectors draw on one after another.",
            StyleTexture.None, string.Empty, NoGuidance),
        new(DrawStyle.Neon, "neon",
            "neon glow illustration, glowing lines on a dark background",
            "glowing accent lines on the dark background (a soft glow, no fills)",
            "Glowing lines draw on, then breathe softly; at most one flicker per scene.",
            StyleTexture.Grain, "ocean", NoGuidance),
        new(DrawStyle.PencilSketch, "pencil-sketch",
            "pencil sketch, graphite lines and cross-hatching on warm off-white paper, hand-drawn, no colour except one small accent",
            "graphite pencil drawings: a light construction line first, then a darker confident line, shading only with hatching",
            "Everything is drawn on, never popped: pencil first, then the dark line, then a quick scribbled colour; one accent colour, on the subject "
            + "only; no gradients.",
            StyleTexture.Paper, "sketch", NoGuidance),
        new(DrawStyle.Cartoon, "cartoon",
            "2D cartoon animation style, bold clean outlines, flat cel shading, expressive simple characters, vibrant limited palette, storybook "
            + "background",
            "cartoon shapes with thick dark outlines and flat cel-shaded colours, rounded friendly forms",
            "Squash and stretch on entrances (at most 0.45 s), a small anticipation before a move, a short hold after it.",
            StyleTexture.None, string.Empty, NoGuidance, PrefersImages: true),
        new(DrawStyle.Blueprint, "blueprint",
            "white technical blueprint drawing on deep blueprint blue, fine grid, dimension lines, schematic, no text",
            "white technical line drawings on the blue background: outlines, section views, dimension arrows and a faint grid",
            "Lines draw on (stroke-dashoffset), then dimension arrows extend and small labels appear; nothing bounces.",
            StyleTexture.Grid, "blueprint", NoGuidance),
        new(DrawStyle.Hologram, "hologram",
            "cyan sci-fi hologram, glowing thin wireframes and HUD readouts on a near-black background, futuristic interface, no text",
            "thin cyan wireframes, rings and readout panels that glow on the dark background, one warm amber accent",
            "Wireframes draw on and rotate slowly; panels open with a scan line; the opacity flickers gently between 0.85 and 1.",
            StyleTexture.Scanlines, "hologram", NoGuidance),
        new(DrawStyle.Terminal, "terminal",
            "retro computer terminal screen, green phosphor text-mode graphics on black, ASCII art look, CRT glow",
            "monospace text-mode drawings: shapes built from characters (the ramp \" .:-=+*#%@\"), boxes and progress bars in the accent colour",
            "Text types in character by character (12–15 per second) with a blinking cursor; whole lines scroll up; no smooth tweening.",
            StyleTexture.Scanlines, "terminal", NoGuidance),
        new(DrawStyle.Isometric, "isometric",
            "isometric illustration, small 3D-looking blocks and buildings at 30 degrees, clean flat shading, soft palette",
            "isometric shapes (30° projection): blocks, platforms and small buildings, outline first, then the faces filled",
            "Each object draws its outline along its length, then its faces fill in; objects rise onto the grid one after another.",
            StyleTexture.None, string.Empty, NoGuidance),
        new(DrawStyle.Watercolor, "watercolor",
            "watercolour painting, soft wet-edged washes and blooms on textured paper, light pencil underdrawing",
            "soft translucent colour washes with lighter edges, a thin pencil line under them",
            "Washes bloom in slowly (about 0.8 s) and stay; no hard pops.",
            StyleTexture.Paper, "paper", NoGuidance, PrefersImages: true),
    ];

    /// <summary>The definition of <paramref name="style"/> (an unknown value reads as <see cref="DrawStyle.Flat"/>).</summary>
    public static DrawStyleDefinition Of(DrawStyle style) => All.FirstOrDefault(d => d.Style == style) ?? All.First(d => d.Style == DrawStyle.Flat);

    /// <summary>The definition with the token <paramref name="key"/> ("pencil-sketch"); null when there is none (and for "auto").</summary>
    public static DrawStyleDefinition? FindKey(string? key) =>
        All.FirstOrDefault(d => d.Style != DrawStyle.Auto && string.Equals(d.Key, key?.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// The concept prompt's style line for concepts written for <paramref name="style"/> in a prompt language ("tr" or English): the style's
    /// own guidance (a storytelling style such as the stick-figure narrator), otherwise one sentence that the scenes must be drawable in its
    /// look. Empty for no style and for <see cref="DrawStyle.Auto"/> (the look is chosen per video).
    /// </summary>
    public static string ConceptLine(DrawStyle? style, string language)
    {
        if (style is not { } chosen || chosen == DrawStyle.Auto)
        {
            return string.Empty;
        }

        var definition = Of(chosen);
        if (definition.HasConceptGuidance)
        {
            return definition.GuidanceFor(language);
        }

        return language == Profiles.ContentLanguages.Turkish
            ? "Video şu tarzda çizilecek: " + definition.Drawing + ". \"ekranda\" satırlarını bu tarzda çizilebilecek biçimde yaz."
            : "The video is drawn as " + definition.Drawing + ". Write the \"ekranda\" lines so they can be drawn in this look.";
    }

    /// <summary>The styles the model may choose from for <see cref="DrawStyle.Auto"/>.</summary>
    public static IEnumerable<DrawStyleDefinition> Choosable => All.Where(d => d.Style != DrawStyle.Auto);
}
