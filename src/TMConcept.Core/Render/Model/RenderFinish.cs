namespace TMConcept.Core.Render;

/// <summary>
/// The finishing layer for the app's drawer (F12, 0.1.43; input.json <c>style.finish</c>, written by <see cref="RenderFinishBuilder"/> only
/// when the video's look has its finishing on and the style has effects; absent otherwise, and then <c>plan.js</c> draws no canvas and the video
/// looks exactly as before). The strengths are 0 (off) to 1 (<see cref="Styles.StyleFinish"/>). <see cref="Seed"/> fixes every random choice
/// of the layer, so a frame is the same however often and in whatever order it is drawn; <see cref="Steps"/> are the boil's 12 offsets
/// (x, y pairs from −1 to 1) made from it.
/// </summary>
public sealed class RenderFinish
{
    /// <summary>The seed of the layer's randomness (from the style and the video's title).</summary>
    public long Seed { get; set; }

    /// <summary>Paper tooth.</summary>
    public double Tooth { get; set; }

    /// <summary>Film grain (12 changes a second).</summary>
    public double Grain { get; set; }

    /// <summary>Darker corners.</summary>
    public double Vignette { get; set; }

    /// <summary>Soft glow of the accent colour.</summary>
    public double Bloom { get; set; }

    /// <summary>Drop shadow by depth.</summary>
    public double Shadow { get; set; }

    /// <summary>Trembling in 12 steps a second.</summary>
    public double Boil { get; set; }

    /// <summary>The boil's offsets: 12 x, y pairs (24 numbers from −1 to 1); empty when <see cref="Boil"/> is 0.</summary>
    public List<double> Steps { get; set; } = [];
}
