namespace TMConcept.Core.Styles;

/// <summary>
/// The finishing layer of a drawing style (F12, 0.1.43; <see cref="DrawStyleDefinition.Finish"/>): which effects the app's drawer lays over
/// the scenes and how strong they are, each 0 (off) to 1. <paramref name="Tooth"/>: the paper's tooth, light pits of the paper showing through
/// the picture. <paramref name="Grain"/>: film grain that changes 12 times a second. <paramref name="Vignette"/>: darker corners.
/// <paramref name="Bloom"/>: a soft glow of the accent colour (an approximation: the page cannot read its own pixels).
/// <paramref name="Shadow"/>: a drop shadow by depth (drawings one step above the background, labels two). <paramref name="Boil"/>: the
/// picture trembles a little in 12 steps a second, like hand-made frames. Drawn only when the video's look (the profile's choice) has its finishing on
/// (<see cref="VideoStyle.Finish"/>).
/// </summary>
public sealed record StyleFinish(double Tooth = 0, double Grain = 0, double Vignette = 0, double Bloom = 0, double Shadow = 0, double Boil = 0)
{
    /// <summary>No effect.</summary>
    public static StyleFinish None { get; } = new();

    /// <summary>Whether no effect is on.</summary>
    public bool IsEmpty => Effects.Count == 0;

    /// <summary>The names of the effects that are on, in a fixed order (tooth, grain, vignette, bloom, shadow, boil).</summary>
    public IReadOnlyList<string> Effects
    {
        get
        {
            var names = new List<string>(6);
            Add(names, "tooth", Tooth);
            Add(names, "grain", Grain);
            Add(names, "vignette", Vignette);
            Add(names, "bloom", Bloom);
            Add(names, "shadow", Shadow);
            Add(names, "boil", Boil);
            return names;
        }
    }

    private static void Add(List<string> names, string name, double value)
    {
        if (value > 0)
        {
            names.Add(name);
        }
    }
}
