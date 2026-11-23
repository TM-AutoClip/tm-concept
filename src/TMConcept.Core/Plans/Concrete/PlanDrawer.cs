using TMConcept.Core.Engines;
using TMConcept.Core.Render;

namespace TMConcept.Core.Plans;

/// <summary>
/// The app's scene drawer (0.1.33): <c>plan.js</c>, the HyperFrames scene file that draws every scene from its plan with the app's own
/// overlay library (no model writes it, so it is not sent to the code check), and the world map it draws map scenes from (Natural Earth
/// 1:110m countries, public domain, built by <c>tools/world-map.py</c>). Only HyperFrames videos with a template use it: the drawer stands on
/// the template helper file (<c>tm.js</c>).
/// </summary>
public static class PlanDrawer
{
    /// <summary>The file the world map is written to next to the page (<c>window.TM_WORLD</c>).</summary>
    public const string WorldFile = "world.js";

    /// <summary>The first line of the drawer: a kept scene file that starts with it is the app's, drawn again from the input's plans.</summary>
    public const string Marker = "// The app's file (TM Concept 0.1.33): draws every scene from its plan";

    private static readonly Lazy<string> DrawerCode = new(() => Resource("TMConcept.Plans.plan.js"));

    private static readonly Lazy<string> World = new(() => "window.TM_WORLD = " + Resource("TMConcept.Plans.world.json").Trim() + ";\n");

    /// <summary>The drawer's code (the scene file).</summary>
    public static string Code => DrawerCode.Value;

    /// <summary>The world map script (<see cref="WorldFile"/>).</summary>
    public static string WorldScript => World.Value;

    /// <summary>Whether <paramref name="code"/> is the app's drawer.</summary>
    public static bool IsDrawer(string? code) => code is not null && code.StartsWith(Marker, StringComparison.Ordinal);

    /// <summary>Whether a scene of <paramref name="input"/> needs the world map.</summary>
    public static bool NeedsWorld(RenderInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        return input.Scenes.Any(s => s.Plan?.Base == PlanBases.Map);
    }

    /// <summary>Whether a video with <paramref name="engine"/> and <paramref name="style"/> is drawn by the app: HyperFrames, a template, its drawer chosen.</summary>
    public static bool Draws(EngineKind engine, Styles.VideoStyle? style) =>
        engine == EngineKind.HyperFrames && style is not null && style.Drawer == Styles.SceneDrawer.App;

    private static string Resource(string name)
    {
        using var stream = typeof(PlanDrawer).Assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException(name + " is not embedded.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd().ReplaceLineEndings("\n");
    }
}
