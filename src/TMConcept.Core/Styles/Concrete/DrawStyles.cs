using TMConcept.Core.Templates;

namespace TMConcept.Core.Styles;

/// <summary>
/// The definitions of the drawing styles (0.1.32: every place that needs a style's words reads them here, so a new style is one entry). The
/// material and motion rules were written for this app after reading MIT projects as recipes, without taking their code or prompt text:
/// ersinkoc/AI-Video-Factory (entrance length caps, paper slide-in with a fixed shadow, isometric outline-then-fill, texture layers),
/// deenukhan/animation-styles and cth9191/animate (blueprint, hologram, terminal and pencil-sketch looks) and kaomei/stickman-video-director
/// (a stick-figure presenter, setup → action → reaction, a visual change every two to three seconds). F12 (0.1.43): every style's known
/// mistakes (<see cref="DrawStyleDefinition.Pitfalls"/>, a "known pitfalls" list per style as in ReelMimic's style file template, MIT) and
/// its finishing layer (<see cref="DrawStyleDefinition.Finish"/>: paper tooth, grain, vignette, glow, shadow by depth and a 12-step boil,
/// effects read from ReelMimic's own engines, MIT; the watercolour and pastel engines built on p5.js are not used). F13 (0.1.51): pixel art,
/// after ReelMimic's own pixel-art engine (MIT; <see cref="PixelArtLimits"/>), with its own transition family and no finishing layer.
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
            StyleTexture.None, string.Empty, NoGuidance)
        {
            Pitfalls =
            [
                "Flat shapes carry no detail: a map region wider than a continent or a diagram with more than six boxes turns into confetti.",
                "Without outlines the accent and the surface colours must differ: never put an accent pin on an accent country.",
                "Pops need room: keep every anchor at least a tenth of the frame away from the others.",
            ],
            Finish = new(Grain: 0.25, Vignette: 0.3),
        },
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
            })
        {
            Pitfalls =
            [
                "The presenter stands in the lower left corner: keep pins, labels and diagram boxes out of that corner.",
                "A beat without an action leaves the presenter idle: every beat points at, walks to or reacts to something.",
                "Stick figures are tiny: walkers only on routes longer than a third of the frame.",
            ],
            Finish = new(Tooth: 0.35, Vignette: 0.2, Boil: 0.5),
        },
        new(DrawStyle.LineArt, "line-art",
            "clean line art, thin ink lines, minimal shading",
            "thin one-colour line drawings without fills",
            "Lines draw on along their length (stroke-dashoffset, 0.6–1.2 s) before anything is filled.",
            StyleTexture.Paper, string.Empty, NoGuidance)
        {
            Pitfalls =
            [
                "Thin lines vanish on a busy picture: on an image base use at most two paths and short labels.",
                "Lines draw on one after another: no more than two drawn paths in one beat.",
                "No filled shapes: prefer pins, paths and arrows to country fills.",
            ],
            Finish = new(Tooth: 0.45, Grain: 0.15, Vignette: 0.2),
        },
        new(DrawStyle.PaperCut, "paper-cut",
            "layered paper cut-out illustration, soft shadows, craft paper texture",
            "layered paper cut-out shapes with soft shadows",
            "Paper pieces slide in horizontally to their place (at most 0.9 s) and keep one fixed soft shadow; paper never fades, morphs or bends.",
            StyleTexture.Paper, "paper", NoGuidance, Transition: Scenes.SceneTransitions.Slide)
        {
            Pitfalls =
            [
                "Paper never fades, morphs or bends: no zoom deeper than 1.6 and no camera move shorter than its sentence.",
                "Every piece casts a shadow: keep labels and pins apart, overlapping pieces read as a pile.",
                "Paper slides in from the side: do not start two pieces at the same place in one beat.",
            ],
            Finish = new(Tooth: 0.5, Vignette: 0.25, Shadow: 1, Boil: 0.6),
        },
        new(DrawStyle.Infographic, "infographic",
            "clean infographic-style illustration with simple icons and diagrams (no text)",
            "an infographic: big numbers, simple charts, icons and arrows",
            "Bars grow from zero, numbers count up and land with a small scale, connectors draw on one after another.",
            StyleTexture.None, string.Empty, NoGuidance)
        {
            Pitfalls =
            [
                "A number without a source sentence is a lie: every counter shows a number the narration says.",
                "Counters need time to count: no counter on a sentence shorter than two seconds.",
                "One chart idea per scene: no more than one counter and one diagram in a scene.",
            ],
            Finish = new(Grain: 0.15, Vignette: 0.2),
        },
        new(DrawStyle.Neon, "neon",
            "neon glow illustration, glowing lines on a dark background",
            "glowing accent lines on the dark background (a soft glow, no fills)",
            "Glowing lines draw on, then breathe softly; at most one flicker per scene.",
            StyleTexture.Grain, "ocean", NoGuidance)
        {
            Pitfalls =
            [
                "Glow adds up: a map with many coloured countries becomes a wall of light; colour at most three countries in a scene.",
                "Glowing lines need the dark: keep the camera off bright image areas and prefer map and diagram bases.",
                "At most one flicker per scene: do not highlight the same point twice.",
            ],
            Finish = new(Grain: 0.35, Vignette: 0.6, Bloom: 0.8),
        },
        new(DrawStyle.PencilSketch, "pencil-sketch",
            "pencil sketch, graphite lines and cross-hatching on warm off-white paper, hand-drawn, no colour except one small accent",
            "graphite pencil drawings: a light construction line first, then a darker confident line, shading only with hatching",
            "Everything is drawn on, never popped: pencil first, then the dark line, then a quick scribbled colour; one accent colour, on the subject "
            + "only; no gradients.",
            StyleTexture.Paper, "sketch", NoGuidance, Transition: Scenes.SceneTransitions.Wipe)
        {
            Pitfalls =
            [
                "Graphite is drawn, never popped: give every drawn path a sentence of its own.",
                "One accent colour, on the subject only: colour one country or one highlight per scene, not more.",
                "Hatching needs space: diagrams with at most five boxes.",
            ],
            Finish = new(Tooth: 0.8, Grain: 0.25, Vignette: 0.3, Boil: 0.4),
        },
        new(DrawStyle.Cartoon, "cartoon",
            "2D cartoon animation style, bold clean outlines, flat cel shading, expressive simple characters, vibrant limited palette, storybook "
            + "background",
            "cartoon shapes with thick dark outlines and flat cel-shaded colours, rounded friendly forms",
            "Squash and stretch on entrances (at most 0.45 s), a small anticipation before a move, a short hold after it.",
            StyleTexture.None, string.Empty, NoGuidance, PrefersImages: true)
        {
            Pitfalls =
            [
                "Bold outlines need size: labels stay short (one or two words) and anchors far from the edges.",
                "Squash and stretch on every entrance tires the eye: at most two actions per beat.",
                "Cartoon scenes need a place: prefer the picture or a map to a bare title card.",
            ],
            Finish = new(Grain: 0.15, Vignette: 0.3, Boil: 0.3),
        },
        new(DrawStyle.Blueprint, "blueprint",
            "white technical blueprint drawing on deep blueprint blue, fine grid, dimension lines, schematic, no text",
            "white technical line drawings on the blue background: outlines, section views, dimension arrows and a faint grid",
            "Lines draw on (stroke-dashoffset), then dimension arrows extend and small labels appear; nothing bounces.",
            StyleTexture.Grid, "blueprint", NoGuidance, Transition: Scenes.SceneTransitions.Wipe)
        {
            Pitfalls =
            [
                "A blueprint explains how something is built: prefer diagram bases with the parts as boxes and the links between them.",
                "Nothing bounces: use calm eases (power2.inOut or sine.inOut) for the camera.",
                "White lines on blue lose fine detail: no map region smaller than a country.",
            ],
            Finish = new(Tooth: 0.25, Grain: 0.25, Vignette: 0.4),
        },
        new(DrawStyle.Hologram, "hologram",
            "cyan sci-fi hologram, glowing thin wireframes and HUD readouts on a near-black background, futuristic interface, no text",
            "thin cyan wireframes, rings and readout panels that glow on the dark background, one warm amber accent",
            "Wireframes draw on and rotate slowly; panels open with a scan line; the opacity flickers gently between 0.85 and 1.",
            StyleTexture.Scanlines, "hologram", NoGuidance)
        {
            Pitfalls =
            [
                "Wireframes glow on the dark: avoid image bases with bright skies or white backgrounds.",
                "Scan lines already move: no more than one highlight per beat.",
                "Readout panels need room: diagrams with at most six boxes, labels of at most two words.",
            ],
            Finish = new(Grain: 0.35, Vignette: 0.6, Bloom: 0.7, Boil: 0.2),
        },
        new(DrawStyle.Terminal, "terminal",
            "retro computer terminal screen, green phosphor text-mode graphics on black, ASCII art look, CRT glow",
            "monospace text-mode drawings: shapes built from characters (the ramp \" .:-=+*#%@\"), boxes and progress bars in the accent colour",
            "Text types in character by character (12–15 per second) with a blinking cursor; whole lines scroll up; no smooth tweening.",
            StyleTexture.Scanlines, "terminal", NoGuidance, Transition: Scenes.SceneTransitions.Cut)
        {
            Pitfalls =
            [
                "Text types in at about 13 characters a second: labels of at most two words, and none on a sentence shorter than two seconds.",
                "Character graphics lose detail: prefer title cards, counters and diagrams to maps with many borders.",
                "No smooth tweening: few camera moves, and no travelling walkers.",
            ],
            Finish = new(Grain: 0.45, Vignette: 0.7, Bloom: 0.5),
        },
        new(DrawStyle.Isometric, "isometric",
            "isometric illustration, small 3D-looking blocks and buildings at 30 degrees, clean flat shading, soft palette",
            "isometric shapes (30° projection): blocks, platforms and small buildings, outline first, then the faces filled",
            "Each object draws its outline along its length, then its faces fill in; objects rise onto the grid one after another.",
            StyleTexture.None, string.Empty, NoGuidance)
        {
            Pitfalls =
            [
                "Isometric blocks need depth: a map is flat, prefer diagram bases laid out on a diagonal.",
                "Objects rise one after another: at most two actions in a beat.",
                "Keep the camera's zoom at 1.5 or less: a deeper zoom breaks the 30 degree grid.",
            ],
            Finish = new(Grain: 0.15, Vignette: 0.2, Shadow: 0.6),
        },
        new(DrawStyle.Watercolor, "watercolor",
            "watercolour painting, soft wet-edged washes and blooms on textured paper, light pencil underdrawing",
            "soft translucent colour washes with lighter edges, a thin pencil line under them",
            "Washes bloom in slowly (about 0.8 s) and stay; no hard pops.",
            StyleTexture.Paper, "paper", NoGuidance, PrefersImages: true)
        {
            Pitfalls =
            [
                "Washes bloom slowly: no more than one new action in a beat, and none on a sentence shorter than two seconds.",
                "Soft edges blur small things: labels and pins only on large, calm areas of the picture.",
                "No hard pops: counters and fast camera moves fight the look; prefer slow drifts.",
            ],
            Finish = new(Tooth: 0.8, Grain: 0.15, Vignette: 0.3, Bloom: 0.2),
        },
        new(DrawStyle.PixelArt, "pixel-art",
            "pixel art, 16-bit retro game scene, limited colour palette, crisp square pixels, dithered shading, no anti-aliasing, no text",
            "chunky pixel art on a 480 × 270 grid blown up four times: a few colour ramps of the palette, shading by ordered dithering instead "
            + "of gradients, hard square edges",
            "Everything snaps to whole pixels and changes in steps, 12 a second; no soft gradients, glows or blur; scenes change by a dither "
            + "dissolve, an iris or stair-stepped blocks straight into the next scene, never through black.",
            StyleTexture.None, string.Empty, NoGuidance, Transition: Scenes.SceneTransitions.Dissolve)
        {
            Transitions = [Scenes.SceneTransitions.Dissolve, Scenes.SceneTransitions.Iris, Scenes.SceneTransitions.Blocks],
            Pitfalls =
            [
                "A transition through black for three or more frames reads as a blink: go from scene to scene by dissolve, iris or blocks.",
                "Pixels are few: labels of at most two words, and no map region smaller than a large country.",
                "Soft moves break the grid: few camera moves, zoom 1 or 2, and no counter on a sentence shorter than two seconds.",
            ],
        },
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
